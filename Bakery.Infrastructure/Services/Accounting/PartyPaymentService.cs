using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Bakery.Shared.Helpers;
using System;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Bakery.Infrastructure.Services;

public sealed class PartyPaymentService : IPartyPaymentService
{
    private readonly BakeryDbContext _db;
    private readonly IWorkingDayService _days;
    private readonly IPartyService _parties;
    private readonly ISafeService _safes;
    private readonly IAuditService _audit;
    private readonly IPermissionService _permissionService;
    private readonly IUserSafePermissionService _userSafePermissionService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<PartyPaymentService> _logger;

    public PartyPaymentService(
        BakeryDbContext db,
        IWorkingDayService days,
        IPartyService parties,
        ISafeService safes,
        IAuditService audit,
        IPermissionService permissionService,
        IUserSafePermissionService userSafePermissionService,
        ICurrentUserService currentUserService,
        ILogger<PartyPaymentService> logger)
    {
        _db = db;
        _days = days;
        _parties = parties;
        _safes = safes;
        _audit = audit;
        _permissionService = permissionService;
        _userSafePermissionService = userSafePermissionService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> ProcessPaymentAsync(
        int partyId, 
        int safeId, 
        decimal amount, 
        string description, 
        bool? isReceipt = null,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.TreasuryCashIn, PermissionKeys.TreasuryCashOut);
        var operationKey = NormalizeIdempotencyKey(idempotencyKey);
        if (amount <= 0) return (false, "المبلغ يجب أن يكون أكبر من صفر");

        var currentUserId = _currentUserService.UserId ?? 0;
        if (!await _userSafePermissionService.CanAccessSafeAsync(currentUserId, safeId, ct))
        {
            throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeAccess);
        }

        var currentDay = await _days.EnsureActiveWorkingDayAsync(ct);

        var party = await _db.Parties.FindAsync(new object[] { partyId }, ct);
        if (party == null || !party.IsActive) return (false, "الطرف غير موجود أو غير نشط");

        var safe = await _db.Safes.FindAsync(new object[] { safeId }, ct);
        if (safe == null || !safe.IsActive) return (false, "الخزنة غير موجودة أو غير نشطة");

        bool actualIsReceipt = isReceipt ?? (party.Type != PartyType.Supplier);
        _permissionService.EnsurePermission(actualIsReceipt ? PermissionKeys.TreasuryCashIn : PermissionKeys.TreasuryCashOut);

