using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class PartyLookupService : IPartyLookupService
{
    private readonly BakeryDbContext _db;
    private readonly IPermissionService _permissionService;

    public PartyLookupService(BakeryDbContext db, IPermissionService permissionService)
    {
        _db = db;
        _permissionService = permissionService;
    }

    public async Task<(PartyType Type, int? EmployeeId)> GetPartyRoutingInfoAsync(int partyId, CancellationToken ct = default)
    {
        var party = await _db.Parties.AsNoTracking().FirstOrDefaultAsync(x => x.Id == partyId, ct);
        if (party == null) return (PartyType.Customer, null);

        if (party.Type == PartyType.Customer)
            _permissionService.EnsurePermission(PermissionKeys.CustomersView);
        else if (party.Type == PartyType.Supplier)
            _permissionService.EnsurePermission(PermissionKeys.PurchasesView);
        else if (party.Type == PartyType.Mixed)
        {
            _permissionService.EnsurePermission(PermissionKeys.CustomersView);
            _permissionService.EnsurePermission(PermissionKeys.PurchasesView);
        }
        else if (party.Type == PartyType.Employee)
            _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);

        int? employeeId = null;
        if (party.Type == PartyType.Employee)
        {
            var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.PartyId == partyId, ct);
            employeeId = employee?.Id;
        }

        return (party.Type, employeeId);
    }
}
