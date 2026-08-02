using Bakery.Application.Interfaces;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Repositories;
using Bakery.Infrastructure.Security;
using Bakery.Infrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Bakery.Infrastructure.Services.Backup;

namespace Bakery.Infrastructure.Services;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BakeryDatabase")
            ?? throw new InvalidOperationException("Missing BakeryDatabase connection string.");

        services.AddDbContext<BakeryDbContext>(options => options.UseSqlServer(connectionString));
        services.AddSingleton<IApplicationPathService>(_ => new ApplicationPathService());
        services.AddScoped(typeof(IRepository<>), typeof(BaseRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        services.AddScoped<DefaultDataSeeder>();
        services.AddScoped<IDefaultCashSafeService, DefaultCashSafeService>();
        services.AddScoped<ISystemSafeService, SystemSafeService>();
        services.AddScoped<IUserSafePermissionService, UserSafePermissionService>();
        services.AddSingleton<UserSessionService>();
        services.AddSingleton<IUserSessionService>(provider => provider.GetRequiredService<UserSessionService>());
        services.AddSingleton<ICurrentUserService>(provider => provider.GetRequiredService<UserSessionService>());
        services.AddSingleton<BranchContext>();
        services.AddSingleton<IBranchContext>(provider => provider.GetRequiredService<BranchContext>());
        services.AddSingleton<IInternalBranchContext>(provider => provider.GetRequiredService<BranchContext>());
        services.AddSingleton<SafeContext>();
        services.AddSingleton<ISafeContext>(provider => provider.GetRequiredService<SafeContext>());
        services.AddSingleton<IInternalSafeContext>(provider => provider.GetRequiredService<SafeContext>());
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IFirstRunSetupService, FirstRunSetupService>();
        services.AddScoped<IRoleManagementService, RoleManagementService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IAttachmentStorageService, AttachmentStorageService>();
        services.AddScoped<IWorkingDayService, WorkingDayService>();
        services.AddScoped<IWorkingDayReopenResolutionService, WorkingDayReopenResolutionService>();
        services.AddScoped<IBusinessDateService, BusinessDateService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<IUnitService, UnitService>();
        services.AddScoped<IItemUnitConversionService, ItemUnitConversionService>();
        services.AddScoped<StockMutationLock>();
        services.AddScoped<IStockMutationLock>(provider => provider.GetRequiredService<StockMutationLock>());
        services.AddScoped<IStockCalculationService, StockCalculationService>();
        services.AddScoped<IInventoryService, InventoryService>();
        
        // Parties & Statements
        services.AddScoped<IPartyService, PartyService>();
        services.AddScoped<IPartyLookupService, PartyLookupService>();
        services.AddScoped<IPartyStatementProvider, PartyStatementProvider>();
        services.AddScoped<IEmployeeStatementProvider, EmployeeStatementProvider>();
        services.AddScoped<IStatementService, StatementService>();
        
        services.AddScoped<ISafeService, SafeService>();
        services.AddScoped<ISaleInvoiceService, SaleInvoiceService>();
        services.AddScoped<IPurchaseInvoiceService, PurchaseInvoiceService>();
        services.AddScoped<IInvoiceNumberAllocator, InvoiceNumberAllocator>();
        services.AddScoped<IProductionPostingEngine, Bakery.Infrastructure.Engines.ProductionPostingEngine>();
        services.AddScoped<IRecipeService, RecipeService>();
        services.AddScoped<IProductionService, ProductionService>();
        services.AddScoped<IWasteService, WasteService>();
        services.AddScoped<IJobRoleService, JobRoleService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IEmployeeWageService, EmployeeWageService>();
        services.AddSingleton<IOwnerResetCodeVerifier, OwnerResetCodeVerifier>();
        services.AddSingleton<SystemResetOperationGate>();
        services.AddSingleton<ISystemResetFailureInjector, NoOpSystemResetFailureInjector>();
        services.AddScoped<ISystemResetService, SystemResetService>();
        services.AddScoped<ISettlementService, SettlementService>();
        services.AddScoped<IPartyPaymentService, PartyPaymentService>();

        services.AddSingleton<IRecoveryService, Bakery.Infrastructure.Services.Recovery.RecoveryService>();
        services.AddSingleton<BackupPathProvider>(provider =>
            new BackupPathProvider(provider.GetRequiredService<IApplicationPathService>()));
        services.AddSingleton<BackupOperationGate>();
        services.AddSingleton<GoogleTokenStore>();
        services.AddSingleton<IBackupStatusNotifier, BackupStatusNotifier>();
        services.AddSingleton<IConnectivityService, ConnectivityService>();
        services.AddSingleton<BackupEncryptionService>();
        services.AddScoped<IBackupValidationService, BackupValidationService>();
        services.AddScoped<IBackupRetentionService, BackupRetentionService>();
        services.AddScoped<ICloudBackupService, GoogleDriveCloudBackupService>();
        services.AddScoped<BackupService>();
        services.AddScoped<IBackupService>(provider => provider.GetRequiredService<BackupService>());
        services.AddScoped<IRestoreService, BackupRestoreService>();
        services.AddSingleton<IRestoreFailureInjector, NoOpRestoreFailureInjector>();
        services.AddSingleton<BackupQueueService>();
        services.AddSingleton<IBackupQueueService>(provider => provider.GetRequiredService<BackupQueueService>());
        services.AddSingleton<IBackupStartupService, BackupStartupService>();
        services.AddHostedService(provider => provider.GetRequiredService<BackupQueueService>());
        services.AddScoped<IBranchProvisioningService, BranchProvisioningService>();
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<IBranchSwitchService, BranchSwitchService>();
        services.AddScoped<ISafeSwitchService, SafeSwitchService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IIntegrityCheckService, IntegrityCheckService>();
        services.AddScoped<IValidationService, ValidationService>();
        services.AddScoped<IExceptionTranslator, ExceptionTranslator>();

        return services;
    }
}
