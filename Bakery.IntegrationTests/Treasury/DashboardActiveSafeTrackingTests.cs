using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Services;
using Bakery.WPF.Services;
using Bakery.WPF.ViewModels;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests.Treasury;

public class DashboardActiveSafeTrackingTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly BakeryDbContext _dbContext;
    private readonly string _dbName;

    private int _branch1Id;
    private int _branch2Id;
    private int _dailySafeId;
    private int _mainSafeId;
    private int _privateSafeId;
    private int _branch2SafeId;

    public DashboardActiveSafeTrackingTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        _dbName = $"BakeryERP_DashboardTest_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={_dbName};Trusted_Connection=True;MultipleActiveResultSets=true";

        services.AddDbContext<BakeryDbContext>(options => options.UseSqlServer(connectionString));

        services.AddSingleton<SafeContext>();
        services.AddSingleton<ISafeContext>(sp => sp.GetRequiredService<SafeContext>());
        services.AddSingleton<IInternalSafeContext>(sp => sp.GetRequiredService<SafeContext>());
        services.AddSingleton<IOperationalContextRefreshNotifier, OperationalContextRefreshNotifier>();

        services.AddScoped<ISafeService, SafeService>();
        services.AddScoped<ISafeSwitchService, SafeSwitchService>();
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IBranchProvisioningService, TestBranchProvisioningService>();
        services.AddScoped<IBranchSwitchService, BranchSwitchService>();
        services.AddScoped<IPermissionService, TestPermissionService>();
        services.AddScoped<IUserSessionService, TestUserSessionService>();
        services.AddScoped<ICurrentUserService>(sp => sp.GetRequiredService<IUserSessionService>());
        services.AddScoped<IUserSafePermissionService, TestUserSafePermissionService>();
        services.AddScoped<IWorkingDayService, TestWorkingDayService>();
        services.AddScoped<IDefaultCashSafeService, TestDefaultCashSafeService>();
        services.AddScoped<ISystemSafeService, TestSystemSafeService>();
        services.AddScoped<IAuditService, TestAuditService>();
        services.AddScoped<IAttachmentStorageService, TestAttachmentStorageService>();
        services.AddScoped<IStockCalculationService, TestStockCalculationService>();
        services.AddScoped<INavigationService, TestNavigationService>();
        services.AddScoped<IMessageService, TestMessageService>();
        services.AddScoped<IDialogService, TestDialogService>();
        services.AddScoped<IBranchContext, TestBranchContext>();
        services.AddScoped<IBackupService, TestBackupService>();
        services.AddSingleton<IBackupStatusNotifier, TestBackupStatusNotifier>();

        services.AddValidatorsFromAssemblyContaining<CreateBranchRequest>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<TreasuryViewModel>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<BakeryDbContext>();
        _dbContext.Database.EnsureCreated();

        SeedDataAsync().GetAwaiter().GetResult();
    }

    private async Task SeedDataAsync()
    {
        var user = new User { Id = 1, Username = "admin", FullName = "Admin", IsActive = true };
        _dbContext.Users.Add(user);

        var b1 = new Branch { Name = "Branch 1", Code = "B1", IsActive = true };
        var b2 = new Branch { Name = "Branch 2", Code = "B2", IsActive = true };
        _dbContext.Branches.AddRange(b1, b2);
        await _dbContext.SaveChangesAsync();
        _branch1Id = b1.Id;
        _branch2Id = b2.Id;

        _dbContext.UserBranches.AddRange(
            new UserBranch { UserId = 1, BranchId = _branch1Id },
            new UserBranch { UserId = 1, BranchId = _branch2Id }
        );
        await _dbContext.SaveChangesAsync();

        var safeDaily = new Safe { BranchId = _branch1Id, Name = "Daily Safe", ArabicName = "خزنة رصيد اليوم", Type = SafeType.Daily, IsActive = true };
        var safeMain = new Safe { BranchId = _branch1Id, Name = "Main Safe", ArabicName = "الخزنة الرئيسية", Type = SafeType.Main, IsActive = true };
        var safePrivate = new Safe { BranchId = _branch1Id, Name = "Private Safe", ArabicName = "الخزنة الخاصة", Type = SafeType.Private, IsActive = true };
        var safeB2 = new Safe { BranchId = _branch2Id, Name = "Branch 2 Safe", ArabicName = "خزنة الفرع الثاني", Type = SafeType.Daily, IsActive = true };
        _dbContext.Safes.AddRange(safeDaily, safeMain, safePrivate, safeB2);
        await _dbContext.SaveChangesAsync();

        _dailySafeId = safeDaily.Id;
        _mainSafeId = safeMain.Id;
        _privateSafeId = safePrivate.Id;
        _branch2SafeId = safeB2.Id;

        var day = new WorkingDay { BranchId = _branch1Id, BusinessDate = DateOnly.FromDateTime(DateTime.Today), OpeningCash = 0, Status = WorkingDayStatus.Open };
        _dbContext.WorkingDays.Add(day);
        await _dbContext.SaveChangesAsync();

        // Seed initial balances:
        // Daily Safe: 500
        // Main Safe: 1000
        // Private Safe: 2000
        _dbContext.SafeMovements.AddRange(
            new SafeMovement { BranchId = _branch1Id, WorkingDayId = day.Id, SafeId = _dailySafeId, Amount = 500m, Description = "Init Daily", Type = SafeMovementType.OpeningBalance },
            new SafeMovement { BranchId = _branch1Id, WorkingDayId = day.Id, SafeId = _mainSafeId, Amount = 1000m, Description = "Init Main", Type = SafeMovementType.OpeningBalance },
            new SafeMovement { BranchId = _branch1Id, WorkingDayId = day.Id, SafeId = _privateSafeId, Amount = 2000m, Description = "Init Private", Type = SafeMovementType.OpeningBalance }
        );
        await _dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Scenario1_DashboardDisplaysSelectedSafeBalanceOnly_NotSumOfAllSafes()
    {
        var safeContext = _serviceProvider.GetRequiredService<ISafeContext>();
        var internalContext = _serviceProvider.GetRequiredService<IInternalSafeContext>();

        // Set active safe to "Daily Safe" (500.00)
        internalContext.ConfigureSafe(new SafeDto(_dailySafeId, "Daily Safe", "خزنة رصيد اليوم", 500m, SafeType.Daily));

        using var scope = _serviceProvider.CreateScope();
        var dashboard = scope.ServiceProvider.GetRequiredService<DashboardViewModel>();
        await dashboard.InitializationTask;

        // Balance must equal 500.00, NOT 3500.00
        Assert.Equal(500m, dashboard.CurrentSafeBalance);
        Assert.Equal("500.00", dashboard.TreasuryBalanceText);
        Assert.Equal("خزنة رصيد اليوم", dashboard.CurrentSafeName);

        var safeMetric = dashboard.PrimaryMetrics.FirstOrDefault(m => m.Title == "رصيد الخزنة الحالية");
        Assert.NotNull(safeMetric);
        Assert.Equal("500.00", safeMetric.Value);
        Assert.Equal("خزنة رصيد اليوم", safeMetric.Subtitle);
    }

    [Fact]
    public async Task Scenario2_SafeSwitchingUpdatesDashboardImmediately()
    {
        var safeContext = _serviceProvider.GetRequiredService<ISafeContext>();
        var internalContext = _serviceProvider.GetRequiredService<IInternalSafeContext>();
        var switchService = _serviceProvider.GetRequiredService<ISafeSwitchService>();

        internalContext.ConfigureSafe(new SafeDto(_dailySafeId, "Daily Safe", "خزنة رصيد اليوم", 500m, SafeType.Daily));

        using var scope = _serviceProvider.CreateScope();
        var dashboard = scope.ServiceProvider.GetRequiredService<DashboardViewModel>();
        await dashboard.InitializationTask;

        Assert.Equal(500m, dashboard.CurrentSafeBalance);

        // Switch safe to Main Safe (1000.00)
        await switchService.SwitchSafeAsync(new SafeDto(_mainSafeId, "Main Safe", "الخزنة الرئيسية", 1000m, SafeType.Main));

        // Allow background safe change handler to finish processing
        await Task.Delay(300);

        Assert.Equal(1000m, dashboard.CurrentSafeBalance);
        Assert.Equal("1,000.00", dashboard.TreasuryBalanceText);
        Assert.Equal("الخزنة الرئيسية", dashboard.CurrentSafeName);

        var safeMetric = dashboard.PrimaryMetrics.FirstOrDefault(m => m.Title == "رصيد الخزنة الحالية");
        Assert.NotNull(safeMetric);
        Assert.Equal("1,000.00", safeMetric.Value);
        Assert.Equal("الخزنة الرئيسية", safeMetric.Subtitle);
    }

    [Fact]
    public async Task Scenario3_DepositAndWithdrawalUpdateDashboard()
    {
        var safeContext = _serviceProvider.GetRequiredService<ISafeContext>();
        var internalContext = _serviceProvider.GetRequiredService<IInternalSafeContext>();
        var notifier = _serviceProvider.GetRequiredService<IOperationalContextRefreshNotifier>();

        internalContext.ConfigureSafe(new SafeDto(_dailySafeId, "Daily Safe", "خزنة رصيد اليوم", 500m, SafeType.Daily));

        using var scope = _serviceProvider.CreateScope();
        var dashboard = scope.ServiceProvider.GetRequiredService<DashboardViewModel>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        await dashboard.InitializationTask;

        Assert.Equal(500m, dashboard.CurrentSafeBalance);

        // Deposit 250 into Daily Safe
        await safeService.DepositAsync(_dailySafeId, 250m, "Deposit Test");
        await notifier.RequestRefreshAsync();

        Assert.Equal(750m, dashboard.CurrentSafeBalance);
        Assert.Equal("750.00", dashboard.TreasuryBalanceText);

        // Withdraw 100 from Daily Safe
        await safeService.WithdrawAsync(_dailySafeId, 100m, "Withdraw Test");
        await notifier.RequestRefreshAsync();

        Assert.Equal(650m, dashboard.CurrentSafeBalance);
        Assert.Equal("650.00", dashboard.TreasuryBalanceText);
    }

    [Fact]
    public async Task Scenario4_TransferRefreshesSourceAndDestinationBalances()
    {
        var safeContext = _serviceProvider.GetRequiredService<ISafeContext>();
        var internalContext = _serviceProvider.GetRequiredService<IInternalSafeContext>();
        var notifier = _serviceProvider.GetRequiredService<IOperationalContextRefreshNotifier>();

        internalContext.ConfigureSafe(new SafeDto(_dailySafeId, "Daily Safe", "خزنة رصيد اليوم", 500m, SafeType.Daily));

        using var scope = _serviceProvider.CreateScope();
        var dashboard = scope.ServiceProvider.GetRequiredService<DashboardViewModel>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        await dashboard.InitializationTask;

        // Daily Safe has 500, Main Safe has 1000
        Assert.Equal(500m, dashboard.CurrentSafeBalance);

        // Transfer 300 from Main Safe to Daily Safe
        await safeService.TransferAsync(_mainSafeId, _dailySafeId, 300m, "Transfer Test");
        await notifier.RequestRefreshAsync();

        // Daily Safe should now have 500 + 300 = 800
        Assert.Equal(800m, dashboard.CurrentSafeBalance);
        Assert.Equal("800.00", dashboard.TreasuryBalanceText);

        // Switch to Main Safe (1000 - 300 = 700)
        internalContext.ConfigureSafe(new SafeDto(_mainSafeId, "Main Safe", "الخزنة الرئيسية", 700m, SafeType.Main));
        await Task.Delay(300);

        Assert.Equal(700m, dashboard.CurrentSafeBalance);
        Assert.Equal("700.00", dashboard.TreasuryBalanceText);
    }

    [Fact]
    public async Task Scenario5_BranchSwitchingRejectsInvalidPreviousBranchSafe()
    {
        var internalContext = _serviceProvider.GetRequiredService<IInternalSafeContext>();
        var branchSwitchService = _serviceProvider.GetRequiredService<IBranchSwitchService>();

        // Currently in Branch 1 with Daily Safe selected
        internalContext.ConfigureSafe(new SafeDto(_dailySafeId, "Daily Safe", "خزنة رصيد اليوم", 500m, SafeType.Daily));
        Assert.Equal(_dailySafeId, internalContext.CurrentSafeId);

        // Switch to Branch 2
        var branch2Dto = new BranchDto(_branch2Id, "B2", "Branch 2", true, null);
        await branchSwitchService.SwitchBranchAsync(branch2Dto);

        // Current safe MUST be updated to Branch 2 safe (Branch 2 Safe), NOT the old Branch 1 safe
        Assert.Equal(_branch2SafeId, internalContext.CurrentSafeId);
        Assert.Equal("خزنة الفرع الثاني", internalContext.CurrentSafe?.DisplayName);
    }

    [Fact]
    public async Task Scenario6_DisposedDashboardViewModelNoLongerReactsToSafeChanged()
    {
        var internalContext = _serviceProvider.GetRequiredService<IInternalSafeContext>();
        var switchService = _serviceProvider.GetRequiredService<ISafeSwitchService>();

        internalContext.ConfigureSafe(new SafeDto(_dailySafeId, "Daily Safe", "خزنة رصيد اليوم", 500m, SafeType.Daily));

        DashboardViewModel dashboard;
        using (var scope = _serviceProvider.CreateScope())
        {
            dashboard = scope.ServiceProvider.GetRequiredService<DashboardViewModel>();
            await dashboard.InitializationTask;
            dashboard.Dispose();
        }

        // Switching safe after disposal
        await switchService.SwitchSafeAsync(new SafeDto(_mainSafeId, "Main Safe", "الخزنة الرئيسية", 1000m, SafeType.Main));
        await Task.Delay(200);

        // Disposed dashboard safe balance remains unchanged (500m) and no exception is thrown
        Assert.Equal(500m, dashboard.CurrentSafeBalance);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        _serviceProvider.Dispose();
    }

    private class TestBranchProvisioningService : IBranchProvisioningService
    {
        public Task ProvisionBranchAsync(int branchId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class TestPermissionService : IPermissionService
    {
        public bool HasPermission(string key) => true;
        public bool HasAnyPermission(params string[] keys) => true;
        public void EnsurePermission(string key) { }
        public bool IsAdmin() => true;
    }

    private class TestUserSessionService : IUserSessionService
    {
        public AuthenticatedUserDto? CurrentUser { get; set; } = new AuthenticatedUserDto(1, "admin", "Admin", [], true);
        public bool IsAuthenticated => true;
        public void SignIn(AuthenticatedUserDto user) => CurrentUser = user;
        public void SignOut() => CurrentUser = null;
        public bool HasPermission(string permissionKey) => true;
        public int? UserId => 1;
        public string Username => "admin";
        public string FullName => "Admin";
        public IReadOnlyCollection<string> Permissions => Array.Empty<string>();
        public bool IsSuperAdmin => true;
    }

    private class TestUserSafePermissionService : IUserSafePermissionService
    {
        public Task<GetUserSafePermissionsResponse> GetUserPermissionsAsync(int userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateUserPermissionsAsync(UpdateUserSafePermissionsRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CanAccessSafeAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanViewBalanceAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanViewLedgerAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanCashInAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanCashOutAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanTransferFromAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanReceiveTransferAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private class TestWorkingDayService : IWorkingDayService
    {
        private readonly BakeryDbContext _db;
        public TestWorkingDayService(BakeryDbContext db) => _db = db;

        public async Task<WorkingDay> EnsureActiveWorkingDayAsync(CancellationToken cancellationToken = default)
        {
            var day = await _db.WorkingDays.FirstOrDefaultAsync(cancellationToken);
            return day ?? throw new InvalidOperationException("No working day.");
        }

        public Task<WorkingDay?> GetCurrentOpenDayAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> OpenDayAsync(OpenWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> CloseCurrentDayAsync(CloseWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> EndCurrentDayAndOpenNextAsync(CloseWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayCloseReadinessDto> GetEndOfDayReadinessAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> AutoOpenIfNeededAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> SimplifiedCloseAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public async Task<WorkingDaySummaryDto?> GetCurrentDaySummaryAsync(CancellationToken cancellationToken = default)
        {
            var day = await _db.WorkingDays.FirstOrDefaultAsync(cancellationToken);
            if (day == null) return null;
            return new WorkingDaySummaryDto(day.Id, day.BusinessDate, day.Status, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
        public Task<WorkingDayReopenEligibilityDto> GetReopenEligibilityAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DashboardTrendPointDto>> GetRecentDashboardTrendAsync(int days = 7, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DashboardTrendPointDto>>([]);
        public Task<decimal> CalculateExpectedClosingCashAsync(int workingDayId, CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<(bool Match, decimal Difference, string Details)> VerifyTreasuryIntegrityAsync(int dayId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> ReopenDayAsync(int dayId, string reason, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ClosingReportDto?> GetClosingReportAsync(int dayId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class TestDefaultCashSafeService : IDefaultCashSafeService
    {
        private readonly BakeryDbContext _db;
        public TestDefaultCashSafeService(BakeryDbContext db) => _db = db;
        public async Task<Safe> GetDefaultCashSafeAsync(CancellationToken cancellationToken = default)
        {
            return await _db.Safes.FirstAsync(s => s.Type == SafeType.Daily, cancellationToken);
        }
    }

    private class TestSystemSafeService : ISystemSafeService
    {
        private readonly BakeryDbContext _db;
        public TestSystemSafeService(BakeryDbContext db) => _db = db;
        public Task EnsureSystemSafesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Safe> GetDailySafeAsync(CancellationToken cancellationToken = default) => _db.Safes.FirstAsync(s => s.Type == SafeType.Daily, cancellationToken);
        public Task<Safe> GetMainSafeAsync(CancellationToken cancellationToken = default) => _db.Safes.FirstAsync(s => s.Type == SafeType.Main, cancellationToken);
        public Task<Safe> GetPrivateSafeAsync(CancellationToken cancellationToken = default) => _db.Safes.FirstAsync(s => s.Type == SafeType.Private, cancellationToken);
        public Task<Safe?> GetSafeByTypeAsync(SafeType type, CancellationToken cancellationToken = default) => _db.Safes.FirstOrDefaultAsync(s => s.Type == type, cancellationToken);
    }

    private class TestAuditService : IAuditService
    {
        public Task LogAsync(string action, string entityName, int? entityId = null, string? oldValue = null, string? newValue = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class TestAttachmentStorageService : IAttachmentStorageService
    {
        public Task<string> SaveAttachmentAsync(string tempFilePath, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task DeleteAttachmentAsync(string attachmentPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public string GetFullPath(string relativePath) => relativePath;
    }

    private class TestStockCalculationService : IStockCalculationService
    {
        public Task<decimal> GetCurrentStockAsync(int itemId, CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<IReadOnlyDictionary<int, decimal>> GetCurrentStockAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<int, decimal>>(new Dictionary<int, decimal>());
        public Task<IReadOnlyList<StockItemDto>> GetCurrentStockAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StockItemDto>>([]);
        public Task<IReadOnlyList<StockItemDto>> GetLowStockItemsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StockItemDto>>([]);
        public Task<decimal> GetStockValuationAsync(CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<bool> HasAvailableStockAsync(int itemId, decimal quantity, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private class TestNavigationService : INavigationService
    {
        public CommunityToolkit.Mvvm.ComponentModel.ObservableObject? CurrentViewModel { get; set; }
        public TViewModel NavigateTo<TViewModel>() where TViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject => throw new NotImplementedException();
    }

    private class TestMessageService : IMessageService
    {
        public void ShowInfo(string message) { }
        public void ShowError(string message) { }
        public bool Confirm(string message) => true;
        public Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "") => Task.FromResult<string?>(null);
    }

    private class TestDialogService : IDialogService
    {
        public Task<DialogResult<TViewModel>> ShowDialogAsync<TViewModel>(Func<TViewModel, Task>? initialize = null) where TViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject => throw new NotImplementedException();
        public DialogResult<TViewModel> ShowDialog<TViewModel>(Action<TViewModel>? initialize = null) where TViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject => throw new NotImplementedException();
    }

    private class TestBranchContext : IInternalBranchContext
    {
        public int? CurrentBranchId => CurrentBranch?.Id;
        public BranchDto? CurrentBranch { get; set; } = new BranchDto(1, "B1", "Branch 1", true, null);
        public void ConfigureBranch(BranchDto branch) => CurrentBranch = branch;
        public void Clear() => CurrentBranch = null;
    }

    private class TestBackupService : IBackupService
    {
        public Task<string> CreateBackupAsync(string? customPath = null, string? password = null, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<string> CreateSafetySnapshotAsync(string operationName, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<BackupMetadata>> GetBackupHistoryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<BackupMetadata>>([]);
        public Task EnforceRetentionPolicyAsync(int maxBackups = 30, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<BackupStatusSummary> GetStatusSummaryAsync(CancellationToken cancellationToken = default) => Task.FromResult(new BackupStatusSummary(null, false, 0, null, null, "Healthy"));
    }

    private class TestBackupStatusNotifier : IBackupStatusNotifier
    {
        public event EventHandler? StatusChanged;
        public void NotifyChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
