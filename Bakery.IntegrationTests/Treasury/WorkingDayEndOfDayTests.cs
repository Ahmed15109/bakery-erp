using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
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

public sealed class WorkingDayEndOfDayTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WorkingDayEndOfDayTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EndOfDay_ShouldCloseCurrentDayAndOpenNextBusinessDateAtomically()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var systemSafes = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var closedBusinessDate = new DateOnly(2031, 11, 24);

        var opened = await service.OpenDayAsync(
            new OpenWorkingDayRequest(closedBusinessDate, 1_000m, "End-of-day test"));
        opened.Succeeded.Should().BeTrue(opened.ErrorMessage);
        var closedDayId = opened.Summary!.WorkingDayId;

        var closeRequest = new CloseWorkingDayRequest(
            700m,
            300m,
            "تم الإغلاق وفتح اليوم التالي",
            ExpectedWorkingDayId: closedDayId,
            OperationId: Guid.NewGuid());
        var result = await service.EndCurrentDayAndOpenNextAsync(closeRequest);

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        result.Summary.Should().NotBeNull();
        result.Summary!.WorkingDayId.Should().NotBe(closedDayId);
        result.Summary.BusinessDate.Should().Be(closedBusinessDate.AddDays(1),
            "the next date must be derived from the closed business date");
        result.Summary.Status.Should().Be(WorkingDayStatus.Open);
        result.Summary.OpeningCash.Should().Be(300m);
        result.Summary.DailySafeBalance.Should().Be(300m);
        result.Summary.TotalSales.Should().Be(0m);
        result.Summary.TotalPurchases.Should().Be(0m);
        result.Summary.InvoiceCount.Should().Be(0);
        result.Summary.TransactionCount.Should().Be(0);

        db.ChangeTracker.Clear();
        var closedDay = await db.WorkingDays.SingleAsync(day => day.Id == closedDayId);
        var nextDay = await db.WorkingDays.SingleAsync(day => day.Id == result.Summary.WorkingDayId);
        closedDay.Status.Should().Be(WorkingDayStatus.Closed);
        closedDay.BusinessDate.Should().Be(closedBusinessDate);
        closedDay.ClosingCash.Should().Be(1_000m);
        closedDay.TransferredToMainSafe.Should().Be(700m);
        closedDay.CarryOverBalance.Should().Be(300m);
        nextDay.Status.Should().Be(WorkingDayStatus.Open);
        nextDay.BusinessDate.Should().Be(closedBusinessDate.AddDays(1));
        nextDay.BranchId.Should().Be(closedDay.BranchId);

        (await db.WorkingDays.CountAsync()).Should().Be(2);
        (await db.WorkingDays.CountAsync(day => day.Status == WorkingDayStatus.Open)).Should().Be(1);
        (await db.WorkingDays.CountAsync(day => day.BusinessDate == closedBusinessDate.AddDays(1))).Should().Be(1);

        var closeMovements = await db.SafeMovements
            .Where(movement => movement.WorkingDayId == closedDayId &&
                movement.ReferenceType == LedgerReferenceTypes.WorkingDayClose)
            .ToListAsync();
        closeMovements.Should().HaveCount(2);
        closeMovements.Select(movement => movement.TransferId).Distinct().Should().ContainSingle();
        closeMovements.Sum(movement => movement.Amount).Should().Be(0m);
        (await db.SafeMovements.CountAsync(movement => movement.WorkingDayId == nextDay.Id))
            .Should().Be(0, "the carry-over is existing cash and must not be posted twice");

        var dailySafe = await systemSafes.GetDailySafeAsync();
        var mainSafe = await systemSafes.GetMainSafeAsync();
        (await BalanceAsync(db, dailySafe.Id)).Should().Be(300m);
        (await BalanceAsync(db, mainSafe.Id)).Should().Be(700m);

        (await db.AuditLogs.CountAsync(audit => audit.EntityName == nameof(WorkingDay) &&
            audit.EntityId == closedDayId && audit.Action == "CloseDay")).Should().Be(1);
        var openAudit = await db.AuditLogs.SingleAsync(audit => audit.EntityName == nameof(WorkingDay) &&
            audit.EntityId == nextDay.Id && audit.Action == "OpenDay");
        using (var auditJson = JsonDocument.Parse(openAudit.NewValues!))
        {
            auditJson.RootElement.GetProperty("Source").GetString().Should().Be("EndOfDay");
            auditJson.RootElement.GetProperty("PreviousWorkingDayId").GetInt32().Should().Be(closedDayId);
        }

        var currentSummary = await service.GetCurrentDaySummaryAsync();
        currentSummary!.WorkingDayId.Should().Be(nextDay.Id);
        currentSummary.BusinessDate.Should().Be(closedBusinessDate.AddDays(1));

        var retryResult = await service.EndCurrentDayAndOpenNextAsync(closeRequest);
        retryResult.Succeeded.Should().BeTrue(retryResult.ErrorMessage);
        retryResult.WasAlreadyCompleted.Should().BeTrue();
        retryResult.Summary!.WorkingDayId.Should().Be(nextDay.Id);
        (await db.WorkingDays.CountAsync()).Should().Be(2);
        (await db.AuditLogs.CountAsync(audit => audit.EntityName == nameof(WorkingDay) &&
            audit.EntityId == closedDayId && audit.Action == "CloseDay")).Should().Be(1);

        var staleDialogResult = await service.EndCurrentDayAndOpenNextAsync(
            new CloseWorkingDayRequest(
                0m,
                300m,
                "Stale dialog must not close the newly opened day",
                ExpectedWorkingDayId: closedDayId,
                OperationId: Guid.NewGuid()));
        staleDialogResult.Succeeded.Should().BeFalse();
        staleDialogResult.ErrorMessage.Should().Contain("تم تغيير يوم العمل النشط");
        (await db.WorkingDays.CountAsync()).Should().Be(2);
        (await db.WorkingDays.SingleAsync(day => day.Id == nextDay.Id)).Status.Should().Be(WorkingDayStatus.Open);

        var closedReport = await service.GetClosingReportAsync(closedDayId);
        closedReport!.DaySummary.Status.Should().Be(WorkingDayStatus.Closed);
        closedReport.DaySummary.ActualCash.Should().Be(1_000m);
    }

    private static async Task<decimal> BalanceAsync(BakeryDbContext db, int safeId)
    {
        return await db.SafeMovements
            .Where(movement => movement.SafeId == safeId)
            .SumAsync(movement => (decimal?)movement.Amount) ?? 0m;
    }
}

