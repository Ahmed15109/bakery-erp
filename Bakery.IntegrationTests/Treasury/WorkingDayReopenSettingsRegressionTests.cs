using System.Text.Json;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Bakery.Shared.Auditing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakery.IntegrationTests;

public sealed class WorkingDayReopenSettingsRegressionTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WorkingDayReopenSettingsRegressionTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AutoOpenedSuccessor_IsDisplayedSeparately_AndOnlyAnEmptySuccessorCanBeDiscarded()
    {
        var businessDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-120));
        int closedDayId;
        int firstSuccessorId;

        using (var closeScope = _fixture.ServiceProvider.CreateScope())
        {
            var service = closeScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
            var opened = await service.OpenDayAsync(new OpenWorkingDayRequest(
                businessDate,
                0,
                "إعادة إنتاج عيب شاشة الإعدادات"));
            opened.Succeeded.Should().BeTrue(opened.ErrorMessage);
            closedDayId = opened.Summary!.WorkingDayId;

            var closed = await service.EndCurrentDayAndOpenNextAsync(new CloseWorkingDayRequest(
                0,
                0,
                "إغلاق وفتح اليوم التالي تلقائياً",
                ExpectedWorkingDayId: closedDayId,
                OperationId: Guid.NewGuid()));
            closed.Succeeded.Should().BeTrue(closed.ErrorMessage);
            closed.Summary!.Status.Should().Be(WorkingDayStatus.Open);
            closed.Summary.BusinessDate.Should().Be(businessDate.AddDays(1));
            firstSuccessorId = closed.Summary.WorkingDayId;
        }

        using (var eligibilityScope = _fixture.ServiceProvider.CreateScope())
        {
            var service = eligibilityScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
            var eligibility = await service.GetReopenEligibilityAsync();

            eligibility.CurrentActiveDay.Should().NotBeNull();
            eligibility.CurrentActiveDay!.WorkingDayId.Should().Be(firstSuccessorId);
            eligibility.CurrentActiveDay.BusinessDate.Should().Be(businessDate.AddDays(1));
            eligibility.CurrentActiveDay.Status.Should().Be(WorkingDayStatus.Open);
            eligibility.LastClosedDay.Should().NotBeNull();
            eligibility.LastClosedDay!.WorkingDayId.Should().Be(closedDayId);
            eligibility.LastClosedDay.BusinessDate.Should().Be(businessDate);
            eligibility.LastClosedDay.Status.Should().Be(WorkingDayStatus.Closed);
            eligibility.LastClosedDay.LastClosedBy.Should().Be("test-admin");
            eligibility.LastClosedDay.LastClosedAt.Should().NotBeNull();
            eligibility.CanReopen.Should().BeTrue(eligibility.StatusMessage);
            eligibility.StatusMessage.Should().Contain(businessDate.AddDays(1).ToString("dd/MM/yyyy"));

            var reopened = await service.ReopenDayAsync(closedDayId, "تصحيح إغلاق يوم العمل السابق");
            reopened.Succeeded.Should().BeTrue(reopened.ErrorMessage);
            reopened.Summary!.WorkingDayId.Should().Be(closedDayId);
            reopened.Summary.Status.Should().Be(WorkingDayStatus.Open);
        }

        using (var verifyScope = _fixture.ServiceProvider.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            var days = await db.WorkingDays.IgnoreQueryFilters()
                .Where(day => day.Id == closedDayId || day.Id == firstSuccessorId)
                .ToListAsync();
            days.Single(day => day.Id == closedDayId).Status.Should().Be(WorkingDayStatus.Open);
            days.Single(day => day.Id == closedDayId).IsDeleted.Should().BeFalse();
            days.Single(day => day.Id == firstSuccessorId).IsDeleted.Should().BeFalse(
                "the cancelled day remains available as lifecycle and audit evidence");
            days.Single(day => day.Id == firstSuccessorId).Status.Should().Be(WorkingDayStatus.Cancelled,
                "the empty automatically opened successor is cancelled atomically");
            (await db.WorkingDays.CountAsync(day => day.Status == WorkingDayStatus.Open)).Should().Be(1);

            var discardAudit = await db.AuditLogs.SingleAsync(audit =>
                audit.EntityName == nameof(WorkingDay) &&
                audit.EntityId == firstSuccessorId &&
                audit.Action == AuditActionKeys.WorkingDayEmptySuccessorDiscarded);
            using var discardJson = JsonDocument.Parse(discardAudit.NewValues!);
            discardJson.RootElement.GetProperty("DiscardedWorkingDayId").GetInt32().Should().Be(firstSuccessorId);
            discardJson.RootElement.GetProperty("ReopenedWorkingDayId").GetInt32().Should().Be(closedDayId);
            discardJson.RootElement.GetProperty("Result").GetString().Should().Be("Succeeded");
            (await db.AuditLogs.CountAsync(audit =>
                audit.EntityName == nameof(WorkingDay) &&
                audit.EntityId == closedDayId &&
                audit.Action == AuditActionKeys.WorkingDayReopened)).Should().Be(1);
        }

        int blockedSuccessorId;
        using (var secondCloseScope = _fixture.ServiceProvider.CreateScope())
        {
            var service = secondCloseScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
            var closedAgain = await service.EndCurrentDayAndOpenNextAsync(new CloseWorkingDayRequest(
                0,
                0,
                "إغلاق ثانٍ لاختبار مانع العمليات",
                ExpectedWorkingDayId: closedDayId,
                OperationId: Guid.NewGuid()));
            closedAgain.Succeeded.Should().BeTrue(closedAgain.ErrorMessage);
            blockedSuccessorId = closedAgain.Summary!.WorkingDayId;
        }

        using (var activityScope = _fixture.ServiceProvider.CreateScope())
        {
            var db = activityScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            var safes = activityScope.ServiceProvider.GetRequiredService<ISystemSafeService>();
            var dailySafe = await safes.GetDailySafeAsync();
            db.SafeMovements.Add(new SafeMovement
            {
                WorkingDayId = blockedSuccessorId,
                SafeId = dailySafe.Id,
                Type = SafeMovementType.Adjustment,
                Amount = 25,
                Description = "حركة تمنع إعادة فتح اليوم السابق"
            });
            await db.SaveChangesAsync();
        }

        using (var blockedScope = _fixture.ServiceProvider.CreateScope())
        {
            var service = blockedScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
            var eligibility = await service.GetReopenEligibilityAsync();
            eligibility.CanReopen.Should().BeFalse();
            eligibility.LastClosedDay!.WorkingDayId.Should().Be(closedDayId);
            eligibility.BlockingReasons.Should().Contain(reason => reason.Contains("حركات خزينة"));
            eligibility.StatusMessage.Should().Contain("حركات خزينة");
            eligibility.Blockers.Should().ContainSingle();
            eligibility.Blockers!.Single().ActionKind.Should().Be(WorkingDayReopenActionKind.None);
            eligibility.Blockers.Single().CanResolve.Should().BeFalse();
            eligibility.Blockers.Single().UnsupportedMessage.Should().Be("لا يمكن التراجع عن هذه العملية تلقائياً");

            var rejected = await service.ReopenDayAsync(closedDayId, "محاولة يجب رفضها لوجود حركة");
            rejected.Succeeded.Should().BeFalse();
            rejected.ErrorMessage.Should().Contain("حركات خزينة");
        }

        using (var finalScope = _fixture.ServiceProvider.CreateScope())
        {
            var db = finalScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            (await db.WorkingDays.SingleAsync(day => day.Id == closedDayId)).Status
                .Should().Be(WorkingDayStatus.Closed);
            (await db.WorkingDays.SingleAsync(day => day.Id == blockedSuccessorId)).Status
                .Should().Be(WorkingDayStatus.Open);
        }
    }
}
