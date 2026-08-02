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

public sealed class SaleInvoiceService : ISaleInvoiceService
{
    private readonly BakeryDbContext _db;
    private readonly IWorkingDayService _days;
    private readonly ISafeService _safes;
    private readonly IStockCalculationService _stock;
    private readonly IAuditService _audit;
    private readonly ILogger<SaleInvoiceService> _logger;
    private readonly IValidator<SaveSaleInvoiceRequest> _validator;
    private readonly IPartyService _parties;
    private readonly IPermissionService _permissionService;
    private readonly IUserSafePermissionService _userSafePermissionService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IItemUnitConversionService _unitConversionService;
    private readonly IStockMutationLock _stockMutationLock;
    private readonly IInvoiceNumberAllocator _numberAllocator;

    public SaleInvoiceService(
        BakeryDbContext db, 
        IWorkingDayService days, 
        ISafeService safes, 
        IStockCalculationService stock, 
        IAuditService audit,
        ILogger<SaleInvoiceService> _logger,
        IValidator<SaveSaleInvoiceRequest> validator,
        IPartyService parties,
        IPermissionService permissionService,
        IUserSafePermissionService userSafePermissionService,
        ICurrentUserService currentUserService,
        IItemUnitConversionService unitConversionService,
        IStockMutationLock stockMutationLock,
        IInvoiceNumberAllocator numberAllocator)
    {
        _db = db;
        _days = days;
        _safes = safes;
        _stock = stock;
        _audit = audit;
        this._logger = _logger;
        _validator = validator;
        _parties = parties;
        _permissionService = permissionService;
        _userSafePermissionService = userSafePermissionService;
        _currentUserService = currentUserService;
        _unitConversionService = unitConversionService;
        _stockMutationLock = stockMutationLock;
        _numberAllocator = numberAllocator;
    }

