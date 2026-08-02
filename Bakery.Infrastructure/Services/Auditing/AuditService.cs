using System.Net;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class AuditService : IAuditService
{
    private static readonly Task<string?> LocalIpAddressTask = Task.Run(ResolveLocalIpAddress);
    private readonly BakeryDbContext _dbContext;
    private readonly IUserSessionService _userSessionService;
    private readonly IBranchContext _branchContext;

    public AuditService(
        BakeryDbContext dbContext,
        IUserSessionService userSessionService,
        IBranchContext branchContext)
    {
        _dbContext = dbContext;
        _userSessionService = userSessionService;
        _branchContext = branchContext;
    }

    public async Task LogAsync(string action, string entityName, int? entityId = null, string? oldValue = null, string? newValue = null, CancellationToken cancellationToken = default)
    {
        if (!AuditActionKeys.IsKnown(action))
            throw new ArgumentException("Audit actions must use AuditActionKeys.", nameof(action));

        var branchId = await ResolveBranchIdAsync(cancellationToken);
        if (branchId == 0)
        {
            // Auditing must not make bootstrap or a pre-branch authentication attempt fail.
            return;
        }

        // Session and branch contexts are established from validated database records.
        // Re-querying both records for every audit event added two avoidable round trips
        // to successful login and every authenticated action.
        var userId = _userSessionService.CurrentUser?.UserId;

        _dbContext.AuditLogs.Add(new AuditLog
        {
            BranchId = branchId,
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValues = oldValue,
            NewValues = newValue,
            MachineName = Environment.MachineName,
            // Host-name resolution can block for seconds on a disconnected or misconfigured
            // network. Audit writes never wait for it; later writes use the cached result.
            IPAddress = LocalIpAddressTask.IsCompletedSuccessfully ? LocalIpAddressTask.Result : null,
            OccurredAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> ResolveBranchIdAsync(CancellationToken cancellationToken)
    {
        var currentBranchId = _branchContext.CurrentBranchId;
        if (currentBranchId is > 0)
        {
            return currentBranchId.Value;
        }

        return await _dbContext.Branches
            .IgnoreQueryFilters()
            .OrderBy(branch => branch.Id)
            .Select(branch => branch.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? ResolveLocalIpAddress()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                ?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
