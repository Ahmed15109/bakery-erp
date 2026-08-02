using Bakery.Domain.Enums;

namespace Bakery.Application.DTOs.Accounting;

public sealed record PartySearchRequest(
    string? Search = null,
    PartyType? Type = null,
    bool? IsActive = null,
    bool IncludeDeleted = false,
    int? Limit = null
);
