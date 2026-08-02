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

public sealed class StockConcurrencyTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public StockConcurrencyTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentSales_ForTheSameAvailableStock_ShouldNotOversell()
    {
        int firstInvoiceId;
        int secondInvoiceId;
        int itemId;

        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            var days = setupScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
            var sales = setupScope.ServiceProvider.GetRequiredService<ISaleInvoiceService>();
            if (await days.GetCurrentOpenDayAsync() is null)
            {
                await days.OpenDayAsync(new OpenWorkingDayRequest(
                    DateOnly.FromDateTime(DateTime.Today), 0m, "Concurrent stock test"));
            }
            var day = (await days.GetCurrentOpenDayAsync())!;
            var customer = await db.Parties.FirstAsync(party => party.Type == PartyType.Customer);
            var unit = await db.Units.FirstAsync();
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var item = new Item
            {
                Code = $"RACE-{suffix}",
                Name = $"Race item {suffix}",
                Type = ItemType.FinishedProduct,
                BaseUnitId = unit.Id,
                PurchasePrice = 2m,
                SalePrice = 5m
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            itemId = item.Id;
            db.InventoryMovements.Add(new InventoryMovement
            {
                WorkingDayId = day.Id,
                ItemId = item.Id,
                UnitId = unit.Id,
                Type = InventoryMovementType.OpeningBalance,
                Quantity = 10m,
                UnitCost = 2m
            });
            await db.SaveChangesAsync();

            var request = new SaveSaleInvoiceRequest(
                null,
                customer.Id,
                PaymentType.Credit,
                0m,
                "Controlled oversell race",
                [new InvoiceLineRequest(item.Id, unit.Id, 10m, 5m)],
                null);
            var firstDraft = await sales.SaveDraftAsync(request);
            var secondDraft = await sales.SaveDraftAsync(request);
            firstDraft.Succeeded.Should().BeTrue(firstDraft.ErrorMessage);
            secondDraft.Succeeded.Should().BeTrue(secondDraft.ErrorMessage);
            firstInvoiceId = firstDraft.InvoiceId!.Value;
            secondInvoiceId = secondDraft.InvoiceId!.Value;
        }

        // Release both independent DbContexts together. Whichever transaction
        // acquires the database item lock second must observe the first commit.
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _fixture.BackupControl.SkipSafetySnapshots = true;

        async Task<(bool Succeeded, string? ErrorMessage)> PostInIndependentScopeAsync(int invoiceId)
        {
            await start.Task;
            using var scope = _fixture.ServiceProvider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ISaleInvoiceService>().PostAsync(invoiceId);
        }

        var firstPost = Task.Run(() => PostInIndependentScopeAsync(firstInvoiceId));
        var secondPost = Task.Run(() => PostInIndependentScopeAsync(secondInvoiceId));
        (bool Succeeded, string? ErrorMessage)[] results;
        try
        {
            start.TrySetResult(true);
            results = await Task.WhenAll(firstPost, secondPost).WaitAsync(TimeSpan.FromSeconds(60));
        }
        finally
        {
            start.TrySetResult(true);
            _fixture.BackupControl.SkipSafetySnapshots = false;
        }

        results.Count(result => result.Succeeded).Should().Be(1);
        results.Count(result => !result.Succeeded).Should().Be(1);

        using var assertionScope = _fixture.ServiceProvider.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var finalBalance = await assertionDb.InventoryMovements
            .Where(movement => movement.ItemId == itemId)
            .SumAsync(movement => movement.Quantity);
        finalBalance.Should().Be(0m);
        var statuses = await assertionDb.SaleInvoices
            .Where(invoice => invoice.Id == firstInvoiceId || invoice.Id == secondInvoiceId)
            .Select(invoice => invoice.Status)
            .ToListAsync();
        statuses.Should().ContainSingle(status => status == InvoiceStatus.Posted);
        statuses.Should().ContainSingle(status => status == InvoiceStatus.Draft);
    }
}
