using System.Data;
using System.Text.Json;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class SystemResetService : ISystemResetService
{
    private readonly BakeryDbContext _db;
    private readonly IAuditService _auditService;
    private readonly IPermissionService _permissionService;
    private readonly IBranchContext _branchContext;
    private readonly IUserSessionService _userSessionService;
    private readonly IOwnerResetCodeVerifier _ownerResetCodeVerifier;
    private readonly IBackupService _backupService;
    private readonly IBackupValidationService _backupValidationService;
    private readonly SystemResetOperationGate _operationGate;
    private readonly ISystemResetFailureInjector _failureInjector;

    public SystemResetService(
        BakeryDbContext db,
        IAuditService auditService,
        IPermissionService permissionService,
        IBranchContext branchContext,
        IUserSessionService userSessionService,
        IOwnerResetCodeVerifier ownerResetCodeVerifier,
        IBackupService backupService,
        IBackupValidationService backupValidationService,
        SystemResetOperationGate operationGate,
        ISystemResetFailureInjector failureInjector)
    {
        _db = db;
        _auditService = auditService;
        _permissionService = permissionService;
        _branchContext = branchContext;
        _userSessionService = userSessionService;
        _ownerResetCodeVerifier = ownerResetCodeVerifier;
        _backupService = backupService;
        _backupValidationService = backupValidationService;
        _operationGate = operationGate;
        _failureInjector = failureInjector;
    }

    public async Task ResetTransactionalDataAsync(
        IOwnerResetAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.SettingsResetSystem);
        if (!_permissionService.IsAdmin())
        {
            throw new UnauthorizedAccessException("إعادة ضبط النظام متاحة لمسؤول النظام الأعلى فقط.");
        }

        using var operationLease = await _operationGate.TryEnterAsync(cancellationToken)
            ?? throw new InvalidOperationException("توجد عملية إعادة ضبط للنظام قيد التنفيذ بالفعل.");

        if (!_ownerResetCodeVerifier.TryConsumeAuthorization(authorization))
        {
            throw new UnauthorizedAccessException("انتهت صلاحية رمز المالك أو سبق استخدامه.");
        }

        var branchId = _branchContext.CurrentBranchId
            ?? throw new InvalidOperationException("No branch is selected. Cannot reset data without a branch context.");

        string safetyBackupPath;
        try
        {
            safetyBackupPath = await _backupService.CreateSafetySnapshotAsync(
                "FactoryReset",
                cancellationToken);
            var validation = await _backupValidationService.ValidateAsync(safetyBackupPath, cancellationToken);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(validation.ErrorSummary ?? "فشل التحقق من النسخة الاحتياطية الآمنة.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "تعذر إنشاء نسخة احتياطية آمنة والتحقق منها. لم يتم حذف أي بيانات.",
                exception);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            // Delete in order to satisfy FK constraints — scoped to current branch only
            var param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);

            await _db.Database.ExecuteSqlRawAsync("DELETE FROM SaleInvoiceLines WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM SaleInvoices WHERE BranchId = @BranchId", param);
            
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM PurchaseInvoiceLines WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM PurchaseInvoices WHERE BranchId = @BranchId", param);
            
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM ProductionConsumedItems WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM ProductionProducedItems WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM ProductionOrderEmployees WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM ProductionOrders WHERE BranchId = @BranchId", param);

            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM RecipeItems WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Recipes WHERE BranchId = @BranchId", param);
            
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM StockCountLines WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM StockCountSessions WHERE BranchId = @BranchId", param);
            
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM InventoryMovements WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM EmployeeTransactions WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM EmployeeSettlements WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM PayrollPeriods WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM SafeMovements WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM PartyLedgerEntries WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM EmployeeWages WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Attendances WHERE BranchId = @BranchId", param);
            
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM WasteEntries WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Expenses WHERE BranchId = @BranchId", param);
            
            // Delete Employees before Parties if they are linked
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Employees WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Parties WHERE BranchId = @BranchId", param);
            
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM WorkingDays WHERE BranchId = @BranchId", param);

            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM ItemUnits WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Items WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Units WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM JobRoles WHERE BranchId = @BranchId", param);

            // Delete UserSafePermissions for safes in this branch before deleting safes
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM UserSafePermissions WHERE BranchId = @BranchId", param);
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM Safes WHERE BranchId = @BranchId", param);
            
            // We keep AuditLogs scoped to this branch
            param = new Microsoft.Data.SqlClient.SqlParameter("@BranchId", branchId);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM AuditLogs WHERE BranchId = @BranchId", param);

            await _failureInjector.BeforeCommitAsync(cancellationToken);

            var completedAtUtc = DateTime.UtcNow;
            await _auditService.LogAsync(
                AuditActionKeys.FactoryReset,
                "System",
                0,
                null,
                JsonSerializer.Serialize(new
                {
                    Operation = "FactoryReset",
                    Result = "Succeeded",
                    BranchId = branchId,
                    UserId = _userSessionService.UserId,
                    SafetyBackupFile = Path.GetFileName(safetyBackupPath),
                    Timestamp = completedAtUtc
                }),
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            _db.ChangeTracker.Clear();
        }
        catch (OperationCanceledException)
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw new InvalidOperationException(
                "فشلت إعادة ضبط النظام. تم التراجع عن العملية ولم تُترك بيانات محذوفة جزئياً.",
                exception);
        }
    }
}

public interface ISystemResetFailureInjector
{
    Task BeforeCommitAsync(CancellationToken cancellationToken);
}

public sealed class NoOpSystemResetFailureInjector : ISystemResetFailureInjector
{
    public Task BeforeCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SystemResetOperationGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IDisposable?> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) return null;
        return new Releaser(_gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
