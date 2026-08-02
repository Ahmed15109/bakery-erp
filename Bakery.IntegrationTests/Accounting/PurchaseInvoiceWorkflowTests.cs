using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class PurchaseInvoiceWorkflowTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public PurchaseInvoiceWorkflowTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaveDraftAndPost_ShouldWorkWithWorkingDayTreasuryColumns()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var purchaseService = scope.ServiceProvider.GetRequiredService<IPurchaseInvoiceService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        await workingDayService.OpenDayAsync(new OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today), 500m, "Purchase workflow"));

        var supplier = await db.Parties.FirstAsync(p => p.Type == PartyType.Supplier);
        var flour = await db.Items.FirstAsync(i => i.Name == "Flour");
        var kg = await db.Units.FirstAsync(u => u.Symbol == "kg");

        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var safe = await safeService.GetDefaultCashSafeAsync();

        var request = new SavePurchaseInvoiceRequest(
            null,
            supplier.Id,
            PaymentType.Cash,
            200m,
            "Purchase workflow test",
            new List<InvoiceLineRequest> { new(flour.Id, kg.Id, 10m, 20m) },
            safe.Id);

        var draftResult = await purchaseService.SaveDraftAsync(request);
        draftResult.Succeeded.Should().BeTrue(draftResult.ErrorMessage);
        draftResult.InvoiceId.Should().NotBeNull();

        var postResult = await purchaseService.PostAsync(draftResult.InvoiceId!.Value);
        postResult.Succeeded.Should().BeTrue(postResult.ErrorMessage);

        var flourStock = await db.InventoryMovements.Where(m => m.ItemId == flour.Id).SumAsync(m => m.Quantity);
        flourStock.Should().Be(10m);

        var summary = await workingDayService.GetCurrentDaySummaryAsync();
        summary.Should().NotBeNull();
        summary!.WorkingDayId.Should().BeGreaterThan(0);
    }
}
