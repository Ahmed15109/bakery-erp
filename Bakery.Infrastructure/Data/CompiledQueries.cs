using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Data;

public static class CompiledQueries
{
    public static readonly Func<BakeryDbContext, int, IAsyncEnumerable<InventoryMovement>> GetMovementsByWorkingDay =
        EF.CompileAsyncQuery((BakeryDbContext context, int workingDayId) =>
            context.Set<InventoryMovement>()
                .AsNoTracking()
                .Where(m => m.WorkingDayId == workingDayId));

    public static readonly Func<BakeryDbContext, string, IAsyncEnumerable<Item>> GetItemsByType =
        EF.CompileAsyncQuery((BakeryDbContext context, string type) =>
            context.Set<Item>()
                .AsNoTracking()
                .Where(i => EF.Property<string>(i, "Type") == type && !i.IsDeleted));
}
