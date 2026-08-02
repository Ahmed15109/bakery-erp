using Bakery.Application.DTOs.Accounting;

namespace Bakery.Application.Interfaces;

public interface IPartyStatementProvider
{
    Task<IReadOnlyList<PartyStatementLineDto>> GetStatementAsync(int partyId, CancellationToken cancellationToken = default);
}
