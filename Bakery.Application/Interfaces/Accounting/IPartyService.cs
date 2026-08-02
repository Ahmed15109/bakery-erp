using Bakery.Application.DTOs.Accounting;
using Bakery.Domain.Enums;

namespace Bakery.Application.Interfaces;

public interface IPartyService
{
    Task<IReadOnlyList<PartyDto>> SearchAsync(PartySearchRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PartyDto>> LookupAsync(PartySearchRequest request, CancellationToken cancellationToken = default);
    Task<DuplicateValidationResult> CheckNameDuplicatesAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage, PartyDto? Party)> SaveAsync(SavePartyRequest request, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<decimal> GetBalanceAsync(int partyId, CancellationToken cancellationToken = default);
    Task<PartySummaryDto> GetPartySummaryAsync(int partyId, CancellationToken cancellationToken = default);
    Task ValidateBalanceLimitAsync(int partyId, decimal reductionAmount, decimal invoiceTotal = 0, CancellationToken cancellationToken = default);
    Task<PartyStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);
}
