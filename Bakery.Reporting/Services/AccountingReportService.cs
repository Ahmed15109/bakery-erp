using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Enums;
using Bakery.Reporting.Interfaces;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Reporting.Services;

public sealed class AccountingReportService : IAccountingReportService
{
    private readonly BakeryDbContext _db;
    private readonly IPartyService _parties;
    private readonly IPermissionService _permissionService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUserSafePermissionService _userSafePermissionService;
    private readonly IBusinessDateService _businessDateService;
    private readonly IItemUnitConversionService _unitConversionService;

    public AccountingReportService(
        BakeryDbContext db, 
        IPartyService parties, 
        IPermissionService permissionService,
        ICurrentUserService currentUserService,
        IUserSafePermissionService userSafePermissionService,
        IBusinessDateService businessDateService,
        IItemUnitConversionService unitConversionService)
    {
        _db = db;
        _parties = parties;
        _permissionService = permissionService;
        _currentUserService = currentUserService;
        _userSafePermissionService = userSafePermissionService;
        _businessDateService = businessDateService;
        _unitConversionService = unitConversionService;
    }

    public async Task<decimal> GetDailySalesAsync(DateOnly date, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsSales);
        var day = await _businessDateService.GetAsync(date, ct);
        if (day is null) return 0m;
        return await _db.SaleInvoices
            .Where(x => x.Status == InvoiceStatus.Posted && x.WorkingDayId == day.Value.WorkingDayId)
            .SumAsync(x => x.TotalAmount, ct);
    }

    public async Task<decimal> GetDailyPurchasesAsync(DateOnly date, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsFinancial);
        var day = await _businessDateService.GetAsync(date, ct);
        if (day is null) return 0m;
        return await _db.PurchaseInvoices
            .Where(x => x.Status == InvoiceStatus.Posted && x.WorkingDayId == day.Value.WorkingDayId)
            .SumAsync(x => x.TotalAmount, ct);
    }

    public async Task<IReadOnlyList<SalesByItemDto>> GetSalesByItemAsync(DateOnly date, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsSales);
        var day = await _businessDateService.GetAsync(date, ct);
        if (day is null) return [];

        var returnedInvoiceIds = await _db.InventoryMovements
            .AsNoTracking()
            .Where(movement => movement.WorkingDayId == day.Value.WorkingDayId &&
                movement.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.SaleCancel &&
                movement.ReferenceId != null)
            .Select(movement => movement.ReferenceId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var lines = await _db.SaleInvoiceLines
            .AsNoTracking()
            .Where(line => line.SaleInvoice.WorkingDayId == day.Value.WorkingDayId &&
                (line.SaleInvoice.Status == InvoiceStatus.Posted ||
                 returnedInvoiceIds.Contains(line.SaleInvoiceId)))
            .Select(line => new
            {
                line.SaleInvoiceId,
                line.ItemId,
                line.Item.Code,
                line.Item.Name,
                BaseUnit = line.Item.BaseUnit.Symbol,
                line.UnitId,
                line.Quantity,
                line.LineTotal
            })
            .ToListAsync(ct);
        if (lines.Count == 0) return [];

        var conversions = await _unitConversionService.GetConversionsAsync(
            lines.Select(line => new ItemUnitKey(line.ItemId, line.UnitId)), ct);
        var returned = returnedInvoiceIds.ToHashSet();
        return lines
            .GroupBy(line => new { line.ItemId, line.Code, line.Name, line.BaseUnit })
            .Select(group =>
            {
                var grossQuantity = group.Sum(line => conversions[
                    new ItemUnitKey(line.ItemId, line.UnitId)].ToBaseQuantity(line.Quantity));
                var grossSales = group.Sum(line => line.LineTotal);
                var returnLines = group.Where(line => returned.Contains(line.SaleInvoiceId)).ToList();
                var returnQuantity = returnLines.Sum(line => conversions[
                    new ItemUnitKey(line.ItemId, line.UnitId)].ToBaseQuantity(line.Quantity));
                var returns = returnLines.Sum(line => line.LineTotal);
                const decimal discounts = 0m; // No discount field exists in the current invoice schema.
                return new SalesByItemDto(
                    group.Key.ItemId,
                    group.Key.Code,
                    group.Key.Name,
                    group.Key.BaseUnit,
                    grossQuantity,
                    grossSales,
                    discounts,
                    returnQuantity,
                    returns,
                    grossQuantity - returnQuantity,
                    grossSales - discounts - returns);
            })
            .OrderByDescending(item => item.NetSales)
            .ThenBy(item => item.ItemName)
            .ToList();
    }

    public async Task<IReadOnlyList<PartyDto>> GetCustomerBalancesAsync(CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsFinancial);
        
        var parties = await _db.Parties
            .Where(p => p.Type == PartyType.Customer || p.Type == PartyType.Mixed)
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var balances = await _db.PartyLedgerEntries.GroupBy(x => x.PartyId).Select(g => new { PartyId = g.Key, Balance = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.PartyId, x => x.Balance, ct);
        return parties.Select(p => new PartyDto(p.Id, p.Name, p.Type, p.Phone, p.Address, p.NationalId, p.Notes, p.IsActive, balances.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<IReadOnlyList<PartyDto>> GetSupplierBalancesAsync(CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsFinancial);
        
        var parties = await _db.Parties
            .Where(p => p.Type == PartyType.Supplier || p.Type == PartyType.Mixed)
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var balances = await _db.PartyLedgerEntries.GroupBy(x => x.PartyId).Select(g => new { PartyId = g.Key, Balance = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.PartyId, x => x.Balance, ct);
        return parties.Select(p => new PartyDto(p.Id, p.Name, p.Type, p.Phone, p.Address, p.NationalId, p.Notes, p.IsActive, balances.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<IReadOnlyList<InvoiceDto>> GetInvoiceHistoryAsync(CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsFinancial);
        
        var sales = await _db.SaleInvoices.Include(x => x.Party).AsNoTracking()
            .Select(x => new InvoiceDto(x.Id, x.InvoiceNumber, x.InvoiceDate, x.Party.Name, x.PaymentType, x.Status, x.TotalAmount, x.PaidAmount, x.RemainingAmount))
            .ToListAsync(ct);
            
        var purchases = await _db.PurchaseInvoices.Include(x => x.Party).AsNoTracking()
            .Select(x => new InvoiceDto(x.Id, x.InvoiceNumber, x.InvoiceDate, x.Party.Name, x.PaymentType, x.Status, x.TotalAmount, x.PaidAmount, x.RemainingAmount))
            .ToListAsync(ct);
            
        return sales.Concat(purchases).OrderByDescending(x => x.Date).ToList();
    }

    public async Task<decimal> GetCashMovementSummaryAsync(DateOnly date, CancellationToken ct = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.ReportsFinancial);
        var day = await _businessDateService.GetAsync(date, ct);
        if (day is null) return 0m;

        var currentUserId = _currentUserService.UserId ?? 0;
        var activeSafes = await _db.Safes.Where(x => x.IsActive && !x.IsDeleted).ToListAsync(ct);
        var allowedSafeIds = new List<int>();
        foreach (var safe in activeSafes)
        {
            if (await _userSafePermissionService.CanAccessSafeAsync(currentUserId, safe.Id, ct) &&
                await _userSafePermissionService.CanViewBalanceAsync(currentUserId, safe.Id, ct))
            {
                allowedSafeIds.Add(safe.Id);
            }
        }

        return await _db.SafeMovements
            .Where(x => x.WorkingDayId == day.Value.WorkingDayId && allowedSafeIds.Contains(x.SafeId))
            .SumAsync(x => x.Amount, ct);
    }
}
