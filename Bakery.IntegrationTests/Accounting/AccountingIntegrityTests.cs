using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Engines;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class AccountingIntegrityTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public AccountingIntegrityTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CancelProduction_ShouldFullyReverseAccountingAndInventory()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var prodService = scope.ServiceProvider.GetRequiredService<IProductionService>();
        var engine = scope.ServiceProvider.GetRequiredService<IProductionPostingEngine>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        // Open day if not open
        if (await workingDayService.GetCurrentOpenDayAsync() == null)
            await workingDayService.OpenDayAsync(new OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today), 0m, "Test"));

        var day = await workingDayService.GetCurrentOpenDayAsync();
        
        // Find entities
        var flour = await db.Items.FirstAsync(i => i.Name == "Flour");
        var bread = await db.Items.FirstAsync(i => i.Name == "Bread");
        var empParty = await db.Parties.FirstAsync(p => p.Type == PartyType.Employee);
        var employee = await db.Employees.FirstAsync(e => e.PartyId == empParty.Id);
        var pieceUnit = await db.Units.FirstAsync(u => u.Symbol == "pcs");
        var kgUnit = await db.Units.FirstAsync(u => u.Symbol == "kg");

        // Seed Stock for Flour
        db.InventoryMovements.Add(new InventoryMovement
        {
            WorkingDayId = day!.Id,
            ItemId = flour.Id,
            UnitId = kgUnit.Id,
            Type = InventoryMovementType.Adjustment,
            Quantity = 1000,
            UnitCost = 10
        });
        await db.SaveChangesAsync();

        // Create Order
        var order = new ProductionOrder
        {
            ProductionNumber = "PRD-TEST-1",
            WorkingDayId = day!.Id,
            Status = ProductionStatus.Draft,
            ConsumedItems = new List<ProductionConsumedItem>
            {
                new ProductionConsumedItem { ItemId = flour.Id, UnitId = kgUnit.Id, Quantity = 100, UnitCost = 20 }
            },
            ProducedItems = new List<ProductionProducedItem>
            {
                new ProductionProducedItem { ItemId = bread.Id, UnitId = pieceUnit.Id, ExpectedProducedQty = 1000, ActualProducedQty = 1000, UnitCost = 2 }
            },
            Employees = new List<ProductionOrderEmployee>
            {
                new ProductionOrderEmployee { EmployeeId = employee.Id, ContributionPercentage = 1.0m }
            }
        };

        order = await prodService.CreateProductionOrderAsync(order);

        // Act: Post Production
        await prodService.PostProductionOrderAsync(order.Id);

        // Verify Posting Accounting
        var initialWages = await db.EmployeeWages.Where(w => w.EmployeeId == employee.Id && !w.IsReversed).SumAsync(w => w.Amount);
        var initialLedger = await db.PartyLedgerEntries.Where(l => l.PartyId == empParty.Id && !l.IsReversed).SumAsync(l => l.Amount);
        
        initialWages.Should().BeGreaterThan(0, "Wages should be generated.");
        initialLedger.Should().BeLessThan(0, "Ledger should show bakery owes money.");

        var initialFlourStock = await db.InventoryMovements.Where(m => m.ItemId == flour.Id && !m.IsReversed).SumAsync(m => m.Quantity);
        
        // Act: Cancel Production
        await prodService.CancelProductionOrderAsync(order.Id);

        // Assert: Full Reversals
        var finalWages = await db.EmployeeWages.Where(w => w.EmployeeId == employee.Id).SumAsync(w => w.Amount);
        var finalLedger = await db.PartyLedgerEntries.Where(l => l.PartyId == empParty.Id).SumAsync(l => l.Amount);
        var finalFlourStock = await db.InventoryMovements.Where(m => m.ItemId == flour.Id).SumAsync(m => m.Quantity);

        finalWages.Should().Be(0, "All wages should be reversed exactly to 0.");
        finalLedger.Should().Be(0, "All ledgers should net to 0 exactly after reversal.");
        finalFlourStock.Should().Be(1000, "All stock should be restored to seeded amount after reversal.");

        // Assert Reversal Flags
        var originalWage = await db.EmployeeWages.FirstAsync(w => w.Amount > 0 && w.EmployeeId == employee.Id);
        var originalLedger = await db.PartyLedgerEntries.FirstAsync(l => l.Amount < 0 && l.PartyId == empParty.Id);

        originalWage.IsReversed.Should().BeTrue();
        originalLedger.IsReversed.Should().BeTrue();
    }
}
