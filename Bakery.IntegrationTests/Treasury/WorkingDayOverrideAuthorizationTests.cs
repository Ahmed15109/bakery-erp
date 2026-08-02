using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakery.IntegrationTests;

public sealed class WorkingDayOverrideAuthorizationTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WorkingDayOverrideAuthorizationTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CloseOverride_RequiresDedicatedPermission_AndSucceedsWhenGranted()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        var opened = await service.OpenDayAsync(
            new OpenWorkingDayRequest(new DateOnly(2040, 1, 10), 100m, "Override authorization test"));
        opened.Succeeded.Should().BeTrue(opened.ErrorMessage);
        var dayId = opened.Summary!.WorkingDayId;

        db.StockCountSessions.Add(new StockCountSession
        {
            StartedBy = "test-admin",
            IsCompleted = false
        });
        await db.SaveChangesAsync();

        session.SignIn(new AuthenticatedUserDto(
            1,
            "close-operator",
            "Close Operator",
            [PermissionKeys.WorkingDayView, PermissionKeys.WorkingDayClose],
            false));

        var denied = await service.CloseCurrentDayAsync(new CloseWorkingDayRequest(
            0m,
            100m,
            "Unauthorized override",
            AdminOverride: true,
            OverrideReason: "Must be rejected"));

        denied.Succeeded.Should().BeFalse();
        denied.ErrorMessage.Should().Contain("صلاحية التجاوز الإداري");
        (await db.WorkingDays.SingleAsync(day => day.Id == dayId)).Status.Should().Be(WorkingDayStatus.Open);
        (await db.AuditLogs.AnyAsync(audit =>
            audit.Action == "AuthorizationDenied" &&
            audit.NewValues!.Contains(PermissionKeys.WorkingDayOverrideCloseBlockers))).Should().BeTrue();

        session.SignIn(new AuthenticatedUserDto(
            1,
            "override-manager",
            "Override Manager",
            [
                PermissionKeys.WorkingDayView,
                PermissionKeys.WorkingDayClose,
                PermissionKeys.WorkingDayOverrideCloseBlockers
            ],
            false));

        var result = await service.CloseCurrentDayAsync(new CloseWorkingDayRequest(
            0m,
            100m,
            "Authorized override",
            AdminOverride: true,
            OverrideReason: "Approved exception"));

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        (await db.WorkingDays.SingleAsync(day => day.Id == dayId)).Status.Should().Be(WorkingDayStatus.Closed);
        var audit = await db.AuditLogs.SingleAsync(item =>
            item.Action == "CloseDay" && item.EntityId == dayId);
        audit.NewValues.Should().Contain("Approved exception");
    }
}
