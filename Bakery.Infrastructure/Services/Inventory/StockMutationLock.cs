using Bakery.Application.Interfaces;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class StockMutationLock : IStockMutationLock
{
    private readonly BakeryDbContext _dbContext;
    private readonly IBranchContext _branchContext;

    public StockMutationLock(BakeryDbContext dbContext, IBranchContext branchContext)
    {
        _dbContext = dbContext;
        _branchContext = branchContext;
    }

    public async Task AcquireAsync(
        IEnumerable<int> itemIds,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A database transaction is required before locking stock items.");

        var branchId = _branchContext.CurrentBranchId
            ?? throw new InvalidOperationException("A branch must be selected before locking stock items.");

        // Stable ordering prevents two multi-item postings from deadlocking each other.
        foreach (var itemId in itemIds.Where(id => id > 0).Distinct().OrderBy(id => id))
        {
            var resource = $"BakeryERP:Stock:{branchId}:{itemId}";
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
                DECLARE @lockResult int;
                EXEC @lockResult = sys.sp_getapplock
                    @Resource = {{resource}},
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 15000;
                IF @lockResult < 0
                    THROW 51001, 'Could not acquire the stock mutation lock.', 1;
                """, cancellationToken);
        }
    }
}
