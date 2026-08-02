namespace Bakery.Application.DTOs.Accounting;

public sealed record DuplicateValidationResult(
    bool HasDuplicates,
    IReadOnlyList<PartyDto> MatchingParties,
    string WarningMessage
);
