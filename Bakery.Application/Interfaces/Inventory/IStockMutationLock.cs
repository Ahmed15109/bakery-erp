namespace Bakery.Application.Interfaces;

/// <summary>
/// Acquires transaction-owned, database-level locks for stock ledger mutations.
/// Every movement writer must use the same ordered item locks so availability
/// checks and their corresponding writes are one serialized operation.
/// </summary>
public interface IStockMutationLock
{
    Task AcquireAsync(
        IEnumerable<int> itemIds,
        CancellationToken cancellationToken = default);
}
