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

public sealed class BusinessDateReportingTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public BusinessDateReportingTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DailyReportsAndDashboard_ShouldFollowWorkingDay_NotUtcCalendarDate()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var businessDates = scope.ServiceProvider.GetRequiredService<IBusinessDateService>();
        var dayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var wasteService = scope.ServiceProvider.GetRequiredService<IWasteService>();
        var productionService = scope.ServiceProvider.GetRequiredService<IProductionService>();

        var firstDate = new DateOnly(2035, 1, 2);
        var secondDate = firstDate.AddDays(1);
        var firstDay = new WorkingDay
        {
            BusinessDate = firstDate,
            Status = WorkingDayStatus.Open,
            OpenedAt = new DateTime(2035, 1, 1, 22, 30, 0, DateTimeKind.Utc),
            OpenedBy = "business-date-test"
        };
        var secondDay = new WorkingDay
        {
            BusinessDate = secondDate,
            Status = WorkingDayStatus.Open,
            OpenedAt = new DateTime(2035, 1, 2, 8, 0, 0, DateTimeKind.Utc),
            OpenedBy = "business-date-test"
        };
        db.WorkingDays.Add(firstDay);
        await db.SaveChangesAsync();

        var customer = await db.Parties.FirstAsync(party => party.Type == PartyType.Customer);
        var supplier = await db.Parties.FirstAsync(party => party.Type == PartyType.Supplier);
        var safe = await db.Safes.FirstAsync(candidate => candidate.IsActive);
        var item = await db.Items.Include(candidate => candidate.BaseUnit).FirstAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // 22:30 UTC can be the next local day in Egypt. Conversely, the second
        // working day deliberately uses a timestamp on the first business date.
        db.SaleInvoices.Add(new SaleInvoice
        {
            InvoiceNumber = $"BD-S1-{suffix}",
            InvoiceDate = new DateTime(2035, 1, 1, 22, 30, 0, DateTimeKind.Utc),
            WorkingDayId = firstDay.Id,
            PartyId = customer.Id,
            PaymentType = PaymentType.Cash,
            Status = InvoiceStatus.Posted,
            TotalAmount = 100m,
            PaidAmount = 100m
        });
        db.PurchaseInvoices.Add(new PurchaseInvoice
        {
            InvoiceNumber = $"BD-P1-{suffix}",
            InvoiceDate = new DateTime(2035, 1, 1, 22, 45, 0, DateTimeKind.Utc),
            WorkingDayId = firstDay.Id,
            PartyId = supplier.Id,
            PaymentType = PaymentType.Cash,
            Status = InvoiceStatus.Posted,
            TotalAmount = 40m,
            PaidAmount = 40m
        });
        var firstCash = new SafeMovement
        {
            WorkingDayId = firstDay.Id,
            SafeId = safe.Id,
            Type = SafeMovementType.Adjustment,
            Amount = 25m,
            Description = "First business day cash",
            CreatedAt = new DateTime(2035, 1, 1, 22, 50, 0, DateTimeKind.Utc)
        };
        db.SafeMovements.Add(firstCash);
        db.WasteEntries.Add(new WasteEntry
        {
            WorkingDayId = firstDay.Id,
            ItemId = item.Id,
            UnitId = item.BaseUnitId,
            Quantity = 2m,
            UnitCost = 3m,
            WasteCost = 6m,
            Reason = "first day"
        });
        db.ProductionOrders.Add(new ProductionOrder
        {
            ProductionNumber = $"BD-PR1-{suffix}",
            WorkingDayId = firstDay.Id,
            StartedAt = new DateTime(2035, 1, 1, 22, 55, 0, DateTimeKind.Utc),
            Status = ProductionStatus.Completed
        });
        await db.SaveChangesAsync();

        firstDay.Status = WorkingDayStatus.Closed;
        firstDay.ClosedAt = new DateTime(2035, 1, 2, 7, 30, 0, DateTimeKind.Utc);
        firstDay.ClosedBy = "business-date-test";
        await db.SaveChangesAsync();
        db.WorkingDays.Add(secondDay);
        await db.SaveChangesAsync();

        db.SaleInvoices.Add(new SaleInvoice
        {
            InvoiceNumber = $"BD-S2-{suffix}",
            InvoiceDate = new DateTime(2035, 1, 2, 10, 0, 0, DateTimeKind.Utc),
            WorkingDayId = secondDay.Id,
            PartyId = customer.Id,
            PaymentType = PaymentType.Cash,
            Status = InvoiceStatus.Posted,
            TotalAmount = 999m,
            PaidAmount = 999m
        });
        db.PurchaseInvoices.Add(new PurchaseInvoice
        {
            InvoiceNumber = $"BD-P2-{suffix}",
            InvoiceDate = new DateTime(2035, 1, 2, 11, 0, 0, DateTimeKind.Utc),
            WorkingDayId = secondDay.Id,
            PartyId = supplier.Id,
            PaymentType = PaymentType.Cash,
            Status = InvoiceStatus.Posted,
            TotalAmount = 888m,
            PaidAmount = 888m
        });
        var secondCash = new SafeMovement
        {
            WorkingDayId = secondDay.Id,
            SafeId = safe.Id,
            Type = SafeMovementType.Adjustment,
            Amount = 777m,
            Description = "Second business day cash",
            CreatedAt = new DateTime(2035, 1, 2, 12, 0, 0, DateTimeKind.Utc)
        };
        db.SafeMovements.Add(secondCash);
        db.WasteEntries.Add(new WasteEntry
        {
            WorkingDayId = secondDay.Id,
            ItemId = item.Id,
            UnitId = item.BaseUnitId,
            Quantity = 9m,
            UnitCost = 3m,
            WasteCost = 27m,
            Reason = "second day"
        });
        db.ProductionOrders.Add(new ProductionOrder
        {
            ProductionNumber = $"BD-PR2-{suffix}",
            WorkingDayId = secondDay.Id,
            StartedAt = new DateTime(2035, 1, 2, 13, 0, 0, DateTimeKind.Utc),
            Status = ProductionStatus.Completed
        });
        await db.SaveChangesAsync();

        // Ensure the source timestamps are exactly the cross-midnight values even
        // if the audit interceptor initialized CreatedAt during insertion.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE SafeMovements SET CreatedAt = {new DateTime(2035, 1, 1, 22, 50, 0, DateTimeKind.Utc)} WHERE Id = {firstCash.Id};
            UPDATE SafeMovements SET CreatedAt = {new DateTime(2035, 1, 2, 12, 0, 0, DateTimeKind.Utc)} WHERE Id = {secondCash.Id};
            """);

        var reports = new AccountingReportService(
            db,
            scope.ServiceProvider.GetRequiredService<IPartyService>(),
            scope.ServiceProvider.GetRequiredService<IPermissionService>(),
            scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
            scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>(),
            businessDates,
            scope.ServiceProvider.GetRequiredService<IItemUnitConversionService>());

        (await reports.GetDailySalesAsync(firstDate)).Should().Be(100m);
        (await reports.GetDailyPurchasesAsync(firstDate)).Should().Be(40m);
        (await reports.GetCashMovementSummaryAsync(firstDate)).Should().Be(25m);
        (await reports.GetDailySalesAsync(secondDate)).Should().Be(999m);
        (await reports.GetDailyPurchasesAsync(secondDate)).Should().Be(888m);
        (await reports.GetCashMovementSummaryAsync(secondDate)).Should().Be(777m);
        (await reports.GetDailySalesAsync(firstDate.AddDays(-1))).Should().Be(0m);

        var currentSummary = await dayService.GetCurrentDaySummaryAsync();
        currentSummary!.BusinessDate.Should().Be(secondDate);
        currentSummary.TotalSales.Should().Be(999m);
        var trend = await dayService.GetRecentDashboardTrendAsync(2);
        trend.Single(point => point.BusinessDate == firstDate).Sales.Should().Be(100m);
        trend.Single(point => point.BusinessDate == secondDate).Sales.Should().Be(999m);

        var wasteSummary = await wasteService.GetTodaySummaryAsync();
        wasteSummary.TodayCount.Should().Be(1);
        wasteSummary.TodayQuantity.Should().Be(9m);
        wasteSummary.TodayCost.Should().Be(27m);
        var productionSummary = await productionService.GetProductionSummaryAsync();
        productionSummary.TodayOrdersCount.Should().Be(1);
    }
}
