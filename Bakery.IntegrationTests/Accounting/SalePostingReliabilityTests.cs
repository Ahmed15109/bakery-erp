using System.Diagnostics;
using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Bakery.IntegrationTests;

public sealed class SalePostingReliabilityTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SalePostingReliabilityTests(DatabaseFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task RoutineSale_PostsTransactionallyWithoutBackup_AndRetryIsIdempotent()
    {
        _fixture.BackupControl.SkipSafetySnapshots = false;
        _fixture.BackupControl.ResetSafetySnapshotMetrics();

        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var days = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var sales = scope.ServiceProvider.GetRequiredService<ISaleInvoiceService>();
        if (await days.GetCurrentOpenDayAsync() is null)
        {
            var open = await days.OpenDayAsync(new OpenWorkingDayRequest(
                DateOnly.FromDateTime(DateTime.Today), 0m, "Sale posting baseline"));
            open.Succeeded.Should().BeTrue(open.ErrorMessage);
        }

        var day = (await days.GetCurrentOpenDayAsync())!;
        var customer = await db.Parties.FirstAsync(party => party.Type == PartyType.Customer);
        var unit = await db.Units.FirstAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var item = new Item
        {
            Code = $"POST-{suffix}",
            Name = $"Posting item {suffix}",
            Type = ItemType.FinishedProduct,
            BaseUnitId = unit.Id,
            PurchasePrice = 2m,
            SalePrice = 5m
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        db.InventoryMovements.Add(new InventoryMovement
        {
            WorkingDayId = day.Id,
            ItemId = item.Id,
            UnitId = unit.Id,
            Type = InventoryMovementType.OpeningBalance,
            Quantity = 5m,
            UnitCost = 2m
        });
        await db.SaveChangesAsync();

        var draft = await sales.SaveDraftAsync(new SaveSaleInvoiceRequest(
            null,
            customer.Id,
            PaymentType.Credit,
            0m,
            "Routine posting measurement",
            [new InvoiceLineRequest(item.Id, unit.Id, 1m, 5m)],
            null));
        draft.Succeeded.Should().BeTrue(draft.ErrorMessage);

        var stopwatch = Stopwatch.StartNew();
        var posted = await sales.PostAsync(draft.InvoiceId!.Value);
        stopwatch.Stop();

        _output.WriteLine($"Posting elapsed: {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
        _output.WriteLine($"Safety snapshot elapsed: {_fixture.BackupControl.LastSafetySnapshotElapsed.TotalMilliseconds:F0} ms");
        posted.Succeeded.Should().BeTrue(posted.ErrorMessage);
        _fixture.BackupControl.SafetySnapshotCount.Should().Be(0);

        var retry = await sales.PostAsync(draft.InvoiceId.Value);
        retry.Succeeded.Should().BeTrue(retry.ErrorMessage);

        var persisted = await db.SaleInvoices.AsNoTracking()
            .SingleAsync(invoice => invoice.Id == draft.InvoiceId.Value);
        persisted.Status.Should().Be(InvoiceStatus.Posted);
        (await db.InventoryMovements.AsNoTracking().CountAsync(movement =>
            movement.ReferenceType == "SaleInvoice" && movement.ReferenceId == persisted.Id)).Should().Be(1);
        (await db.PartyLedgerEntries.AsNoTracking().CountAsync(entry =>
            entry.ReferenceType == "SaleInvoice" && entry.ReferenceId == persisted.Id)).Should().Be(1);
        (await db.AuditLogs.AsNoTracking().CountAsync(entry =>
            entry.EntityName == nameof(SaleInvoice) && entry.EntityId == persisted.Id)).Should().Be(1);
        _fixture.BackupControl.SafetySnapshotCount.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentRetries_ForSameSale_CommitAccountingExactlyOnce()
    {
        int invoiceId;
        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
            var days = setupScope.ServiceProvider.GetRequiredService<IWorkingDayService>();
            var sales = setupScope.ServiceProvider.GetRequiredService<ISaleInvoiceService>();
            if (await days.GetCurrentOpenDayAsync() is null)
            {
                var open = await days.OpenDayAsync(new OpenWorkingDayRequest(
                    DateOnly.FromDateTime(DateTime.Today), 0m, "Concurrent sale retry"));
                open.Succeeded.Should().BeTrue(open.ErrorMessage);
            }

            var day = (await days.GetCurrentOpenDayAsync())!;
            var customer = await db.Parties.FirstAsync(party => party.Type == PartyType.Customer);
            var unit = await db.Units.FirstAsync();
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var item = new Item
            {
                Code = $"RETRY-{suffix}",
                Name = $"Retry item {suffix}",
                Type = ItemType.FinishedProduct,
                BaseUnitId = unit.Id,
                PurchasePrice = 2m,
                SalePrice = 5m
            };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            db.InventoryMovements.Add(new InventoryMovement
            {
                WorkingDayId = day.Id,
                ItemId = item.Id,
                UnitId = unit.Id,
                Type = InventoryMovementType.OpeningBalance,
                Quantity = 5m,
                UnitCost = 2m
            });
            await db.SaveChangesAsync();

            var draft = await sales.SaveDraftAsync(new SaveSaleInvoiceRequest(
                null,
                customer.Id,
                PaymentType.Credit,
                0m,
                "Concurrent retry",
                [new InvoiceLineRequest(item.Id, unit.Id, 1m, 5m)],
                null));
            draft.Succeeded.Should().BeTrue(draft.ErrorMessage);
            invoiceId = draft.InvoiceId!.Value;
        }

        _fixture.BackupControl.ResetSafetySnapshotMetrics();
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(bool Succeeded, string? ErrorMessage)> PostAsync()
        {
            await start.Task;
            using var scope = _fixture.ServiceProvider.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ISaleInvoiceService>().PostAsync(invoiceId);
        }

        var first = Task.Run(PostAsync);
        var second = Task.Run(PostAsync);
        start.TrySetResult(true);
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        results.Should().OnlyContain(result => result.Succeeded);

        using var assertionScope = _fixture.ServiceProvider.CreateScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        (await assertionDb.InventoryMovements.AsNoTracking().CountAsync(movement =>
            movement.ReferenceType == "SaleInvoice" && movement.ReferenceId == invoiceId)).Should().Be(1);
        (await assertionDb.PartyLedgerEntries.AsNoTracking().CountAsync(entry =>
            entry.ReferenceType == "SaleInvoice" && entry.ReferenceId == invoiceId)).Should().Be(1);
        (await assertionDb.AuditLogs.AsNoTracking().CountAsync(entry =>
            entry.EntityName == nameof(SaleInvoice) && entry.EntityId == invoiceId)).Should().Be(1);
        _fixture.BackupControl.SafetySnapshotCount.Should().Be(0);
    }
}
