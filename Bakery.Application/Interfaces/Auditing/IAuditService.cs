namespace Bakery.Application.Interfaces;

public interface IAuditService
{
    Task LogAsync(string action, string entityName, int? entityId = null, string? oldValue = null, string? newValue = null, CancellationToken cancellationToken = default);
}