public sealed class WorkingDayEndOfDayRollbackTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WorkingDayEndOfDayRollbackTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EndOfDay_WhenNextDateAlreadyExistsClosed_ShouldRollbackEntireClose()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var systemSafes = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var businessDate = new DateOnly(2032, 3, 8);

        var opened = await service.OpenDayAsync(new OpenWorkingDayRequest(businessDate, 500m, null));
        opened.Succeeded.Should().BeTrue(opened.ErrorMessage);
        var currentDayId = opened.Summary!.WorkingDayId;
        var currentDay = await db.WorkingDays.SingleAsync(day => day.Id == currentDayId);

        db.WorkingDays.Add(new WorkingDay
        {
            BranchId = currentDay.BranchId,
            BusinessDate = businessDate.AddDays(1),
            Status = WorkingDayStatus.Closed,
            OpenedAt = DateTime.UtcNow.AddDays(-1),
            ClosedAt = DateTime.UtcNow,
            OpenedBy = "test-admin",
            ClosedBy = "test-admin",
            Notes = "Existing closed next date"
        });
        await db.SaveChangesAsync();

        var result = await service.EndCurrentDayAndOpenNextAsync(
            new CloseWorkingDayRequest(
                400m,
                100m,
                "Must roll back",
                ExpectedWorkingDayId: currentDayId,
                OperationId: Guid.NewGuid()));

        result.Succeeded.Should().BeFalse("an existing closed next date cannot be duplicated or replaced");
        result.ErrorMessage.Should().StartWith("تعذر إنهاء يوم العمل وفتح اليوم التالي");

        db.ChangeTracker.Clear();
        var persistedCurrentDay = await db.WorkingDays.SingleAsync(day => day.Id == currentDayId);
        persistedCurrentDay.Status.Should().Be(WorkingDayStatus.Open);
        persistedCurrentDay.ClosedAt.Should().BeNull();
        persistedCurrentDay.ClosingCash.Should().BeNull();
        (await db.WorkingDays.CountAsync()).Should().Be(2);
        (await db.WorkingDays.CountAsync(day => day.Status == WorkingDayStatus.Open)).Should().Be(1);
        (await db.SafeMovements.CountAsync(movement => movement.WorkingDayId == currentDayId &&
            movement.ReferenceType == LedgerReferenceTypes.WorkingDayClose)).Should().Be(0);
        (await db.AuditLogs.CountAsync(audit => audit.EntityName == nameof(WorkingDay) &&
            audit.EntityId == currentDayId && audit.Action == "CloseDay")).Should().Be(0);

        var dailySafe = await systemSafes.GetDailySafeAsync();
        var mainSafe = await systemSafes.GetMainSafeAsync();
        (await db.SafeMovements.Where(movement => movement.SafeId == dailySafe.Id)
            .SumAsync(movement => (decimal?)movement.Amount) ?? 0m).Should().Be(500m);
        (await db.SafeMovements.Where(movement => movement.SafeId == mainSafe.Id)
            .SumAsync(movement => (decimal?)movement.Amount) ?? 0m).Should().Be(0m);

        var currentSummary = await service.GetCurrentDaySummaryAsync();
        currentSummary!.WorkingDayId.Should().Be(currentDayId);
        currentSummary.Status.Should().Be(WorkingDayStatus.Open);
    }
}

