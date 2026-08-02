using Bakery.Application.Interfaces;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Services;

public sealed class ItemUnitConversionService : IItemUnitConversionService
{
    private readonly BakeryDbContext _dbContext;

    public ItemUnitConversionService(BakeryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ItemUnitConversion> GetConversionAsync(
        int itemId,
        int unitId,
        CancellationToken cancellationToken = default)
    {
        var conversions = await GetConversionsAsync(
            [new ItemUnitKey(itemId, unitId)], cancellationToken);
        return conversions[new ItemUnitKey(itemId, unitId)];
    }

    public async Task<IReadOnlyDictionary<ItemUnitKey, ItemUnitConversion>> GetConversionsAsync(
        IEnumerable<ItemUnitKey> keys,
        CancellationToken cancellationToken = default)
    {
        var requested = keys.Distinct().ToArray();
        if (requested.Length == 0)
            return new Dictionary<ItemUnitKey, ItemUnitConversion>();

        var itemIds = requested.Select(key => key.ItemId).Distinct().ToArray();
        var unitIds = requested.Select(key => key.UnitId).Distinct().ToArray();
        var items = await _dbContext.Items
            .Where(item => itemIds.Contains(item.Id))
            .Select(item => new { item.Id, item.BaseUnitId, item.Name })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var itemUnits = await _dbContext.ItemUnits
            .Where(itemUnit => itemIds.Contains(itemUnit.ItemId) && unitIds.Contains(itemUnit.UnitId))
            .Select(itemUnit => new
            {
                itemUnit.ItemId,
                itemUnit.UnitId,
                itemUnit.ConversionFactorToBaseUnit
            })
            .ToListAsync(cancellationToken);
        var factors = itemUnits.ToDictionary(
            itemUnit => new ItemUnitKey(itemUnit.ItemId, itemUnit.UnitId),
            itemUnit => itemUnit.ConversionFactorToBaseUnit);

        var result = new Dictionary<ItemUnitKey, ItemUnitConversion>(requested.Length);
        foreach (var key in requested)
        {
            if (!items.TryGetValue(key.ItemId, out var item))
                throw new InvalidOperationException("الصنف المحدد غير موجود أو غير متاح في الفرع الحالي.");

            decimal factor;
            if (key.UnitId == item.BaseUnitId)
            {
                factor = 1m;
            }
            else if (!factors.TryGetValue(key, out factor))
            {
                throw new InvalidOperationException($"الوحدة المحددة غير مرتبطة بالصنف «{item.Name}».");
            }

            if (factor <= 0)
                throw new InvalidOperationException($"معامل تحويل وحدة الصنف «{item.Name}» غير صالح.");

            result[key] = new ItemUnitConversion(
                key.ItemId,
                key.UnitId,
                item.BaseUnitId,
                factor);
        }

        return result;
    }
}
