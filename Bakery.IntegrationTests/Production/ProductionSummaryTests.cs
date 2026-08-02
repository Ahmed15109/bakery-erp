using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class ProductionSummaryTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public ProductionSummaryTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Summary_FromFreshScope_ShouldAggregateAllCompletedOrderChildren()
    {
        int workingDayId;
        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            var days = setupScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
            if (await days.GetCurrentOpenDayAsync() is null)
            {
                await days.OpenDayAsync(new Bakery.Application.DTOs.OpenWorkingDayRequest(
                    DateOnly.FromDateTime(DateTime.Today), 0m, "Production summary regression"));
            }
            workingDayId = (await days.GetCurrentOpenDayAsync())!.Id;
            var flour = await db.Items.FirstAsync(item => item.Name == "Flour");
            var bread = await db.Items.FirstAsync(item => item.Name == "Bread");

            db.ProductionOrders.AddRange(
                new ProductionOrder
                {
                    ProductionNumber = $"SUM-1-{Guid.NewGuid():N}",
                    WorkingDayId = workingDayId,
                    Status = ProductionStatus.Completed,
                    ConsumedItems =
                    [
                        new ProductionConsumedItem
                        {
                            ItemId = flour.Id,
                            UnitId = flour.BaseUnitId,
                            Quantity = 2m,
                            UnitCost = 3m
                        }
                    ],
                    ProducedItems =
                    [
                        new ProductionProducedItem
                        {
                            ItemId = bread.Id,
                            UnitId = bread.BaseUnitId,
                            ExpectedProducedQty = 5m,
                            ActualProducedQty = 5m,
                            UnitCost = 4m
                        }
                    ]
                },
                new ProductionOrder
                {
                    ProductionNumber = $"SUM-2-{Guid.NewGuid():N}",
                    WorkingDayId = workingDayId,
                    Status = ProductionStatus.Completed,
                    ConsumedItems =
                    [
                        new ProductionConsumedItem
                        {
                            ItemId = flour.Id,
                            UnitId = flour.BaseUnitId,
                            Quantity = 4m,
                            UnitCost = 2m
                        }
                    ],
                    ProducedItems =
                    [
                        new ProductionProducedItem
                        {
                            ItemId = bread.Id,
                            UnitId = bread.BaseUnitId,
                            ExpectedProducedQty = 3m,
                            ActualProducedQty = 3m,
                            UnitCost = 6m
                        }
                    ]
                });
            await db.SaveChangesAsync();
        }

        // A new scope proves the result does not depend on tracked or lazy-loaded children.
        using var assertionScope = _fixture.ServiceProvider.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        assertionDb.ChangeTracker.Entries().Should().BeEmpty();
        var summary = await assertionScope.ServiceProvider
            .GetRequiredService<IProductionService>()
            .GetProductionSummaryAsync();

        summary.TodayOrdersCount.Should().Be(2);
        summary.TodayProductionCost.Should().Be(14m);
        summary.TodayProducedValue.Should().Be(38m);
    }
}
