using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class InventoryIntegrityTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public InventoryIntegrityTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CancelSale_ShouldFullyRestoreStock()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var saleService = scope.ServiceProvider.GetRequiredService<ISaleInvoiceService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        // Open day if not open
        if (await workingDayService.GetCurrentOpenDayAsync() == null)
            await workingDayService.OpenDayAsync(new OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today), 0m, "Test"));

        var customer = await db.Parties.FirstAsync(p => p.Type == PartyType.Customer);
        var bread = await db.Items.FirstAsync(i => i.Name == "Bread");
        var pieceUnit = await db.Units.FirstAsync(u => u.Symbol == "pcs");

        // Give some initial stock
        var day = await workingDayService.GetCurrentOpenDayAsync();
        db.InventoryMovements.Add(new InventoryMovement
        {
            WorkingDayId = day!.Id,
            ItemId = bread.Id,
            UnitId = pieceUnit.Id,
            Type = InventoryMovementType.Adjustment,
            Quantity = 100, // +100 Bread
            UnitCost = 0
        });
        await db.SaveChangesAsync();

        var initialStock = await db.InventoryMovements.Where(m => m.ItemId == bread.Id).SumAsync(m => m.Quantity);
        initialStock.Should().Be(100);

        var safe = await db.Safes.FirstAsync();

        // Create Sale
        var request = new SaveSaleInvoiceRequest(
            null, 
            customer.Id, 
            PaymentType.Cash, 
            50m, 
            "Test sale", 
            new List<InvoiceLineRequest> { new InvoiceLineRequest(bread.Id, pieceUnit.Id, 10m, 5m) },
            safe.Id);
        
        var (draftSuccess, _, invoiceId) = await saleService.SaveDraftAsync(request);
        draftSuccess.Should().BeTrue();

        // Post Sale
        var (postSuccess, _) = await saleService.PostAsync(invoiceId!.Value);
        postSuccess.Should().BeTrue();

        var stockAfterSale = await db.InventoryMovements.Where(m => m.ItemId == bread.Id).SumAsync(m => m.Quantity);
        stockAfterSale.Should().Be(90, "Sold 10 pieces.");

        // Cancel Sale
        var (cancelSuccess, _) = await saleService.CancelAsync(invoiceId.Value, "Customer returned");
        cancelSuccess.Should().BeTrue();

        var finalStock = await db.InventoryMovements.Where(m => m.ItemId == bread.Id).SumAsync(m => m.Quantity);
        finalStock.Should().Be(100, "All stock should be returned perfectly.");
    }
}
