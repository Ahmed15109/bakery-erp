using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Enums;
using System.Diagnostics;

namespace Bakery.Infrastructure.Services;

public sealed class StatementService : IStatementService
{
    private readonly IPartyLookupService _lookupService;
    private readonly IPartyStatementProvider _partyProvider;
    private readonly IEmployeeStatementProvider _employeeProvider;
    private readonly IPermissionService _permissionService;

    public StatementService(
        IPartyLookupService lookupService,
        IPartyStatementProvider partyProvider,
        IEmployeeStatementProvider employeeProvider,
        IPermissionService permissionService)
    {
        _lookupService = lookupService;
        _partyProvider = partyProvider;
        _employeeProvider = employeeProvider;
        _permissionService = permissionService;
    }

    public async Task<IReadOnlyList<PartyStatementLineDto>> GetStatementAsync(int partyId, CancellationToken cancellationToken = default)
    {
        Debug.WriteLine($"[StatementService] GetStatementAsync called with partyId={partyId}");
        var (type, employeeId) = await _lookupService.GetPartyRoutingInfoAsync(partyId, cancellationToken);
        Debug.WriteLine($"[StatementService] Routing result: type={type}, employeeId={employeeId}");

        if (type == PartyType.Employee)
        {
            _permissionService.EnsurePermission(PermissionKeys.EmployeesViewSalary);
            if (employeeId == null)
            {
                Debug.WriteLine($"[StatementService] employeeId is NULL — returning empty. Party {partyId} has no Employee record!");
                return [];
            }
            Debug.WriteLine($"[StatementService] Routing to EmployeeStatementProvider.GetStatementAsync({employeeId.Value})");
            var result = await _employeeProvider.GetStatementAsync(employeeId.Value, cancellationToken);
            Debug.WriteLine($"[StatementService] EmployeeStatementProvider returned {result.Count} rows");
            return result;
        }

        if (type == PartyType.Customer)
        {
            _permissionService.EnsurePermission(PermissionKeys.CustomersView);
        }
        else if (type == PartyType.Supplier)
        {
            _permissionService.EnsurePermission(PermissionKeys.PurchasesView);
        }
        else if (type == PartyType.Mixed)
        {
            _permissionService.EnsurePermission(PermissionKeys.CustomersView);
            _permissionService.EnsurePermission(PermissionKeys.PurchasesView);
        }

        Debug.WriteLine($"[StatementService] Routing to PartyStatementProvider.GetStatementAsync({partyId})");
        return await _partyProvider.GetStatementAsync(partyId, cancellationToken);
    }
}
