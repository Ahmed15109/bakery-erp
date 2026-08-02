using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakery.IntegrationTests;

public sealed class InvoiceNumberAllocationTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public InvoiceNumberAllocationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Allocation_UsesBusinessDate_SurvivesDeletion_AndSerializesConcurrentDrafts()
    {
        var businessDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-7);
        int customerId;
        int supplierId;
        int itemId;
        int unitId;
        int firstSaleId;

        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            var days = setupScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
            (await days.OpenDayAsync(new OpenWorkingDayRequest(
                businessDate, 0m, "Invoice number allocation"))).Succeeded.Should().BeTrue();
            customerId = await db.Parties.Where(party => party.Type == PartyType.Customer)
                .Select(party => party.Id).FirstAsync();
            supplierId = await db.Parties.Where(party => party.Type == PartyType.Supplier)
                .Select(party => party.Id).FirstAsync();
            var item = await db.Items.FirstAsync();
            itemId = item.Id;
            unitId = item.BaseUnitId;

            var sale = await setupScope.ServiceProvider.GetRequiredService<ISaleInvoiceService>()
                .SaveDraftAsync(SaleRequest(customerId, itemId, unitId));
            sale.Succeeded.Should().BeTrue(sale.ErrorMessage);
            firstSaleId = sale.InvoiceId!.Value;
            var firstNumber = await db.SaleInvoices
                .Where(invoice => invoice.Id == firstSaleId)
                .Select(invoice => invoice.InvoiceNumber)
                .SingleAsync();
            firstNumber.Should().Be($"S-{businessDate:yyyyMMdd}-0001");

            var purchase = await setupScope.ServiceProvider.GetRequiredService<IPurchaseInvoiceService>()
                .SaveDraftAsync(new SavePurchaseInvoiceRequest(
                    null, supplierId, PaymentType.Credit, 0m, null,
                    [new InvoiceLineRequest(itemId, unitId, 1m, 1m)], null));
            purchase.Succeeded.Should().BeTrue(purchase.ErrorMessage);
            (await db.PurchaseInvoices.Where(invoice => invoice.Id == purchase.InvoiceId)
                .Select(invoice => invoice.InvoiceNumber).SingleAsync())
                .Should().Be($"P-{businessDate:yyyyMMdd}-0001");

            var deleted = await db.SaleInvoices.SingleAsync(invoice => invoice.Id == firstSaleId);
            deleted.IsDeleted = true;
            deleted.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var afterDelete = await setupScope.ServiceProvider.GetRequiredService<ISaleInvoiceService>()
                .SaveDraftAsync(SaleRequest(customerId, itemId, unitId));
            afterDelete.Succeeded.Should().BeTrue(afterDelete.ErrorMessage);
            (await db.SaleInvoices.Where(invoice => invoice.Id == afterDelete.InvoiceId)
                .Select(invoice => invoice.InvoiceNumber).SingleAsync())
                .Should().Be($"S-{businessDate:yyyyMMdd}-0002");
        }

        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<(bool Succeeded, string? ErrorMessage, int? InvoiceId)> CreateAsync()
        {
            await start.Task;
            using var scope = _fixture.ServiceProvider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ISaleInvoiceService>()
                .SaveDraftAsync(SaleRequest(customerId, itemId, unitId));
        }

        var first = Task.Run(CreateAsync);
        var second = Task.Run(CreateAsync);
        start.TrySetResult(true);
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        results.Should().OnlyContain(result => result.Succeeded);

        using var assertionScope = _fixture.ServiceProvider.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var concurrentIds = results.Select(result => result.InvoiceId!.Value).ToArray();
        var concurrentNumbers = await assertionDb.SaleInvoices
            .Where(invoice => concurrentIds.Contains(invoice.Id))
            .Select(invoice => invoice.InvoiceNumber)
            .OrderBy(number => number)
            .ToListAsync();
        concurrentNumbers.Should().Equal(
            $"S-{businessDate:yyyyMMdd}-0003",
            $"S-{businessDate:yyyyMMdd}-0004");
        concurrentNumbers.Should().OnlyHaveUniqueItems();

        var saleCounter = await assertionDb.TransactionNumberCounters
            .SingleAsync(counter => counter.Prefix == $"INVOICE:S:{businessDate:yyyyMMdd}");
        saleCounter.LastValue.Should().Be(4);
        (await assertionDb.SaleInvoices.IgnoreQueryFilters()
            .CountAsync(invoice => invoice.InvoiceNumber == $"S-{businessDate:yyyyMMdd}-0001"))
            .Should().Be(1, "deleting an invoice must not permit number reuse");
    }

    private static SaveSaleInvoiceRequest SaleRequest(int customerId, int itemId, int unitId)
        => new(
            null,
            customerId,
            PaymentType.Credit,
            0m,
            "Invoice number regression",
            [new InvoiceLineRequest(itemId, unitId, 1m, 1m)],
            null);
}
