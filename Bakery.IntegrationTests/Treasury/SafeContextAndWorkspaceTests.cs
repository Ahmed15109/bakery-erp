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
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class SafeContextAndWorkspaceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly BakeryDbContext _dbContext;
    private readonly string _dbName;
    private readonly string _applicationRoot;

    private int _branchId;
    private int _safeAId;
    private int _safeBId;
    private int _workingDayId;
    private int _customerId;
    private int _unitId;
    private int _itemId;

    public SafeContextAndWorkspaceTests()
    {
        var services = new ServiceCollection();

        services.AddLogging();

        _dbName = $"BakeryERP_Test_{Guid.NewGuid():N}";
        _applicationRoot = Path.Combine(
            Path.GetTempPath(), "BakeryERP", "SafeContextTests", Guid.NewGuid().ToString("N"));
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={_dbName};Trusted_Connection=True;MultipleActiveResultSets=true";

        services.AddDbContext<BakeryDbContext>(options =>
            options.UseSqlServer(connectionString));

        // Application Services
        services.AddScoped<ISafeService, SafeService>();
        services.AddScoped<ISaleInvoiceService, SaleInvoiceService>();
        services.AddScoped<IPurchaseInvoiceService, PurchaseInvoiceService>();
        services.AddScoped<IPermissionService, FakePermissionService>();
        services.AddScoped<IUserSessionService, FakeUserSessionService>();
        services.AddScoped<ICurrentUserService>(sp => sp.GetRequiredService<IUserSessionService>());
        services.AddScoped<IUserSafePermissionService, FakeUserSafePermissionService>();
        services.AddScoped<IWorkingDayService, FakeWorkingDayService>();
        services.AddScoped<IDefaultCashSafeService, FakeDefaultCashSafeService>();
        services.AddScoped<ISystemSafeService, FakeSystemSafeService>();
        services.AddScoped<IStockCalculationService, FakeStockCalculationService>();
        services.AddScoped<IItemUnitConversionService, ItemUnitConversionService>();
        services.AddScoped<IStockMutationLock, StockMutationLock>();
        services.AddScoped<IInvoiceNumberAllocator, InvoiceNumberAllocator>();
        services.AddScoped<IAuditService, FakeAuditService>();
        services.AddScoped<IAttachmentStorageService, AttachmentStorageService>();
        services.AddSingleton<IApplicationPathService>(new ApplicationPathService(_applicationRoot));
        services.AddScoped<IBackupService, FakeBackupService>();
        services.AddScoped<IPartyService, PartyService>();
        services.AddScoped<IValidationService, FakeValidationService>();
        services.AddScoped<IBranchContext, FakeBranchContext>();
        
        // Validators
        services.AddValidatorsFromAssemblyContaining<SaveSaleInvoiceRequest>();
        
        // Safe Context Setup
        services.AddScoped<SafeContext>();
        services.AddScoped<ISafeContext>(sp => sp.GetRequiredService<SafeContext>());
        services.AddScoped<ISafeSwitchService, SafeSwitchService>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<BakeryDbContext>();
        
        _dbContext.Database.EnsureCreated();

        SeedDataAsync().GetAwaiter().GetResult();
    }

    private async Task SeedDataAsync()
    {
        // Add default branch
        var branch = new Branch { Name = "Test Branch", Code = "TB", IsActive = true };
        _dbContext.Branches.Add(branch);
        await _dbContext.SaveChangesAsync();
        _branchId = branch.Id;

        // Add safes
        var safeA = new Safe { BranchId = _branchId, Name = "Safe A", Type = SafeType.Daily, IsActive = true };
        var safeB = new Safe { BranchId = _branchId, Name = "Safe B", Type = SafeType.Normal, IsActive = true };
        _dbContext.Safes.AddRange(safeA, safeB);
        await _dbContext.SaveChangesAsync();
        _safeAId = safeA.Id;
        _safeBId = safeB.Id;

        // Add a working day
        var day = new WorkingDay { BranchId = _branchId, BusinessDate = DateOnly.FromDateTime(DateTime.Today), OpeningCash = 1000, Status = WorkingDayStatus.Open };
        _dbContext.WorkingDays.Add(day);
        await _dbContext.SaveChangesAsync();
        _workingDayId = day.Id;

        // Add customer party
        var customer = new Party { Name = "Customer A", Type = PartyType.Customer, Phone = "123", Address = "Addr", NationalId = "Nat", Notes = "Notes", IsActive = true };
        _dbContext.Parties.Add(customer);
        await _dbContext.SaveChangesAsync();
        _customerId = customer.Id;

        // Add base unit
        var unit = new Unit { BranchId = _branchId, Name = "Pieces", Symbol = "Pcs", IsActive = true };
        _dbContext.Units.Add(unit);
        await _dbContext.SaveChangesAsync();
        _unitId = unit.Id;

        // Add item
        var item = new Item { BranchId = _branchId, Name = "Item A", Code = "ITEM-001", Barcode = "BAR", BaseUnitId = _unitId, PurchasePrice = 10, SalePrice = 15, MinStockLevel = 0, ReorderLevel = 0, IsActive = true };
        _dbContext.Items.Add(item);
        await _dbContext.SaveChangesAsync();
        _itemId = item.Id;
    }

    [Fact]
    public void SafeContext_InitiallyNull()
    {
        var safeContext = _serviceProvider.GetRequiredService<ISafeContext>();
        Assert.Null(safeContext.CurrentSafe);
        Assert.Null(safeContext.CurrentSafeId);
    }

    [Fact]
    public async Task SafeSwitchService_SwitchSafe_UpdatesContextAndRaisesEvent()
    {
        var safeContext = _serviceProvider.GetRequiredService<ISafeContext>();
        var switchService = _serviceProvider.GetRequiredService<ISafeSwitchService>();
        var userPermissionService = (FakeUserSafePermissionService)_serviceProvider.GetRequiredService<IUserSafePermissionService>();

        userPermissionService.HasAccess = true;

        bool eventRaised = false;
        safeContext.SafeChanged += (s, e) => { eventRaised = true; };

        var safeDto = new SafeDto(_safeAId, "Safe A", "Safe A", 1000);
        await switchService.SwitchSafeAsync(safeDto);

        Assert.True(eventRaised);
        Assert.NotNull(safeContext.CurrentSafe);
        Assert.Equal(_safeAId, safeContext.CurrentSafeId);
        Assert.Equal("Safe A", safeContext.CurrentSafe?.DisplayName);
    }

    [Fact]
    public async Task SafeSwitchService_SwitchSafe_FailsIfNoPermission()
    {
        var safeContext = _serviceProvider.GetRequiredService<ISafeContext>();
        var switchService = _serviceProvider.GetRequiredService<ISafeSwitchService>();
        var userPermissionService = (FakeUserSafePermissionService)_serviceProvider.GetRequiredService<IUserSafePermissionService>();

        userPermissionService.HasAccess = false;

        var safeDto = new SafeDto(_safeAId, "Safe A", "Safe A", 1000);
        
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => 
        {
            await switchService.SwitchSafeAsync(safeDto);
        });

        Assert.Null(safeContext.CurrentSafe);
    }

    [Fact]
    public async Task InvoiceServices_RespectExplicitSafeIdInRequest()
    {
        var saleService = _serviceProvider.GetRequiredService<ISaleInvoiceService>();
        var purchaseService = _serviceProvider.GetRequiredService<IPurchaseInvoiceService>();

        // Create Sale Draft with Safe B
        var lines = new List<InvoiceLineRequest> { new InvoiceLineRequest(_itemId, _unitId, 2, 15) };
        var saleReq = new SaveSaleInvoiceRequest(null, _customerId, PaymentType.Cash, 30, "Notes", lines, _safeBId);
        var saleRes = await saleService.SaveDraftAsync(saleReq);

        Assert.True(saleRes.Succeeded);
        var savedSale = await _dbContext.SaleInvoices.FindAsync(saleRes.InvoiceId!.Value);
        Assert.NotNull(savedSale);
        Assert.Equal(_safeBId, savedSale.SafeId);

        _dbContext.SafeMovements.Add(new SafeMovement
        {
            BranchId = _branchId,
            WorkingDayId = _workingDayId,
            SafeId = _safeAId,
            Type = SafeMovementType.OpeningBalance,
            Amount = 30m,
            Description = "Fund explicit purchase safe"
        });
        await _dbContext.SaveChangesAsync();

        // Create Purchase Draft with Safe A
        var purchaseReq = new SavePurchaseInvoiceRequest(null, _customerId, PaymentType.Cash, 30, "Notes", lines, _safeAId);
        var purchaseRes = await purchaseService.SaveDraftAsync(purchaseReq);

        Assert.True(purchaseRes.Succeeded);
        var savedPurchase = await _dbContext.PurchaseInvoices.FindAsync(purchaseRes.InvoiceId!.Value);
        Assert.NotNull(savedPurchase);
        Assert.Equal(_safeAId, savedPurchase.SafeId);
    }

    [Fact]
    public async Task InvoiceServices_Fail_IfRequestSafeIdIsNull()
    {
        var saleService = _serviceProvider.GetRequiredService<ISaleInvoiceService>();

        // Create Sale Draft with no SafeId (null)
        var lines = new List<InvoiceLineRequest> { new InvoiceLineRequest(_itemId, _unitId, 2, 15) };
        var saleReq = new SaveSaleInvoiceRequest(null, _customerId, PaymentType.Cash, 30, "Notes", lines, null);
        var saleRes = await saleService.SaveDraftAsync(saleReq);

        Assert.False(saleRes.Succeeded);
        Assert.Contains("الخزنة", saleRes.ErrorMessage);
    }

    [Fact]
    public async Task TreasurySnapshotLedgerAndReport_ShouldRemainScopedToSelectedTreasury()
    {
        _dbContext.SafeMovements.AddRange(
            new SafeMovement
            {
                BranchId = _branchId,
                WorkingDayId = _workingDayId,
                SafeId = _safeAId,
                Type = SafeMovementType.OpeningBalance,
                Amount = 100m,
                Description = "Opening A",
                TransactionNumber = "A-OPEN"
            },
            new SafeMovement
            {
                BranchId = _branchId,
                WorkingDayId = _workingDayId,
                SafeId = _safeAId,
                Type = SafeMovementType.SaleCollection,
                Amount = 50m,
                Description = "Selected sale",
                TransactionNumber = "A-SALE",
                CreatedByUserName = "Cashier A"
            },
            new SafeMovement
            {
                BranchId = _branchId,
                WorkingDayId = _workingDayId,
                SafeId = _safeAId,
                Type = SafeMovementType.ExpensePayment,
                Amount = -10m,
                Description = "Selected expense",
                TransactionNumber = "A-EXP"
            },
            new SafeMovement
            {
                BranchId = _branchId,
                WorkingDayId = _workingDayId,
                SafeId = _safeBId,
                Type = SafeMovementType.OpeningBalance,
                Amount = 500m,
                Description = "Opening B",
                TransactionNumber = "B-OPEN"
            },
            new SafeMovement
            {
                BranchId = _branchId,
                WorkingDayId = _workingDayId,
                SafeId = _safeBId,
                Type = SafeMovementType.SaleCollection,
                Amount = 80m,
                Description = "Other treasury sale",
                TransactionNumber = "B-SALE",
                CreatedByUserName = "Cashier B"
            });
        await _dbContext.SaveChangesAsync();

        var safeService = _serviceProvider.GetRequiredService<ISafeService>();

        var summary = await safeService.GetTreasurySnapshotAsync(_safeAId);
        var ledger = await safeService.GetLedgerAsync(_safeAId);
        var filtered = await safeService.GetLedgerAsync(
            _safeAId,
            movementType: SafeMovementType.SaleCollection,
            search: "Cashier A");
        var report = await safeService.GetTreasuryReportAsync(_safeAId);

        Assert.Equal(_safeAId, summary.TreasuryId);
        Assert.Equal(140m, summary.CurrentBalance);
        Assert.Equal(50m, summary.TodayReceipts);
        Assert.Equal(10m, summary.TodayPayments);
        Assert.Equal(100m, summary.OpeningBalance);
        Assert.Equal(50m, summary.TodaySales);
        Assert.All(ledger, movement => Assert.Equal(_safeAId, movement.TreasuryId));
        Assert.DoesNotContain(ledger, movement => movement.TransactionNumber == "B-SALE");
        Assert.Single(filtered);
        Assert.Equal("A-SALE", filtered[0].TransactionNumber);
        Assert.Equal(_safeAId, report.TreasuryId);
        Assert.Equal(_safeAId, report.Summary.TreasuryId);
        Assert.All(report.Movements, movement => Assert.Equal(_safeAId, movement.TreasuryId));
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
        _serviceProvider.Dispose();
        try { if (Directory.Exists(_applicationRoot)) Directory.Delete(_applicationRoot, recursive: true); } catch { }
    }

    private class FakePermissionService : IPermissionService
    {
        public bool HasAccess { get; set; } = true;

        public bool HasPermission(string key) => HasAccess;

        public bool HasAnyPermission(params string[] keys) => HasAccess;

        public void EnsurePermission(string key)
        {
            if (!HasAccess) throw new UnauthorizedAccessException();
        }

        public bool IsAdmin() => HasAccess;
    }

    private class FakeUserSessionService : IUserSessionService
    {
        public AuthenticatedUserDto? CurrentUser { get; set; } = new AuthenticatedUserDto(42, "user", "User", []);
        public bool IsAuthenticated => CurrentUser != null;
        public void SignIn(AuthenticatedUserDto user) => CurrentUser = user;
        public void SignOut() => CurrentUser = null;
        public bool HasPermission(string permissionKey) => CurrentUser?.Permissions.Contains(permissionKey) ?? false;

        public int? UserId => CurrentUser?.UserId;
        public string Username => CurrentUser?.Username ?? string.Empty;
        public string FullName => CurrentUser?.FullName ?? string.Empty;
        public IReadOnlyCollection<string> Permissions => CurrentUser?.Permissions ?? Array.Empty<string>();
        public bool IsSuperAdmin => CurrentUser?.IsSuperAdmin ?? false;
    }

    private class FakeUserSafePermissionService : IUserSafePermissionService
    {
        public bool HasAccess { get; set; } = true;
        public Task<GetUserSafePermissionsResponse> GetUserPermissionsAsync(int userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateUserPermissionsAsync(UpdateUserSafePermissionsRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CanAccessSafeAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(HasAccess);
        public Task<bool> CanViewBalanceAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(HasAccess);
        public Task<bool> CanViewLedgerAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(HasAccess);
        public Task<bool> CanCashInAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(HasAccess);
        public Task<bool> CanCashOutAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(HasAccess);
        public Task<bool> CanTransferFromAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(HasAccess);
        public Task<bool> CanReceiveTransferAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(HasAccess);
    }

    private class FakeWorkingDayService : IWorkingDayService
    {
        private readonly BakeryDbContext _db;
        public FakeWorkingDayService(BakeryDbContext db) => _db = db;

        public async Task<WorkingDay> EnsureActiveWorkingDayAsync(CancellationToken cancellationToken = default)
        {
            var day = await _db.WorkingDays.FirstOrDefaultAsync(cancellationToken);
            return day ?? throw new InvalidOperationException("No working day seeded.");
        }

        public Task<WorkingDay?> GetCurrentOpenDayAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> OpenDayAsync(OpenWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> CloseCurrentDayAsync(CloseWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> EndCurrentDayAndOpenNextAsync(CloseWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayCloseReadinessDto> GetEndOfDayReadinessAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> AutoOpenIfNeededAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> SimplifiedCloseAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDaySummaryDto?> GetCurrentDaySummaryAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayReopenEligibilityDto> GetReopenEligibilityAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DashboardTrendPointDto>> GetRecentDashboardTrendAsync(int days = 7, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<decimal> CalculateExpectedClosingCashAsync(int workingDayId, CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<(bool Match, decimal Difference, string Details)> VerifyTreasuryIntegrityAsync(int dayId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> ReopenDayAsync(int dayId, string reason, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ClosingReportDto?> GetClosingReportAsync(int dayId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class FakeDefaultCashSafeService : IDefaultCashSafeService
    {
        private readonly BakeryDbContext _db;
        public FakeDefaultCashSafeService(BakeryDbContext db) => _db = db;

        public async Task<Safe> GetDefaultCashSafeAsync(CancellationToken cancellationToken = default)
        {
            var defaultSafe = await _db.Safes.FirstOrDefaultAsync(s => s.Type == SafeType.Daily, cancellationToken);
            return defaultSafe ?? throw new InvalidOperationException("No daily safe seeded.");
        }
    }

    private class FakeSystemSafeService : ISystemSafeService
    {
        private readonly BakeryDbContext _db;
        public FakeSystemSafeService(BakeryDbContext db) => _db = db;

        public Task EnsureSystemSafesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<Safe> GetDailySafeAsync(CancellationToken cancellationToken = default)
        {
            var dailySafe = await _db.Safes.FirstOrDefaultAsync(s => s.Type == SafeType.Daily, cancellationToken);
            return dailySafe ?? throw new InvalidOperationException("No daily safe seeded.");
        }

        public async Task<Safe> GetMainSafeAsync(CancellationToken cancellationToken = default)
        {
            var mainSafe = await _db.Safes.FirstOrDefaultAsync(s => s.Type == SafeType.Main, cancellationToken);
            return mainSafe ?? throw new InvalidOperationException("No main safe seeded.");
        }

        public async Task<Safe> GetPrivateSafeAsync(CancellationToken cancellationToken = default)
        {
            var privateSafe = await _db.Safes.FirstOrDefaultAsync(s => s.Type == SafeType.Private, cancellationToken);
            return privateSafe ?? throw new InvalidOperationException("No private safe seeded.");
        }

        public async Task<Safe?> GetSafeByTypeAsync(SafeType type, CancellationToken cancellationToken = default)
        {
            return await _db.Safes.FirstOrDefaultAsync(s => s.Type == type, cancellationToken);
        }
    }

    private class FakeStockCalculationService : IStockCalculationService
    {
        public Task<decimal> GetCurrentStockAsync(int itemId, CancellationToken cancellationToken = default) => Task.FromResult(9999m);
        public Task<IReadOnlyDictionary<int, decimal>> GetCurrentStockAsync(IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<int, decimal>>(itemIds.ToDictionary(itemId => itemId, _ => 9999m));
        public Task<IReadOnlyList<StockItemDto>> GetCurrentStockAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StockItemDto>>([]);
        public Task<IReadOnlyList<StockItemDto>> GetLowStockItemsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StockItemDto>>([]);
        public Task<decimal> GetStockValuationAsync(CancellationToken cancellationToken = default) => Task.FromResult(0m);
        public Task<bool> HasAvailableStockAsync(int itemId, decimal quantity, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private class FakeAuditService : IAuditService
    {
        public Task LogAsync(string action, string entityName, int? entityId = null, string? oldValue = null, string? newValue = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeBackupService : IBackupService
    {
        public Task<string> CreateBackupAsync(string? customPath = null, string? password = null, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<string> CreateSafetySnapshotAsync(string operationName, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task RestoreBackupAsync(string backupFilePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IEnumerable<BackupMetadata>> GetBackupHistoryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<BackupMetadata>>([]);
        public Task EnforceRetentionPolicyAsync(int maxBackups = 30, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeValidationService : IValidationService
    {
        public Task<bool> IsItemCodeUsedAsync(string code, int? excludeId = null) => Task.FromResult(false);
        public Task<bool> IsBarcodeUsedAsync(string? barcode, int? excludeId = null) => Task.FromResult(false);
        public Task<bool> IsUsernameUsedAsync(string username, int? excludeId = null) => Task.FromResult(false);
        public Task<bool> IsEmployeeCodeUsedAsync(string code, int? excludeId = null) => Task.FromResult(false);
        public Task<bool> IsSafeNameUsedAsync(string name, int? excludeId = null) => Task.FromResult(false);
        public Task<bool> IsJobRoleNameUsedAsync(string name, int? excludeId = null) => Task.FromResult(false);
        public Task<bool> IsPartyNameUsedAsync(string name, int? excludeId = null) => Task.FromResult(false);
    }

    private class FakeBranchContext : IInternalBranchContext
    {
        public int? CurrentBranchId => CurrentBranch?.Id;
        public BranchDto? CurrentBranch { get; set; } = new BranchDto(1, "Test Branch", "TB", true, null);
        public void ConfigureBranch(BranchDto branch) => CurrentBranch = branch;
        public void Clear() => CurrentBranch = null;
    }
}