    public async Task<IReadOnlyList<InvoiceDto>> ListAsync(InvoiceStatus? status = null, CancellationToken ct = default)
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.SalesView, PermissionKeys.ReportsSales);
        var q = _db.SaleInvoices.Include(x => x.Party).AsNoTracking();
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

    public async Task<(bool Succeeded, string? ErrorMessage, int? InvoiceId)> SaveDraftAsync(SaveSaleInvoiceRequest request, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(request.Id is null or 0 ? PermissionKeys.SalesCreate : PermissionKeys.SalesEdit);
        var v = await _validator.ValidateAsync(request, ct);
        if (!v.IsValid) return (false, v.Errors[0].ErrorMessage, null);
        try
        {
            await _unitConversionService.GetConversionsAsync(
                request.Lines.Select(line => new ItemUnitKey(line.ItemId, line.UnitId)), ct);
        }
        catch (InvalidOperationException exception)
        {
            return (false, exception.Message, null);
        }

        var total = request.Lines.Sum(x => x.Quantity * x.UnitPrice);
        if (request.PaidAmount > total) return (false, Loc.ErrPaidExceedsTotal, null);

        var currentDay = await _days.EnsureActiveWorkingDayAsync(ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            SaleInvoice inv;
            if (request.Id is null or 0)
            {
                var invoiceNumber = await _numberAllocator.AllocateSaleNumberAsync(
                    currentDay.BranchId, currentDay.BusinessDate, ct);
                // Number allocation serializes new drafts for this branch/date. Refresh the
                // working-day concurrency token while that lock is held so a preceding
                // terminal's operational save does not make this otherwise-valid draft stale.
                await _db.Entry(currentDay).ReloadAsync(ct);
                inv = new SaleInvoice
                {
                    InvoiceNumber = invoiceNumber,
                    WorkingDayId = currentDay.Id,
                    InvoiceDate = DateTime.UtcNow,
                    Status = InvoiceStatus.Draft
                };
                _db.SaleInvoices.Add(inv);
            }
            else
            {
                inv = await _db.SaleInvoices.Include(x => x.Lines).FirstAsync(x => x.Id == request.Id, ct);
                await EnsureDayOpenAsync(inv.WorkingDayId, ct);
                if (inv.Status != InvoiceStatus.Draft) return (false, Loc.ErrOnlyDraftsEditable, null);
                _db.SaleInvoiceLines.RemoveRange(inv.Lines);
            }

            inv.PartyId = request.CustomerId;
            inv.PaymentType = request.PaymentType;
            inv.TotalAmount = total;
            inv.PaidAmount = request.PaidAmount;
            inv.RemainingAmount = total - request.PaidAmount;
            inv.Notes = request.Notes;
            inv.SafeId = request.SafeId;

            inv.Lines = request.Lines.Select(x => new SaleInvoiceLine
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
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            return (false, "فشل الحفظ بسبب تعديل متزامن من مستخدم آخر. يرجى التحديث والمحاولة مرة أخرى.", null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to save sale invoice draft");
            return (false, Bakery.Application.UserErrorMessages.FromException(ex), null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> PostAsync(int invoiceId, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.SalesCreate);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var inv = await _db.SaleInvoices.Include(x => x.Lines).Include(x => x.Party).FirstAsync(x => x.Id == invoiceId, ct);
            await EnsureDayOpenAsync(inv.WorkingDayId, ct);
            
            if (!inv.Party.IsActive)
                throw new ValidationException("لا يمكن ترحيل الفاتورة لأن الطرف مرتبط بحساب موقوف. يرجى تعديل الفاتورة واختيار طرف نشط.");

            // A repeated request for an already committed post is successful and must
            // not create duplicate stock, treasury, party-ledger, or audit records.
            if (inv.Status == InvoiceStatus.Posted) return (true, null);
            if (inv.Status != InvoiceStatus.Draft) return (false, Loc.ErrOnlyDraftsEditable);
            if (!inv.Lines.Any()) return (false, Loc.ErrInvoiceEmpty);

            var conversions = await _unitConversionService.GetConversionsAsync(
                inv.Lines.Select(line => new ItemUnitKey(line.ItemId, line.UnitId)), ct);
            var itemIds = inv.Lines.Select(line => line.ItemId).Distinct().ToArray();
            await _stockMutationLock.AcquireAsync(itemIds, ct);
            var items = await _db.Items
                .Where(item => itemIds.Contains(item.Id))
                .Select(item => new { item.Id, item.Name, item.PurchasePrice })
                .ToDictionaryAsync(item => item.Id, ct);

            foreach (var line in inv.Lines)
            {
                var conversion = conversions[new ItemUnitKey(line.ItemId, line.UnitId)];
                var baseQuantity = conversion.ToBaseQuantity(line.Quantity);
                var available = await _stock.GetCurrentStockAsync(line.ItemId, ct);
                if (available < baseQuantity)
                {
                    var item = items[line.ItemId];
                    return (false,
                        $"الكمية المطلوبة من الصنف «{item.Name}» أكبر من الرصيد المتاح. المتاح بالوحدة الأساسية: {available:N2}، المطلوب: {baseQuantity:N2}.");
                }
            }

            foreach (var line in inv.Lines)
            {
                var conversion = conversions[new ItemUnitKey(line.ItemId, line.UnitId)];
                var item = items[line.ItemId];
                _db.InventoryMovements.Add(new InventoryMovement
                {
                    WorkingDayId = inv.WorkingDayId,
                    ItemId = line.ItemId,
                    UnitId = conversion.BaseUnitId,
                    Type = InventoryMovementType.Sale,
                    Quantity = -conversion.ToBaseQuantity(line.Quantity),
                    UnitCost = item.PurchasePrice,
                    ReferenceType = Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice,
                    ReferenceId = inv.Id,
                    Notes = inv.InvoiceNumber
                });
            }

            if (inv.PaidAmount > 0)
            {
                var currentUserId = _currentUserService.UserId ?? 0;
                if (!await _userSafePermissionService.CanAccessSafeAsync(currentUserId, inv.SafeId!.Value, ct))
                {
                    throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeAccess);
                }
                if (!await _userSafePermissionService.CanCashInAsync(currentUserId, inv.SafeId!.Value, ct))
                {
                    throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashIn);
                }

                await _parties.ValidateBalanceLimitAsync(inv.PartyId, inv.PaidAmount, inv.TotalAmount, ct);
            }

            // Record in Party Ledger (Full accounting movement)
            _db.PartyLedgerEntries.Add(new PartyLedgerEntry 
            { 
                WorkingDayId = inv.WorkingDayId, 
                PartyId = inv.PartyId, 
                Debit = inv.TotalAmount,
                Credit = inv.PaidAmount,
                Amount = inv.TotalAmount - inv.PaidAmount, 
                Description = $"فاتورة بيع رقم {inv.InvoiceNumber}", 
                ReferenceType = Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice, 
                ReferenceId = inv.Id 
            });

            if (inv.PaidAmount > 0)
                _db.SafeMovements.Add(new SafeMovement { WorkingDayId = inv.WorkingDayId, SafeId = inv.SafeId!.Value, Type = SafeMovementType.SaleCollection, Amount = inv.PaidAmount, Description = $"{Loc.DescSaleCash} {inv.InvoiceNumber}", ReferenceType = Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice, ReferenceId = inv.Id });

            inv.Status = InvoiceStatus.Posted;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(AuditActionKeys.SaleInvoicePosted, nameof(SaleInvoice), inv.Id, null, inv.InvoiceNumber, ct);
            await tx.CommitAsync(ct);
            return (true, null);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await tx.RollbackAsync(ct);
            _logger.LogWarning(exception, "Concurrent sale post detected for invoice {InvoiceId}", invoiceId);
            _db.ChangeTracker.Clear();
            if (await _db.SaleInvoices.AsNoTracking()
                .AnyAsync(invoice => invoice.Id == invoiceId && invoice.Status == InvoiceStatus.Posted, ct))
            {
                return (true, null);
            }
            return (false, "فشل الترحيل بسبب تعديل متزامن. يرجى التحديث.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to post sale invoice {InvoiceId}", invoiceId);
            return (false, Bakery.Application.UserErrorMessages.FromException(ex));
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> CancelAsync(int invoiceId, string reason, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.SalesCancel);
        if (string.IsNullOrWhiteSpace(reason)) return (false, Loc.ErrCancelReasonReq);
        
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var inv = await _db.SaleInvoices.Include(x => x.Lines).FirstAsync(x => x.Id == invoiceId, ct);
            await EnsureDayOpenAsync(inv.WorkingDayId, ct);

            if (inv.Status == InvoiceStatus.Cancelled) return (true, null);

            // Only reverse financial/inventory records if the invoice was Posted.
            // Draft invoices have no movements to reverse.
            if (inv.Status == InvoiceStatus.Posted)
            {
                await _stockMutationLock.AcquireAsync(inv.Lines.Select(line => line.ItemId), ct);
                // ── Reverse Inventory Movements ──
                var originalInventory = await _db.InventoryMovements
                    .Where(m => m.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice && m.ReferenceId == inv.Id && !m.IsReversed)
                    .ToListAsync(ct);

                foreach (var original in originalInventory)
                {
                    original.IsReversed = true;
                    _db.InventoryMovements.Add(new InventoryMovement
                    {
                        WorkingDayId = inv.WorkingDayId,
                        ItemId = original.ItemId,
                        UnitId = original.UnitId,
                        Type = InventoryMovementType.Adjustment,
                        Quantity = -original.Quantity, // original is negative, so this restores stock
                        UnitCost = original.UnitCost,
                        ReferenceType = Bakery.Domain.Constants.LedgerReferenceTypes.SaleCancel,
                        ReferenceId = inv.Id,
                        ReversalReferenceId = original.Id,
                        Notes = reason
                    });
                }

                // ── Reverse Party Ledger Entries ──
                var originalLedger = await _db.PartyLedgerEntries
                    .Where(e => e.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice && e.ReferenceId == inv.Id && !e.IsReversed)
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
                        Description = $"{Loc.DescCancelSale} {inv.InvoiceNumber}",
                        ReferenceType = Bakery.Domain.Constants.LedgerReferenceTypes.SaleCancel,
                        ReferenceId = inv.Id,
                        ReversalReferenceId = original.Id
                    });
                }

                // ── Reverse Safe Movements ──
                var originalSafe = await _db.SafeMovements
                    .Where(m => m.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice && m.ReferenceId == inv.Id && !m.IsReversed)
                    .ToListAsync(ct);

                foreach (var original in originalSafe)
                {
                    var currentUserId = _currentUserService.UserId ?? 0;
                    if (!await _userSafePermissionService.CanAccessSafeAsync(currentUserId, original.SafeId, ct))
                    {
                        throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeAccessCancel);
                    }
                    if (!await _userSafePermissionService.CanCashOutAsync(currentUserId, original.SafeId, ct))
                    {
                        throw new UnauthorizedAccessException(Loc.ErrUnauthorizedSafeCashOutCancel);
                    }

                    original.IsReversed = true;
                    _db.SafeMovements.Add(new SafeMovement
                    {
                        WorkingDayId = inv.WorkingDayId,
                        SafeId = original.SafeId,
                        Type = SafeMovementType.Adjustment,
                        Amount = -original.Amount,
                        Description = $"{Loc.DescCancelSaleCash} {inv.InvoiceNumber}",
                        ReferenceType = Bakery.Domain.Constants.LedgerReferenceTypes.SaleCancel,
                        ReferenceId = inv.Id,
                        ReversalReferenceId = original.Id
                    });
                }
            }

            inv.Status = InvoiceStatus.Cancelled;
            inv.CancellationReason = reason;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(AuditActionKeys.SaleInvoiceCancelled, nameof(SaleInvoice), inv.Id, inv.InvoiceNumber, reason, ct);
            await tx.CommitAsync(ct);
            return (true, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(ct);
            return (false, "فشل الإلغاء بسبب تعديل متزامن.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Failed to cancel sale invoice {InvoiceId}", invoiceId);
            return (false, Bakery.Application.UserErrorMessages.FromException(ex));
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteDraftAsync(
        int invoiceId,
        string reason,
        CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.SalesDelete);
        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length == 0 || !ContainsArabicLetter(reason))
            return (false, "يجب إدخال سبب حذف المسودة باللغة العربية.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var invoice = await _db.SaleInvoices
                .Include(item => item.Lines)
                .SingleOrDefaultAsync(item => item.Id == invoiceId, ct);
            if (invoice is null) return (true, null);
            await EnsureDayOpenAsync(invoice.WorkingDayId, ct);
            if (invoice.Status != InvoiceStatus.Draft)
                return (false, "لا يمكن حذف إلا فاتورة بيع مسودة لم يتم ترحيلها.");

            var hasEffects = await _db.InventoryMovements.AnyAsync(item =>
                    item.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice && item.ReferenceId == invoiceId, ct) ||
                await _db.SafeMovements.AnyAsync(item =>
                    item.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice && item.ReferenceId == invoiceId, ct) ||
                await _db.PartyLedgerEntries.AnyAsync(item =>
                    item.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice && item.ReferenceId == invoiceId, ct);
            if (hasEffects)
                return (false, "لا يمكن حذف المسودة لأنها تحتوي على آثار مرحّلة. يرجى مراجعة الفاتورة.");

            invoice.IsDeleted = true;
            foreach (var line in invoice.Lines) line.IsDeleted = true;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(
                AuditActionKeys.SaleInvoiceDraftDeleted,
                nameof(SaleInvoice),
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
            _logger.LogError(ex, "Failed to delete sale invoice draft {InvoiceId}", invoiceId);
            return (false, Bakery.Application.UserErrorMessages.FromException(ex));
        }
    }

    public async Task<InvoicePrintDto?> GetPrintAsync(int invoiceId, string layout, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.SalesPrint);
        var inv = await _db.SaleInvoices
            .AsNoTracking()
            .Include(x => x.Party)
            .Include(x => x.Branch)
            .Include(x => x.Lines).ThenInclude(x => x.Item)
            .Include(x => x.Lines).ThenInclude(x => x.Unit)
            .FirstOrDefaultAsync(x => x.Id == invoiceId, ct);
        return inv is null ? null : new InvoicePrintDto(
            inv.InvoiceNumber,
            inv.InvoiceDate,
            inv.Party.Name,
            inv.Lines.Select(line => new InvoicePrintLineDto(
                line.Item.Name,
                line.Quantity,
                line.UnitPrice,
                line.LineTotal,
                line.Unit.Symbol)).ToList(),
            inv.TotalAmount,
            inv.PaidAmount,
            inv.RemainingAmount,
            layout,
            Loc.AppTitle,
            inv.Branch.Name,
            inv.CreatedBy,
            "فاتورة بيع",
            inv.PaymentType,
            0m,
            inv.TaxAmount,
            Loc.ReceiptFooter);
    }

    private static bool ContainsArabicLetter(string value) => value.Any(character =>
        character is >= '\u0600' and <= '\u06FF' or >= '\u0750' and <= '\u077F' or >= '\u08A0' and <= '\u08FF');

}
