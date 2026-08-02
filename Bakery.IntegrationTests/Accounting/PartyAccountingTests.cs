using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class PartyAccountingTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public PartyAccountingTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(PartyDto Supplier, PartyDto Customer, ItemDto Item, Unit Unit)> SetupTestDataAsync(IServiceProvider sp)
    {
        var workingDayService = sp.GetRequiredService<IWorkingDayService>();
        var partyService = sp.GetRequiredService<IPartyService>();
        var itemService = sp.GetRequiredService<IItemService>();
        var safeService = sp.GetRequiredService<ISafeService>();
        var db = sp.GetRequiredService<BakeryDbContext>();

        // Open day if not open
        if (await workingDayService.GetCurrentOpenDayAsync() == null)
        {
            await workingDayService.OpenDayAsync(new OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today), 50000m, "Accounting Test Day"));
        }

        var activeDay = await workingDayService.GetCurrentOpenDayAsync();
        var safeId = await safeService.GetDefaultSafeIdAsync();

        // Inject Safe Balance to bypass Treasury validations during payments
        if (await db.SafeMovements.Where(x => x.SafeId == safeId).SumAsync(x => (decimal?)x.Amount) < 10000m)
        {
            db.SafeMovements.Add(new SafeMovement { SafeId = safeId, WorkingDayId = activeDay!.Id, Type = SafeMovementType.Adjustment, Amount = 100000m, Description = "Test Funding" });
            await db.SaveChangesAsync();
        }

        var supplierResult = await partyService.SaveAsync(new SavePartyRequest(null, $"Supplier {Guid.NewGuid()}", PartyType.Supplier, "0112233", "Giza", "123", "Test supplier", true));
        var customerResult = await partyService.SaveAsync(new SavePartyRequest(null, $"Customer {Guid.NewGuid()}", PartyType.Customer, "0122334", "Cairo", "456", "Test customer", true));
        
        var kgUnit = await db.Units.FirstAsync(u => u.Symbol == "kg");
        var itemResult = await itemService.SaveAsync(new SaveItemRequest(null, $"ITEM-{Guid.NewGuid()}", "Test Item", null, ItemType.RawMaterial, kgUnit.Id, 30m, 0m, 10m, 20m, true, "Desc"));
        var item = itemResult.Item!;
        
        // Add stock to bypass stock validation in Sale invoices
        db.InventoryMovements.Add(new InventoryMovement { ItemId = item.Id, UnitId = kgUnit.Id, WorkingDayId = activeDay!.Id, Quantity = 10000m, Type = InventoryMovementType.Adjustment, UnitCost = 10m });
        await db.SaveChangesAsync();

        return (supplierResult.Party!, customerResult.Party!, item, kgUnit);
    }

    [Fact]
    public async Task Scenario01_SupplierInvoice_WithoutPayment()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var purchaseService = scope.ServiceProvider.GetRequiredService<IPurchaseInvoiceService>();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        
        var (supplier, _, item, unit) = await SetupTestDataAsync(scope.ServiceProvider);
        var safeId = await safeService.GetDefaultSafeIdAsync();

        var req = new SavePurchaseInvoiceRequest(null, supplier.Id, PaymentType.Cash, 0m, "Test", new List<InvoiceLineRequest> { new(item.Id, unit.Id, 100m, 10m) }, safeId); // Total 1000
        var draft = await purchaseService.SaveDraftAsync(req);
        var postResult = await purchaseService.PostAsync(draft.InvoiceId!.Value);
        postResult.Succeeded.Should().BeTrue(postResult.ErrorMessage);

        var summary = await partyService.GetPartySummaryAsync(supplier.Id);
        summary.TotalIncrease.Should().Be(1000m); // Purchases
        summary.TotalDecrease.Should().Be(0m);    // Paid
        summary.CurrentBalance.Should().Be(1000m);       // Remaining
    }

    [Fact]
    public async Task Scenario02_SupplierInvoice_WithPartialDownPayment()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var purchaseService = scope.ServiceProvider.GetRequiredService<IPurchaseInvoiceService>();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        
        var (supplier, _, item, unit) = await SetupTestDataAsync(scope.ServiceProvider);
        var safeId = await safeService.GetDefaultSafeIdAsync();

        var req = new SavePurchaseInvoiceRequest(null, supplier.Id, PaymentType.Cash, 300m, "Test", new List<InvoiceLineRequest> { new(item.Id, unit.Id, 100m, 10m) }, safeId); // Total 1000, Paid 300
        var draft = await purchaseService.SaveDraftAsync(req);
        var postResult = await purchaseService.PostAsync(draft.InvoiceId!.Value);
        postResult.Succeeded.Should().BeTrue(postResult.ErrorMessage);

        var summary = await partyService.GetPartySummaryAsync(supplier.Id);
        summary.TotalIncrease.Should().Be(1000m); 
        summary.TotalDecrease.Should().Be(300m);  
        summary.CurrentBalance.Should().Be(700m);       
    }

    [Fact]
    public async Task Scenario03_StandaloneSupplierPayment()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var purchaseService = scope.ServiceProvider.GetRequiredService<IPurchaseInvoiceService>();
        var paymentService = scope.ServiceProvider.GetRequiredService<IPartyPaymentService>();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        
        var (supplier, _, item, unit) = await SetupTestDataAsync(scope.ServiceProvider);
        var safeId = await safeService.GetDefaultSafeIdAsync();

        var req = new SavePurchaseInvoiceRequest(null, supplier.Id, PaymentType.Cash, 0m, "Test", new List<InvoiceLineRequest> { new(item.Id, unit.Id, 100m, 10m) }, safeId); // Total 1000
        var draft = await purchaseService.SaveDraftAsync(req);
        var postResult = await purchaseService.PostAsync(draft.InvoiceId!.Value);
        postResult.Succeeded.Should().BeTrue(postResult.ErrorMessage);

        await paymentService.ProcessPaymentAsync(supplier.Id, safeId, 400m, "Standalone payment");

        var summary = await partyService.GetPartySummaryAsync(supplier.Id);
        summary.TotalIncrease.Should().Be(1000m); 
        summary.TotalDecrease.Should().Be(400m);  
        summary.CurrentBalance.Should().Be(600m);       
    }

    [Fact]
    public async Task Scenario04_MultiplePartialSupplierPayments()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var purchaseService = scope.ServiceProvider.GetRequiredService<IPurchaseInvoiceService>();
        var paymentService = scope.ServiceProvider.GetRequiredService<IPartyPaymentService>();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        
        var (supplier, _, item, unit) = await SetupTestDataAsync(scope.ServiceProvider);
        var safeId = await safeService.GetDefaultSafeIdAsync();

        var req = new SavePurchaseInvoiceRequest(null, supplier.Id, PaymentType.Cash, 200m, "Test", new List<InvoiceLineRequest> { new(item.Id, unit.Id, 100m, 10m) }, safeId); // Total 1000, Paid 200
        var draft = await purchaseService.SaveDraftAsync(req);
        var postResult = await purchaseService.PostAsync(draft.InvoiceId!.Value);
        postResult.Succeeded.Should().BeTrue(postResult.ErrorMessage);

        await paymentService.ProcessPaymentAsync(supplier.Id, safeId, 100m, "Payment 1");
        await paymentService.ProcessPaymentAsync(supplier.Id, safeId, 150m, "Payment 2");

        var summary = await partyService.GetPartySummaryAsync(supplier.Id);
        summary.TotalIncrease.Should().Be(1000m); 
        summary.TotalDecrease.Should().Be(200m + 100m + 150m); // 450m
        summary.CurrentBalance.Should().Be(550m);       
    }

    [Fact]
    public async Task Scenario05_SupplierInvoiceCancellation()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var purchaseService = scope.ServiceProvider.GetRequiredService<IPurchaseInvoiceService>();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        
        var (supplier, _, item, unit) = await SetupTestDataAsync(scope.ServiceProvider);
        var safeId = await safeService.GetDefaultSafeIdAsync();

        var req = new SavePurchaseInvoiceRequest(null, supplier.Id, PaymentType.Cash, 300m, "Test", new List<InvoiceLineRequest> { new(item.Id, unit.Id, 100m, 10m) }, safeId); // Total 1000, Paid 300
        var draft = await purchaseService.SaveDraftAsync(req);
        var postResult = await purchaseService.PostAsync(draft.InvoiceId!.Value);
        postResult.Succeeded.Should().BeTrue(postResult.ErrorMessage);

        await purchaseService.CancelAsync(draft.InvoiceId!.Value, "Mistake");

        var summary = await partyService.GetPartySummaryAsync(supplier.Id);
        summary.TotalIncrease.Should().Be(0m); 
        summary.TotalDecrease.Should().Be(0m);  
        summary.CurrentBalance.Should().Be(0m);       
    }

    [Fact]
    public async Task Scenario06_CustomerInvoice_WithoutCollection()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var saleService = scope.ServiceProvider.GetRequiredService<ISaleInvoiceService>();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        
        var (_, customer, item, unit) = await SetupTestDataAsync(scope.ServiceProvider);
        var safeId = await safeService.GetDefaultSafeIdAsync();

        var req = new SaveSaleInvoiceRequest(null, customer.Id, PaymentType.Cash, 0m, "Test", new List<InvoiceLineRequest> { new(item.Id, unit.Id, 50m, 20m) }, safeId); // Total 1000
        var draft = await saleService.SaveDraftAsync(req);
        var postResult = await saleService.PostAsync(draft.InvoiceId!.Value);
        postResult.Succeeded.Should().BeTrue(postResult.ErrorMessage);

        var summary = await partyService.GetPartySummaryAsync(customer.Id);
        summary.TotalIncrease.Should().Be(1000m); // Sales
        summary.TotalDecrease.Should().Be(0m);    // Collected
        summary.CurrentBalance.Should().Be(1000m);       
    }

    [Fact]
    public async Task Scenario07_CustomerInvoice_WithPartialCollection()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var saleService = scope.ServiceProvider.GetRequiredService<ISaleInvoiceService>();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        
        var (_, customer, item, unit) = await SetupTestDataAsync(scope.ServiceProvider);
        var safeId = await safeService.GetDefaultSafeIdAsync();

        var req = new SaveSaleInvoiceRequest(null, customer.Id, PaymentType.Cash, 400m, "Test", new List<InvoiceLineRequest> { new(item.Id, unit.Id, 50m, 20m) }, safeId); // Total 1000, Paid 400
        var draft = await saleService.SaveDraftAsync(req);
        var postResult = await saleService.PostAsync(draft.InvoiceId!.Value);
        postResult.Succeeded.Should().BeTrue(postResult.ErrorMessage);

        var summary = await partyService.GetPartySummaryAsync(customer.Id);
        summary.TotalIncrease.Should().Be(1000m); 
        summary.TotalDecrease.Should().Be(400m);  
        summary.CurrentBalance.Should().Be(600m);       
    }

    [Fact]
    public async Task Scenario08_StandaloneCustomerReceipt()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var saleService = scope.ServiceProvider.GetRequiredService<ISaleInvoiceService>();
        var paymentService = scope.ServiceProvider.GetRequiredService<IPartyPaymentService>();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        
        var (_, customer, item, unit) = await SetupTestDataAsync(scope.ServiceProvider);
        var safeId = await safeService.GetDefaultSafeIdAsync();

        var req = new SaveSaleInvoiceRequest(null, customer.Id, PaymentType.Cash, 0m, "Test", new List<InvoiceLineRequest> { new(item.Id, unit.Id, 50m, 20m) }, safeId); // Total 1000
        var draft = await saleService.SaveDraftAsync(req);
        var postResult = await saleService.PostAsync(draft.InvoiceId!.Value);
        postResult.Succeeded.Should().BeTrue(postResult.ErrorMessage);

        await paymentService.ProcessPaymentAsync(customer.Id, safeId, 600m, "Standalone receipt");

        var summary = await partyService.GetPartySummaryAsync(customer.Id);
        summary.TotalIncrease.Should().Be(1000m); 
        summary.TotalDecrease.Should().Be(600m);  
        summary.CurrentBalance.Should().Be(400m);       
    }

    [Fact]
    public async Task Scenario09_CustomerInvoiceCancellation()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var saleService = scope.ServiceProvider.GetRequiredService<ISaleInvoiceService>();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        
        var (_, customer, item, unit) = await SetupTestDataAsync(scope.ServiceProvider);
        var safeId = await safeService.GetDefaultSafeIdAsync();

        var req = new SaveSaleInvoiceRequest(null, customer.Id, PaymentType.Cash, 400m, "Test", new List<InvoiceLineRequest> { new(item.Id, unit.Id, 50m, 20m) }, safeId); // Total 1000, Paid 400
        var draft = await saleService.SaveDraftAsync(req);
        var postResult = await saleService.PostAsync(draft.InvoiceId!.Value);
        postResult.Succeeded.Should().BeTrue(postResult.ErrorMessage);

        await saleService.CancelAsync(draft.InvoiceId!.Value, "Mistake");

        var summary = await partyService.GetPartySummaryAsync(customer.Id);
        summary.TotalIncrease.Should().Be(0m); 
        summary.TotalDecrease.Should().Be(0m);  
        summary.CurrentBalance.Should().Be(0m);       
    }

    [Fact]
    public async Task Scenario10_VerifyNoDoubleCounting()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var saleService = scope.ServiceProvider.GetRequiredService<ISaleInvoiceService>();
        var paymentService = scope.ServiceProvider.GetRequiredService<IPartyPaymentService>();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        
        var (_, customer, item, unit) = await SetupTestDataAsync(scope.ServiceProvider);
        var safeId = await safeService.GetDefaultSafeIdAsync();

        var req = new SaveSaleInvoiceRequest(null, customer.Id, PaymentType.Cash, 500m, "Test", new List<InvoiceLineRequest> { new(item.Id, unit.Id, 50m, 20m) }, safeId); // Total 1000, Paid 500
        var draft = await saleService.SaveDraftAsync(req);
        var postResult = await saleService.PostAsync(draft.InvoiceId!.Value);
        postResult.Succeeded.Should().BeTrue(postResult.ErrorMessage);

        await paymentService.ProcessPaymentAsync(customer.Id, safeId, 500m, "Clear remaining balance");

        var summary = await partyService.GetPartySummaryAsync(customer.Id);
        summary.TotalIncrease.Should().Be(1000m); 
        summary.TotalDecrease.Should().Be(1000m); // 500 down payment + 500 standalone
        summary.CurrentBalance.Should().Be(0m);       
    }

    [Fact]
    public async Task Scenario11_VerifyReversedLedgerEntriesAreIgnored()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        
        var (supplier, _, _, _) = await SetupTestDataAsync(scope.ServiceProvider);
        var activeDay = await workingDayService.GetCurrentOpenDayAsync();

        // Inject a reversed entry manually
        var entry1 = new PartyLedgerEntry
        {
            WorkingDayId = activeDay!.Id,
            PartyId = supplier.Id,
            Credit = 5000m,
            Debit = 0m,
            Amount = 5000m,
            ReferenceType = Bakery.Domain.Constants.LedgerReferenceTypes.PurchaseInvoice,
            IsReversed = true // This should be completely ignored by the summary calculation
        };
        db.PartyLedgerEntries.Add(entry1);
        await db.SaveChangesAsync();

        var summary = await partyService.GetPartySummaryAsync(supplier.Id);
        summary.TotalIncrease.Should().Be(0m); 
        summary.TotalDecrease.Should().Be(0m);
        summary.CurrentBalance.Should().Be(0m);       
    }

    [Fact]
    public async Task Scenario12_VerifyNewReferenceTypesDoNotAffectSummary()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        
        var (supplier, _, _, _) = await SetupTestDataAsync(scope.ServiceProvider);
        var activeDay = await workingDayService.GetCurrentOpenDayAsync();

        // Inject an unknown reference type manually
        var entry1 = new PartyLedgerEntry
        {
            WorkingDayId = activeDay!.Id,
            PartyId = supplier.Id,
            Credit = 5000m,
            Debit = 0m,
            Amount = 5000m,
            ReferenceType = "CreditNote" // Unknown reference type
        };
        db.PartyLedgerEntries.Add(entry1);
        await db.SaveChangesAsync();

        var summary = await partyService.GetPartySummaryAsync(supplier.Id);
        
        // Because "CreditNote" is unknown, GetAccountingImpact should return (0, 0)
        summary.TotalIncrease.Should().Be(0m); 
        summary.TotalDecrease.Should().Be(0m);
        
        // HOWEVER, the balance is derived mathematically from Increase - Decrease.
        // Wait, PartyService says: decimal balance = totalIncrease - totalDecrease;
        // Therefore Balance should be 0.
        summary.CurrentBalance.Should().Be(0m);       
    }

    [Fact]
    public async Task Scenario13_SoftDeletedParty_ShouldNotBeReturnedInSearch()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var partyService = scope.ServiceProvider.GetRequiredService<IPartyService>();
        
        var (supplier, _, _, _) = await SetupTestDataAsync(scope.ServiceProvider);
        
        // Soft delete the supplier
        var deleteResult = await partyService.DeleteAsync(supplier.Id);
        deleteResult.Succeeded.Should().BeTrue();
        
        // Search without including deleted
        var activeParties = await partyService.SearchAsync(new PartySearchRequest { IncludeDeleted = false });
        activeParties.Any(p => p.Id == supplier.Id).Should().BeFalse();
        
        // Search with including deleted
        var allParties = await partyService.SearchAsync(new PartySearchRequest { IncludeDeleted = true });
        allParties.Any(p => p.Id == supplier.Id).Should().BeTrue();
    }
}