        if (actualIsReceipt)
        {
            if (!await _userSafePermissionService.CanCashInAsync(currentUserId, safeId, ct))
            {
                throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashIn);
            }
        }
        else
        {
            if (!await _userSafePermissionService.CanCashOutAsync(currentUserId, safeId, ct))
            {
                throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashOut);
            }
        }

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (operationKey is not null)
        {
            var existing = await _db.SafeMovements.AsNoTracking()
                .SingleOrDefaultAsync(item => item.IdempotencyKey == operationKey, ct);
            if (existing is not null)
            {
                var expectedType = actualIsReceipt
                    ? SafeMovementType.SaleCollection
                    : SafeMovementType.PurchasePayment;
                var expectedReferenceType = actualIsReceipt
                    ? Bakery.Domain.Constants.LedgerReferenceTypes.CustomerReceipt
                    : Bakery.Domain.Constants.LedgerReferenceTypes.SupplierPayment;
                var expectedAmount = actualIsReceipt ? amount : -amount;
                if (existing.SafeId != safeId || existing.Type != expectedType ||
                    existing.Amount != expectedAmount || existing.ReferenceId != partyId ||
                    existing.ReferenceType != expectedReferenceType)
                {
                    return (false, "مفتاح العملية مستخدم لعملية مالية مختلفة.");
                }

                await tx.CommitAsync(ct);
                return (true, null);
            }
        }
        var currentBalance = await _parties.GetBalanceAsync(partyId, ct);
        // Only enforce balance limit for non-Mixed parties
        if (party.Type != PartyType.Mixed && amount > currentBalance) return (false, "المبلغ المدفوع يتجاوز الرصيد المستحق");

        if (!actualIsReceipt)
        {
            await _safes.ValidateSufficientBalanceAsync(safeId, amount, ct);
        }

        try
        {
            SafeMovementType safeMovementType;
            string refType;
            string defaultDesc;
            SafeMovement safeMovement;
            PartyLedgerEntry ledgerEntry;

            if (!actualIsReceipt)
            {
                safeMovementType = SafeMovementType.PurchasePayment;
                refType = Bakery.Domain.Constants.LedgerReferenceTypes.SupplierPayment;
                defaultDesc = $"سداد للمورد {party.Name}";

                safeMovement = new SafeMovement
                {
                    WorkingDayId = currentDay.Id,
                    SafeId = safeId,
                    Type = safeMovementType,
                    Amount = -amount,
                    Description = !string.IsNullOrWhiteSpace(description) ? description : defaultDesc,
                    ReferenceType = refType,
                    ReferenceId = partyId,
                    IdempotencyKey = operationKey
                };
                _db.SafeMovements.Add(safeMovement);

                ledgerEntry = new PartyLedgerEntry
                {
                    WorkingDayId = currentDay.Id,
                    PartyId = partyId,
                    Debit = amount,
                    Credit = 0,
                    Amount = -amount,
                    Description = !string.IsNullOrWhiteSpace(description) ? description : defaultDesc,
                    ReferenceType = refType,
                    ReferenceId = partyId
                };
                _db.PartyLedgerEntries.Add(ledgerEntry);
            }
            else
            {
                safeMovementType = SafeMovementType.SaleCollection;
                refType = Bakery.Domain.Constants.LedgerReferenceTypes.CustomerReceipt;
                defaultDesc = $"استلام من العميل {party.Name}";

                safeMovement = new SafeMovement
                {
                    WorkingDayId = currentDay.Id,
                    SafeId = safeId,
                    Type = safeMovementType,
                    Amount = amount,
                    Description = !string.IsNullOrWhiteSpace(description) ? description : defaultDesc,
                    ReferenceType = refType,
                    ReferenceId = partyId,
                    IdempotencyKey = operationKey
                };
                _db.SafeMovements.Add(safeMovement);

                ledgerEntry = new PartyLedgerEntry
                {
                    WorkingDayId = currentDay.Id,
                    PartyId = partyId,
                    Debit = 0,
                    Credit = amount,
                    Amount = -amount,
                    Description = !string.IsNullOrWhiteSpace(description) ? description : defaultDesc,
                    ReferenceType = refType,
                    ReferenceId = partyId
                };
                _db.PartyLedgerEntries.Add(ledgerEntry);
            }

            await _db.SaveChangesAsync(ct);
            ledgerEntry.SourceSafeMovementId = safeMovement.Id;
            await _db.SaveChangesAsync(ct);
            string auditInfo = $"Amount: {amount:N2}, SafeId: {safeId}, Desc: {description}";
            await _audit.LogAsync(AuditActionKeys.PartyPaymentProcessed, "PartyLedger", partyId, null, auditInfo, ct);
            await tx.CommitAsync(ct);
            return (true, null);
        }
        catch (ValidationException ex)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Failed to process party payment for PartyId={PartyId}", partyId);
            return (false, Bakery.Application.UserErrorMessages.FromException(ex));
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? ReversalMovementId)> ReversePaymentAsync(
        int originalSafeMovementId,
        string reason,
        Guid correlationId,
        bool fromWorkingDayReopenWorkflow,
        CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.TreasuryReversePartyPayment);
        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length == 0 || !ContainsArabicLetter(reason))
            return (false, "يجب إدخال سبب التراجع باللغة العربية.", null);
        if (reason.Length > 500)
            return (false, "سبب التراجع يجب ألا يتجاوز 500 حرف.", null);

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var original = await _db.SafeMovements
                .Include(item => item.Safe)
                .SingleOrDefaultAsync(item => item.Id == originalSafeMovementId, ct);
            if (original is null)
                return (false, "حركة الدفعة الأصلية غير موجودة.", null);

            if (original.ReferenceType is not (Bakery.Domain.Constants.LedgerReferenceTypes.CustomerReceipt or
                Bakery.Domain.Constants.LedgerReferenceTypes.SupplierPayment))
                return (false, "الحركة المحددة ليست دفعة عميل أو مورد.", null);

            if (original.IsReversed || original.ReversedBy is not null)
            {
                var existingReversalId = original.ReverseTransactionId ?? await _db.SafeMovements
                    .Where(item => item.OriginalTransactionId == original.Id)
                    .Select(item => (int?)item.Id)
                    .SingleOrDefaultAsync(ct);
                await tx.CommitAsync(ct);
                return (true, null, existingReversalId);
            }

            var ledger = await _db.PartyLedgerEntries
                .SingleOrDefaultAsync(item => item.SourceSafeMovementId == original.Id, ct);
            if (ledger is null)
                return (false, "لا يمكن التراجع عن هذه الدفعة تلقائياً لأن رابط القيد الأصلي غير متاح.", null);

            var currentDay = await _days.EnsureActiveWorkingDayAsync(ct);
            if (currentDay.Id != original.WorkingDayId)
                return (false, "لا يمكن عكس دفعة مرتبطة بيوم عمل غير اليوم المفتوح.", null);

            var currentUserId = _currentUserService.UserId ?? 0;
            if (!await _userSafePermissionService.CanAccessSafeAsync(currentUserId, original.SafeId, ct))
                throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeAccess);

            if (original.Amount > 0)
            {
                if (!await _userSafePermissionService.CanCashOutAsync(currentUserId, original.SafeId, ct))
                    throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashOut);
                await _safes.ValidateSufficientBalanceAsync(original.SafeId, original.Amount, ct);
            }
            else if (!await _userSafePermissionService.CanCashInAsync(currentUserId, original.SafeId, ct))
            {
                throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashIn);
            }

            var username = _currentUserService.Username;
            var now = DateTime.UtcNow;
            original.IsReversed = true;
            original.ReversedAt = now;
            original.ReversedBy = username;
            original.ReverseReason = reason;
            ledger.IsReversed = true;

            var reversalMovement = new SafeMovement
            {
                WorkingDayId = original.WorkingDayId,
                SafeId = original.SafeId,
                Type = original.Type,
                Amount = -original.Amount,
                Description = $"عكس دفعة رقم {original.TransactionNumber ?? original.Id.ToString()} - {reason}",
                ReferenceType = original.ReferenceType,
                ReferenceId = original.ReferenceId,
                ReversalReferenceId = original.Id,
                OriginalTransactionId = original.Id,
                Origin = CashMovementOrigin.Reverse,
                Notes = $"CorrelationId: {correlationId}",
                CreatedByUserId = currentUserId,
                CreatedByUserName = username
            };
            _db.SafeMovements.Add(reversalMovement);

            var reversalLedger = new PartyLedgerEntry
            {
                WorkingDayId = ledger.WorkingDayId,
                PartyId = ledger.PartyId,
                EntryDate = now,
                Amount = -ledger.Amount,
                Debit = ledger.Credit,
                Credit = ledger.Debit,
                Description = $"عكس دفعة - {reason}",
                ReferenceType = ledger.ReferenceType,
                ReferenceId = ledger.ReferenceId,
                ReversalReferenceId = ledger.Id
            };
            _db.PartyLedgerEntries.Add(reversalLedger);

            await _db.SaveChangesAsync(ct);
            original.ReverseTransactionId = reversalMovement.Id;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(
                AuditActionKeys.PartyPaymentReversed,
                nameof(SafeMovement),
                original.Id,
                JsonSerializer.Serialize(new
                {
                    OriginalSafeMovementId = original.Id,
                    OriginalPartyLedgerEntryId = ledger.Id,
                    original.WorkingDayId,
                    original.BranchId,
                    Amount = original.Amount,
                    BeforeStatus = "Active"
                }),
                JsonSerializer.Serialize(new
                {
                    ReversalSafeMovementId = reversalMovement.Id,
                    ReversalPartyLedgerEntryId = reversalLedger.Id,
                    AfterStatus = "Reversed",
                    Reason = reason,
                    CorrelationId = correlationId,
                    FromWorkingDayReopenWorkflow = fromWorkingDayReopenWorkflow,
                    Timestamp = now,
                    UserId = currentUserId
                }),
                ct);
            await tx.CommitAsync(ct);
            return (true, null, reversalMovement.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            return (false, "تم تعديل الدفعة من جهاز آخر. يرجى التحديث.", null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Failed to reverse party payment {MovementId}", originalSafeMovementId);
            return (false, Bakery.Application.UserErrorMessages.FromException(ex), null);
        }
    }

    private static string? NormalizeIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return null;
        var normalized = idempotencyKey.Trim();
        return normalized.Length <= 100 ? normalized : throw new ValidationException("مفتاح العملية غير صالح.");
    }

    private static bool ContainsArabicLetter(string value) => value.Any(character =>
        character is >= '\u0600' and <= '\u06FF' or >= '\u0750' and <= '\u077F' or >= '\u08A0' and <= '\u08FF');
}
