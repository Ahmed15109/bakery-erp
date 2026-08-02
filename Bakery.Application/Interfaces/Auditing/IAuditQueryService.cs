using Bakery.Application.DTOs;

namespace Bakery.Application.Interfaces;

public interface IAuditQueryService
{
    Task<IReadOnlyList<AuditLogDto>> SearchAsync(AuditSearchRequest request, CancellationToken cancellationToken = default);
    Task ExportCsvAsync(AuditSearchRequest request, string destinationPath, CancellationToken cancellationToken = default);
}
