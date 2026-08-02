namespace Bakery.Application.Interfaces;

public readonly record struct ItemUnitKey(int ItemId, int UnitId);

public readonly record struct ItemUnitConversion(
    int ItemId,
    int RequestedUnitId,
    int BaseUnitId,
    decimal FactorToBaseUnit)
{
    public decimal ToBaseQuantity(decimal quantity) => quantity * FactorToBaseUnit;

    public decimal ToBaseUnitCost(decimal requestedUnitCost)
        => requestedUnitCost / FactorToBaseUnit;
}

public interface IItemUnitConversionService
{
    Task<ItemUnitConversion> GetConversionAsync(
        int itemId,
        int unitId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<ItemUnitKey, ItemUnitConversion>> GetConversionsAsync(
        IEnumerable<ItemUnitKey> keys,
        CancellationToken cancellationToken = default);
}
