using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Domain.Constants;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Bakery.Reporting.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class SalesByItemReportTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public SalesByItemReportTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Report_ShouldReturnGrossDiscountReturnsAndNet_InBaseUnits()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var days = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        if (await days.GetCurrentOpenDayAsync() is null)
        {
            await days.OpenDayAsync(new OpenWorkingDayRequest(
                DateOnly.FromDateTime(DateTime.Today), 0m, "Sales by item regression"));
        }
        var day = (await days.GetCurrentOpenDayAsync())!;
        var customer = await db.Parties.FirstAsync(party => party.Type == PartyType.Customer);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var each = new Unit { Name = $"Report each {suffix}", Symbol = $"re{suffix}" };
        var caseUnit = new Unit { Name = $"Report case {suffix}", Symbol = $"rc{suffix}" };
        db.Units.AddRange(each, caseUnit);
        await db.SaveChangesAsync();

        var caseItem = new Item
        {
            Code = $"RPT-A-{suffix}",
            Name = $"Report item A {suffix}",
            Type = ItemType.FinishedProduct,
            BaseUnitId = each.Id,
            PurchasePrice = 5m,
            SalePrice = 10m
        };
        var eachItem = new Item
        {
            Code = $"RPT-B-{suffix}",
            Name = $"Report item B {suffix}",
            Type = ItemType.FinishedProduct,
            BaseUnitId = each.Id,
            PurchasePrice = 2m,
            SalePrice = 10m
        };
        db.Items.AddRange(caseItem, eachItem);
        await db.SaveChangesAsync();
        db.ItemUnits.Add(new ItemUnit
        {
            ItemId = caseItem.Id,
            UnitId = caseUnit.Id,
            ConversionFactorToBaseUnit = 12m
        });
        await db.SaveChangesAsync();

        var posted = new SaleInvoice
        {
            InvoiceNumber = $"RPT-POST-{suffix}",
            WorkingDayId = day.Id,
            PartyId = customer.Id,
            PaymentType = PaymentType.Credit,
            Status = InvoiceStatus.Posted,
            TotalAmount = 270m,
            RemainingAmount = 270m,
            Lines =
            [
                new SaleInvoiceLine
                {
                    ItemId = caseItem.Id,
                    UnitId = caseUnit.Id,
                    Quantity = 2m,
                    UnitPrice = 120m,
                    LineTotal = 240m
                },
                new SaleInvoiceLine
                {
                    ItemId = eachItem.Id,
                    UnitId = each.Id,
                    Quantity = 3m,
                    UnitPrice = 10m,
                    LineTotal = 30m
                }
            ]
        };
        var returned = new SaleInvoice
        {
            InvoiceNumber = $"RPT-RETURN-{suffix}",
            WorkingDayId = day.Id,
            PartyId = customer.Id,
            PaymentType = PaymentType.Credit,
            Status = InvoiceStatus.Cancelled,
            TotalAmount = 120m,
            RemainingAmount = 120m,
            CancellationReason = "posted sale returned",
            Lines =
            [
                new SaleInvoiceLine
                {
                    ItemId = caseItem.Id,
                    UnitId = caseUnit.Id,
                    Quantity = 1m,
                    UnitPrice = 120m,
                    LineTotal = 120m
                }
            ]
        };
        var cancelledDraft = new SaleInvoice
        {
            InvoiceNumber = $"RPT-DRAFT-{suffix}",
            WorkingDayId = day.Id,
            PartyId = customer.Id,
            PaymentType = PaymentType.Credit,
            Status = InvoiceStatus.Cancelled,
            TotalAmount = 500m,
            RemainingAmount = 500m,
            CancellationReason = "draft discarded",
            Lines =
            [
                new SaleInvoiceLine
                {
                    ItemId = eachItem.Id,
                    UnitId = each.Id,
                    Quantity = 50m,
                    UnitPrice = 10m,
                    LineTotal = 500m
                }
            ]
        };
        db.SaleInvoices.AddRange(posted, returned, cancelledDraft);
        await db.SaveChangesAsync();
        db.InventoryMovements.Add(new InventoryMovement
        {
            WorkingDayId = day.Id,
            ItemId = caseItem.Id,
            UnitId = each.Id,
            Type = InventoryMovementType.Adjustment,
            Quantity = 12m,
            UnitCost = 5m,
            ReferenceType = LedgerReferenceTypes.SaleCancel,
            ReferenceId = returned.Id,
            Notes = "return marker"
        });
        await db.SaveChangesAsync();

        var reportService = new AccountingReportService(
            db,
            scope.ServiceProvider.GetRequiredService<IPartyService>(),
            scope.ServiceProvider.GetRequiredService<IPermissionService>(),
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
            scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>(),
            scope.ServiceProvider.GetRequiredService<IBusinessDateService>(),
            scope.ServiceProvider.GetRequiredService<IItemUnitConversionService>());
        var result = await reportService.GetSalesByItemAsync(day.BusinessDate);

        result.Should().HaveCount(2);
        var itemA = result.Single(row => row.ItemId == caseItem.Id);
        itemA.BaseUnit.Should().Be(each.Symbol);
        itemA.Quantity.Should().Be(36m);
        itemA.GrossSales.Should().Be(360m);
        itemA.Discounts.Should().Be(0m);
        itemA.ReturnQuantity.Should().Be(12m);
        itemA.Returns.Should().Be(120m);
        itemA.NetQuantity.Should().Be(24m);
        itemA.NetSales.Should().Be(240m);

        var itemB = result.Single(row => row.ItemId == eachItem.Id);
        itemB.Quantity.Should().Be(3m);
        itemB.GrossSales.Should().Be(30m);
        itemB.ReturnQuantity.Should().Be(0m);
        itemB.Returns.Should().Be(0m);
        itemB.NetQuantity.Should().Be(3m);
        itemB.NetSales.Should().Be(30m);

        (await reportService.GetSalesByItemAsync(day.BusinessDate.AddDays(-1))).Should().BeEmpty();
    }
}
