using Bakery.Application.Interfaces;
using Bakery.Infrastructure.Data;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakery.Infrastructure.Services;

public sealed class IntegrityCheckService : IIntegrityCheckService
{
    private readonly BakeryDbContext _db;
    private readonly ILogger<IntegrityCheckService> _logger;

    public IntegrityCheckService(BakeryDbContext db, ILogger<IntegrityCheckService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> RunFullCheckAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Full ERP Integrity Health Check...");
        bool isHealthy = true;

        // 1. Working Day Validation
        var openDays = await _db.WorkingDays.Where(d => d.Status == WorkingDayStatus.Open).ToListAsync(cancellationToken);
        if (openDays.Count > 1)
        {
            _logger.LogCritical("CRITICAL: Multiple open working days detected: {Ids}", string.Join(", ", openDays.Select(d => d.Id)));
            isHealthy = false;
        }

        // 2. Orphaned Reversals
        var orphanLedgers = await _db.PartyLedgerEntries
            .Where(x => x.ReversalReferenceId != null && !_db.PartyLedgerEntries.Any(p => p.Id == x.ReversalReferenceId))
            .CountAsync(cancellationToken);

        if (orphanLedgers > 0)
        {
            _logger.LogWarning("Integrity Check Failed: Found {Count} orphan PartyLedgerEntry reversals.", orphanLedgers);
            isHealthy = false;
        }

        var orphanMovements = await _db.InventoryMovements
            .Where(x => x.ReversalReferenceId != null && !_db.InventoryMovements.Any(p => p.Id == x.ReversalReferenceId))
            .CountAsync(cancellationToken);

        if (orphanMovements > 0)
        {
            _logger.LogWarning("Integrity Check Failed: Found {Count} orphan InventoryMovement reversals.", orphanMovements);
            isHealthy = false;
        }

        // 3. Treasury Consistency
        var safes = await _db.Safes.ToListAsync(cancellationToken);
        foreach (var safe in safes)
        {
            var movementsSum = await _db.SafeMovements
                .Where(m => m.SafeId == safe.Id)
                .SumAsync(m => (decimal?)m.Amount, cancellationToken) ?? 0;
                
            // Currently Safes don't have a 'CurrentBalance' property to compare against, 
            // but if they did, we would check it here. 
            // For now, we check for 'null' working days on posted movements.
            var orphanSafeMovements = await _db.SafeMovements
                .Where(m => m.WorkingDayId == 0)
                .CountAsync(cancellationToken);
                
            if (orphanSafeMovements > 0)
            {
                _logger.LogError("Found {Count} SafeMovements without an associated WorkingDay.", orphanSafeMovements);
                isHealthy = false;
            }
        }

        // 4. Invoice Consistency
        var orphanSaleLines = await _db.SaleInvoiceLines
            .Where(l => !_db.SaleInvoices.Any(i => i.Id == l.SaleInvoiceId))
            .CountAsync(cancellationToken);
            
        if (orphanSaleLines > 0)
        {
            _logger.LogError("Found {Count} orphaned SaleInvoiceLines.", orphanSaleLines);
            isHealthy = false;
        }

        if (isHealthy)
            _logger.LogInformation("Integrity Check Passed: ERP state is consistent.");
        else
            _logger.LogWarning("Integrity Check completed with errors. See logs for details.");

        return isHealthy;
    }
}
