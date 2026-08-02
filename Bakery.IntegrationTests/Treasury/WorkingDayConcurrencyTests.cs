using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Domain.Constants;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class WorkingDayConcurrencyTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WorkingDayConcurrencyTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentLifecycleAndStaleTerminal_ShouldRemainAtomicAndBalanced()
    {
        int dayId;
        int dailySafeId;
        int mainSafeId;

        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var service = setupScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
            var safes = setupScope.ServiceProvider.GetRequiredService<ISystemSafeService>();
            var opened = await service.OpenDayAsync(new OpenWorkingDayRequest(
                DateOnly.FromDateTime(DateTime.Today.AddDays(-40)), 500m, "Concurrency test"));
            opened.Succeeded.Should().BeTrue(opened.ErrorMessage);
            dayId = opened.Summary!.WorkingDayId;
            dailySafeId = (await safes.GetDailySafeAsync()).Id;
            mainSafeId = (await safes.GetMainSafeAsync()).Id;
        }

        using var staleScope = _fixture.ServiceProvider.CreateScope();
        var staleService = staleScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var staleDb = staleScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        (await staleService.EnsureActiveWorkingDayAsync()).Id.Should().Be(dayId);

        using var closeScope1 = _fixture.ServiceProvider.CreateScope();
        using var closeScope2 = _fixture.ServiceProvider.CreateScope();
        var closeTask1 = closeScope1.ServiceProvider.GetRequiredService<IWorkingDayService>()
            .CloseCurrentDayAsync(new CloseWorkingDayRequest(400m, 100m, "Concurrent close 1"));
        var closeTask2 = closeScope2.ServiceProvider.GetRequiredService<IWorkingDayService>()
            .CloseCurrentDayAsync(new CloseWorkingDayRequest(400m, 100m, "Concurrent close 2"));
        var closeResults = await Task.WhenAll(closeTask1, closeTask2);
        closeResults.Count(result => result.Succeeded).Should().Be(1);

        using (var verifyCloseScope = _fixture.ServiceProvider.CreateScope())
        {
            var db = verifyCloseScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            var closeMovements = await db.SafeMovements
                .Where(m => m.WorkingDayId == dayId && m.ReferenceType == LedgerReferenceTypes.WorkingDayClose)
                .ToListAsync();
            closeMovements.Should().HaveCount(2);
            closeMovements.Sum(m => m.Amount).Should().Be(0m);
            closeMovements.Select(m => m.TransferId).Distinct().Should().ContainSingle();
            (await BalanceAsync(db, dailySafeId)).Should().Be(100m);
            (await BalanceAsync(db, mainSafeId)).Should().Be(400m);
            (await db.AuditLogs.CountAsync(a => a.EntityName == nameof(WorkingDay) && a.EntityId == dayId && a.Action == "CloseDay"))
                .Should().Be(1);
        }

        staleDb.SafeMovements.Add(new SafeMovement
        {
            WorkingDayId = dayId,
            SafeId = dailySafeId,
            Type = SafeMovementType.Adjustment,
            Amount = 25m,
            Description = "Stale terminal operation"
        });
        Func<Task> staleSave = () => staleDb.SaveChangesAsync();
        await staleSave.Should().ThrowAsync<DbUpdateConcurrencyException>();

        using (var verifyRejectedScope = _fixture.ServiceProvider.CreateScope())
        {
            var db = verifyRejectedScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            (await db.SafeMovements.AnyAsync(m => m.Description == "Stale terminal operation")).Should().BeFalse();
        }

        using var reopenScope1 = _fixture.ServiceProvider.CreateScope();
        using var reopenScope2 = _fixture.ServiceProvider.CreateScope();
        var reopenTask1 = reopenScope1.ServiceProvider.GetRequiredService<IWorkingDayService>()
            .ReopenDayAsync(dayId, "تصحيح متزامن أول");
        var reopenTask2 = reopenScope2.ServiceProvider.GetRequiredService<IWorkingDayService>()
            .ReopenDayAsync(dayId, "تصحيح متزامن ثان");
        var reopenResults = await Task.WhenAll(reopenTask1, reopenTask2);
        reopenResults.Count(result => result.Succeeded).Should().Be(1);

        using var verifyReopenScope = _fixture.ServiceProvider.CreateScope();
        var verifyDb = verifyReopenScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        (await verifyDb.WorkingDays.SingleAsync(day => day.Id == dayId)).Status.Should().Be(WorkingDayStatus.Open);
        (await verifyDb.AuditLogs.CountAsync(a => a.EntityName == nameof(WorkingDay) && a.EntityId == dayId && a.Action == "ReopenDay"))
            .Should().Be(1);
        (await verifyDb.SafeMovements.CountAsync(m => m.WorkingDayId == dayId && m.ReferenceType == LedgerReferenceTypes.WorkingDayReopen))
            .Should().Be(2);
        (await BalanceAsync(verifyDb, dailySafeId)).Should().Be(500m);
        (await BalanceAsync(verifyDb, mainSafeId)).Should().Be(0m);
    }

    private static async Task<decimal> BalanceAsync(BakeryDbContext db, int safeId)
        => await db.SafeMovements.Where(m => m.SafeId == safeId).SumAsync(m => (decimal?)m.Amount) ?? 0m;
}
