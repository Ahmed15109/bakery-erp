namespace Bakery.Application.DTOs;

public sealed record AuditLogDto(
    int Id,
    DateTime OccurredAt,
    string UserName,
    string Action,
    string EntityName,
    int? EntityId,
    string? OldValues,
    string? NewValues,
    string? MachineName,
    string? IPAddress);

public sealed record AuditSearchRequest(
    string? SearchText = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Take = 500);