public sealed class WorkingDayEndOfDayStockCountBlockerTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WorkingDayEndOfDayStockCountBlockerTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EndOfDay_WithIncompleteStockCount_ShouldReturnBlockerWithoutChangingDay()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var opened = await service.OpenDayAsync(
            new OpenWorkingDayRequest(new DateOnly(2033, 5, 12), 500m, null));
        opened.Succeeded.Should().BeTrue(opened.ErrorMessage);
        var dayId = opened.Summary!.WorkingDayId;

        var stockCount = new StockCountSession
        {
            StartedAt = DateTime.UtcNow,
            StartedBy = "test-admin",
            IsCompleted = false
        };
        db.StockCountSessions.Add(stockCount);
        await db.SaveChangesAsync();

        var readiness = await service.GetEndOfDayReadinessAsync();
        readiness.Summary!.WorkingDayId.Should().Be(dayId);
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Kind == WorkingDayBlockerKind.StockCount && blocker.EntityId == stockCount.Id);

        var result = await service.EndCurrentDayAndOpenNextAsync(new CloseWorkingDayRequest(
            0m,
            500m,
            null,
            ExpectedWorkingDayId: dayId,
            OperationId: Guid.NewGuid()));

        result.Succeeded.Should().BeFalse();
        result.Blockers.Should().ContainSingle(blocker =>
            blocker.Kind == WorkingDayBlockerKind.StockCount && blocker.EntityId == stockCount.Id);
        result.ErrorMessage.Should().Contain($"توجد جلسة جرد غير مكتملة رقم {stockCount.Id}");

        db.ChangeTracker.Clear();
        var persistedDay = await db.WorkingDays.SingleAsync(day => day.Id == dayId);
        persistedDay.Status.Should().Be(WorkingDayStatus.Open);
        persistedDay.ClosedAt.Should().BeNull();
        (await db.WorkingDays.CountAsync()).Should().Be(1);
        (await db.SafeMovements.CountAsync(movement => movement.WorkingDayId == dayId &&
            movement.ReferenceType == LedgerReferenceTypes.WorkingDayClose)).Should().Be(0);
        (await db.AuditLogs.CountAsync(audit => audit.EntityName == nameof(WorkingDay) &&
            audit.EntityId == dayId && audit.Action == "CloseDay")).Should().Be(0);
    }
}

