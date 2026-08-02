using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class PartyStatementProvider : IPartyStatementProvider
{
    private readonly BakeryDbContext _db;
    private readonly IPermissionService _permissionService;

    public PartyStatementProvider(BakeryDbContext db, IPermissionService permissionService)
    {
        _db = db;
        _permissionService = permissionService;
    }

    public async Task<IReadOnlyList<PartyStatementLineDto>> GetStatementAsync(int partyId, CancellationToken ct = default)
    {
        var party = await _db.Parties.AsNoTracking().FirstOrDefaultAsync(x => x.Id == partyId, ct);
        if (party == null) return new List<PartyStatementLineDto>();
        EnsurePartyPermission(party.Type);

        var entries = await _db.PartyLedgerEntries
            .Where(x => x.PartyId == partyId)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        decimal balance = 0;
        return entries.Select(x =>
        {
            balance += x.Amount;
            
            string description = x.ReferenceType switch
            {
                Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice => $"بيع آجل فاتورة #{x.ReferenceId:D4}",
                Bakery.Domain.Constants.LedgerReferenceTypes.SaleCancel => $"مرتجع بيع فاتورة #{x.ReferenceId:D4}",
                Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice => $"شراء خامات فاتورة #{x.ReferenceId:D4}",
                Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseCancel => $"مرتجع شراء فاتورة #{x.ReferenceId:D4}",
                "Payment" => party.Type == PartyType.Customer ? "دفعة من العميل" : "دفعة للمورد",
                _ => x.Description
            };

            if (description.Contains("Payment") || (description.Contains("سداد") && !description.Contains("فاتورة")))
            {
                description = party.Type == PartyType.Customer ? "دفعة من العميل" : "دفعة للمورد";
            }

            // Fallback for legacy records (when both Debit and Credit are exactly 0 but Amount is not)
            bool isLegacy = x.Debit == 0 && x.Credit == 0;
            decimal dbt = isLegacy ? (x.Amount > 0 ? x.Amount : 0) : x.Debit;
            decimal crd = isLegacy ? (x.Amount < 0 ? -x.Amount : 0) : x.Credit;

            bool isCustomerStyle = party.Type == PartyType.Customer 
                || (party.Type == PartyType.Mixed && (x.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.SaleInvoice || x.ReferenceType == "CustomerReceipt" || x.ReferenceType == Bakery.Domain.Constants.LedgerReferenceTypes.SaleCancel));
                
            decimal increase = isCustomerStyle ? dbt : crd;
            decimal decrease = isCustomerStyle ? crd : dbt;
            
            // For invoices, remaining is the gap on that specific transaction
            // If it's a legacy record, we only have the net amount, so remaining = net
            decimal remaining = (x.Debit != 0 || x.Credit != 0) 
                ? Math.Max(0, Math.Abs(x.Debit - x.Credit))
                : Math.Abs(x.Amount);

            return new PartyStatementLineDto(
                x.CreatedAt,
                description,
                increase,
                decrease,
                remaining,
                balance,
                x.ReferenceType,
                x.ReferenceId
            );
        }).ToList();
    }

    private void EnsurePartyPermission(PartyType type)
    {
        if (type == PartyType.Customer)
            _permissionService.EnsurePermission(PermissionKeys.CustomersView);
        else if (type == PartyType.Supplier)
            _permissionService.EnsurePermission(PermissionKeys.PurchasesView);
        else if (type == PartyType.Mixed)
        {
            _permissionService.EnsurePermission(PermissionKeys.CustomersView);
            _permissionService.EnsurePermission(PermissionKeys.PurchasesView);
        }
        else if (type == PartyType.Employee)
            _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
    }
}
