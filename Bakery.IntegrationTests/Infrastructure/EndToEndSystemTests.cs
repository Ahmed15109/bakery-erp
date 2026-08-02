using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Bakery.Reporting.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class EndToEndSystemTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public EndToEndSystemTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CompleteBusinessDayWorkflow_ShouldSucceedAndMaintainIntegrity()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var itemService = scope.ServiceProvider.GetRequiredService<IItemService>();
        var recipeService = scope.ServiceProvider.GetRequiredService<IRecipeService>();
        var purchaseService = scope.ServiceProvider.GetRequiredService<IPurchaseInvoiceService>();
        var saleService = scope.ServiceProvider.GetRequiredService<ISaleInvoiceService>();
        var productionService = scope.ServiceProvider.GetRequiredService<IProductionService>();
        var integrityService = scope.ServiceProvider.GetRequiredService<IIntegrityCheckService>();
        var stockService = scope.ServiceProvider.GetRequiredService<IStockCalculationService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        // ==========================================
        // 1. OPEN WORKING DAY
        // ==========================================
        var openResult = await workingDayService.OpenDayAsync(new OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today), 50000m, "End-to-End Test Day"));
        openResult.Succeeded.Should().BeTrue(openResult.ErrorMessage);

        var activeDay = await workingDayService.GetCurrentOpenDayAsync();
        activeDay.Should().NotBeNull();
        activeDay!.Status.Should().Be(WorkingDayStatus.Open);

        // ==========================================
        // 2. ADD SUPPLIER & CUSTOMER
        // ==========================================
        var supplierRequest = new SavePartyRequest(null, "Giza Flour Mill Ltd", PartyType.Supplier, "01122334455", "Giza Industrial Zone", "12345678901234", "Primary supplier of flour and sugar", true);
        var supplierResult = await partyService.SaveAsync(supplierRequest);
        supplierResult.Succeeded.Should().BeTrue(supplierResult.ErrorMessage);
        var supplier = supplierResult.Party;
        supplier.Should().NotBeNull();
        supplier!.Id.Should().BeGreaterThan(0);

        var customerRequest = new SavePartyRequest(null, "Al-Noor Supermarket", PartyType.Customer, "01223344556", "Maadi, Cairo", "23456789012345", "Frequent wholesale client", true);
        var customerResult = await partyService.SaveAsync(customerRequest);
        customerResult.Succeeded.Should().BeTrue(customerResult.ErrorMessage);
        var customer = customerResult.Party;
        customer.Should().NotBeNull();
        customer!.Id.Should().BeGreaterThan(0);

        // Get pieces and kilograms units
        var kgUnit = await db.Units.FirstAsync(u => u.Symbol == "kg");
        var pcsUnit = await db.Units.FirstAsync(u => u.Symbol == "pcs");

        // ==========================================
        // 3. ADD RAW MATERIAL & FINISHED PRODUCT
        // ==========================================
        // Add Sugar raw material
        var sugarRequest = new SaveItemRequest(null, "SUGAR-002", "White Sugar", null, ItemType.RawMaterial, kgUnit.Id, 30m, 0m, 10m, 20m, true, "Raw sugar for desserts");
        var sugarResult = await itemService.SaveAsync(sugarRequest);
        sugarResult.Succeeded.Should().BeTrue(sugarResult.ErrorMessage);
        var sugar = sugarResult.Item;
        sugar.Should().NotBeNull();
        sugar!.Id.Should().BeGreaterThan(0);

        // Add Cake finished product
        var cakeRequest = new SaveItemRequest(null, "CAKE-002", "Vanilla Cake", null, ItemType.FinishedProduct, pcsUnit.Id, 0m, 15m, 5m, 10m, true, "Produced vanilla cake");
        var cakeResult = await itemService.SaveAsync(cakeRequest);
        cakeResult.Succeeded.Should().BeTrue(cakeResult.ErrorMessage);
        var cake = cakeResult.Item;
        cake.Should().NotBeNull();
        cake!.Id.Should().BeGreaterThan(0);

        // Get the seeded Flour item ID
        var flour = await db.Items.FirstAsync(i => i.Code == "FLOUR");

        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var safe = await safeService.GetDefaultCashSafeAsync();

        // ==========================================
        // 4. PURCHASE RAW MATERIALS
        // ==========================================
        var purchaseRequest = new SavePurchaseInvoiceRequest(
            null,
            supplier.Id,
            PaymentType.Cash,
            7000m, // Pay 7000 cash for 100kg sugar @ 30 and 200kg flour @ 20
            "Initial raw materials purchase",
            new List<InvoiceLineRequest>
            {
                new(sugar.Id, kgUnit.Id, 100m, 30m),
                new(flour.Id, kgUnit.Id, 200m, 20m)
            },
            safe.Id);

        var purchaseDraftResult = await purchaseService.SaveDraftAsync(purchaseRequest);
        purchaseDraftResult.Succeeded.Should().BeTrue(purchaseDraftResult.ErrorMessage);
        purchaseDraftResult.InvoiceId.Should().NotBeNull();

        var purchasePostResult = await purchaseService.PostAsync(purchaseDraftResult.InvoiceId!.Value);
        purchasePostResult.Succeeded.Should().BeTrue(purchasePostResult.ErrorMessage);

        // Verify stock levels after purchase
        var sugarStock = await db.InventoryMovements.Where(m => m.ItemId == sugar.Id).SumAsync(m => m.Quantity);
        var flourStock = await db.InventoryMovements.Where(m => m.ItemId == flour.Id).SumAsync(m => m.Quantity);
        sugarStock.Should().Be(100m);
        flourStock.Should().Be(200m); // Wait, seed flour was already there, so it's 200m + seeded amount if any. But in DatabaseFixture seed, it added 0 stock (just added Item). Let's check: yes!

        // ==========================================
        // 5. CREATE RECIPE
        // ==========================================
        var recipe = new Recipe
        {
            Name = "Standard Vanilla Cake Recipe",
            ProducedItemId = cake.Id,
            ProducedQuantity = 1m,
            ConsumedItems = new List<RecipeItem>
            {
                new() { RawItemId = sugar.Id, Quantity = 0.2m, UnitId = kgUnit.Id },
                new() { RawItemId = flour.Id, Quantity = 0.5m, UnitId = kgUnit.Id }
            }
        };
        await recipeService.CreateRecipeAsync(recipe);
        recipe.Id.Should().BeGreaterThan(0);

        // ==========================================
        // 6. RECORD PRODUCTION ORDER
        // ==========================================
        // We will produce 50 Vanilla Cakes
        // Required Sugar: 50 * 0.2 = 10 kg
        // Required Flour: 50 * 0.5 = 25 kg
        var seededEmp = await db.Employees.FirstAsync();

        var order = new ProductionOrder
        {
            ProductionNumber = "PRD-CAKE-001",
            WorkingDayId = activeDay.Id,
            RecipeId = recipe.Id,
            Status = ProductionStatus.Draft,
            ConsumedItems = new List<ProductionConsumedItem>
            {
                new() { ItemId = sugar.Id, UnitId = kgUnit.Id, Quantity = 10m, UnitCost = 30m },
                new() { ItemId = flour.Id, UnitId = kgUnit.Id, Quantity = 25m, UnitCost = 20m }
            },
            ProducedItems = new List<ProductionProducedItem>
            {
                new() { ItemId = cake.Id, UnitId = pcsUnit.Id, ExpectedProducedQty = 50m, ActualProducedQty = 50m, UnitCost = 16m }
            },
            Employees = new List<ProductionOrderEmployee>
            {
                new() { EmployeeId = seededEmp.Id, ContributionPercentage = 1.0m }
            }
        };

        var createdOrder = await productionService.CreateProductionOrderAsync(order);
        createdOrder.Id.Should().BeGreaterThan(0);

        await productionService.PostProductionOrderAsync(createdOrder.Id);

        // Verify stock levels after production
        var sugarStockAfterProduction = await db.InventoryMovements.Where(m => m.ItemId == sugar.Id).SumAsync(m => m.Quantity);
        var flourStockAfterProduction = await db.InventoryMovements.Where(m => m.ItemId == flour.Id).SumAsync(m => m.Quantity);
        var cakeStockAfterProduction = await db.InventoryMovements.Where(m => m.ItemId == cake.Id).SumAsync(m => m.Quantity);

        sugarStockAfterProduction.Should().Be(90m); // 100 - 10
        flourStockAfterProduction.Should().Be(175m); // 200 - 25
        cakeStockAfterProduction.Should().Be(50m); // Produced 50

        // ==========================================
        // 7. RECORD SALE INVOICE
        // ==========================================
        // Sell 20 Vanilla Cakes @ 15m = 300m total. Cash paid: 300m.
        var saleRequest = new SaveSaleInvoiceRequest(
            null,
            customer.Id,
            PaymentType.Cash,
            300m,
            "Sale of fresh vanilla cakes",
            new List<InvoiceLineRequest>
            {
                new(cake.Id, pcsUnit.Id, 20m, 15m)
            },
            safe.Id);

        var saleDraftResult = await saleService.SaveDraftAsync(saleRequest);
        saleDraftResult.Succeeded.Should().BeTrue(saleDraftResult.ErrorMessage);
        saleDraftResult.InvoiceId.Should().NotBeNull();

        var salePostResult = await saleService.PostAsync(saleDraftResult.InvoiceId!.Value);
        salePostResult.Succeeded.Should().BeTrue(salePostResult.ErrorMessage);

        // Verify stock levels after sale
        var cakeStockAfterSale = await db.InventoryMovements.Where(m => m.ItemId == cake.Id).SumAsync(m => m.Quantity);
        cakeStockAfterSale.Should().Be(30m); // 50 - 20

        // ==========================================
        // 8. INDEPENDENT DATABASE RECONCILIATION
        // ==========================================
        // Invoice headers must reconcile exactly to persisted line totals.
        var persistedPurchase = await db.PurchaseInvoices.AsNoTracking()
            .Include(invoice => invoice.Lines)
            .SingleAsync(invoice => invoice.Id == purchaseDraftResult.InvoiceId.Value);
        persistedPurchase.TotalAmount.Should().Be(persistedPurchase.Lines.Sum(line => line.LineTotal));
        persistedPurchase.TotalAmount.Should().Be(7000m);
        persistedPurchase.PaidAmount.Should().Be(7000m);
        persistedPurchase.RemainingAmount.Should().Be(0m);

        var persistedSale = await db.SaleInvoices.AsNoTracking()
            .Include(invoice => invoice.Lines)
            .SingleAsync(invoice => invoice.Id == saleDraftResult.InvoiceId.Value);
        persistedSale.TotalAmount.Should().Be(persistedSale.Lines.Sum(line => line.LineTotal));
        persistedSale.TotalAmount.Should().Be(300m);
        persistedSale.PaidAmount.Should().Be(300m);
        persistedSale.RemainingAmount.Should().Be(0m);

        // Service stock must equal the signed base-unit inventory ledger.
        var directCakeBalance = await db.InventoryMovements.AsNoTracking()
            .Where(movement => movement.ItemId == cake.Id)
            .SumAsync(movement => movement.Quantity);
        (await stockService.GetCurrentStockAsync(cake.Id)).Should().Be(directCakeBalance);
        directCakeBalance.Should().Be(30m);

        // Production service totals must equal fresh aggregates over completed child rows.
        var directConsumedCost = await db.Set<ProductionConsumedItem>().AsNoTracking()
            .Where(item => item.ProductionOrderId == createdOrder.Id)
            .SumAsync(item => item.Quantity * item.UnitCost);
        var directProducedValue = await db.Set<ProductionProducedItem>().AsNoTracking()
            .Where(item => item.ProductionOrderId == createdOrder.Id)
            .SumAsync(item => item.ActualProducedQty * item.UnitCost);
        var productionSummary = await productionService.GetProductionSummaryAsync();
        productionSummary.TodayOrdersCount.Should().Be(1);
        productionSummary.TodayProductionCost.Should().Be(directConsumedCost).And.Be(800m);
        productionSummary.TodayProducedValue.Should().Be(directProducedValue).And.Be(800m);

        var reports = new AccountingReportService(
            db,
            partyService,
            scope.ServiceProvider.GetRequiredService<IPermissionService>(),
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
            scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>(),
            scope.ServiceProvider.GetRequiredService<IBusinessDateService>(),
            scope.ServiceProvider.GetRequiredService<IItemUnitConversionService>());
        (await reports.GetDailyPurchasesAsync(activeDay.BusinessDate)).Should().Be(persistedPurchase.TotalAmount);
        (await reports.GetDailySalesAsync(activeDay.BusinessDate)).Should().Be(persistedSale.TotalAmount);

        var itemSales = await reports.GetSalesByItemAsync(activeDay.BusinessDate);
        var cakeSales = itemSales.Single(item => item.ItemId == cake.Id);
        cakeSales.Quantity.Should().Be(20m);
        cakeSales.GrossSales.Should().Be(persistedSale.TotalAmount);
        cakeSales.ReturnQuantity.Should().Be(0m);
        cakeSales.NetQuantity.Should().Be(20m);
        cakeSales.NetSales.Should().Be(300m);

        var directTreasuryBalance = await db.SafeMovements.AsNoTracking()
            .Where(movement => movement.WorkingDayId == activeDay.Id)
            .SumAsync(movement => movement.Amount);
        (await reports.GetCashMovementSummaryAsync(activeDay.BusinessDate)).Should().Be(directTreasuryBalance);
        directTreasuryBalance.Should().Be(43300m);

        // ==========================================
        // 9. DATA INTEGRITY CHECK
        // ==========================================
        var isHealthy = await integrityService.RunFullCheckAsync();
        isHealthy.Should().BeTrue("No orphaned ledger entries, duplicate constraints, or treasury mismatches should exist.");

        // ==========================================
        // 10. CLOSE WORKING DAY
        // ==========================================
        // Expected cash: OpeningCash + Safe Movements
        // Opening cash: 50000
        // Purchase: -7000
        // Sale: +300
        // Expected closing: 50000 - 7000 + 300 = 43300.
        var expectedCash = await workingDayService.CalculateExpectedClosingCashAsync(activeDay.Id);
        expectedCash.Should().Be(43300m);

        var closeResult = await workingDayService.CloseCurrentDayAsync(new CloseWorkingDayRequest(40000m, 3300m, "End of test close", true, "E2E Override"));
        closeResult.Succeeded.Should().BeTrue(closeResult.ErrorMessage);

        var closedDay = await workingDayService.GetCurrentOpenDayAsync();
        closedDay.Should().BeNull();
    }
}
