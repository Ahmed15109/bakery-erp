using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using Bakery.Application.Interfaces;
using Bakery.Application.Services;
using Bakery.Infrastructure.Services;
using Bakery.Reporting.Interfaces;
using Bakery.Reporting.Services;
using Bakery.Shared.Helpers;
using Bakery.Shared.Security;
using Bakery.WPF.Helpers;
using Bakery.WPF.Logging;
using Bakery.WPF.Services;
using Bakery.WPF.Views;
using Bakery.WPF.ViewModels;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Settings.Configuration;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SkiaSharp;

namespace Bakery.WPF;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private IServiceScope? _loginScope;
    private IServiceScope? _sessionScope;
    private SingleInstanceGuard? _instanceGuard;

    public IServiceProvider Services => _host?.Services
        ?? throw new InvalidOperationException("Application services are not available.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        ConfigureGlobalExceptionHandling();
        try
        {
            ConfigureBootstrapLogging();
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _instanceGuard = new SingleInstanceGuard(SingleInstanceGuard.ProductionMutexName);
            if (!_instanceGuard.IsPrimaryInstance)
            {
                Log.Warning("A second application instance was blocked by the process mutex");
                MessageBox.Show(
                    "نظام المخبز قيد التشغيل بالفعل. أغلق النافذة الحالية قبل تشغيل نسخة أخرى أو تثبيت تحديث.",
                    "Bakery ERP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown(2);
                return;
            }

            var userConfiguration = UserConfigurationBootstrapper.Ensure(
                ApplicationPathDefaults.UserConfigurationFile,
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

            var culture = new CultureInfo("ar-EG");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(
                    XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

            LiveCharts.Configure(settings => settings
                .UseDefaults()
                .UseRightToLeftSettings()
                .HasTextSettings(new TextSettings
                {
                    DefaultTypeface = SKTypeface.FromFamilyName("Cairo")
                }));

            _host = CreateHostBuilder(e.Args, userConfiguration.FilePath).Build();
            ApplicationConfigurationValidator.Validate(
                _host.Services.GetRequiredService<IConfiguration>());
            await _host.StartAsync();

            var applicationPaths = _host.Services.GetRequiredService<IApplicationPathService>();
            applicationPaths.EnsureDirectoriesExist();
            DataGridPersistence.Configure(applicationPaths);

            using (var scope = _host.Services.CreateScope())
            {
                var dbInitializer = scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
                await dbInitializer.InitializeAsync();

                var integrityCheck = scope.ServiceProvider.GetRequiredService<IIntegrityCheckService>();
                var isHealthy = await integrityCheck.RunFullCheckAsync();
                if (!isHealthy)
                {
                    var recoveryView = scope.ServiceProvider.GetRequiredService<RecoveryView>();
                    var recoveryViewModel = scope.ServiceProvider.GetRequiredService<RecoveryViewModel>();
                    recoveryView.DataContext = recoveryViewModel;
                    recoveryView.ShowDialog();

                    // Re-check after recovery window closes
                    isHealthy = await integrityCheck.RunFullCheckAsync();
                    if (!isHealthy)
                    {
                        Shutdown();
                        return;
                    }
                }
            }

            if (!await CompleteFirstRunSetupAsync())
            {
                Shutdown();
                return;
            }

            ShowLoginWindow();
            ShutdownMode = ShutdownMode.OnLastWindowClose;

            // Backup recovery is deliberately started only after database initialization,
            // integrity checks, and the first application window have completed.
            await _host.Services.GetRequiredService<IBackupStartupService>()
                .RunLightweightStartupRecoveryAsync();

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed before the first usable window was shown");
            TryWriteEarlyStartupFailure(ex);
            Log.CloseAndFlush();
            MessageBox.Show(
                "تعذر تشغيل نظام المخبز. تم حفظ تفاصيل الخطأ في سجل بدء التشغيل داخل مجلد بيانات المستخدم.",
                "Bakery ERP - خطأ بدء التشغيل",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _loginScope?.Dispose();
        _loginScope = null;
        _sessionScope?.Dispose();
        _sessionScope = null;

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        Log.CloseAndFlush();
        _instanceGuard?.Dispose();
        _instanceGuard = null;
        base.OnExit(e);
    }

    private static IHostBuilder CreateHostBuilder(
        string[] args,
        string userConfigurationPath)
    {
        return Host.CreateDefaultBuilder(args)
            .UseSerilog((context, loggerConfiguration) =>
            {
                // Single-file bundles do not expose a DependencyContext that Serilog can
                // scan for sinks. Supplying the sink assembly keeps structured JSON-based
                // configuration working in both bundled and multi-file deployments.
                var readerOptions = new ConfigurationReaderOptions(
                    typeof(FileLoggerConfigurationExtensions).Assembly);
                var logDirectory = ApplicationPathDefaults.LogsDirectory;
                Directory.CreateDirectory(logDirectory);
                loggerConfiguration
                    .ReadFrom.Configuration(context.Configuration, readerOptions)
                    .WriteTo.File(
                        new RedactingJsonFormatter(),
                        Path.Combine(logDirectory, "bakery-erp-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30,
                        fileSizeLimitBytes: 10 * 1024 * 1024,
                        rollOnFileSizeLimit: true,
                        shared: true);
            })
            .ConfigureAppConfiguration(configuration =>
            {
                // Build a deterministic precedence chain. Clearing the default sources
                // prevents an appsettings.json in the launch working directory from
                // becoming an undocumented customer-configuration source.
                configuration.Sources.Clear();
                configuration.SetBasePath(AppContext.BaseDirectory);
                configuration.AddJsonFile("appsettings.defaults.json", optional: false, reloadOnChange: false);
                configuration.AddJsonFile(userConfigurationPath, optional: false, reloadOnChange: false);
                configuration.AddEnvironmentVariables();
                configuration.AddCommandLine(args);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddInfrastructure(context.Configuration);
                services.AddScoped<IInventoryReportService, InventoryReportService>();
                services.AddScoped<IAccountingReportService, AccountingReportService>();
                services.AddValidatorsFromAssemblyContaining<ApplicationAssemblyMarker>();
                services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

                services.AddScoped<INavigationService, NavigationService>();
                services.AddSingleton<IMessageService, MessageService>();
                services.AddScoped<IDialogService, DialogService>();
                services.AddSingleton<IOperationalContextRefreshNotifier, OperationalContextRefreshNotifier>();
                services.AddSingleton<IOwnerResetAuthorizationPrompt, OwnerResetAuthorizationPrompt>();
                services.AddSingleton<IFileLauncherService, FileLauncherService>();
                
                services.AddTransient<IReceiptPrintService, Bakery.WPF.Services.Print.ThermalPrintService>();
                services.AddTransient<IReceiptRenderer, Bakery.WPF.Services.Print.ThermalReceiptRenderer>();
                services.AddTransient<IReportPrintService, Bakery.WPF.Services.Print.A4PrintService>();
                services.AddTransient<IPdfExportService, Bakery.Reporting.Services.ReportPdfGenerator>();
                services.AddTransient<IExcelExportService, Bakery.WPF.Services.Print.ExcelExportService>();
                services.AddTransient<LoginWindow>();
                services.AddTransient<LoginViewModel>();
                services.AddTransient<FirstRunSetupWindow>();
                services.AddTransient<FirstRunSetupViewModel>();
                services.AddTransient<MainWindow>();
                services.AddTransient<MainViewModel>();
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ItemsViewModel>();
                services.AddTransient<UnitsViewModel>();
                services.AddTransient<InventoryViewModel>();
                services.AddTransient<StockCountViewModel>();
                services.AddTransient<InventoryMovementsViewModel>();
                services.AddTransient<PartiesViewModel>();
                services.AddTransient<PartyStatementViewModel>();
                services.AddTransient<SalesViewModel>();
                services.AddTransient<PurchasesViewModel>();
                services.AddTransient<SaleInvoiceDialog>();
                services.AddTransient<SaleInvoiceDialogViewModel>();
                services.AddTransient<PurchaseInvoiceDialog>();
                services.AddTransient<PurchaseInvoiceDialogViewModel>();
                services.AddTransient<CloseDayDialog>();
                services.AddTransient<CloseDayDialogViewModel>();
                services.AddTransient<ReopenWorkingDayDialog>();
                services.AddTransient<ReopenWorkingDayDialogViewModel>();
                services.AddTransient<ItemFormDialog>();
                services.AddTransient<ItemFormDialogViewModel>();
                services.AddTransient<PartyPaymentDialog>();
                services.AddTransient<PartyPaymentDialogViewModel>();
                services.AddTransient<InventoryAdjustmentDialog>();
                services.AddTransient<InventoryAdjustmentDialogViewModel>();

                services.AddTransient<RecipesViewModel>();
                services.AddTransient<RecipesView>();
                services.AddTransient<ProductionViewModel>();
                services.AddTransient<ProductionView>();
                services.AddTransient<ProductionOrderViewModel>();
                services.AddTransient<ProductionOrdersView>();
                services.AddTransient<ProductionHistoryViewModel>();
                services.AddTransient<ProductionHistoryView>();
                services.AddTransient<WasteViewModel>();
                services.AddTransient<WasteView>();
                services.AddTransient<EmployeesViewModel>();
                services.AddTransient<EmployeesView>();
                services.AddTransient<EmployeeWagesViewModel>();
                services.AddTransient<EmployeeWagesView>();
                services.AddTransient<JobRolesViewModel>();
                services.AddTransient<JobRolesView>();
                services.AddTransient<SettlementViewModel>();
                services.AddTransient<SettlementView>();
                services.AddTransient<EmployeeLedgerViewModel>();
                services.AddTransient<EmployeeLedgerView>();

                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsView>();
                services.AddTransient<UsersViewModel>();
                services.AddTransient<UsersView>();
                services.AddTransient<UserFormDialog>();
                services.AddTransient<UserFormDialogViewModel>();
                services.AddTransient<ResetPasswordDialog>();
                services.AddTransient<ResetPasswordDialogViewModel>();
                services.AddTransient<ChangePasswordDialog>();
                services.AddTransient<ChangePasswordDialogViewModel>();
                services.AddTransient<RolesViewModel>();
                services.AddTransient<RolesView>();
                services.AddTransient<RoleFormDialog>();
                services.AddTransient<RoleFormDialogViewModel>();
                services.AddTransient<AuditLogViewModel>();
                services.AddTransient<AuditLogView>();
                services.AddTransient<BranchesViewModel>();
                services.AddTransient<BranchesView>();
                services.AddTransient<BranchFormDialog>();
                services.AddTransient<BranchFormDialogViewModel>();
                services.AddTransient<BranchSelectionDialog>();
                services.AddTransient<BranchSelectionDialogViewModel>();
                services.AddTransient<HealthMonitorViewModel>();
                services.AddTransient<HealthMonitorView>();
                services.AddTransient<RecoveryViewModel>();
                services.AddTransient<RecoveryView>();
                services.AddTransient<BackupManagementViewModel>();
                services.AddTransient<BackupManagementView>();

                services.AddTransient<DashboardView>();
                services.AddTransient<ItemsView>();
                services.AddTransient<UnitsView>();
                services.AddTransient<InventoryView>();
                services.AddTransient<StockCountView>();
                services.AddTransient<InventoryMovementsView>();
                services.AddTransient<PartiesView>();
                services.AddTransient<PartyStatementView>();
                services.AddTransient<SalesView>();
                services.AddTransient<PurchasesView>();

                services.AddTransient<InventoryHomeViewModel>();
                services.AddTransient<InventoryHomeView>();
                services.AddTransient<ReportsViewModel>();
                services.AddTransient<ReportsView>();
                services.AddTransient<ReportDetailsViewModel>();
                services.AddTransient<ReportDetailsView>();

                // Treasury Module
                services.AddTransient<TreasuryViewModel>();
                services.AddTransient<TreasuryView>();
                services.AddTransient<TreasuryTransactionDialog>();
                services.AddTransient<TreasuryTransactionDialogViewModel>();
                services.AddTransient<TreasuryTransferDialog>();
                services.AddTransient<TreasuryTransferDialogViewModel>();
                services.AddTransient<ReverseTransactionDialog>();
                services.AddTransient<ReverseTransactionDialogViewModel>();

                services.AddTransient<SafeManagementDialog>();
                services.AddTransient<SafeManagementDialogViewModel>();
                services.AddTransient<SafeFormDialog>();
                services.AddTransient<SafeFormDialogViewModel>();
                services.AddTransient<SafeSelectionDialog>();
                services.AddTransient<SafeSelectionDialogViewModel>();

                services.AddTransient<InvoiceWorkspaceViewModel>();
                services.AddTransient<InvoiceWorkspaceView>();
                services.AddTransient<SafeMismatchDialog>();
                services.AddTransient<SafeMismatchDialogViewModel>();
            });
    }

    private static void ConfigureBootstrapLogging()
    {
        var logDirectory = GetEarlyStartupLogDirectory();
        Directory.CreateDirectory(logDirectory);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                new RedactingJsonFormatter(),
                Path.Combine(logDirectory, "startup-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true)
            .CreateBootstrapLogger();
    }

    private static string GetEarlyStartupLogDirectory()
        => ApplicationPathDefaults.LogsDirectory;

    private static void TryWriteEarlyStartupFailure(Exception exception)
    {
        try
        {
            var directory = GetEarlyStartupLogDirectory();
            Directory.CreateDirectory(directory);
            var entry = SensitiveDataRedactor.Redact(
                $"[{DateTimeOffset.UtcNow:O}] Startup failure{Environment.NewLine}{exception}{Environment.NewLine}");
            File.AppendAllText(Path.Combine(directory, "startup-fallback.log"), entry);
        }
        catch
        {
            // Startup is already failing. Logging must never mask the original exception.
        }
    }

    private void ShowLoginWindow()
    {
        _loginScope?.Dispose();
        _loginScope = _host!.Services.CreateScope();
        var loginWindow = _loginScope.ServiceProvider.GetRequiredService<LoginWindow>();
        loginWindow.LoginSucceeded += (_, _) => CompleteLogin(loginWindow);
        MainWindow = loginWindow;
        loginWindow.Show();
    }

    private async Task<bool> CompleteFirstRunSetupAsync()
    {
        using var scope = _host!.Services.CreateScope();
        var setupService = scope.ServiceProvider.GetRequiredService<IFirstRunSetupService>();
        if (!await setupService.IsSetupRequiredAsync()) return true;

        var setupWindow = scope.ServiceProvider.GetRequiredService<FirstRunSetupWindow>();
        MainWindow = setupWindow;
        var completed = setupWindow.ShowDialog() == true;
        MainWindow = null;
        return completed && !await setupService.IsSetupRequiredAsync();
    }

    private async void CompleteLogin(LoginWindow loginWindow)
    {
        IServiceScope? newSessionScope = null;
        MainWindow? mainWindow = null;
        try
        {
            newSessionScope = _host!.Services.CreateScope();
            mainWindow = newSessionScope.ServiceProvider.GetRequiredService<MainWindow>();
            if (mainWindow.DataContext is MainViewModel mainViewModel)
            {
                mainViewModel.LoggedOut += (_, _) =>
                    Dispatcher.BeginInvoke(() => ReturnToLogin(mainWindow));
                await mainViewModel.InitializationTask;
            }

            // The login window remains visible until the main window is actually open.
            mainWindow.Show();
            MainWindow = mainWindow;
            loginWindow.Close();
            _sessionScope = newSessionScope;
            newSessionScope = null;

            var completedLoginScope = _loginScope;
            _loginScope = null;
            _ = Dispatcher.BeginInvoke(() => completedLoginScope?.Dispose());
        }
        catch
        {
            if (mainWindow?.IsVisible == true)
            {
                mainWindow.Close();
            }

            newSessionScope?.Dispose();
            MainWindow = loginWindow;
            throw;
        }
    }

    private void ReturnToLogin(MainWindow mainWindow)
    {
        var completedSessionScope = _sessionScope;

        // Show the next login window before closing the last visible window.
        ShowLoginWindow();
        _sessionScope = null;
        mainWindow.Close();
        completedSessionScope?.Dispose();
    }

    private void ConfigureGlobalExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Log.Fatal(ex, "Unhandled application domain exception");
            LogEmergencySync(ex, "AppDomain.UnhandledException");
        };

        Current.DispatcherUnhandledException += (sender, args) =>
        {
            args.Handled = true;
            Log.Fatal(args.Exception, "Unhandled WPF dispatcher exception");
            LogEmergencySync(args.Exception, "DispatcherUnhandledException");
            MessageBox.Show(
                "حدث خطأ غير متوقع في النظام. تم حفظ بيانات الطوارئ بنجاح. يرجى إعادة تشغيل التطبيق.",
                "Bakery ERP - خطأ النظام",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            args.SetObserved();
            Log.Fatal(args.Exception, "Unhandled task exception");
            LogEmergencySync(args.Exception, "UnobservedTaskException");
        };
    }

    private void LogEmergencySync(Exception? ex, string context)
    {
        if (ex != null && _host != null)
        {
            try
            {
                using var scope = _host.Services.CreateScope();
                var recovery = scope.ServiceProvider.GetRequiredService<IRecoveryService>();
                recovery.LogEmergencyAsync(ex, context).GetAwaiter().GetResult();
            }
            catch { /* Ignore errors during emergency logging */ }
        }
    }
}
