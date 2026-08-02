using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
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

public class UserSafePermissionTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public UserSafePermissionTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<BakeryDbContext> PrepareCleanDatabaseAsync(IServiceProvider serviceProvider)
    {
        var db = serviceProvider.GetRequiredService<BakeryDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        var session = serviceProvider.GetRequiredService<IUserSessionService>();
        session.SignIn(new AuthenticatedUserDto(
            1,
            "test-admin",
            "Test Admin",
            PermissionCatalog.All.Select(permission => permission.Key).ToArray(),
            true));
        return db;
    }

    [Fact]
    public async Task GetUserPermissionsAsync_WithNoConfiguredPermissions_ShouldReturnAllTrue()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        // Act
        var result = await permService.GetUserPermissionsAsync(1); // Admin user

        // Assert
        result.Should().NotBeNull();
        result.Permissions.Should().NotBeEmpty();
        foreach (var p in result.Permissions)
        {
            p.CanAccess.Should().BeTrue();
            p.CanViewBalance.Should().BeTrue();
            p.CanViewLedger.Should().BeTrue();
            p.CanCashIn.Should().BeTrue();
            p.CanCashOut.Should().BeTrue();
            p.CanTransferFrom.Should().BeTrue();
            p.CanReceiveTransfer.Should().BeTrue();
        }
    }

    [Fact]
    public async Task UpdateUserPermissionsAsync_ShouldPersistPermissionsCorrectly()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();
        var adminUser = new User { Username = "security-admin", FullName = "Security Admin", PasswordHash = "hash", IsActive = true, IsSuperAdmin = true };
        db.Users.Add(adminUser);
        await db.SaveChangesAsync();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        session.SignIn(new AuthenticatedUserDto(adminUser.Id, adminUser.Username, adminUser.FullName, [PermissionKeys.UsersChangePermissions], true));

        var safes = await db.Safes.Where(s => !s.IsDeleted).ToListAsync();
        var safeId = safes.First().Id;

        var userSafePermissions = new List<UserSafePermissionDto>
        {
            new()
            {
                UserId = 1,
                SafeId = safeId,
                CanAccess = true,
                CanViewBalance = false,
                CanViewLedger = true,
                CanCashIn = false,
                CanCashOut = true,
                CanTransferFrom = false,
                CanReceiveTransfer = true
            }
        };

        // Act
        await permService.UpdateUserPermissionsAsync(new UpdateUserSafePermissionsRequest(1, userSafePermissions));

        // Assert
        var result = await permService.GetUserPermissionsAsync(1);
        var testSafePerm = result.Permissions.First(p => p.SafeId == safeId);
        testSafePerm.CanAccess.Should().BeTrue();
        testSafePerm.CanViewBalance.Should().BeFalse();
        testSafePerm.CanViewLedger.Should().BeTrue();
        testSafePerm.CanCashIn.Should().BeFalse();
        testSafePerm.CanCashOut.Should().BeTrue();
        testSafePerm.CanTransferFrom.Should().BeFalse();
        testSafePerm.CanReceiveTransfer.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateUserPermissionsAsync_WithInvalidUserId_ShouldThrowException()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();

        var request = new UpdateUserSafePermissionsRequest(999, new List<UserSafePermissionDto>());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            permService.UpdateUserPermissionsAsync(request));
    }

    [Fact]
    public async Task SafeService_ListSafesAsync_ShouldOnlyIncludeSafesUserCanAccess()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var safes = await db.Safes.Where(s => !s.IsDeleted).ToListAsync();
        var safe1 = safes[0];
        var safe2 = safes[1];

        // Create a regular user
        var user = new User { Username = "regular", FullName = "Regular User", PasswordHash = "hash", IsActive = true, IsSuperAdmin = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Configure user to only access safe1
        var userSafePermissions = new List<UserSafePermissionDto>
        {
            new()
            {
                UserId = user.Id,
                SafeId = safe1.Id,
                CanAccess = true,
                CanViewBalance = true,
                CanViewLedger = true,
                CanCashIn = true,
                CanCashOut = true,
                CanTransferFrom = true,
                CanReceiveTransfer = true
            },
            new()
            {
                UserId = user.Id,
                SafeId = safe2.Id,
                CanAccess = false,
                CanViewBalance = false,
                CanViewLedger = false,
                CanCashIn = false,
                CanCashOut = false,
                CanTransferFrom = false,
                CanReceiveTransfer = false
            }
        };
        await permService.UpdateUserPermissionsAsync(new UpdateUserSafePermissionsRequest(user.Id, userSafePermissions));

        // Sign in as user with global view permissions
        session.SignIn(new AuthenticatedUserDto(user.Id, user.Username, user.FullName, new[] { PermissionKeys.TreasuryView }, false));

        // Act
        var visibleSafes = await safeService.ListSafesAsync();

        // Assert
        visibleSafes.Should().ContainSingle(s => s.Id == safe1.Id);
        visibleSafes.Should().NotContain(s => s.Id == safe2.Id);
    }

    [Fact]
    public async Task SafeService_ListSafesAsync_ShouldHideBalance_IfCanViewBalanceIsFalse()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var safes = await db.Safes.Where(s => !s.IsDeleted).ToListAsync();
        var safe = safes[0];

        // Create a regular user
        var user = new User { Username = "regular", FullName = "Regular User", PasswordHash = "hash", IsActive = true, IsSuperAdmin = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Put some money in the safe
        var workingDay = await scope.ServiceProvider.GetRequiredService<IWorkingDayService>().EnsureActiveWorkingDayAsync();
        db.SafeMovements.Add(new SafeMovement
        {
            SafeId = safe.Id,
            Amount = 500,
            Description = "Deposit",
            Type = SafeMovementType.Adjustment,
            WorkingDayId = workingDay.Id
        });
        await db.SaveChangesAsync();

        // Configure user to have access but NO view balance permission
        var userSafePermissions = new List<UserSafePermissionDto>
        {
            new()
            {
                UserId = user.Id,
                SafeId = safe.Id,
                CanAccess = true,
                CanViewBalance = false,
                CanViewLedger = true,
                CanCashIn = true,
                CanCashOut = true,
                CanTransferFrom = true,
                CanReceiveTransfer = true
            }
        };
        await permService.UpdateUserPermissionsAsync(new UpdateUserSafePermissionsRequest(user.Id, userSafePermissions));

        // Sign in as user with global view permissions
        session.SignIn(new AuthenticatedUserDto(user.Id, user.Username, user.FullName, new[] { PermissionKeys.TreasuryView }, false));

        // Act
        var visibleSafes = await safeService.ListSafesAsync();
        var targetSafe = visibleSafes.First(s => s.Id == safe.Id);

        // Assert
        targetSafe.Balance.Should().Be(0); // Hidden
    }

    [Fact]
    public async Task SafeService_GetBalanceAsync_ShouldReturnZero_IfCanViewBalanceIsFalse()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var safes = await db.Safes.Where(s => !s.IsDeleted).ToListAsync();
        var safe = safes[0];

        // Create a regular user
        var user = new User { Username = "regular", FullName = "Regular User", PasswordHash = "hash", IsActive = true, IsSuperAdmin = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Put some money in the safe
        var workingDay = await scope.ServiceProvider.GetRequiredService<IWorkingDayService>().EnsureActiveWorkingDayAsync();
        db.SafeMovements.Add(new SafeMovement
        {
            SafeId = safe.Id,
            Amount = 1000,
            Description = "Deposit",
            Type = SafeMovementType.Adjustment,
            WorkingDayId = workingDay.Id
        });
        await db.SaveChangesAsync();

        // Configure user to have access but NO view balance permission
        var userSafePermissions = new List<UserSafePermissionDto>
        {
            new()
            {
                UserId = user.Id,
                SafeId = safe.Id,
                CanAccess = true,
                CanViewBalance = false,
                CanViewLedger = true,
                CanCashIn = true,
                CanCashOut = true,
                CanTransferFrom = true,
                CanReceiveTransfer = true
            }
        };
        await permService.UpdateUserPermissionsAsync(new UpdateUserSafePermissionsRequest(user.Id, userSafePermissions));

        // Sign in as user with global view permissions
        session.SignIn(new AuthenticatedUserDto(user.Id, user.Username, user.FullName, new[] { PermissionKeys.TreasuryView }, false));

        // Act
        var balance = await safeService.GetBalanceAsync(safe.Id);

        // Assert
        balance.Should().Be(0);
    }

    [Fact]
    public async Task SafeService_GetMovementsAsync_ShouldReturnEmpty_IfCanViewLedgerIsFalse()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var safes = await db.Safes.Where(s => !s.IsDeleted).ToListAsync();
        var safe = safes[0];

        // Create a regular user
        var user = new User { Username = "regular", FullName = "Regular User", PasswordHash = "hash", IsActive = true, IsSuperAdmin = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Configure user to have access but NO ledger permission
        var userSafePermissions = new List<UserSafePermissionDto>
        {
            new()
            {
                UserId = user.Id,
                SafeId = safe.Id,
                CanAccess = true,
                CanViewBalance = true,
                CanViewLedger = false,
                CanCashIn = true,
                CanCashOut = true,
                CanTransferFrom = true,
                CanReceiveTransfer = true
            }
        };
        await permService.UpdateUserPermissionsAsync(new UpdateUserSafePermissionsRequest(user.Id, userSafePermissions));

        // Sign in as user with global view permissions
        session.SignIn(new AuthenticatedUserDto(user.Id, user.Username, user.FullName, new[] { PermissionKeys.TreasuryView }, false));

        // Act
        var moves = await safeService.GetMovementsAsync(safe.Id);

        // Assert
        moves.Should().BeEmpty();
    }

    [Fact]
    public async Task SafeService_DepositAsync_ShouldThrowUnauthorized_IfCanCashInIsFalse()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var safes = await db.Safes.Where(s => !s.IsDeleted).ToListAsync();
        var safe = safes[0];

        // Create a regular user
        var user = new User { Username = "regular", FullName = "Regular User", PasswordHash = "hash", IsActive = true, IsSuperAdmin = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Configure user to have access but NO Cash In permission
        var userSafePermissions = new List<UserSafePermissionDto>
        {
            new()
            {
                UserId = user.Id,
                SafeId = safe.Id,
                CanAccess = true,
                CanViewBalance = true,
                CanViewLedger = true,
                CanCashIn = false,
                CanCashOut = true,
                CanTransferFrom = true,
                CanReceiveTransfer = true
            }
        };
        await permService.UpdateUserPermissionsAsync(new UpdateUserSafePermissionsRequest(user.Id, userSafePermissions));

        // Sign in as user with global cash in permissions
        session.SignIn(new AuthenticatedUserDto(user.Id, user.Username, user.FullName, new[] { PermissionKeys.TreasuryCashIn }, false));

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            safeService.DepositAsync(safe.Id, 100, "Dep"));
    }

    [Fact]
    public async Task SafeService_WithdrawAsync_ShouldThrowUnauthorized_IfCanCashOutIsFalse()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var safes = await db.Safes.Where(s => !s.IsDeleted).ToListAsync();
        var safe = safes[0];

        // Create a regular user
        var user = new User { Username = "regular", FullName = "Regular User", PasswordHash = "hash", IsActive = true, IsSuperAdmin = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Put some money in the safe first to bypass balance checks
        var workingDay = await scope.ServiceProvider.GetRequiredService<IWorkingDayService>().EnsureActiveWorkingDayAsync();
        db.SafeMovements.Add(new SafeMovement
        {
            SafeId = safe.Id,
            Amount = 500,
            Description = "Deposit",
            Type = SafeMovementType.Adjustment,
            WorkingDayId = workingDay.Id
        });
        await db.SaveChangesAsync();

        // Configure user to have access but NO Cash Out permission
        var userSafePermissions = new List<UserSafePermissionDto>
        {
            new()
            {
                UserId = user.Id,
                SafeId = safe.Id,
                CanAccess = true,
                CanViewBalance = true,
                CanViewLedger = true,
                CanCashIn = true,
                CanCashOut = false,
                CanTransferFrom = true,
                CanReceiveTransfer = true
            }
        };
        await permService.UpdateUserPermissionsAsync(new UpdateUserSafePermissionsRequest(user.Id, userSafePermissions));

        // Sign in as user with global cash out permissions
        session.SignIn(new AuthenticatedUserDto(user.Id, user.Username, user.FullName, new[] { PermissionKeys.TreasuryCashOut }, false));

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            safeService.WithdrawAsync(safe.Id, 100, "With"));
    }

    [Fact]
    public async Task SafeService_TransferAsync_ShouldThrowUnauthorized_IfCanTransferFromIsFalse()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var safes = await db.Safes.Where(s => !s.IsDeleted).ToListAsync();
        var safe1 = safes[0];
        var safe2 = safes[1];

        // Create a regular user
        var user = new User { Username = "regular", FullName = "Regular User", PasswordHash = "hash", IsActive = true, IsSuperAdmin = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Put some money in the source safe first
        var workingDay = await scope.ServiceProvider.GetRequiredService<IWorkingDayService>().EnsureActiveWorkingDayAsync();
        db.SafeMovements.Add(new SafeMovement
        {
            SafeId = safe1.Id,
            Amount = 500,
            Description = "Deposit",
            Type = SafeMovementType.Adjustment,
            WorkingDayId = workingDay.Id
        });
        await db.SaveChangesAsync();

        // Configure user: cannot transfer from safe1, can receive transfer in safe2
        var userSafePermissions = new List<UserSafePermissionDto>
        {
            new()
            {
                UserId = user.Id,
                SafeId = safe1.Id,
                CanAccess = true,
                CanViewBalance = true,
                CanViewLedger = true,
                CanCashIn = true,
                CanCashOut = true,
                CanTransferFrom = false,
                CanReceiveTransfer = true
            },
            new()
            {
                UserId = user.Id,
                SafeId = safe2.Id,
                CanAccess = true,
                CanViewBalance = true,
                CanViewLedger = true,
                CanCashIn = true,
                CanCashOut = true,
                CanTransferFrom = true,
                CanReceiveTransfer = true
            }
        };
        await permService.UpdateUserPermissionsAsync(new UpdateUserSafePermissionsRequest(user.Id, userSafePermissions));

        // Sign in as user with global transfer permissions
        session.SignIn(new AuthenticatedUserDto(user.Id, user.Username, user.FullName, new[] { PermissionKeys.TreasuryTransfer }, false));

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            safeService.TransferAsync(safe1.Id, safe2.Id, 100, "notes"));
    }

    [Fact]
    public async Task SafeService_TransferAsync_ShouldThrowUnauthorized_IfCanReceiveTransferIsFalse()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var safes = await db.Safes.Where(s => !s.IsDeleted).ToListAsync();
        var safe1 = safes[0];
        var safe2 = safes[1];

        // Create a regular user
        var user = new User { Username = "regular", FullName = "Regular User", PasswordHash = "hash", IsActive = true, IsSuperAdmin = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Put some money in the source safe first
        var workingDay = await scope.ServiceProvider.GetRequiredService<IWorkingDayService>().EnsureActiveWorkingDayAsync();
        db.SafeMovements.Add(new SafeMovement
        {
            SafeId = safe1.Id,
            Amount = 500,
            Description = "Deposit",
            Type = SafeMovementType.Adjustment,
            WorkingDayId = workingDay.Id
        });
        await db.SaveChangesAsync();

        // Configure user: can transfer from safe1, cannot receive transfer in safe2
        var userSafePermissions = new List<UserSafePermissionDto>
        {
            new()
            {
                UserId = user.Id,
                SafeId = safe1.Id,
                CanAccess = true,
                CanViewBalance = true,
                CanViewLedger = true,
                CanCashIn = true,
                CanCashOut = true,
                CanTransferFrom = true,
                CanReceiveTransfer = true
            },
            new()
            {
                UserId = user.Id,
                SafeId = safe2.Id,
                CanAccess = true,
                CanViewBalance = true,
                CanViewLedger = true,
                CanCashIn = true,
                CanCashOut = true,
                CanTransferFrom = true,
                CanReceiveTransfer = false
            }
        };
        await permService.UpdateUserPermissionsAsync(new UpdateUserSafePermissionsRequest(user.Id, userSafePermissions));

        // Sign in as user with global transfer permissions
        session.SignIn(new AuthenticatedUserDto(user.Id, user.Username, user.FullName, new[] { PermissionKeys.TreasuryTransfer }, false));

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            safeService.TransferAsync(safe1.Id, safe2.Id, 100, "notes"));
    }

    [Fact]
    public async Task GetUserPermissionsAsync_WithNoConfiguredPermissions_AndNotAdmin_ShouldReturnAllFalse()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        // Create a regular user
        var user = new User { Username = "regular", FullName = "Regular User", PasswordHash = "hash", IsActive = true, IsSuperAdmin = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Act
        var result = await permService.GetUserPermissionsAsync(user.Id);

        // Assert
        result.Should().NotBeNull();
        result.Permissions.Should().NotBeEmpty();
        foreach (var p in result.Permissions)
        {
            p.CanAccess.Should().BeFalse();
            p.CanViewBalance.Should().BeFalse();
        }
    }

    [Fact]
    public async Task CheckPermission_WithNoConfiguredPermissions_AndNotAdmin_ShouldDenyAccess()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var safe = await db.Safes.FirstAsync();

        // Create a regular user
        var user = new User { Username = "regular", FullName = "Regular User", PasswordHash = "hash", IsActive = true, IsSuperAdmin = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Act
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        session.SignIn(new AuthenticatedUserDto(user.Id, user.Username, user.FullName, Array.Empty<string>(), false));

        var canAccess = await permService.CanAccessSafeAsync(user.Id, safe.Id);

        // Assert
        canAccess.Should().BeFalse();
    }

    [Fact]
    public async Task CheckPermission_ForUserWithPermissionsInAnotherBranch_ShouldDenyAccessInCurrentBranch()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        // Create a user
        var user = new User { Username = "regular", FullName = "Regular User", PasswordHash = "hash", IsActive = true, IsSuperAdmin = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var branch1 = await db.Branches.FirstAsync();
        var branch2 = new Branch { Code = "BR2", Name = "Branch 2", IsActive = true };
        db.Branches.Add(branch2);
        await db.SaveChangesAsync();

        var safeInBranch1 = await db.Safes.FirstAsync(s => s.BranchId == branch1.Id);
        var safeInBranch2 = new Safe { Code = "SAFE2", Name = "Safe 2", BranchId = branch2.Id, IsActive = true };
        db.Safes.Add(safeInBranch2);
        await db.SaveChangesAsync();

        // Add permission for safeInBranch2 under Branch 2
        var perm = new UserSafePermission
        {
            UserId = user.Id,
            SafeId = safeInBranch2.Id,
            BranchId = branch2.Id,
            CanAccess = true,
            CanViewBalance = true
        };
        db.UserSafePermissions.Add(perm);
        await db.SaveChangesAsync();

        // Act & Assert
        // When logged into branch 1, user should NOT have access to safeInBranch2
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        session.SignIn(new AuthenticatedUserDto(user.Id, user.Username, user.FullName, Array.Empty<string>(), false));
        var branchContext = (IInternalBranchContext)scope.ServiceProvider.GetRequiredService<IBranchContext>();
        branchContext.ConfigureBranch(new BranchDto(branch1.Id, branch1.Code, branch1.Name, branch1.IsActive, branch1.Notes));

        var canAccess = await permService.CanAccessSafeAsync(user.Id, safeInBranch2.Id);
        canAccess.Should().BeFalse(); // Denied due to global query filter of branch 1
    }

    [Fact]
    public async Task ListSafesAsync_WhenUserHasNoPermissions_ShouldReturnEmptyList()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        // Create a regular user
        var user = new User { Username = "regular", FullName = "Regular User", PasswordHash = "hash", IsActive = true, IsSuperAdmin = false };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        session.SignIn(new AuthenticatedUserDto(user.Id, user.Username, user.FullName, Array.Empty<string>(), false));

        // Act
        var safes = await safeService.ListSafesAsync();

        // Assert
        safes.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckPermission_ForSuperAdmin_ShouldBypassAllChecks()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var safe = await db.Safes.FirstAsync();

        // Create a SuperAdmin user
        var superAdmin = new User { Username = "super", FullName = "Super Admin", PasswordHash = "hash", IsActive = true, IsSuperAdmin = true };
        db.Users.Add(superAdmin);
        await db.SaveChangesAsync();

        // Case A: No permission record exists at all
        var canAccessA = await permService.CanAccessSafeAsync(superAdmin.Id, safe.Id);
        var canViewBalanceA = await permService.CanViewBalanceAsync(superAdmin.Id, safe.Id);
        canAccessA.Should().BeTrue();
        canViewBalanceA.Should().BeTrue();

        // Case B: Explicit Deny permission record exists
        var perm = new UserSafePermission
        {
            UserId = superAdmin.Id,
            SafeId = safe.Id,
            CanAccess = false,
            CanViewBalance = false,
            CanViewLedger = false,
            CanCashIn = false,
            CanCashOut = false,
            CanTransferFrom = false,
            CanReceiveTransfer = false
        };
        db.UserSafePermissions.Add(perm);
        await db.SaveChangesAsync();

        var canAccessB = await permService.CanAccessSafeAsync(superAdmin.Id, safe.Id);
        var canViewBalanceB = await permService.CanViewBalanceAsync(superAdmin.Id, safe.Id);
        canAccessB.Should().BeTrue(); // Bypassed
        canViewBalanceB.Should().BeTrue(); // Bypassed
    }

    [Fact]
    public async Task SafeService_ListSafesForTransfer_ShouldReturnSafes_WhenUserHasPermissions()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        // 1. Log in as super admin
        var superAdmin = await db.Users.FirstAsync(u => u.IsSuperAdmin);
        session.SignIn(new AuthenticatedUserDto(superAdmin.Id, superAdmin.Username, superAdmin.FullName, new[] { PermissionKeys.TreasuryTransfer, PermissionKeys.TreasuryView }, true));

        // Act
        var sources = await safeService.ListSafesForTransferSourceAsync();
        var dests = await safeService.ListSafesForTransferDestAsync();

        // Assert
        sources.Should().NotBeEmpty();
        dests.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SafeService_WithdrawAsync_ShouldUseActualBalance_WhenBalanceIsHidden()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var permissionService = scope.ServiceProvider.GetRequiredService<IUserSafePermissionService>();
        var safeService = scope.ServiceProvider.GetRequiredService<ISafeService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await new DefaultDataSeeder(db, passwordHasher, systemSafeService).SeedAsync();

        var safe = await db.Safes.FirstAsync(item => !item.IsDeleted);
        var workingDay = await scope.ServiceProvider.GetRequiredService<IWorkingDayService>()
            .EnsureActiveWorkingDayAsync();
        db.SafeMovements.Add(new SafeMovement
        {
            SafeId = safe.Id,
            Amount = 500m,
            Description = "Opening balance",
            Type = SafeMovementType.Adjustment,
            WorkingDayId = workingDay.Id
        });
        var user = new User
        {
            Username = "hidden-balance-cashier",
            FullName = "Hidden Balance Cashier",
            PasswordHash = "hash",
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await permissionService.UpdateUserPermissionsAsync(new UpdateUserSafePermissionsRequest(
            user.Id,
            [new UserSafePermissionDto
            {
                UserId = user.Id,
                SafeId = safe.Id,
                CanAccess = true,
                CanViewBalance = false,
                CanViewLedger = false,
                CanCashIn = false,
                CanCashOut = true,
                CanTransferFrom = false,
                CanReceiveTransfer = false
            }]));
        session.SignIn(new AuthenticatedUserDto(
            user.Id,
            user.Username,
            user.FullName,
            [PermissionKeys.TreasuryCashOut]));

        var result = await safeService.WithdrawAsync(safe.Id, 100m, "Cash out");

        result.Should().BeTrue();
        (await safeService.GetBalanceAsync(safe.Id)).Should().Be(0m);
        (await db.SafeMovements.Where(item => item.SafeId == safe.Id).SumAsync(item => item.Amount))
            .Should().Be(400m);
    }
}
