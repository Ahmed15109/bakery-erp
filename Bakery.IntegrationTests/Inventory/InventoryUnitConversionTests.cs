using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.DTOs.Inventory;
using Bakery.Application.DTOs.Waste;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class InventoryUnitConversionTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public InventoryUnitConversionTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OperationalWorkflows_ShouldUseBaseQuantities_AndRejectUnrelatedUnits()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var days = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var conversions = scope.ServiceProvider.GetRequiredService<IItemUnitConversionService>();
        var stock = scope.ServiceProvider.GetRequiredService<IStockCalculationService>();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var units = scope.ServiceProvider.GetRequiredService<IUnitService>();
        var waste = scope.ServiceProvider.GetRequiredService<IWasteService>();
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseInvoiceService>();
        var sales = scope.ServiceProvider.GetRequiredService<ISaleInvoiceService>();
        var recipes = scope.ServiceProvider.GetRequiredService<IRecipeService>();
        var production = scope.ServiceProvider.GetRequiredService<IProductionService>();

        if (await days.GetCurrentOpenDayAsync() is null)
        {
            await days.OpenDayAsync(new OpenWorkingDayRequest(
                DateOnly.FromDateTime(DateTime.Today), 0m, "Inventory unit conversion regression"));
        }
        var day = (await days.GetCurrentOpenDayAsync())!;

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var each = new Unit { Name = $"Each {suffix}", Symbol = $"ea{suffix}" };
        var caseUnit = new Unit { Name = $"Case {suffix}", Symbol = $"cs{suffix}" };
        var unrelated = new Unit { Name = $"Unrelated {suffix}", Symbol = $"xx{suffix}" };
        var pack = new Unit { Name = $"Pack {suffix}", Symbol = $"pk{suffix}" };
        db.Units.AddRange(each, caseUnit, unrelated, pack);
        await db.SaveChangesAsync();

        var ingredient = new Item
        {
            Code = $"UNIT-RAW-{suffix}",
            Name = $"Unit raw {suffix}",
            Type = ItemType.RawMaterial,
            BaseUnitId = each.Id,
            PurchasePrice = 10m,
            SalePrice = 20m
        };
        var product = new Item
        {
            Code = $"UNIT-FG-{suffix}",
            Name = $"Unit product {suffix}",
            Type = ItemType.FinishedProduct,
            BaseUnitId = each.Id,
            PurchasePrice = 4m,
            SalePrice = 8m
        };
        db.Items.AddRange(ingredient, product);
        await db.SaveChangesAsync();

        db.ItemUnits.AddRange(
            new ItemUnit
            {
                ItemId = ingredient.Id,
                UnitId = caseUnit.Id,
                ConversionFactorToBaseUnit = 12m,
                IsDefaultPurchaseUnit = true,
                IsDefaultSaleUnit = true
            },
            new ItemUnit
            {
                ItemId = product.Id,
                UnitId = pack.Id,
                ConversionFactorToBaseUnit = 5m
            });
        await db.SaveChangesAsync();

        var baseConversion = await conversions.GetConversionAsync(ingredient.Id, each.Id);
        baseConversion.FactorToBaseUnit.Should().Be(1m);
        var caseConversion = await conversions.GetConversionAsync(ingredient.Id, caseUnit.Id);
        caseConversion.ToBaseQuantity(2m).Should().Be(24m);
        caseConversion.ToBaseUnitCost(120m).Should().Be(10m);
        await FluentActions.Invoking(() => conversions.GetConversionAsync(ingredient.Id, unrelated.Id))
            .Should().ThrowAsync<InvalidOperationException>();

        var allowedUnits = await units.GetItemUnitsAsync(ingredient.Id);
        allowedUnits.Select(unit => unit.UnitId).Should().BeEquivalentTo([each.Id, caseUnit.Id]);
        allowedUnits.Should().NotContain(unit => unit.UnitId == unrelated.Id);

        // A legacy, non-base movement must still be interpreted correctly without rewriting history.
        var legacyMovement = new InventoryMovement
        {
            WorkingDayId = day.Id,
            ItemId = ingredient.Id,
            UnitId = caseUnit.Id,
            Type = InventoryMovementType.OpeningBalance,
            Quantity = 2m,
            UnitCost = 120m,
            Notes = "legacy non-base quantity"
        };
        db.InventoryMovements.Add(legacyMovement);
        await db.SaveChangesAsync();
        (await stock.GetCurrentStockAsync(ingredient.Id)).Should().Be(24m);

        var history = await inventory.GetMovementHistoryAsync(null, null, ingredient.Id, null);
        var normalizedLegacy = history.Single(movement => movement.Id == legacyMovement.Id);
        normalizedLegacy.Quantity.Should().Be(24m);
        normalizedLegacy.UnitCost.Should().Be(10m);
        normalizedLegacy.Unit.Should().Be(each.Symbol);

        var adjustment = await inventory.AdjustStockAsync(new InventoryAdjustmentRequest(
            ingredient.Id, caseUnit.Id, 0.5m, true, "half case"));
        adjustment.Succeeded.Should().BeTrue(adjustment.ErrorMessage);
        await AssertLatestBaseMovementAsync(db, ingredient.Id, InventoryMovementType.Adjustment, each.Id, 6m);

        var movementCount = await db.InventoryMovements.CountAsync(movement => movement.ItemId == ingredient.Id);
        var rejectedAdjustment = await inventory.AdjustStockAsync(new InventoryAdjustmentRequest(
            ingredient.Id, unrelated.Id, 1m, true, "invalid unit"));
        rejectedAdjustment.Succeeded.Should().BeFalse();
        (await db.InventoryMovements.CountAsync(movement => movement.ItemId == ingredient.Id)).Should().Be(movementCount);

        // The client-supplied system quantity is ignored; physical quantity is converted to base units.
        var countSessionId = await inventory.StartStockCountAsync(new StartStockCountRequest("case count"));
        var countResult = await inventory.CompleteStockCountAsync(new CompleteStockCountRequest(
            countSessionId,
            [new StockCountLineDto(ingredient.Id, ingredient.Code, ingredient.Name, caseUnit.Id, caseUnit.Symbol, 999m, 3m, 0m)]));
        countResult.Succeeded.Should().BeTrue(countResult.ErrorMessage);
        var countLine = await db.StockCountLines.AsNoTracking().SingleAsync(line => line.StockCountSessionId == countSessionId);
        countLine.UnitId.Should().Be(each.Id);
        countLine.SystemQuantity.Should().Be(30m);
        countLine.PhysicalQuantity.Should().Be(36m);
        countLine.VarianceQuantity.Should().Be(6m);

        var wasteResult = await waste.SaveAsync(new SaveWasteEntryRequest(
            ingredient.Id, caseUnit.Id, 0.5m, 120m, "test waste", null));
        wasteResult.Succeeded.Should().BeTrue(wasteResult.ErrorMessage);
        var wasteEntry = await db.WasteEntries.AsNoTracking().OrderByDescending(entry => entry.Id).FirstAsync();
        wasteEntry.UnitId.Should().Be(each.Id);
        wasteEntry.Quantity.Should().Be(6m);
        wasteEntry.UnitCost.Should().Be(10m);
        wasteEntry.WasteCost.Should().Be(60m);
        await AssertLatestBaseMovementAsync(db, ingredient.Id, InventoryMovementType.Waste, each.Id, -6m);
        var wasteHistory = await waste.GetEntriesAsync(null, null, null, null, ingredient.Id);
        wasteHistory.Single(entry => entry.Id == wasteEntry.Id).StockAfter.Should().Be(30m);

        var rejectedWaste = await waste.SaveAsync(new SaveWasteEntryRequest(
            ingredient.Id, unrelated.Id, 1m, 10m, "invalid", null));
        rejectedWaste.Succeeded.Should().BeFalse();

        var supplier = await db.Parties.FirstAsync(party => party.Type == PartyType.Supplier);
        var customer = await db.Parties.FirstAsync(party => party.Type == PartyType.Customer);

        var rejectedPurchase = await purchases.SaveDraftAsync(new SavePurchaseInvoiceRequest(
            null, supplier.Id, PaymentType.Credit, 0m, null,
            [new InvoiceLineRequest(ingredient.Id, unrelated.Id, 1m, 10m)], null));
        rejectedPurchase.Succeeded.Should().BeFalse();

        var purchase = await purchases.SaveDraftAsync(new SavePurchaseInvoiceRequest(
            null, supplier.Id, PaymentType.Credit, 0m, "one case",
            [new InvoiceLineRequest(ingredient.Id, caseUnit.Id, 1m, 120m)], null));
        purchase.Succeeded.Should().BeTrue(purchase.ErrorMessage);
        var purchasePost = await purchases.PostAsync(purchase.InvoiceId!.Value);
        purchasePost.Succeeded.Should().BeTrue(purchasePost.ErrorMessage);
        var purchaseMovement = await db.InventoryMovements.AsNoTracking()
            .SingleAsync(movement => movement.ReferenceType == "PurchaseInvoice" && movement.ReferenceId == purchase.InvoiceId);
        purchaseMovement.UnitId.Should().Be(each.Id);
        purchaseMovement.Quantity.Should().Be(12m);
        purchaseMovement.UnitCost.Should().Be(10m);
        var purchaseCancel = await purchases.CancelAsync(purchase.InvoiceId.Value, "unit conversion return");
        purchaseCancel.Succeeded.Should().BeTrue(purchaseCancel.ErrorMessage);
        var purchaseReversal = await db.InventoryMovements.AsNoTracking()
            .SingleAsync(movement => movement.ReversalReferenceId == purchaseMovement.Id);
        purchaseReversal.UnitId.Should().Be(each.Id);
        purchaseReversal.Quantity.Should().Be(-12m);

        var rejectedSale = await sales.SaveDraftAsync(new SaveSaleInvoiceRequest(
            null, customer.Id, PaymentType.Credit, 0m, null,
            [new InvoiceLineRequest(ingredient.Id, unrelated.Id, 1m, 20m)], null));
        rejectedSale.Succeeded.Should().BeFalse();

        var sale = await sales.SaveDraftAsync(new SaveSaleInvoiceRequest(
            null, customer.Id, PaymentType.Credit, 0m, "half case",
            [new InvoiceLineRequest(ingredient.Id, caseUnit.Id, 0.5m, 120m)], null));
        sale.Succeeded.Should().BeTrue(sale.ErrorMessage);
        var salePost = await sales.PostAsync(sale.InvoiceId!.Value);
        salePost.Succeeded.Should().BeTrue(salePost.ErrorMessage);
        var saleMovement = await db.InventoryMovements.AsNoTracking()
            .SingleAsync(movement => movement.ReferenceType == "SaleInvoice" && movement.ReferenceId == sale.InvoiceId);
        saleMovement.UnitId.Should().Be(each.Id);
        saleMovement.Quantity.Should().Be(-6m);
        saleMovement.UnitCost.Should().Be(10m);
        var saleCancel = await sales.CancelAsync(sale.InvoiceId.Value, "unit conversion return");
        saleCancel.Succeeded.Should().BeTrue(saleCancel.ErrorMessage);
        var saleReversal = await db.InventoryMovements.AsNoTracking()
            .SingleAsync(movement => movement.ReversalReferenceId == saleMovement.Id);
        saleReversal.UnitId.Should().Be(each.Id);
        saleReversal.Quantity.Should().Be(6m);

        var recipe = await recipes.CreateRecipeAsync(new Recipe
        {
            Name = $"Unit recipe {suffix}",
            ProducedItemId = product.Id,
            ProducedQuantity = 5m,
            ConsumedItems =
            [
                new RecipeItem { RawItemId = ingredient.Id, UnitId = caseUnit.Id, Quantity = 0.5m }
            ]
        });
        var savedRecipeItem = await db.Set<RecipeItem>().AsNoTracking().SingleAsync(item => item.RecipeId == recipe.Id);
        savedRecipeItem.UnitId.Should().Be(each.Id);
        savedRecipeItem.Quantity.Should().Be(6m);

        var order = await production.CreateProductionOrderAsync(new ProductionOrder
        {
            ProductionNumber = $"PRD-UNIT-{suffix}",
            WorkingDayId = day.Id,
            Status = ProductionStatus.Draft,
            ConsumedItems =
            [
                new ProductionConsumedItem
                {
                    ItemId = ingredient.Id,
                    UnitId = caseUnit.Id,
                    Quantity = 0.5m,
                    UnitCost = 120m
                }
            ],
            ProducedItems =
            [
                new ProductionProducedItem
                {
                    ItemId = product.Id,
                    UnitId = pack.Id,
                    ExpectedProducedQty = 2m,
                    ActualProducedQty = 2m,
                    UnitCost = 20m
                }
            ]
        });
        order.ConsumedItems.Single().UnitId.Should().Be(each.Id);
        order.ConsumedItems.Single().Quantity.Should().Be(6m);
        order.ConsumedItems.Single().UnitCost.Should().Be(10m);
        order.ProducedItems.Single().UnitId.Should().Be(each.Id);
        order.ProducedItems.Single().ActualProducedQty.Should().Be(10m);
        order.ProducedItems.Single().UnitCost.Should().Be(4m);

        await production.PostProductionOrderAsync(order.Id);
        var productionMovements = await db.InventoryMovements.AsNoTracking()
            .Where(movement => movement.ReferenceType == "ProductionOrder" && movement.ReferenceId == order.Id)
            .OrderBy(movement => movement.Type)
            .ToListAsync();
        productionMovements.Should().HaveCount(2);
        productionMovements.Should().OnlyContain(movement => movement.UnitId == each.Id);
        productionMovements.Single(movement => movement.ItemId == ingredient.Id).Quantity.Should().Be(-6m);
        productionMovements.Single(movement => movement.ItemId == product.Id).Quantity.Should().Be(10m);

        await production.CancelProductionOrderAsync(order.Id);
        var productionReversals = await db.InventoryMovements.AsNoTracking()
            .Where(movement => movement.ReferenceType == "ProductionCancel" && movement.ReferenceId == order.Id)
            .ToListAsync();
        productionReversals.Should().HaveCount(2);
        productionReversals.Should().OnlyContain(movement => movement.UnitId == each.Id);
        productionReversals.Single(movement => movement.ItemId == ingredient.Id).Quantity.Should().Be(6m);
        productionReversals.Single(movement => movement.ItemId == product.Id).Quantity.Should().Be(-10m);

        (await stock.GetCurrentStockAsync(ingredient.Id)).Should().Be(30m);
        (await stock.GetCurrentStockAsync(product.Id)).Should().Be(0m);
        (await stock.GetStockValuationAsync()).Should().Be(300m);
    }

    private static async Task AssertLatestBaseMovementAsync(
        BakeryDbContext db,
        int itemId,
        InventoryMovementType type,
        int baseUnitId,
        decimal expectedQuantity)
    {
        var movement = await db.InventoryMovements.AsNoTracking()
            .Where(candidate => candidate.ItemId == itemId && candidate.Type == type)
            .OrderByDescending(candidate => candidate.Id)
            .FirstAsync();
        movement.UnitId.Should().Be(baseUnitId);
        movement.Quantity.Should().Be(expectedQuantity);
    }
}
