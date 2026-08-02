using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Engines;
using Bakery.Infrastructure.Services;
using Bakery.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class ProductionWorkflowTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public ProductionWorkflowTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProductionWorkflow_ShouldDeductIngredientsAndAddProduct()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var productionService = scope.ServiceProvider.GetRequiredService<IProductionService>();
        var recipeService = scope.ServiceProvider.GetRequiredService<IRecipeService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        // 1. Setup Working Day
        if (await workingDayService.GetCurrentOpenDayAsync() == null)
            await workingDayService.OpenDayAsync(new OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today), 0m, "Test Production"));
        var activeDay = await workingDayService.GetCurrentOpenDayAsync();

        // 2. Setup Items
        var flour = await db.Items.FirstOrDefaultAsync(i => i.Name == "Flour") 
                    ?? (await db.Items.AddAsync(new Item { Name = "Flour", Code = "FL01", BaseUnitId = 1, PurchasePrice = 10 })).Entity;
        var bread = await db.Items.FirstOrDefaultAsync(i => i.Name == "Bread")
                    ?? (await db.Items.AddAsync(new Item { Name = "Bread", Code = "BR01", BaseUnitId = 1, PurchasePrice = 0 })).Entity;
        var flourUnit = await db.Units.SingleAsync(candidate => candidate.Id == flour.BaseUnitId);
        var breadUnit = await db.Units.SingleAsync(candidate => candidate.Id == bread.BaseUnitId);
        await db.SaveChangesAsync();

        // 3. Create Recipe (1 KG Bread requires 0.8 KG Flour)
        var recipe = new Recipe
        {
            Name = "Standard Bread",
            ProducedItemId = bread.Id,
            ProducedQuantity = 1,
            ConsumedItems = new List<RecipeItem>
            {
                new RecipeItem { RawItemId = flour.Id, Quantity = 0.8m, UnitId = flourUnit.Id }
            }
        };
        await recipeService.CreateRecipeAsync(recipe);

        // 4. Seed Stock for Flour
        db.InventoryMovements.Add(new InventoryMovement
        {
            WorkingDayId = activeDay!.Id,
            ItemId = flour.Id,
            UnitId = flourUnit.Id,
            Type = InventoryMovementType.Adjustment,
            Quantity = 10, // 10 KG Flour
            UnitCost = 10
        });
        await db.SaveChangesAsync();

        // 5. Validate Manual Items (Produce 5 KG Bread -> needs 4 KG Flour)
        var consumedItems = new List<ProductionConsumedItem>
        {
            new ProductionConsumedItem { ItemId = flour.Id, UnitId = flourUnit.Id, Quantity = 4, Item = flour, Unit = flourUnit }
        };
        var validation = await productionService.ValidateProductionItemsStockAsync(consumedItems);
        validation.IsValid.Should().BeTrue();

        // 6. Create Production Order
        var order = new ProductionOrder
        {
            ProductionNumber = "PRD-MANUAL-001",
            WorkingDayId = activeDay.Id,
            RecipeId = null,
            Status = ProductionStatus.Draft,
            ConsumedItems = new List<ProductionConsumedItem>
            {
                new ProductionConsumedItem { ItemId = flour.Id, UnitId = flourUnit.Id, Quantity = 4, UnitCost = 10 }
            },
            ProducedItems = new List<ProductionProducedItem>
            {
                new ProductionProducedItem { ItemId = bread.Id, UnitId = breadUnit.Id, ExpectedProducedQty = 5, ActualProducedQty = 5, UnitCost = 8 }
            }
        };
        var created = await productionService.CreateProductionOrderAsync(order);

        // 7. Post Production
        await productionService.PostProductionOrderAsync(created.Id);

        // 8. Verify Inventory
        var flourStock = await db.InventoryMovements.Where(m => m.ItemId == flour.Id).SumAsync(m => m.Quantity);
        var breadStock = await db.InventoryMovements.Where(m => m.ItemId == bread.Id).SumAsync(m => m.Quantity);

        flourStock.Should().Be(6);
        breadStock.Should().Be(5);
    }

    [Fact]
    public async Task Should_Fail_Posting_When_Stock_Is_Insufficient()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var productionService = scope.ServiceProvider.GetRequiredService<IProductionService>();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        // 1. Setup Data
        var flour = new Item { Name = "Flour_Fail", Code = "FL_FAIL", BaseUnitId = 1, PurchasePrice = 10 };
        var bread = new Item { Name = "Bread_Fail", Code = "BR_FAIL", BaseUnitId = 1, PurchasePrice = 1 };
        db.Items.AddRange(flour, bread);
        
        if (await workingDayService.GetCurrentOpenDayAsync() == null)
            await workingDayService.OpenDayAsync(new OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today), 0m, "Test"));
        var activeDay = await workingDayService.GetCurrentOpenDayAsync();
        await db.SaveChangesAsync();

        // 2. Create Order with 10 KG requirement (Current Stock 0)
        var order = new ProductionOrder
        {
            ProductionNumber = "PRD-FAIL-001",
            WorkingDayId = activeDay!.Id,
            Status = ProductionStatus.Draft,
            ConsumedItems = new List<ProductionConsumedItem>
            {
                new ProductionConsumedItem { ItemId = flour.Id, UnitId = 1, Quantity = 10, UnitCost = 10 }
            },
            ProducedItems = new List<ProductionProducedItem>
            {
                new ProductionProducedItem { ItemId = bread.Id, UnitId = 1, ExpectedProducedQty = 5, ActualProducedQty = 5, UnitCost = 20 }
            }
        };
        var created = await productionService.CreateProductionOrderAsync(order);

        // 3. Try Post - Should throw exception
        Func<Task> act = async () => await productionService.PostProductionOrderAsync(created.Id);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Insufficient stock*");

        // 4. Verify no inventory movements were created for this order
        var movements = await db.InventoryMovements.CountAsync(m => m.ReferenceType == "ProductionOrder" && m.ReferenceId == created.Id);
        movements.Should().Be(0);

        // 5. Verify status remains Draft
        var reloaded = await productionService.GetProductionOrderByIdAsync(created.Id);
        reloaded!.Status.Should().Be(ProductionStatus.Draft);
    }
}
