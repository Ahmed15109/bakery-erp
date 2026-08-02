using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Bakery.Shared.Helpers;
using Microsoft.Extensions.Logging;

namespace Bakery.Infrastructure.Services;

public sealed class PurchaseInvoiceService : IPurchaseInvoiceService
{
    private readonly BakeryDbContext _db;
    private readonly IWorkingDayService _days;
    private readonly ISafeService _safes;
    private readonly IAuditService _audit;
    private readonly IValidator<SavePurchaseInvoiceRequest> _validator;
    private readonly IPartyService _parties;
    private readonly IPermissionService _permissionService;
    private readonly IUserSafePermissionService _userSafePermissionService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IItemUnitConversionService _unitConversionService;
    private readonly IStockMutationLock _stockMutationLock;
    private readonly IInvoiceNumberAllocator _numberAllocator;
    private readonly ILogger<PurchaseInvoiceService> _logger;
    private readonly IStockCalculationService? _stockCalculationService;

    public PurchaseInvoiceService(
        BakeryDbContext db,
        IWorkingDayService days,
        ISafeService safes,
        IAuditService audit,
        IValidator<SavePurchaseInvoiceRequest> validator,
        IPartyService parties,
        IPermissionService permissionService,
        IUserSafePermissionService userSafePermissionService,
        ICurrentUserService currentUserService,
        IItemUnitConversionService unitConversionService,
        IStockMutationLock stockMutationLock,
        IInvoiceNumberAllocator numberAllocator,
        ILogger<PurchaseInvoiceService> logger,
        IStockCalculationService? stockCalculationService = null)
    {
        _db = db;
        _days = days;
        _safes = safes;
        _audit = audit;
        _validator = validator;
        _parties = parties;
        _permissionService = permissionService;
        _userSafePermissionService = userSafePermissionService;
        _currentUserService = currentUserService;
        _unitConversionService = unitConversionService;
        _stockMutationLock = stockMutationLock;
        _numberAllocator = numberAllocator;
        _logger = logger;
        _stockCalculationService = stockCalculationService;
    }

