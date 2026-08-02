using Bakery.Domain.Enums;

namespace Bakery.Application.Interfaces;

public interface IPartyLookupService
{
    Task<(PartyType Type, int? EmployeeId)> GetPartyRoutingInfoAsync(int partyId, CancellationToken ct = default);
}
