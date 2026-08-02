using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Xunit;

namespace Bakery.IntegrationTests;

public class DashboardPerformanceTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public DashboardPerformanceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DashboardMetrics_ShouldLoadWithinReasonableTime()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var stockService      = scope.ServiceProvider.GetRequiredService<IStockCalculationService>();

        // Ensure a day is open for reporting to work
        if (await workingDayService.GetCurrentOpenDayAsync() == null)
            await workingDayService.OpenDayAsync(new OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today), 0m, "Perf Test"));

        var sw = Stopwatch.StartNew();

        // Simulate what the dashboard does
        var summary     = await workingDayService.GetCurrentDaySummaryAsync();
        var trend       = await workingDayService.GetRecentDashboardTrendAsync();
        var lowStock    = await stockService.GetLowStockItemsAsync();

        sw.Stop();

        summary.Should().NotBeNull();
        trend.Should().NotBeNull();
        trend.Select(point => point.BusinessDate).Should().BeInAscendingOrder();
        lowStock.Should().NotBeNull();
        sw.ElapsedMilliseconds.Should().BeLessThan(2000,
            "Dashboard queries must return in under 2 seconds even with aggregated data.");
    }
}
