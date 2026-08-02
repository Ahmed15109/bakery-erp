using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class WorkingDayWorkflowTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WorkingDayWorkflowTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CompleteWorkflow_ShouldMaintainIntegrity()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();

        // 1. Open Working Day
        var openRequest = new OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today), 500m, "Integration Test Day");
        var openResult = await workingDayService.OpenDayAsync(openRequest);
        openResult.Succeeded.Should().BeTrue("Day should open successfully");

        // Verify active day
        var activeDay = await workingDayService.GetCurrentOpenDayAsync();
        activeDay.Should().NotBeNull("An open day should exist");
        activeDay!.Status.Should().Be(WorkingDayStatus.Open);

        // 2. Close day with treasury transfer
        // Request: TransferredToMainSafe=400, CarryOverBalance=100 (Total Actual=500), Notes, AdminOverride=true, Reason
        var closeRequest = new CloseWorkingDayRequest(400m, 100m, "Integration Test close", true, "Test Override");
        var closeResult = await workingDayService.CloseCurrentDayAsync(closeRequest);
        closeResult.Succeeded.Should().BeTrue("Day should close successfully");

        // Verify day is closed
        var closedDay = await workingDayService.GetCurrentOpenDayAsync();
        closedDay.Should().BeNull("No open day should exist after closing");
        
        // Verify summary values
        closeResult.Summary.Should().NotBeNull();
        closeResult.Summary!.TransferredToMainSafe.Should().Be(400m);
        closeResult.Summary!.CarryOverBalance.Should().Be(100m);
    }

    [Fact]
    public async Task GetClosingReport_ShouldExecuteSuccessfully()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();

        // Open Day
        var openRequest = new OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today.AddDays(-5)), 100m, "Report Test Day");
        var openResult = await workingDayService.OpenDayAsync(openRequest);
        openResult.Succeeded.Should().BeTrue();

        var activeDay = await workingDayService.GetCurrentOpenDayAsync();
        activeDay.Should().NotBeNull();

        // Get closing report
        var report = await workingDayService.GetClosingReportAsync(activeDay!.Id);
        report.Should().NotBeNull();
        report!.DaySummary.Should().NotBeNull();
        report.TopProducts.Should().NotBeNull();
        report.Settlements.Should().NotBeNull();
        report.Expenses.Should().NotBeNull();

        // Clean up: Close day so as not to affect other tests
        var closeRequest = new CloseWorkingDayRequest(0m, 100m, "Clean up close", true, "Clean up");
        var closeResult = await workingDayService.CloseCurrentDayAsync(closeRequest);
        closeResult.Succeeded.Should().BeTrue();
    }
}
