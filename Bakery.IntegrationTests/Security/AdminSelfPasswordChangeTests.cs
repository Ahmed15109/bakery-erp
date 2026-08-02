using System;
using System.Linq;
using System.Threading.Tasks;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Seeders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests.Security;

[Collection(AdminPasswordLocalDbIsolationCollection.Name)]
public class AdminSelfPasswordChangeTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public AdminSelfPasswordChangeTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<BakeryDbContext> PrepareCleanDatabaseAsync(IServiceProvider serviceProvider)
    {
        var db = serviceProvider.GetRequiredService<BakeryDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    [Fact]
    public async Task Admin_ChangesAnotherUserPassword_Successfully()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();

        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var admin = await db.Users.FirstAsync(u => u.Username == "admin");
        session.SignIn(new AuthenticatedUserDto(
            admin.Id,
            admin.Username,
            admin.FullName,
            PermissionCatalog.All.Select(p => p.Key).ToArray(),
            true,
            admin.SecurityStamp));

        var targetUserDto = await userManagementService.CreateAsync(new SaveUserRequest(
            null,
            "targetuser",
            "Target User",
            "InitialPassword!123",
            true,
            new[] { PermissionKeys.SalesView },
            new[] { 1 }));

        // Act - Change target user password
        await userManagementService.UpdateAsync(new SaveUserRequest(
            targetUserDto.Id,
            "targetuser",
            "Target User",
            "UpdatedPassword!456",
            true,
            new[] { PermissionKeys.SalesView },
            new[] { 1 },
            RowVersion: targetUserDto.RowVersion));

        // Assert
        var oldLoginResult = await authService.LoginAsync(new LoginRequest("targetuser", "InitialPassword!123", 1));
        oldLoginResult.Succeeded.Should().BeFalse("old password must fail after change");

        var newLoginResult = await authService.LoginAsync(new LoginRequest("targetuser", "UpdatedPassword!456", 1));
        newLoginResult.Succeeded.Should().BeTrue("new password must succeed after change");
        newLoginResult.User!.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task Admin_ChangesOwnPassword_Successfully()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();

        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var admin = await db.Users.FirstAsync(u => u.Username == "admin");
        session.SignIn(new AuthenticatedUserDto(
            admin.Id,
            admin.Username,
            admin.FullName,
            PermissionCatalog.All.Select(p => p.Key).ToArray(),
            true,
            admin.SecurityStamp));

        var adminDetails = await userManagementService.GetByIdAsync(admin.Id);
        adminDetails.Should().NotBeNull();

        // Act - Admin edits own account and changes password
        var updateRequest = new SaveUserRequest(
            admin.Id,
            "admin",
            "مسؤول النظام",
            "BrandNewAdminPass!123",
            true,
            adminDetails!.PermissionKeys,
            adminDetails.BranchIds,
            adminDetails.RoleIds,
            adminDetails.SafePermissions,
            adminDetails.RowVersion);

        var result = await userManagementService.UpdateAsync(updateRequest);
        result.Should().NotBeNull();

        // Assert - Log out and test credentials
        var oldLogin = await authService.LoginAsync(new LoginRequest("admin", "admin123-test-only", 1));
        oldLogin.Succeeded.Should().BeFalse("old password should no longer work");

        var newLogin = await authService.LoginAsync(new LoginRequest("admin", "BrandNewAdminPass!123", 1));
        newLogin.Succeeded.Should().BeTrue("admin must be able to log in with new password immediately");
        newLogin.User!.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task EmptyPassword_PreservesExistingPassword()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();

        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var admin = await db.Users.FirstAsync(u => u.Username == "admin");
        var originalHash = admin.PasswordHash;

        session.SignIn(new AuthenticatedUserDto(
            admin.Id,
            admin.Username,
            admin.FullName,
            PermissionCatalog.All.Select(p => p.Key).ToArray(),
            true,
            admin.SecurityStamp));

        var adminDetails = await userManagementService.GetByIdAsync(admin.Id);

        // Act - Save with empty string password
        await userManagementService.UpdateAsync(new SaveUserRequest(
            admin.Id,
            "admin",
            "مسؤول النظام المعدل",
            "", // Empty password
            true,
            adminDetails!.PermissionKeys,
            adminDetails.BranchIds,
            adminDetails.RoleIds,
            adminDetails.SafePermissions,
            adminDetails.RowVersion));

        // Assert
        db.ChangeTracker.Clear();
        var updatedAdmin = await db.Users.FirstAsync(u => u.Id == admin.Id);
        updatedAdmin.PasswordHash.Should().Be(originalHash, "password hash must not change when password field is empty");

        var loginResult = await authService.LoginAsync(new LoginRequest("admin", "admin123-test-only", 1));
        loginResult.Succeeded.Should().BeTrue("login with original password must succeed");
    }

    [Fact]
    public async Task UnauthorizedUser_CannotChangePasswords()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();

        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        // Sign in user without password reset / user management permissions
        session.SignIn(new AuthenticatedUserDto(
            55,
            "normaluser",
            "Normal User",
            new[] { PermissionKeys.SalesView }));

        var request = new SaveUserRequest(
            1,
            "admin",
            "Administrator",
            "AttemptedNewPassword!123",
            true,
            new[] { PermissionKeys.SalesView },
            new[] { 1 });

        // Act & Assert
        Func<Task> act = async () => await userManagementService.UpdateAsync(request);
        await act.Should().ThrowAsync<UnauthorizedAccessException>("users without UsersResetPassword permission must be blocked");
    }

    [Fact]
    public async Task AuditLogging_DoesNotStorePlainTextOrHash_InAuditValues()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();

        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var admin = await db.Users.FirstAsync(u => u.Username == "admin");
        session.SignIn(new AuthenticatedUserDto(
            admin.Id,
            admin.Username,
            admin.FullName,
            PermissionCatalog.All.Select(p => p.Key).ToArray(),
            true,
            admin.SecurityStamp));

        var adminDetails = await userManagementService.GetByIdAsync(admin.Id);
        const string secretPassword = "SuperSecretNewPassword!999";

        // Act
        await userManagementService.UpdateAsync(new SaveUserRequest(
            admin.Id,
            "admin",
            "مسؤول النظام",
            secretPassword,
            true,
            adminDetails!.PermissionKeys,
            adminDetails.BranchIds,
            adminDetails.RoleIds,
            adminDetails.SafePermissions,
            adminDetails.RowVersion));

        // Assert - Check AuditLogs table
        var auditLogs = await db.AuditLogs
            .Where(a => a.EntityName == nameof(User) && a.EntityId == admin.Id)
            .ToListAsync();

        auditLogs.Should().NotBeEmpty();
        foreach (var log in auditLogs)
        {
            if (!string.IsNullOrEmpty(log.OldValues))
            {
                log.OldValues.Should().NotContain(secretPassword);
            }
            if (!string.IsNullOrEmpty(log.NewValues))
            {
                log.NewValues.Should().NotContain(secretPassword);
            }
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AdminPasswordLocalDbIsolationCollection
{
    public const string Name = "Admin password LocalDB isolation";
}
