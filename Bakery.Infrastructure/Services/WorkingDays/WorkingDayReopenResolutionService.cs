using System.Collections.Concurrent;
using System.Text.Json;
using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class WorkingDayReopenResolutionService : IWorkingDayReopenResolutionService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResolutionGates = new(StringComparer.Ordinal);
    private readonly BakeryDbContext _db;
    private readonly IWorkingDayService _workingDays;
    private readonly ISaleInvoiceService _sales;
    private readonly IPurchaseInvoiceService _purchases;
    private readonly ISafeService _safes;
    private readonly IPartyPaymentService _partyPayments;
    private readonly IProductionService _production;
    private readonly IPermissionService _permissions;
    private readonly IUserSessionService _userSession;
    private readonly IAuditService _audit;

    public WorkingDayReopenResolutionService(
        BakeryDbContext db,
        IWorkingDayService workingDays,
        ISaleInvoiceService sales,
        IPurchaseInvoiceService purchases,
        ISafeService safes,
        IPartyPaymentService partyPayments,
        IProductionService production,
        IPermissionService permissions,
        IUserSessionService userSession,
        IAuditService audit)
    {
        _db = db;
        _workingDays = workingDays;
        _sales = sales;
        _purchases = purchases;
        _safes = safes;
        _partyPayments = partyPayments;
        _production = production;
        _permissions = permissions;
        _userSession = userSession;
        _audit = audit;
    }

    public async Task<WorkingDayReopenBlockerResolutionResult> ResolveAsync(
        ResolveWorkingDayReopenBlockerRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissions.EnsurePermission(PermissionKeys.WorkingDayReopen);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length == 0 || !ContainsArabicLetter(reason))
            return new(false, "يجب إدخال سبب التراجع باللغة العربية.");
        if (reason.Length > 500)
            return new(false, "سبب التراجع يجب ألا يتجاوز 500 حرف.");
        if (string.IsNullOrWhiteSpace(request.BlockerCode))
            return new(false, "تعذر تحديد العملية المطلوب التراجع عنها.");

        var gateKey = request.BlockerCode;
        var gate = ResolutionGates.GetOrAdd(gateKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var eligibility = await _workingDays.GetReopenEligibilityAsync(cancellationToken);
            var blocker = eligibility.Blockers?.SingleOrDefault(item => item.Code == request.BlockerCode);
            if (blocker is null)
                return new(true, null, eligibility, WasAlreadyResolved: true);
            if (blocker.ActionKind == WorkingDayReopenActionKind.None)
                return new(false, blocker.UnsupportedMessage ?? "لا يمكن التراجع عن هذه العملية تلقائياً", eligibility);
            if (!string.IsNullOrWhiteSpace(blocker.RequiredPermission))
                _permissions.EnsurePermission(blocker.RequiredPermission);

            var operationResult = await ExecuteOfficialResolutionAsync(blocker, reason, request.CorrelationId, cancellationToken);
            if (!operationResult.Succeeded)
            {
                _db.ChangeTracker.Clear();
                var refreshedAfterFailure = await _workingDays.GetReopenEligibilityAsync(cancellationToken);
                return new(false, operationResult.ErrorMessage ?? "تعذر التراجع عن العملية.", refreshedAfterFailure);
            }

            var activeDay = eligibility.CurrentActiveDay;
            var branchId = activeDay is null
                ? 0
                : await _db.WorkingDays.IgnoreQueryFilters().AsNoTracking()
                    .Where(item => item.Id == activeDay.WorkingDayId)
                    .Select(item => item.BranchId)
                    .SingleOrDefaultAsync(cancellationToken);
            await _audit.LogAsync(
                AuditActionKeys.WorkingDayReopenBlockerResolved,
                blocker.TypeLabel,
                blocker.EntityId,
                JsonSerializer.Serialize(new
                {
                    OriginalRecordId = blocker.EntityId,
                    OriginalRecordType = blocker.Kind.ToString(),
                    OriginalRecordNumber = blocker.RecordNumber,
                    BeforeStatus = blocker.Status,
                    blocker.AmountOrQuantity,
                    blocker.AmountOrQuantityLabel,
                    blocker.EffectSummary
                }),
                JsonSerializer.Serialize(new
                {
                    ResolutionOperationId = operationResult.OperationId,
                    WorkingDayId = activeDay?.WorkingDayId,
                    WorkingDayDate = activeDay?.BusinessDate,
                    BranchId = branchId,
                    UserId = _userSession.UserId,
                    Reason = reason,
                    AfterStatus = "Resolved",
                    Timestamp = DateTime.UtcNow,
                    CorrelationId = request.CorrelationId,
                    FromWorkingDayReopenWorkflow = true
                }),
                cancellationToken);

            _db.ChangeTracker.Clear();
            var refreshed = await _workingDays.GetReopenEligibilityAsync(cancellationToken);
            return new(true, null, refreshed);
        }
        catch (UnauthorizedAccessException)
        {
            _db.ChangeTracker.Clear();
            WorkingDayReopenEligibilityDto? refreshed = null;
            try { refreshed = await _workingDays.GetReopenEligibilityAsync(cancellationToken); }
            catch { /* Preserve the authorization failure as the user-facing result. */ }
            return new(false, "ليست لديك الصلاحية المطلوبة للتراجع عن هذه العملية.", refreshed);
        }
        catch (Exception exception)
        {
            _db.ChangeTracker.Clear();
            WorkingDayReopenEligibilityDto? refreshed = null;
            try { refreshed = await _workingDays.GetReopenEligibilityAsync(cancellationToken); }
            catch { /* The original safe error remains authoritative. */ }
            return new(false, Bakery.Application.UserErrorMessages.FromException(exception), refreshed);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(bool Succeeded, string? ErrorMessage, int? OperationId)> ExecuteOfficialResolutionAsync(
        WorkingDayReopenBlockerDto blocker,
        string reason,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        switch (blocker.Kind, blocker.ActionKind)
        {
            case (WorkingDayReopenBlockerKind.SaleInvoice, WorkingDayReopenActionKind.DeleteDraft):
            {
                var result = await _sales.DeleteDraftAsync(blocker.EntityId, reason, cancellationToken);
                return (result.Succeeded, result.ErrorMessage, null);
            }
            case (WorkingDayReopenBlockerKind.SaleInvoice, WorkingDayReopenActionKind.CancelInvoice):
            {
                var result = await _sales.CancelAsync(blocker.EntityId, reason, cancellationToken);
                return (result.Succeeded, result.ErrorMessage, null);
            }
            case (WorkingDayReopenBlockerKind.PurchaseInvoice, WorkingDayReopenActionKind.DeleteDraft):
            {
                var result = await _purchases.DeleteDraftAsync(blocker.EntityId, reason, cancellationToken);
                return (result.Succeeded, result.ErrorMessage, null);
            }
            case (WorkingDayReopenBlockerKind.PurchaseInvoice, WorkingDayReopenActionKind.CancelInvoice):
            {
                var result = await _purchases.CancelAsync(blocker.EntityId, reason, cancellationToken);
                return (result.Succeeded, result.ErrorMessage, null);
            }
            case (WorkingDayReopenBlockerKind.TreasuryTransaction, WorkingDayReopenActionKind.ReverseTransaction):
            {
                var succeeded = await _safes.ReverseManualTransactionAsync(
                    new ReverseTransactionRequest(blocker.EntityId, reason), cancellationToken);
                var reversalId = await _db.SafeMovements.AsNoTracking()
                    .Where(item => item.OriginalTransactionId == blocker.EntityId)
                    .Select(item => (int?)item.Id)
                    .SingleOrDefaultAsync(cancellationToken);
                return (succeeded, succeeded ? null : "تعذر عكس حركة الخزنة.", reversalId);
            }
            case (WorkingDayReopenBlockerKind.PartyPayment, WorkingDayReopenActionKind.ReverseTransaction):
            {
                var result = await _partyPayments.ReversePaymentAsync(
                    blocker.EntityId, reason, correlationId, true, cancellationToken);
                return (result.Succeeded, result.ErrorMessage, result.ReversalMovementId);
            }
            case (WorkingDayReopenBlockerKind.ProductionOrder, WorkingDayReopenActionKind.DeleteDraft):
                await _production.DeleteProductionOrderAsync(blocker.EntityId);
                return (true, null, null);
            case (WorkingDayReopenBlockerKind.ProductionOrder, WorkingDayReopenActionKind.CancelProduction):
                await _production.CancelProductionOrderAsync(blocker.EntityId);
                return (true, null, null);
            default:
                return (false, "لا يمكن التراجع عن هذه العملية تلقائياً", null);
        }
    }

    private static bool ContainsArabicLetter(string value) => value.Any(character =>
        character is >= '\u0600' and <= '\u06FF' or >= '\u0750' and <= '\u077F' or >= '\u08A0' and <= '\u08FF');
}
