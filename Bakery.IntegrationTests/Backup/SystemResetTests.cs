using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Services;
using Bakery.Infrastructure.Repositories;
using Bakery.Shared.Auditing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class SystemResetTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public SystemResetTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FactoryReset_ShouldWipeTransactionalData_ButKeepMasterData()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var resetService = scope.ServiceProvider.GetRequiredService<ISystemResetService>();
        var ownerCodeVerifier = scope.ServiceProvider.GetRequiredService<IOwnerResetCodeVerifier>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        // 1. Seed Master Data (Should stay)
        var unit = new Unit { Name = "KG" };
        var item = new Item { Name = "Flour", Code = "F01", BaseUnit = unit };
        db.Units.Add(unit);
        db.Items.Add(item);

        var admin = new User { Username = "admin", FullName = "Admin", PasswordHash = "hash" };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        // 2. Seed Transactional Data (Should be wiped)
        var activeDay = new WorkingDay
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            Status = WorkingDayStatus.Open,
            OpenedAt = DateTime.UtcNow,
            OpenedBy = "admin"
        };
        db.WorkingDays.Add(activeDay);
        await db.SaveChangesAsync();

        var customer = new Party { Name = "Customer 1", Type = PartyType.Customer };
        db.Parties.Add(customer);
        await db.SaveChangesAsync();

        var invoice = new SaleInvoice
        {
            InvoiceNumber = "INV-001",
            PartyId = customer.Id,
            WorkingDayId = activeDay.Id,
            Lines = new List<SaleInvoiceLine>
            {
                new SaleInvoiceLine { ItemId = item.Id, UnitId = unit.Id, Quantity = 10, UnitPrice = 5 }
            }
        };
        db.SaleInvoices.Add(invoice);

        db.AuditLogs.Add(new AuditLog { Action = "Test", EntityName = "Invoice", OccurredAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        // Verify seeding
        (await db.SaleInvoices.CountAsync()).Should().Be(1);
        (await db.Items.CountAsync()).Should().Be(1);
        (await db.WorkingDays.CountAsync()).Should().Be(1);
        (await db.Parties.CountAsync()).Should().Be(1);

        // 3. Run Reset
        var authorization = ownerCodeVerifier.Authorize("124578");
        authorization.Should().NotBeNull();
        await resetService.ResetTransactionalDataAsync(authorization!);

        // 4. Verify Results
        (await db.SaleInvoices.CountAsync()).Should().Be(0);
        (await db.SaleInvoiceLines.CountAsync()).Should().Be(0);
        (await db.WorkingDays.CountAsync()).Should().Be(0);
        (await db.Parties.CountAsync()).Should().Be(0);
        var resetAudit = await db.AuditLogs.SingleAsync();
        resetAudit.Action.Should().Be(AuditActionKeys.FactoryReset);
        resetAudit.NewValues.Should().Contain("Succeeded");

        // Verify Master Data is wiped
        (await db.Items.CountAsync()).Should().Be(0);
        (await db.Units.CountAsync()).Should().Be(0);
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SafetyBackupFailure_PreventsAnyResetDeletion()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        await PrepareResetCandidateAsync(db);
        var resetService = scope.ServiceProvider.GetRequiredService<ISystemResetService>();
        var verifier = scope.ServiceProvider.GetRequiredService<IOwnerResetCodeVerifier>();
        var authorization = verifier.Authorize("124578");
        authorization.Should().NotBeNull();

        _fixture.BackupControl.SkipSafetySnapshots = true;
        try
        {
            Func<Task> act = async () => await resetService.ResetTransactionalDataAsync(authorization!);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*لم يتم حذف أي بيانات*");
        }
        finally
        {
            _fixture.BackupControl.SkipSafetySnapshots = false;
        }

        db.ChangeTracker.Clear();
        (await db.WorkingDays.CountAsync()).Should().Be(1);
        (await db.Units.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ResetFailure_RollsBackAllPartialDeletion()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        await PrepareResetCandidateAsync(db);
        var resetService = scope.ServiceProvider.GetRequiredService<ISystemResetService>();
        var verifier = scope.ServiceProvider.GetRequiredService<IOwnerResetCodeVerifier>();
        var authorization = verifier.Authorize("124578");
        authorization.Should().NotBeNull();

        _fixture.SystemResetFailureInjector.FailBeforeCommit = true;
        try
        {
            Func<Task> act = async () => await resetService.ResetTransactionalDataAsync(authorization!);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*التراجع عن العملية*");
        }
        finally
        {
            _fixture.SystemResetFailureInjector.FailBeforeCommit = false;
        }

        db.ChangeTracker.Clear();
        (await db.WorkingDays.CountAsync()).Should().Be(1);
        (await db.Units.CountAsync()).Should().Be(1);
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentResetGate_AllowsAtMostOneActiveExecution()
    {
        var gate = new SystemResetOperationGate();
        using var first = await gate.TryEnterAsync(CancellationToken.None);
        using var second = await gate.TryEnterAsync(CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    private static async Task PrepareResetCandidateAsync(BakeryDbContext db)
    {
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new User
        {
            Username = "admin",
            FullName = "Admin",
            PasswordHash = "hash",
            IsSuperAdmin = true
        });
        db.Units.Add(new Unit { Name = "KG" });
        db.WorkingDays.Add(new WorkingDay
        {
            BusinessDate = DateOnly.FromDateTime(DateTime.Today),
            Status = WorkingDayStatus.Open,
            OpenedAt = DateTime.UtcNow,
            OpenedBy = "admin"
        });
        await db.SaveChangesAsync();
    }
}