public sealed class WorkingDayEndOfDayMultipleBlockersTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public WorkingDayEndOfDayMultipleBlockersTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EndOfDay_WithMultipleBlockingOperations_ShouldReturnEveryBlockerTogether()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var systemSafes = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var opened = await service.OpenDayAsync(
            new OpenWorkingDayRequest(new DateOnly(2034, 8, 21), 500m, null));
        opened.Succeeded.Should().BeTrue(opened.ErrorMessage);
        var dayId = opened.Summary!.WorkingDayId;
        var customerId = await db.Parties.Where(party => party.Type == PartyType.Customer).Select(party => party.Id).FirstAsync();
        var supplierId = await db.Parties.Where(party => party.Type == PartyType.Supplier).Select(party => party.Id).FirstAsync();
        var dailySafe = await systemSafes.GetDailySafeAsync();
        var pendingTransferId = Guid.NewGuid();

        db.StockCountSessions.Add(new StockCountSession
        {
            StartedBy = "test-admin",
            IsCompleted = false
        });
        db.ProductionOrders.Add(new ProductionOrder
        {
            WorkingDayId = dayId,
            ProductionNumber = "PRD-BLOCK-1",
            Status = ProductionStatus.InProgress
        });
        db.SaleInvoices.Add(new SaleInvoice
        {
            WorkingDayId = dayId,
            InvoiceNumber = "SAL-BLOCK-1",
            PartyId = customerId,
            PaymentType = PaymentType.Cash,
            Status = InvoiceStatus.Draft
        });
        db.PurchaseInvoices.Add(new PurchaseInvoice
        {
            WorkingDayId = dayId,
            InvoiceNumber = "PUR-BLOCK-1",
            PartyId = supplierId,
            PaymentType = PaymentType.Cash,
            Status = InvoiceStatus.Draft
        });
        db.SafeMovements.Add(new SafeMovement
        {
            WorkingDayId = dayId,
            SafeId = dailySafe.Id,
            Type = SafeMovementType.TransferOut,
            Amount = -10m,
            Description = "Incomplete transfer test",
            TransferId = pendingTransferId
        });
        await db.SaveChangesAsync();

        var result = await service.EndCurrentDayAndOpenNextAsync(new CloseWorkingDayRequest(
            0m,
            490m,
            null,
            ExpectedWorkingDayId: dayId,
            OperationId: Guid.NewGuid()));

        result.Succeeded.Should().BeFalse();
        result.Blockers.Should().NotBeNull();
        result.Blockers!.Select(blocker => blocker.Kind).Should().Contain([
            WorkingDayBlockerKind.StockCount,
            WorkingDayBlockerKind.ProductionOrder,
            WorkingDayBlockerKind.SaleInvoice,
            WorkingDayBlockerKind.PurchaseInvoice,
            WorkingDayBlockerKind.TreasuryMovement
        ]);
        result.ErrorMessage.Should().Contain("PRD-BLOCK-1");
        result.ErrorMessage.Should().Contain("SAL-BLOCK-1");
        result.ErrorMessage.Should().Contain("PUR-BLOCK-1");
        result.ErrorMessage.Should().Contain(pendingTransferId.ToString("N"));
        result.ErrorMessage!.Split(Environment.NewLine)
            .Count(line => line.StartsWith("- ")).Should().BeGreaterThanOrEqualTo(5);

        db.ChangeTracker.Clear();
        (await db.WorkingDays.SingleAsync(day => day.Id == dayId)).Status.Should().Be(WorkingDayStatus.Open);
        (await db.WorkingDays.CountAsync()).Should().Be(1);
        (await db.AuditLogs.CountAsync(audit => audit.EntityName == nameof(WorkingDay) &&
            audit.EntityId == dayId && audit.Action == "CloseDay")).Should().Be(0);
    }
}
