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

public sealed class WorkingDayAutoSuccessorConcurrencyTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WorkingDayAutoSuccessorConcurrencyTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentReopen_WithEmptyAutoOpenedSuccessor_SucceedsOnlyOnce()
    {
        var businessDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-150));
        int closedDayId;
        int successorId;

        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var service = setupScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
            var opened = await service.OpenDayAsync(new OpenWorkingDayRequest(businessDate, 0, "اختبار تزامن إعادة الفتح"));
            opened.Succeeded.Should().BeTrue(opened.ErrorMessage);
            closedDayId = opened.Summary!.WorkingDayId;

            var closed = await service.EndCurrentDayAndOpenNextAsync(new CloseWorkingDayRequest(
                0,
                0,
                "إغلاق متزامن",
                ExpectedWorkingDayId: closedDayId,
                OperationId: Guid.NewGuid()));
            closed.Succeeded.Should().BeTrue(closed.ErrorMessage);
            successorId = closed.Summary!.WorkingDayId;
        }

        using var reopenScope1 = _fixture.ServiceProvider.CreateScope();
        using var reopenScope2 = _fixture.ServiceProvider.CreateScope();
        var reopen1 = reopenScope1.ServiceProvider.GetRequiredService<IWorkingDayService>()
            .ReopenDayAsync(closedDayId, "تصحيح متزامن لليوم المغلق");
        var reopen2 = reopenScope2.ServiceProvider.GetRequiredService<IWorkingDayService>()
            .ReopenDayAsync(closedDayId, "محاولة متزامنة ثانية للتصحيح");
        var results = await Task.WhenAll(reopen1, reopen2);

        results.Count(result => result.Succeeded).Should().Be(1);

        using var verifyScope = _fixture.ServiceProvider.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var target = await db.WorkingDays.SingleAsync(day => day.Id == closedDayId);
        var successor = await db.WorkingDays.IgnoreQueryFilters().SingleAsync(day => day.Id == successorId);
        target.Status.Should().Be(WorkingDayStatus.Open);
        successor.IsDeleted.Should().BeFalse();
        successor.Status.Should().Be(WorkingDayStatus.Cancelled);
        (await db.AuditLogs.CountAsync(audit =>
            audit.EntityName == nameof(WorkingDay) &&
            audit.EntityId == successorId &&
            audit.Action == AuditActionKeys.WorkingDayEmptySuccessorDiscarded)).Should().Be(1);
        (await db.AuditLogs.CountAsync(audit =>
            audit.EntityName == nameof(WorkingDay) &&
            audit.EntityId == closedDayId &&
            audit.Action == AuditActionKeys.WorkingDayReopened)).Should().Be(1);
    }
}
