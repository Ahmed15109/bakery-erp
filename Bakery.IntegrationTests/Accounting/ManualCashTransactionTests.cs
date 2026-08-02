using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Infrastructure.Data;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Application.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Bakery.IntegrationTests;

public class ManualCashTransactionTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public ManualCashTransactionTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ManualTransactionAndReversal_ShouldMaintainIntegrity()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        // Ensure active day is open
        var activeDay = await workingDayService.GetCurrentOpenDayAsync();
        if (activeDay == null)
        {
            var openRequest = new OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today), 1000m, "Opening Day for Manual tests");
            await workingDayService.OpenDayAsync(openRequest);
        }

        // Get safe
        var safe = await db.Safes.FirstAsync(x => x.IsActive);
        var branchId = activeDay?.BranchId ?? db.Branches.IgnoreQueryFilters().First().Id;

        // Ensure admin user exists and has explicit safe permission in db
        var adminUser = await db.Users.FirstOrDefaultAsync(u => u.Username == "test-admin");
        if (adminUser == null)
        {
            adminUser = new User { Username = "test-admin", FullName = "Test Admin", PasswordHash = "test", IsSuperAdmin = true };
            db.Users.Add(adminUser);
            await db.SaveChangesAsync();
        }

        var permAdmin = await db.UserSafePermissions.FirstOrDefaultAsync(p => p.UserId == adminUser.Id && p.SafeId == safe.Id);
        if (permAdmin == null)
        {
            db.UserSafePermissions.Add(new UserSafePermission
            {
                UserId = adminUser.Id,
                SafeId = safe.Id,
                BranchId = branchId,
                CanAccess = true,
                CanViewBalance = true,
                CanViewLedger = true,
                CanCashIn = true,
                CanCashOut = true
            });
            await db.SaveChangesAsync();
        }

        // Sign in as admin
        session.SignIn(new AuthenticatedUserDto(
            adminUser.Id,
            "test-admin",
            "Test Admin",
            PermissionCatalog.All.Select(permission => permission.Key).ToArray(),
            true));

        // Seed a huge initial balance so the safe won't run out of money during reversal withdrawal
        var seedReq = new ManualCashTransactionRequest(
            safe.Id,
            5000m,
            ManualMovementReason.OwnerCapital,
            "Initial Seeding for test robustness",
            "SEED-01",
            null
        );
        await safeService.ManualDepositAsync(seedReq);

        var initialBalance = await safeService.GetBalanceAsync(safe.Id);

        var tempFilePath = System.IO.Path.GetTempFileName();
        await System.IO.File.WriteAllTextAsync(tempFilePath, "Dummy capital attachment content");

        try
        {
            // --- Step 1: Deposit manual cash ---
            var depositReq = new ManualCashTransactionRequest(
                safe.Id,
                250m,
                ManualMovementReason.OwnerCapital,
                "Initial capital injection",
                "REF-DEP-01",
                tempFilePath
            );

            var depositSuccess = await safeService.ManualDepositAsync(depositReq);
            depositSuccess.Should().BeTrue("Deposit transaction should succeed");

            // Verify balance updated
            var balanceAfterDeposit = await safeService.GetBalanceAsync(safe.Id);
            balanceAfterDeposit.Should().Be(initialBalance + 250m);

            // --- Step 2: Withdraw manual cash ---
            var withdrawReq = new ManualCashTransactionRequest(
                safe.Id,
                100m,
                ManualMovementReason.BankDeposit,
                "Depositing daily sales to bank",
                "REF-WDR-01",
                null
            );

            var withdrawSuccess = await safeService.ManualWithdrawalAsync(withdrawReq);
            withdrawSuccess.Should().BeTrue("Withdrawal transaction should succeed");

            var balanceAfterWithdraw = await safeService.GetBalanceAsync(safe.Id);
            balanceAfterWithdraw.Should().Be(balanceAfterDeposit - 100m);

            // Check movements / ledger
            var ledger = await safeService.GetLedgerAsync(safe.Id);
            var depositMove = ledger.FirstOrDefault(x => x.TransactionNumber != null && x.TransactionNumber.StartsWith("DEP") && x.Notes == "REF-DEP-01");
            var withdrawMove = ledger.FirstOrDefault(x => x.TransactionNumber != null && x.TransactionNumber.StartsWith("WDR") && x.Notes == "REF-WDR-01");

            depositMove.Should().NotBeNull();
            depositMove!.Amount.Should().Be(250m);
            depositMove.Reason.Should().Be(ManualMovementReason.OwnerCapital);
            depositMove.Origin.Should().Be(CashMovementOrigin.Manual);
            depositMove.IsReversed.Should().BeFalse();
            depositMove.BalanceBefore.Should().Be(initialBalance);
            depositMove.BalanceAfter.Should().Be(initialBalance + 250m);

            withdrawMove.Should().NotBeNull();
            withdrawMove!.Amount.Should().Be(-100m);
            withdrawMove.Reason.Should().Be(ManualMovementReason.BankDeposit);
            withdrawMove.Origin.Should().Be(CashMovementOrigin.Manual);
            withdrawMove.IsReversed.Should().BeFalse();
            withdrawMove.BalanceBefore.Should().Be(balanceAfterDeposit);
            withdrawMove.BalanceAfter.Should().Be(balanceAfterDeposit - 100m);

            // --- Step 3: Reversal ---
            var reverseReq = new ReverseTransactionRequest(
                depositMove.Id,
                "Correction of amount"
            );

            var reverseSuccess = await safeService.ReverseManualTransactionAsync(reverseReq);
            reverseSuccess.Should().BeTrue("Reversal transaction should succeed");

            // Check balance restored to before deposit
            var balanceAfterReversal = await safeService.GetBalanceAsync(safe.Id);
            balanceAfterReversal.Should().Be(balanceAfterWithdraw - 250m);

            // Check ledger for reversal state
            var updatedLedger = await safeService.GetLedgerAsync(safe.Id);
            var reversedDeposit = updatedLedger.First(x => x.Id == depositMove.Id);
            var reverseTransaction = updatedLedger.First(x => x.Origin == CashMovementOrigin.Reverse && x.OriginalTransactionId == depositMove.Id);

            reversedDeposit.IsReversed.Should().BeTrue("Original transaction should be marked as reversed");
            reversedDeposit.ReversedBy.Should().Be("test-admin");
            reversedDeposit.ReverseReason.Should().Be("Correction of amount");

            reverseTransaction.Amount.Should().Be(-250m, "Reverse transaction should have opposite sign amount");
            reverseTransaction.TransactionNumber.Should().StartWith("REV");
            reverseTransaction.Origin.Should().Be(CashMovementOrigin.Reverse);

            // --- Step 4: Reversing again should fail ---
            Func<Task> doubleReverse = async () => await safeService.ReverseManualTransactionAsync(reverseReq);
            await doubleReverse.Should().ThrowAsync<ValidationException>("Cannot reverse a transaction that is already reversed");
        }
        finally
        {
            if (System.IO.File.Exists(tempFilePath))
            {
                System.IO.File.Delete(tempFilePath);
            }
        }
    }

    [Fact]
    public async Task ManualLedgerFilter_ShouldOnlyShowOwnMovements_WhenLackingViewAllPermission()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        // Ensure active day is open
        var activeDay = await workingDayService.GetCurrentOpenDayAsync();
        if (activeDay == null)
        {
            var openRequest = new OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today), 1000m, "Opening Day for Filter tests");
            await workingDayService.OpenDayAsync(openRequest);
        }

        var safe = await db.Safes.FirstAsync(x => x.IsActive);
        var branchId = activeDay?.BranchId ?? db.Branches.IgnoreQueryFilters().First().Id;

        // Ensure users exist and have explicit safe permission in db
        var u1 = await db.Users.FirstOrDefaultAsync(u => u.Username == "user-1");
        if (u1 == null)
        {
            u1 = new User { Username = "user-1", FullName = "User One", PasswordHash = "test", IsSuperAdmin = false };
            db.Users.Add(u1);
            await db.SaveChangesAsync();
        }

        var u2 = await db.Users.FirstOrDefaultAsync(u => u.Username == "user-2");
        if (u2 == null)
        {
            u2 = new User { Username = "user-2", FullName = "User Two", PasswordHash = "test", IsSuperAdmin = false };
            db.Users.Add(u2);
            await db.SaveChangesAsync();
        }

        var permU1 = await db.UserSafePermissions.FirstOrDefaultAsync(p => p.UserId == u1.Id && p.SafeId == safe.Id);
        if (permU1 == null)
        {
            db.UserSafePermissions.Add(new UserSafePermission
            {
                UserId = u1.Id,
                SafeId = safe.Id,
                BranchId = branchId,
                CanAccess = true,
                CanViewBalance = true,
                CanViewLedger = true,
                CanCashIn = true,
                CanCashOut = true
            });
            await db.SaveChangesAsync();
        }

        var permU2 = await db.UserSafePermissions.FirstOrDefaultAsync(p => p.UserId == u2.Id && p.SafeId == safe.Id);
        if (permU2 == null)
        {
            db.UserSafePermissions.Add(new UserSafePermission
            {
                UserId = u2.Id,
                SafeId = safe.Id,
                BranchId = branchId,
                CanAccess = true,
                CanViewBalance = true,
                CanViewLedger = true,
                CanCashIn = true,
                CanCashOut = true
            });
            await db.SaveChangesAsync();
        }

        // Sign in as user 1 with Cash.Deposit but NOT Cash.ViewAllTransactions
        session.SignIn(new AuthenticatedUserDto(
            u1.Id,
            "user-1",
            "User One",
            new[] { PermissionKeys.TreasuryView, PermissionKeys.CashDeposit },
            false));

        var req1 = new ManualCashTransactionRequest(
            safe.Id,
            10m,
            ManualMovementReason.CashAdjustment,
            "Adjustment by user 1",
            null,
            null
        );
        await safeService.ManualDepositAsync(req1);

        // Sign in as user 2 with Cash.Deposit but NOT Cash.ViewAllTransactions
        session.SignIn(new AuthenticatedUserDto(
            u2.Id,
            "user-2",
            "User Two",
            new[] { PermissionKeys.TreasuryView, PermissionKeys.CashDeposit },
            false));

        var req2 = new ManualCashTransactionRequest(
            safe.Id,
            20m,
            ManualMovementReason.CashAdjustment,
            "Adjustment by user 2",
            null,
            null
        );
        await safeService.ManualDepositAsync(req2);

        // User 2 queries ledger
        var ledgerUser2 = await safeService.GetLedgerAsync(safe.Id);
        
        // User 2 should see their own manual transaction (amount 20) but NOT user 1's transaction (amount 10)
        ledgerUser2.Any(x => x.Amount == 10m && x.Origin == CashMovementOrigin.Manual).Should().BeFalse("Should not view other users manual movements without permission");
        ledgerUser2.Any(x => x.Amount == 20m && x.Origin == CashMovementOrigin.Manual).Should().BeTrue("Should view own manual movements");

        // Sign back in as admin who has Cash.ViewAllTransactions
        var adminUser = await db.Users.FirstOrDefaultAsync(u => u.Username == "test-admin");
        session.SignIn(new AuthenticatedUserDto(
            adminUser!.Id,
            "test-admin",
            "Test Admin",
            PermissionCatalog.All.Select(permission => permission.Key).ToArray(),
            true));

        var ledgerAdmin = await safeService.GetLedgerAsync(safe.Id);
        ledgerAdmin.Any(x => x.Amount == 10m && x.Origin == CashMovementOrigin.Manual).Should().BeTrue("Admin should view user 1 manual movements");
        ledgerAdmin.Any(x => x.Amount == 20m && x.Origin == CashMovementOrigin.Manual).Should().BeTrue("Admin should view user 2 manual movements");
    }
}
