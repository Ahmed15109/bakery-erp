using System.Text;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class AuditQueryService : IAuditQueryService
{
    private readonly BakeryDbContext _dbContext;
    private readonly IPermissionService _permissionService;

    public AuditQueryService(BakeryDbContext dbContext, IPermissionService permissionService)
    {
        _dbContext = dbContext;
        _permissionService = permissionService;
    }

    public async Task<IReadOnlyList<AuditLogDto>> SearchAsync(
        AuditSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.AuditView);
        var query = _dbContext.AuditLogs.AsNoTracking();
        if (request.FromUtc.HasValue) query = query.Where(log => log.OccurredAt >= request.FromUtc.Value);
        if (request.ToUtc.HasValue) query = query.Where(log => log.OccurredAt <= request.ToUtc.Value);
        if (!string.IsNullOrWhiteSpace(request.SearchText))
        {
            var search = request.SearchText.Trim();
            query = query.Where(log => log.Action.Contains(search) || log.EntityName.Contains(search) ||
                (log.User != null && (log.User.Username.Contains(search) || log.User.FullName.Contains(search))));
        }

        return await query.OrderByDescending(log => log.OccurredAt)
            .Take(Math.Clamp(request.Take, 1, 1000))
            .Select(log => new AuditLogDto(
                log.Id,
                log.OccurredAt,
                log.User == null ? "النظام" : log.User.FullName,
                log.Action,
                log.EntityName,
                log.EntityId,
                log.OldValues,
                log.NewValues,
                log.MachineName,
                log.IPAddress))
            .ToListAsync(cancellationToken);
    }

    public async Task ExportCsvAsync(
        AuditSearchRequest request,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        _permissionService.EnsurePermission(PermissionKeys.AuditExport);
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("مسار التصدير مطلوب.", nameof(destinationPath));
        var rows = await SearchAsync(request with { Take = 1000 }, cancellationToken);
        await using var writer = new StreamWriter(destinationPath, false, new UTF8Encoding(true));
        await writer.WriteLineAsync("Timestamp,User,Action,Entity,EntityId,Machine,IPAddress,OldValues,NewValues");
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = new[]
            {
                row.OccurredAt.ToString("O"), row.UserName, row.Action, row.EntityName,
                row.EntityId?.ToString(), row.MachineName, row.IPAddress, row.OldValues, row.NewValues
            };
            await writer.WriteLineAsync(string.Join(',', fields.Select(Escape)));
        }
    }

    private static string Escape(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