    public async Task<IReadOnlyList<InvoiceDto>> ListAsync(InvoiceStatus? status = null, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.PurchasesView);
        var q = _db.PurchaseInvoices.Include(x => x.Party).AsNoTracking();
        if (status.HasValue) q = q.Where(x => x.Status == status.Value);
        return await q.OrderByDescending(x => x.InvoiceDate).Select(x => new InvoiceDto(x.Id, x.InvoiceNumber, x.InvoiceDate, x.Party.Name, x.PaymentType, x.Status, x.TotalAmount, x.PaidAmount, x.RemainingAmount)).ToListAsync(ct);
    }

    private async Task EnsureDayOpenAsync(int? dayId, CancellationToken ct)
    {
        if (dayId == null) await _days.EnsureActiveWorkingDayAsync(ct);
        else
        {
            var day = await _db.WorkingDays.FindAsync(new object[] { dayId.Value }, ct);
            if (day == null || day.Status == WorkingDayStatus.Closed)
                throw new InvalidOperationException("لا يمكن تعديل عمليات مرتبطة بيوم عمل مغلق.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? InvoiceId)> SaveDraftAsync(SavePurchaseInvoiceRequest request, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(request.Id is null or 0 ? PermissionKeys.PurchasesCreate : PermissionKeys.PurchasesEdit);
        var v = await _validator.ValidateAsync(request, ct);
        if (!v.IsValid) return (false, v.Errors[0].ErrorMessage, null);
        var total = request.Lines.Sum(x => x.Quantity * x.UnitPrice);
        if (request.PaidAmount > total) return (false, Loc.ErrPaidExceedsTotal, null);

        if (request.PaidAmount > 0)
        {
            if (!request.SafeId.HasValue)
                return (false, "الخزنة مطلوبة للعملية المالية.", null);

            // This check must remain before the purchase transaction and number allocation.
            // Posting validates again to protect against another operation spending the balance
            // after this preflight check.
            var currentSafeBalance = await _db.SafeMovements
                .AsNoTracking()
                .Where(movement => movement.SafeId == request.SafeId.Value)
                .SumAsync(movement => (decimal?)movement.Amount, ct) ?? 0m;
            if (request.PaidAmount > currentSafeBalance)
            {
                return (false, BuildInsufficientSafeBalanceMessage(currentSafeBalance, request.PaidAmount), null);
            }
        }

        try
        {
            await _unitConversionService.GetConversionsAsync(
                request.Lines.Select(line => new ItemUnitKey(line.ItemId, line.UnitId)), ct);
        }
        catch (InvalidOperationException exception)
        {
            return (false, exception.Message, null);
        }

        var currentDay = await _days.EnsureActiveWorkingDayAsync(ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            PurchaseInvoice inv;
            if (request.Id is null or 0)
            {
                var invoiceNumber = await _numberAllocator.AllocatePurchaseNumberAsync(
                    currentDay.BranchId, currentDay.BusinessDate, ct);
                // Number allocation serializes new drafts for this branch/date. Refresh the
                // working-day concurrency token while that lock is held so a preceding
                // terminal's operational save does not make this otherwise-valid draft stale.
                await _db.Entry(currentDay).ReloadAsync(ct);
                inv = new PurchaseInvoice
                {
                    InvoiceNumber = invoiceNumber,
                    WorkingDayId = currentDay.Id,
                    InvoiceDate = DateTime.UtcNow,
                    Status = InvoiceStatus.Draft
                };
                _db.PurchaseInvoices.Add(inv);
            }
            else
            {
                inv = await _db.PurchaseInvoices.Include(x => x.Lines).FirstAsync(x => x.Id == request.Id, ct);
                await EnsureDayOpenAsync(inv.WorkingDayId, ct);
                if (inv.Status != InvoiceStatus.Draft) return (false, Loc.ErrOnlyDraftsEditable, null);
                _db.PurchaseInvoiceLines.RemoveRange(inv.Lines);
            }

            inv.PartyId = request.SupplierId;
            inv.PaymentType = request.PaymentType;
            inv.TotalAmount = total;
            inv.PaidAmount = request.PaidAmount;
            inv.RemainingAmount = total - request.PaidAmount;
            inv.Notes = request.Notes;
            inv.SafeId = request.SafeId;

            inv.Lines = request.Lines.Select(x => new PurchaseInvoiceLine
            {
                ItemId = x.ItemId,
                UnitId = x.UnitId,
                Quantity = x.Quantity,
                UnitPrice = x.UnitPrice,
                LineTotal = x.Quantity * x.UnitPrice
            }).ToList();

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (true, null, inv.Id);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to save purchase invoice draft");
            return (false, Bakery.Application.UserErrorMessages.FromException(ex), null);
        }
    }

    private static string BuildInsufficientSafeBalanceMessage(decimal currentBalance, decimal paidAmount)
        => $"لا يمكن سداد هذا المبلغ.{Environment.NewLine}" +
           $"رصيد الخزنة الحالية هو:{Environment.NewLine}{currentBalance:N2}{Environment.NewLine}{Environment.NewLine}" +
           $"والمبلغ المطلوب دفعه هو:{Environment.NewLine}{paidAmount:N2}{Environment.NewLine}{Environment.NewLine}" +
           "يرجى تخفيض المبلغ المدفوع أو اختيار خزنة أخرى.";

    public async Task<(bool Succeeded, string? ErrorMessage)> PostAsync(int invoiceId, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.PurchasesCreate);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var inv = await _db.PurchaseInvoices.Include(x => x.Lines).Include(x => x.Party).FirstAsync(x => x.Id == invoiceId, ct);
            await EnsureDayOpenAsync(inv.WorkingDayId, ct);
            
            if (!inv.Party.IsActive)
                throw new ValidationException("لا يمكن ترحيل الفاتورة لأن الطرف مرتبط بحساب موقوف. يرجى تعديل الفاتورة واختيار طرف نشط.");

            // 1. ALL VALIDATIONS MUST RUN FIRST BEFORE ANY DB MUTATION
            if (inv.Status != InvoiceStatus.Draft) return (false, Loc.ErrOnlyDraftsEditable);
            if (!inv.Lines.Any()) return (false, Loc.ErrInvoiceEmpty);

            var conversions = await _unitConversionService.GetConversionsAsync(
                inv.Lines.Select(line => new ItemUnitKey(line.ItemId, line.UnitId)), ct);
            await _stockMutationLock.AcquireAsync(inv.Lines.Select(line => line.ItemId), ct);

            if (inv.PaidAmount > 0)
            {
                var currentUserId = _currentUserService.UserId ?? 0;
                if (!await _userSafePermissionService.CanAccessSafeAsync(currentUserId, inv.SafeId!.Value, ct))
                {
                    throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeAccess);
                }
                if (!await _userSafePermissionService.CanCashOutAsync(currentUserId, inv.SafeId!.Value, ct))
                {
                    throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashOut);
                }

                await _parties.ValidateBalanceLimitAsync(inv.PartyId, inv.PaidAmount, inv.TotalAmount, ct);
                await _safes.ValidateSufficientBalanceAsync(inv.SafeId!.Value, inv.PaidAmount, ct);
            }

            // 2. IN-MEMORY DB CONTEXT MUTATIONS
            foreach (var line in inv.Lines)
            {
                var conversion = conversions[new ItemUnitKey(line.ItemId, line.UnitId)];
                _db.InventoryMovements.Add(new InventoryMovement
                {
                    WorkingDayId = inv.WorkingDayId,
                    ItemId = line.ItemId,
                    UnitId = conversion.BaseUnitId,
                    Type = InventoryMovementType.Purchase,
                    Quantity = conversion.ToBaseQuantity(line.Quantity),
                    UnitCost = conversion.ToBaseUnitCost(line.UnitPrice),
                    ReferenceType = Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice,
                    ReferenceId = inv.Id,
                    Notes = inv.InvoiceNumber
                });
            }

            // Record in Party Ledger (Full accounting movement)
            _db.PartyLedgerEntries.Add(new PartyLedgerEntry 
            { 
                WorkingDayId = inv.WorkingDayId, 
                PartyId = inv.PartyId, 
                Debit = inv.PaidAmount,
                Credit = inv.TotalAmount,
                Amount = inv.TotalAmount - inv.PaidAmount, 
                Description = $"فاتورة شراء رقم {inv.InvoiceNumber}", 
                ReferenceType = Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice, 
                ReferenceId = inv.Id 
            });

            if (inv.PaidAmount > 0)
            {
                _db.SafeMovements.Add(new SafeMovement { WorkingDayId = inv.WorkingDayId, SafeId = inv.SafeId!.Value, Type = SafeMovementType.PurchasePayment, Amount = -inv.PaidAmount, Description = $"{Loc.DescPurchaseCash} {inv.InvoiceNumber}", ReferenceType = Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice, ReferenceId = inv.Id });
            }

            inv.Status = InvoiceStatus.Posted;
            
            // 3. EXECUTE SINGLE ATOMIC SAVE
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(AuditActionKeys.PurchaseInvoicePosted, nameof(PurchaseInvoice), inv.Id, null, inv.InvoiceNumber, ct);
            await tx.CommitAsync(ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Failed to post purchase invoice {InvoiceId}", invoiceId);
            return (false, Bakery.Application.UserErrorMessages.FromException(ex));
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> CancelAsync(int invoiceId, string reason, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.PurchasesCancel);
        if (string.IsNullOrWhiteSpace(reason)) return (false, Loc.ErrCancelReasonReq);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var inv = await _db.PurchaseInvoices.Include(x => x.Lines).FirstAsync(x => x.Id == invoiceId, ct);
            await EnsureDayOpenAsync(inv.WorkingDayId, ct);

            if (inv.Status == InvoiceStatus.Cancelled) return (true, null);

            // Only reverse financial/inventory records if the invoice was Posted.
            // Draft invoices have no movements to reverse.
            if (inv.Status == InvoiceStatus.Posted)
            {
                await _stockMutationLock.AcquireAsync(inv.Lines.Select(line => line.ItemId), ct);
                // ── Reverse Inventory Movements ──
                var originalInventory = await _db.InventoryMovements
                    .Where(m => m.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice && m.ReferenceId == inv.Id && !m.IsReversed)
                    .ToListAsync(ct);

                if (_stockCalculationService is not null)
                {
                    foreach (var group in originalInventory.GroupBy(movement => movement.ItemId))
                    {
                        var purchasedQuantity = group.Sum(movement => movement.Quantity);
                        var available = await _stockCalculationService.GetCurrentStockAsync(group.Key, ct);
                        if (available < purchasedQuantity)
                        {
                            return (false,
                                $"لا يمكن إلغاء فاتورة المشتريات لأن الكمية المتاحة للصنف رقم {group.Key} هي {available:N3} بينما يلزم {purchasedQuantity:N3}. راجع الحركات التابعة أولاً.");
                        }
                    }
                }

                foreach (var original in originalInventory)
                {
                    original.IsReversed = true;
                    _db.InventoryMovements.Add(new InventoryMovement
                    {
                        WorkingDayId = inv.WorkingDayId,
                        ItemId = original.ItemId,
                        UnitId = original.UnitId,
                        Type = InventoryMovementType.Adjustment,
                        Quantity = -original.Quantity, // original is positive, so this deducts stock
                        UnitCost = original.UnitCost,
                        ReferenceType = Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseCancel,
                        ReferenceId = inv.Id,
                        ReversalReferenceId = original.Id,
                        Notes = reason
                    });
                }

                // ── Reverse Party Ledger Entries ──
                var originalLedger = await _db.PartyLedgerEntries
                    .Where(e => e.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice && e.ReferenceId == inv.Id && !e.IsReversed)
                    .ToListAsync(ct);

                foreach (var original in originalLedger)
                {
                    original.IsReversed = true;
                    _db.PartyLedgerEntries.Add(new PartyLedgerEntry
                    {
                        WorkingDayId = inv.WorkingDayId,
                        PartyId = inv.PartyId,
                        Debit = original.Credit,
                        Credit = original.Debit,
                        Amount = -original.Amount,
                        Description = $"{Loc.DescCancelPurchase} {inv.InvoiceNumber}",
                        ReferenceType = Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseCancel,
                        ReferenceId = inv.Id,
                        ReversalReferenceId = original.Id
                    });
                }

                // ── Reverse Safe Movements ──
                var originalSafe = await _db.SafeMovements
                    .Where(m => m.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice && m.ReferenceId == inv.Id && !m.IsReversed)
                    .ToListAsync(ct);

                foreach (var original in originalSafe)
                {
                    var currentUserId = _currentUserService.UserId ?? 0;
                    if (!await _userSafePermissionService.CanAccessSafeAsync(currentUserId, original.SafeId, ct))
                    {
                        throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeAccessCancel);
                    }
                    if (!await _userSafePermissionService.CanCashInAsync(currentUserId, original.SafeId, ct))
                    {
                        throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashInCancel);
                    }

                    original.IsReversed = true;
                    _db.SafeMovements.Add(new SafeMovement
                    {
                        WorkingDayId = inv.WorkingDayId,
                        SafeId = original.SafeId,
                        Type = SafeMovementType.Adjustment,
                        Amount = -original.Amount,
                        Description = $"{Loc.DescCancelPurchaseCash} {inv.InvoiceNumber}",
                        ReferenceType = Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseCancel,
                        ReferenceId = inv.Id,
                        ReversalReferenceId = original.Id
                    });
                }
            }

            inv.Status = InvoiceStatus.Cancelled;
            inv.CancellationReason = reason;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(AuditActionKeys.PurchaseInvoiceCancelled, nameof(PurchaseInvoice), inv.Id, inv.InvoiceNumber, reason, ct);
            await tx.CommitAsync(ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to cancel purchase invoice {InvoiceId}", invoiceId);
            return (false, Bakery.Application.UserErrorMessages.FromException(ex));
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteDraftAsync(
        int invoiceId,
        string reason,
        CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.PurchasesDelete);
        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length == 0 || !ContainsArabicLetter(reason))
            return (false, "يجب إدخال سبب حذف المسودة باللغة العربية.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var invoice = await _db.PurchaseInvoices
                .Include(item => item.Lines)
                .SingleOrDefaultAsync(item => item.Id == invoiceId, ct);
            if (invoice is null) return (true, null);
            await EnsureDayOpenAsync(invoice.WorkingDayId, ct);
            if (invoice.Status != InvoiceStatus.Draft)
                return (false, "لا يمكن حذف إلا فاتورة مشتريات مسودة لم يتم ترحيلها.");

            var hasEffects = await _db.InventoryMovements.AnyAsync(item =>
                    item.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice && item.ReferenceId == invoiceId, ct) ||
                await _db.SafeMovements.AnyAsync(item =>
                    item.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice && item.ReferenceId == invoiceId, ct) ||
                await _db.PartyLedgerEntries.AnyAsync(item =>
                    item.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice && item.ReferenceId == invoiceId, ct);
            if (hasEffects)
                return (false, "لا يمكن حذف المسودة لأنها تحتوي على آثار مرحّلة. يرجى مراجعة الفاتورة.");

            invoice.IsDeleted = true;
            foreach (var line in invoice.Lines) line.IsDeleted = true;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(
                AuditActionKeys.PurchaseInvoiceDraftDeleted,
                nameof(PurchaseInvoice),
                invoice.Id,
                invoice.InvoiceNumber,
                reason,
                ct);
            await tx.CommitAsync(ct);
            return (true, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            return (false, "تم تعديل مسودة الفاتورة من جهاز آخر. يرجى التحديث.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to delete purchase invoice draft {InvoiceId}", invoiceId);
            return (false, Bakery.Application.UserErrorMessages.FromException(ex));
        }
    }

    public async Task<InvoicePrintDto?> GetPrintAsync(int invoiceId, string layout, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.PurchasesPrint);
        var invoice = await _db.PurchaseInvoices.AsNoTracking()
            .Include(item => item.Party)
            .Include(item => item.Branch)
            .Include(item => item.Lines).ThenInclude(line => line.Item)
            .Include(item => item.Lines).ThenInclude(line => line.Unit)
            .SingleOrDefaultAsync(item => item.Id == invoiceId, ct);
        return invoice is null ? null : new InvoicePrintDto(
            invoice.InvoiceNumber,
            invoice.InvoiceDate,
            invoice.Party.Name,
            invoice.Lines.Select(line => new InvoicePrintLineDto(
                line.Item.Name,
                line.Quantity,
                line.UnitPrice,
                line.LineTotal,
                line.Unit.Symbol)).ToArray(),
            invoice.TotalAmount,
            invoice.PaidAmount,
            invoice.RemainingAmount,
            layout,
            Loc.AppTitle,
            invoice.Branch.Name,
            invoice.CreatedBy,
            "فاتورة شراء",
            invoice.PaymentType,
            0m,
            invoice.TaxAmount,
            Loc.ReceiptFooter);
    }

    private static bool ContainsArabicLetter(string value) => value.Any(character =>
        character is >= '\u0600' and <= '\u06FF' or >= '\u0750' and <= '\u077F' or >= '\u08A0' and <= '\u08FF');

}
