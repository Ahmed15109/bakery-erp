using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Constants;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class WorkingDayLifecycleHardeningTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WorkingDayLifecycleHardeningTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FullLifecycle_ShouldAuditAndReverseSafeTransfersWithoutDuplication()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var systemSafes = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var businessDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-20));

        var opened = await service.OpenDayAsync(new OpenWorkingDayRequest(businessDate, 1_000m, "Lifecycle test"));
        opened.Succeeded.Should().BeTrue(opened.ErrorMessage);
        var dayId = opened.Summary!.WorkingDayId;

        var duplicateOpen = await service.OpenDayAsync(new OpenWorkingDayRequest(businessDate.AddDays(1), 0, null));
        duplicateOpen.Succeeded.Should().BeFalse("only one working day may be open");

        var firstClose = await service.CloseCurrentDayAsync(new CloseWorkingDayRequest(700m, 300m, "First close"));
        firstClose.Succeeded.Should().BeTrue(firstClose.ErrorMessage);
        var firstReport = await service.GetClosingReportAsync(dayId);
        firstReport.Should().NotBeNull();
        firstReport!.DaySummary.LastClosedAt.Should().NotBeNull();
        firstReport.DaySummary.LastClosedBy.Should().Be("test-admin");

        var dailySafe = await systemSafes.GetDailySafeAsync();
        var mainSafe = await systemSafes.GetMainSafeAsync();
        (await BalanceAsync(db, dailySafe.Id)).Should().Be(300m);
        (await BalanceAsync(db, mainSafe.Id)).Should().Be(700m);

        var firstCloseMovements = await db.SafeMovements
            .Where(m => m.WorkingDayId == dayId && m.ReferenceType == LedgerReferenceTypes.WorkingDayClose)
            .ToListAsync();
        firstCloseMovements.Should().HaveCount(2);
        firstCloseMovements.Select(m => m.TransferId).Distinct().Should().ContainSingle();
        (await db.AuditLogs.CountAsync(a => a.EntityName == nameof(WorkingDay) && a.EntityId == dayId && a.Action == "CloseDay"))
            .Should().Be(1);

        var duplicateClose = await service.CloseCurrentDayAsync(new CloseWorkingDayRequest(0, 0, null));
        duplicateClose.Succeeded.Should().BeFalse("a closed day cannot be closed again");

        var missingReason = await service.ReopenDayAsync(dayId, "   ");
        missingReason.Succeeded.Should().BeFalse("reopen reason is mandatory in the backend");
        (await BalanceAsync(db, dailySafe.Id)).Should().Be(300m);
        (await BalanceAsync(db, mainSafe.Id)).Should().Be(700m);

        session.SignIn(new AuthenticatedUserDto(
            1,
            "test-operator",
            "Test Operator",
            [PermissionKeys.WorkingDayOpen, PermissionKeys.WorkingDayClose],
            false));
        Func<Task> unauthorizedReport = async () => await service.GetClosingReportAsync(dayId);
        await unauthorizedReport.Should().ThrowAsync<UnauthorizedAccessException>("financial report permission is enforced by the service");
        var unauthorized = await service.ReopenDayAsync(dayId, "Unauthorized attempt");
        unauthorized.Succeeded.Should().BeFalse("reopen requires its dedicated permission");

        session.SignIn(new AuthenticatedUserDto(
            1,
            "test-admin",
            "Test Admin",
            PermissionCatalog.All.Select(permission => permission.Key).ToArray(),
            true));
        var reopened = await service.ReopenDayAsync(dayId, "تصحيح حركة نقدية");
        reopened.Succeeded.Should().BeTrue(reopened.ErrorMessage);
        reopened.Summary!.Status.Should().Be(WorkingDayStatus.Open);
        reopened.Summary.ReopenCount.Should().Be(1);
        reopened.Summary.ReopenReason.Should().Be("تصحيح حركة نقدية");
        reopened.Summary.ReopenedBy.Should().Be("test-admin");

        var unchangedReopenReport = await service.GetClosingReportAsync(dayId);
        unchangedReopenReport.Should().NotBeNull();
        unchangedReopenReport!.DaySummary.ExpectedCash.Should().Be(firstReport.DaySummary.ExpectedCash);
        unchangedReopenReport.DaySummary.TotalSales.Should().Be(firstReport.DaySummary.TotalSales);
        unchangedReopenReport.DaySummary.TotalPurchases.Should().Be(firstReport.DaySummary.TotalPurchases);
        unchangedReopenReport.DaySummary.LastClosedAt.Should().NotBeNull("close history remains visible after reopening");

        db.ChangeTracker.Clear();
        firstCloseMovements = await db.SafeMovements
            .Where(m => m.WorkingDayId == dayId && m.ReferenceType == LedgerReferenceTypes.WorkingDayClose)
            .ToListAsync();
        firstCloseMovements.Should().OnlyContain(m => m.IsReversed && m.ReverseTransactionId.HasValue);
        (await db.SafeMovements.CountAsync(m => m.WorkingDayId == dayId && m.ReferenceType == LedgerReferenceTypes.WorkingDayReopen))
            .Should().Be(2);
        (await BalanceAsync(db, dailySafe.Id)).Should().Be(1_000m);
        (await BalanceAsync(db, mainSafe.Id)).Should().Be(0m);

        db.SafeMovements.Add(new SafeMovement
        {
            WorkingDayId = dayId,
            SafeId = dailySafe.Id,
            Type = SafeMovementType.Adjustment,
            Amount = 50m,
            Description = "تصحيح بعد إعادة الفتح"
        });
        await db.SaveChangesAsync();

        var secondClose = await service.CloseCurrentDayAsync(new CloseWorkingDayRequest(750m, 300m, "Second close"));
        secondClose.Succeeded.Should().BeTrue(secondClose.ErrorMessage);
        secondClose.Summary!.ExpectedCash.Should().Be(1_050m);
        (await BalanceAsync(db, dailySafe.Id)).Should().Be(300m);
        (await BalanceAsync(db, mainSafe.Id)).Should().Be(750m, "the first close transfer was reversed before the second close");
        (await db.SafeMovements.CountAsync(m => m.WorkingDayId == dayId &&
            m.ReferenceType == LedgerReferenceTypes.WorkingDayClose && !m.IsReversed)).Should().Be(2);
        (await db.AuditLogs.CountAsync(a => a.EntityName == nameof(WorkingDay) && a.EntityId == dayId && a.Action == "CloseDay"))
            .Should().Be(2);

        var report = await service.GetClosingReportAsync(dayId);
        report.Should().NotBeNull();
        report!.DaySummary.ExpectedCash.Should().Be(1_050m);
        report.DaySummary.ReopenCount.Should().Be(1);
        report.DaySummary.LastClosedAt.Should().NotBeNull();

        var lifecycleAudits = await db.AuditLogs
            .Where(a => a.EntityName == nameof(WorkingDay) && a.EntityId == dayId)
            .ToListAsync();
        lifecycleAudits.Should().Contain(a => a.Action == "OpenDay" && a.NewValues!.Contains("Succeeded"));
        lifecycleAudits.Should().Contain(a => a.Action == "CloseDay" && a.NewValues!.Contains("Succeeded"));
        var reopenAudit = lifecycleAudits.Single(a => a.Action == "ReopenDay");
        reopenAudit.BranchId.Should().BeGreaterThan(0);
        reopenAudit.UserId.Should().Be(1);
        reopenAudit.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        using (var reopenJson = JsonDocument.Parse(reopenAudit.NewValues!))
        {
            reopenJson.RootElement.GetProperty("Reason").GetString().Should().Be("تصحيح حركة نقدية");
            reopenJson.RootElement.GetProperty("Result").GetString().Should().Be("Succeeded");
            reopenJson.RootElement.GetProperty("WorkingDayId").GetInt32().Should().Be(dayId);
            reopenJson.RootElement.GetProperty("BusinessDate").GetString().Should().Be(businessDate.ToString("yyyy-MM-dd"));
            reopenJson.RootElement.GetProperty("BranchId").GetInt32().Should().Be(reopenAudit.BranchId);
            reopenJson.RootElement.GetProperty("UserId").GetInt32().Should().Be(1);
            reopenJson.RootElement.GetProperty("PreviousStatus").GetString().Should().Be(nameof(WorkingDayStatus.Closed));
            reopenJson.RootElement.GetProperty("NewStatus").GetString().Should().Be(nameof(WorkingDayStatus.Open));
            reopenJson.RootElement.GetProperty("Timestamp").GetDateTime().Should().BeCloseTo(
                reopenAudit.OccurredAt,
                TimeSpan.FromSeconds(5));
        }

        var newerOpen = await service.OpenDayAsync(new OpenWorkingDayRequest(businessDate.AddDays(1), 0, "Newer day"));
        newerOpen.Succeeded.Should().BeTrue(newerOpen.ErrorMessage);
        var newerClose = await service.CloseCurrentDayAsync(new CloseWorkingDayRequest(0, 300m, "Close newer day"));
        newerClose.Succeeded.Should().BeTrue(newerClose.ErrorMessage);

        var staleReopen = await service.ReopenDayAsync(dayId, "Must be rejected");
        staleReopen.Succeeded.Should().BeFalse("a day cannot be reopened when a newer business day exists");
        (await db.WorkingDays.SingleAsync(d => d.Id == dayId)).Status.Should().Be(WorkingDayStatus.Closed);
    }

    private static async Task<decimal> BalanceAsync(BakeryDbContext db, int safeId)
    {
        return await db.SafeMovements
            .Where(m => m.SafeId == safeId)
            .SumAsync(m => (decimal?)m.Amount) ?? 0m;
    }
}
