using Bakery.Application.DTOs.Accounting;

namespace Bakery.Application.Interfaces;

public interface IStatementService
{
    Task<IReadOnlyList<PartyStatementLineDto>> GetStatementAsync(int partyId, CancellationToken cancellationToken = default);
}
