using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Constants;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Seeders;
using Bakery.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class FinancialIdempotencyTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public FinancialIdempotencyTests(DatabaseFixture fixture) => _fixture = fixture;

    private async Task<(BakeryDbContext Db, WorkingDay Day, Safe FirstSafe, Safe SecondSafe)> PrepareAsync(
        IServiceProvider services)
    {
        var db = services.GetRequiredService<BakeryDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await new DefaultDataSeeder(
            db,
            services.GetRequiredService<IPasswordHasher>(),
            services.GetRequiredService<ISystemSafeService>()).SeedAsync();
        var branch = await db.Branches.IgnoreQueryFilters().OrderBy(item => item.Id).FirstAsync();
        ((IInternalBranchContext)services.GetRequiredService<IBranchContext>())
            .ConfigureBranch(new BranchDto(branch.Id, branch.Code, branch.Name, branch.IsActive, branch.Notes));
        var admin = await db.Users.IgnoreQueryFilters().SingleAsync(item => item.IsSuperAdmin);
        services.GetRequiredService<IUserSessionService>().SignIn(new AuthenticatedUserDto(
            admin.Id, admin.Username, admin.FullName,
            PermissionCatalog.All.Select(item => item.Key).ToArray(), true, admin.SecurityStamp));
        var day = await services.GetRequiredService<IWorkingDayService>().EnsureActiveWorkingDayAsync();
        var safes = await db.Safes.OrderBy(item => item.Id).Take(2).ToArrayAsync();
        return (db, day, safes[0], safes[1]);
    }

    [Fact]
    public async Task ManualDeposit_RetryWithSameKey_CreatesOneMovement()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, _, safe, _) = await PrepareAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var key = Guid.NewGuid().ToString("N");
        var request = new ManualCashTransactionRequest(
            safe.Id, 125m, ManualMovementReason.OwnerCapital, "Capital", null, null, key);

        (await service.ManualDepositAsync(request)).Should().BeTrue();
        (await service.ManualDepositAsync(request)).Should().BeTrue();

        (await db.SafeMovements.CountAsync(item => item.IdempotencyKey == key)).Should().Be(1);
        (await db.SafeMovements.Where(item => item.SafeId == safe.Id).SumAsync(item => item.Amount))
            .Should().Be(125m);
    }

    [Fact]
    public async Task ManualWithdrawal_RetryWithSameKey_CreatesOneMovement()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, day, safe, _) = await PrepareAsync(scope.ServiceProvider);
        db.SafeMovements.Add(new SafeMovement
        {
            SafeId = safe.Id, WorkingDayId = day.Id, Amount = 500m,
            Type = SafeMovementType.Adjustment, Description = "Opening"
        });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var key = Guid.NewGuid().ToString("N");
        var request = new ManualCashTransactionRequest(
            safe.Id, 100m, ManualMovementReason.OwnerWithdrawal, "Withdrawal", null, null, key);

        (await service.ManualWithdrawalAsync(request)).Should().BeTrue();
        (await service.ManualWithdrawalAsync(request)).Should().BeTrue();

        (await db.SafeMovements.CountAsync(item => item.IdempotencyKey == key)).Should().Be(1);
        (await db.SafeMovements.Where(item => item.SafeId == safe.Id).SumAsync(item => item.Amount))
            .Should().Be(400m);
    }

    [Fact]
    public async Task Transfer_RetryWithSameKey_CreatesOnlyOneBalancedPair()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, day, source, destination) = await PrepareAsync(scope.ServiceProvider);
        db.SafeMovements.Add(new SafeMovement
        {
            SafeId = source.Id, WorkingDayId = day.Id, Amount = 500m,
            Type = SafeMovementType.Adjustment, Description = "Opening"
        });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var key = Guid.NewGuid().ToString("N");

        (await service.TransferAsync(source.Id, destination.Id, 150m, "Transfer", key)).Should().BeTrue();
        (await service.TransferAsync(source.Id, destination.Id, 150m, "Transfer", key)).Should().BeTrue();

        var keyed = await db.SafeMovements.SingleAsync(item => item.IdempotencyKey == key);
        (await db.SafeMovements.CountAsync(item => item.TransferId == keyed.TransferId)).Should().Be(2);
        (await db.SafeMovements.Where(item => item.TransferId == keyed.TransferId).SumAsync(item => item.Amount))
            .Should().Be(0m);
    }

    [Fact]
    public async Task PartyPayment_RetryWithSameKey_CreatesOneSafeAndLedgerMovement()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, day, safe, _) = await PrepareAsync(scope.ServiceProvider);
        var customer = new Party { Name = "Idempotency Customer", Type = PartyType.Customer, IsActive = true };
        db.Parties.Add(customer);
        await db.SaveChangesAsync();
        db.PartyLedgerEntries.Add(new PartyLedgerEntry
        {
            WorkingDayId = day.Id,
            PartyId = customer.Id,
            Debit = 300m,
            Amount = 300m,
            Description = "Invoice",
            ReferenceType = LedgerReferenceTypes.SaleInvoice,
            ReferenceId = 1
        });
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IPartyPaymentService>();
        var key = Guid.NewGuid().ToString("N");

        (await service.ProcessPaymentAsync(customer.Id, safe.Id, 100m, "Receipt", true, key))
            .Succeeded.Should().BeTrue();
        (await service.ProcessPaymentAsync(customer.Id, safe.Id, 100m, "Receipt", true, key))
            .Succeeded.Should().BeTrue();

        (await db.SafeMovements.CountAsync(item => item.IdempotencyKey == key)).Should().Be(1);
        (await db.PartyLedgerEntries.CountAsync(item =>
            item.PartyId == customer.Id && item.ReferenceType == LedgerReferenceTypes.CustomerReceipt))
            .Should().Be(1);
    }

    [Fact]
    public async Task PartyPayment_ConcurrentSameKeyAcrossDbContexts_RemainsSingleAndRetryable()
    {
        int customerId;
        int safeId;
        string key = Guid.NewGuid().ToString("N");
        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var (db, day, safe, _) = await PrepareAsync(setupScope.ServiceProvider);
            var customer = new Party
            {
                Name = "Concurrent Idempotency Customer",
                Type = PartyType.Customer,
                IsActive = true
            };
            db.Parties.Add(customer);
            await db.SaveChangesAsync();
            db.PartyLedgerEntries.Add(new PartyLedgerEntry
            {
                WorkingDayId = day.Id,
                PartyId = customer.Id,
                Debit = 300m,
                Amount = 300m,
                Description = "Invoice",
                ReferenceType = LedgerReferenceTypes.SaleInvoice,
                ReferenceId = 2
            });
            await db.SaveChangesAsync();
            customerId = customer.Id;
            safeId = safe.Id;
        }

        async Task<bool> TryPayAsync()
        {
            using var scope = _fixture.ServiceProvider.CreateScope();
            try
            {
                return (await scope.ServiceProvider.GetRequiredService<IPartyPaymentService>()
                    .ProcessPaymentAsync(customerId, safeId, 100m, "Receipt", true, key)).Succeeded;
            }
            catch
            {
                // A deadlock victim is allowed to retry the same idempotent command.
                return false;
            }
        }

        var concurrentResults = await Task.WhenAll(TryPayAsync(), TryPayAsync());
        concurrentResults.Should().Contain(true);

        using var retryScope = _fixture.ServiceProvider.CreateScope();
        (await retryScope.ServiceProvider.GetRequiredService<IPartyPaymentService>()
                .ProcessPaymentAsync(customerId, safeId, 100m, "Receipt", true, key))
            .Succeeded.Should().BeTrue();
        var verificationDb = retryScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        (await verificationDb.SafeMovements.CountAsync(item => item.IdempotencyKey == key))
            .Should().Be(1);
        (await verificationDb.PartyLedgerEntries.CountAsync(item =>
                item.PartyId == customerId &&
                item.ReferenceType == LedgerReferenceTypes.CustomerReceipt))
            .Should().Be(1);
    }

    [Fact]
    public async Task ManualReversal_ConcurrentAcrossDbContexts_CreatesOneBalancedReversal()
    {
        int originalMovementId;
        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var (db, _, safe, _) = await PrepareAsync(setupScope.ServiceProvider);
            var key = Guid.NewGuid().ToString("N");
            await setupScope.ServiceProvider.GetRequiredService<ISafeService>()
                .ManualDepositAsync(new ManualCashTransactionRequest(
                    safe.Id, 200m, ManualMovementReason.OwnerCapital,
                    "Reversal race", null, null, key));
            originalMovementId = await db.SafeMovements
                .Where(item => item.IdempotencyKey == key)
                .Select(item => item.Id)
                .SingleAsync();
        }

        async Task<bool> TryReverseAsync()
        {
            using var scope = _fixture.ServiceProvider.CreateScope();
            try
            {
                return await scope.ServiceProvider.GetRequiredService<ISafeService>()
                    .ReverseManualTransactionAsync(new ReverseTransactionRequest(
                        originalMovementId, "Concurrent correction"));
            }
            catch
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TryReverseAsync(), TryReverseAsync());
        results.Count(result => result).Should().Be(1);

        using var verificationScope = _fixture.ServiceProvider.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var original = await verificationDb.SafeMovements
            .AsNoTracking()
            .SingleAsync(item => item.Id == originalMovementId);
        var reversal = await verificationDb.SafeMovements
            .AsNoTracking()
            .SingleAsync(item => item.OriginalTransactionId == originalMovementId);
        original.ReverseTransactionId.Should().Be(reversal.Id);
        original.ReversedBy.Should().NotBeNullOrWhiteSpace();
        (original.Amount + reversal.Amount).Should().Be(0m);
    }

    [Fact]
    public async Task ManualCashCommands_ConcurrentSameKeysAcrossDbContexts_RemainSingleAndRetryable()
    {
        int safeId;
        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var (_, _, safe, _) = await PrepareAsync(setupScope.ServiceProvider);
            safeId = safe.Id;
        }

        var depositKey = Guid.NewGuid().ToString("N");
        var withdrawalKey = Guid.NewGuid().ToString("N");

        async Task<bool> TryManualAsync(bool deposit, string key, decimal amount)
        {
            using var scope = _fixture.ServiceProvider.CreateScope();
            var request = new ManualCashTransactionRequest(
                safeId,
                amount,
                deposit ? ManualMovementReason.OwnerCapital : ManualMovementReason.OwnerWithdrawal,
                deposit ? "Concurrent deposit" : "Concurrent withdrawal",
                null,
                null,
                key);
            try
            {
                var service = scope.ServiceProvider.GetRequiredService<ISafeService>();
                return deposit
                    ? await service.ManualDepositAsync(request)
                    : await service.ManualWithdrawalAsync(request);
            }
            catch
            {
                return false;
            }
        }

        (await Task.WhenAll(
                TryManualAsync(true, depositKey, 150m),
                TryManualAsync(true, depositKey, 150m)))
            .Should().Contain(true);
        (await Task.WhenAll(
                TryManualAsync(false, withdrawalKey, 50m),
                TryManualAsync(false, withdrawalKey, 50m)))
            .Should().Contain(true);

        using var retryScope = _fixture.ServiceProvider.CreateScope();
        var retryService = retryScope.ServiceProvider.GetRequiredService<ISafeService>();
        (await retryService.ManualDepositAsync(new ManualCashTransactionRequest(
            safeId, 150m, ManualMovementReason.OwnerCapital,
            "Concurrent deposit", null, null, depositKey))).Should().BeTrue();
        (await retryService.ManualWithdrawalAsync(new ManualCashTransactionRequest(
            safeId, 50m, ManualMovementReason.OwnerWithdrawal,
            "Concurrent withdrawal", null, null, withdrawalKey))).Should().BeTrue();

        var verificationDb = retryScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        (await verificationDb.SafeMovements.CountAsync(item => item.IdempotencyKey == depositKey))
            .Should().Be(1);
        (await verificationDb.SafeMovements.CountAsync(item => item.IdempotencyKey == withdrawalKey))
            .Should().Be(1);
        (await verificationDb.SafeMovements.Where(item => item.SafeId == safeId)
                .SumAsync(item => item.Amount))
            .Should().Be(100m);
    }

    [Fact]
    public async Task Transfer_ConcurrentSameKeyAcrossDbContexts_CreatesOneBalancedPair()
    {
        int sourceId;
        int destinationId;
        var key = Guid.NewGuid().ToString("N");
        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var (db, day, source, destination) = await PrepareAsync(setupScope.ServiceProvider);
            sourceId = source.Id;
            destinationId = destination.Id;
            db.SafeMovements.Add(new SafeMovement
            {
                SafeId = sourceId,
                WorkingDayId = day.Id,
                Amount = 500m,
                Type = SafeMovementType.Adjustment,
                Description = "Opening"
            });
            await db.SaveChangesAsync();
        }

        async Task<bool> TryTransferAsync()
        {
            using var scope = _fixture.ServiceProvider.CreateScope();
            try
            {
                return await scope.ServiceProvider.GetRequiredService<ISafeService>()
                    .TransferAsync(sourceId, destinationId, 125m, "Concurrent transfer", key);
            }
            catch
            {
                return false;
            }
        }

        (await Task.WhenAll(TryTransferAsync(), TryTransferAsync())).Should().Contain(true);

        using var retryScope = _fixture.ServiceProvider.CreateScope();
        (await retryScope.ServiceProvider.GetRequiredService<ISafeService>()
                .TransferAsync(sourceId, destinationId, 125m, "Concurrent transfer", key))
            .Should().BeTrue();
        var verificationDb = retryScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var keyed = await verificationDb.SafeMovements.SingleAsync(item => item.IdempotencyKey == key);
        var pair = await verificationDb.SafeMovements
            .Where(item => item.TransferId == keyed.TransferId)
            .ToListAsync();
        pair.Should().HaveCount(2);
        pair.Sum(item => item.Amount).Should().Be(0m);
    }
}
