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
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class UserManagementAndSecurityTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public UserManagementAndSecurityTests(DatabaseFixture fixture)
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
    public async Task CreateUser_ShouldSucceed_AndHashPassword_AndSyncPermissions()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        // Seed permissions in database
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        // Sign in as test admin to bypass authorization checks
        session.SignIn(new AuthenticatedUserDto(
            1,
            "admin",
            "Administrator",
            PermissionCatalog.All.Select(p => p.Key).ToArray()));

        var request = new SaveUserRequest(
            null,
            "new-user",
            "New User Full Name",
            "securepassword123",
            true,
            new[] { PermissionKeys.SalesView, PermissionKeys.SalesCreate },
            new[] { 1 }
        );

        // Act
        var result = await userManagementService.CreateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Username.Should().Be("new-user");
        result.FullName.Should().Be("New User Full Name");
        result.IsActive.Should().BeTrue();
        result.PermissionKeys.Should().BeEquivalentTo(new[] { PermissionKeys.SalesView, PermissionKeys.SalesCreate });

        // Verify password is hashed in DB
        var savedUser = await db.Users.FirstOrDefaultAsync(u => u.Username == "new-user");
        savedUser.Should().NotBeNull();
        savedUser!.PasswordHash.Should().NotBeNullOrEmpty();
        savedUser.PasswordHash.Should().NotBe("securepassword123");
    }

    [Fact]
    public async Task CreateUser_WithRoleAndNoDirectPermissions_ShouldSucceed()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        session.SignIn(new AuthenticatedUserDto(
            1,
            "admin",
            "Administrator",
            PermissionCatalog.All.Select(permission => permission.Key).ToArray()));

        var roleId = await db.Roles
            .Where(role => role.RolePermissions.Any())
            .Select(role => role.Id)
            .FirstAsync();
        var rolePermissionKey = await db.RolePermissions
            .Where(item => item.RoleId == roleId)
            .Select(item => item.Permission.Key)
            .FirstAsync();
        var request = new SaveUserRequest(
            null,
            "role-only-user",
            "Role Only User",
            "securepassword123",
            true,
            Array.Empty<string>(),
            new[] { 1 },
            new[] { roleId });

        var result = await userManagementService.CreateAsync(request);

        result.PermissionKeys.Should().BeEmpty();
        result.RoleIds.Should().BeEquivalentTo([roleId]);
        var savedUser = await db.Users.SingleAsync(user => user.Username == "role-only-user");
        savedUser.MustChangePassword.Should().BeFalse();
        savedUser.PasswordHash.Should().NotBe("securepassword123");
        passwordHasher.VerifyPassword("securepassword123", savedUser.PasswordHash).Should().BeTrue();

        var login = await scope.ServiceProvider.GetRequiredService<IAuthService>()
            .LoginAsync(new LoginRequest("role-only-user", "securepassword123", 1));
        login.Succeeded.Should().BeTrue();
        login.User!.MustChangePassword.Should().BeFalse();
        login.User.Permissions.Should().Contain(rolePermissionKey);
        login.User.Roles.Should().NotBeEmpty();
        login.AvailableBranches.Should().ContainSingle(branch => branch.Id == 1);
    }

    [Fact]
    public async Task CreateUser_DuplicateUsername_ShouldThrowException()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        session.SignIn(new AuthenticatedUserDto(
            1,
            "admin",
            "Administrator",
            PermissionCatalog.All.Select(p => p.Key).ToArray()));

        var request1 = new SaveUserRequest(
            null,
            "dup-user",
            "User One",
            "password123!",
            true,
            new[] { PermissionKeys.SalesView },
            new[] { 1 }
        );

        await userManagementService.CreateAsync(request1);

        var request2 = new SaveUserRequest(
            null,
            "dup-user",
            "User Two",
            "password123!",
            true,
            new[] { PermissionKeys.SalesView },
            new[] { 1 }
        );

        // Act & Assert
        Func<Task> act = async () => await userManagementService.CreateAsync(request2);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*مستخدم بالفعل*");
    }

    [Fact]
    public async Task UsernameAvailability_ShouldUseNormalizedUsername()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        session.SignIn(new AuthenticatedUserDto(
            1,
            "admin",
            "Administrator",
            PermissionCatalog.All.Select(permission => permission.Key).ToArray()));

        var validationService = scope.ServiceProvider.GetRequiredService<IValidationService>();

        (await validationService.IsUsernameUsedAsync("ADMIN")).Should().BeTrue();
    }

    [Theory]
    [InlineData("", "Full Name", "password", new[] { PermissionKeys.SalesView }, new int[] { 1 }, "اسم المستخدم مطلوب.")]
    [InlineData("user", "", "password", new[] { PermissionKeys.SalesView }, new int[] { 1 }, "الاسم الكامل مطلوب.")]
    [InlineData("user", "Full Name", "12345", new[] { PermissionKeys.SalesView }, new int[] { 1 }, "يجب أن تتكون كلمة المرور من 12 حرفاً على الأقل.")]
    [InlineData("user", "Full Name", "validpassword123", new string[] { }, new int[] { 1 }, "يجب اختيار صلاحية مباشرة أو دور أمني واحد على الأقل.")]
    [InlineData("user", "Full Name", "validpassword123", new[] { PermissionKeys.SalesView }, new int[] { }, "يجب اختيار فرع واحد على الأقل.")]
    public async Task CreateUser_InvalidInput_ShouldThrowException(
        string username,
        string fullName,
        string password,
        string[] permissionKeys,
        int[] branchIds,
        string expectedError)
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        session.SignIn(new AuthenticatedUserDto(
            1,
            "admin",
            "Administrator",
            PermissionCatalog.All.Select(p => p.Key).ToArray()));

        var request = new SaveUserRequest(null, username, fullName, password, true, permissionKeys, branchIds);

        // Act & Assert
        Func<Task> act = async () => await userManagementService.CreateAsync(request);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(expectedError);
    }

    [Fact]
    public async Task UpdateUser_ShouldPreserveMissingPassword_AndApplyAdministratorPasswordChange()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        session.SignIn(new AuthenticatedUserDto(
            1,
            "admin",
            "Administrator",
            PermissionCatalog.All.Select(p => p.Key).ToArray()));

        var createResult = await userManagementService.CreateAsync(new SaveUserRequest(
            null,
            "test-user",
            "Original Name",
            "password123!",
            true,
            new[] { PermissionKeys.SalesView },
            new[] { 1 }
        ));

        var savedUserBefore = await db.Users.FirstOrDefaultAsync(u => u.Id == createResult.Id);
        var originalHash = savedUserBefore!.PasswordHash;

        var updateRequest = new SaveUserRequest(
            createResult.Id,
            "updated-user",
            "Updated Name",
            "", // Empty password
            true,
            new[] { PermissionKeys.SalesView, PermissionKeys.SalesCreate },
            new[] { 1 }
        );

        // Act
        var updateResult = await userManagementService.UpdateAsync(updateRequest);

        // Assert
        updateResult.Username.Should().Be("updated-user");
        updateResult.FullName.Should().Be("Updated Name");
        updateResult.PermissionKeys.Should().BeEquivalentTo(new[] { PermissionKeys.SalesView, PermissionKeys.SalesCreate });

        var savedUserAfter = await db.Users.FirstOrDefaultAsync(u => u.Id == createResult.Id);
        savedUserAfter!.PasswordHash.Should().Be(originalHash);

        await userManagementService.UpdateAsync(updateRequest with
        {
            Password = "UpdatedPassword!123",
            RowVersion = updateResult.RowVersion
        });

        db.ChangeTracker.Clear();
        var passwordUpdatedUser = await db.Users.SingleAsync(user => user.Id == createResult.Id);
        passwordHasher.VerifyPassword("UpdatedPassword!123", passwordUpdatedUser.PasswordHash).Should().BeTrue();
        passwordHasher.VerifyPassword("password123!", passwordUpdatedUser.PasswordHash).Should().BeFalse();
        passwordUpdatedUser.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task ResetPassword_ShouldHashNewPassword_WithoutForcingAnotherChange()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();
        var admin = await db.Users.SingleAsync(user => user.Username == "admin");
        session.SignIn(new AuthenticatedUserDto(
            admin.Id,
            admin.Username,
            admin.FullName,
            [PermissionKeys.UsersView, PermissionKeys.UsersResetPassword],
            true,
            admin.SecurityStamp));
        var target = new User
        {
            Username = "reset-target",
            FullName = "Reset Target",
            PasswordHash = passwordHasher.HashPassword("OriginalPassword!123"),
            IsActive = true,
            MustChangePassword = true
        };
        db.Users.Add(target);
        await db.SaveChangesAsync();
        var branchId = await db.Branches.Select(branch => branch.Id).FirstAsync();
        db.UserBranches.Add(new UserBranch { UserId = target.Id, BranchId = branchId });
        await db.SaveChangesAsync();

        await userManagementService.ResetPasswordAsync(new ResetPasswordRequest(
            target.Id,
            "ReplacementPassword!123"));

        db.ChangeTracker.Clear();
        var saved = await db.Users.SingleAsync(user => user.Id == target.Id);
        saved.PasswordHash.Should().NotBe("ReplacementPassword!123");
        passwordHasher.VerifyPassword("ReplacementPassword!123", saved.PasswordHash).Should().BeTrue();
        passwordHasher.VerifyPassword("OriginalPassword!123", saved.PasswordHash).Should().BeFalse();
        saved.MustChangePassword.Should().BeFalse();

        var login = await scope.ServiceProvider.GetRequiredService<IAuthService>()
            .LoginAsync(new LoginRequest("reset-target", "ReplacementPassword!123", branchId));
        login.Succeeded.Should().BeTrue();
        login.User!.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public async Task SetActive_DisableLastActiveUser_ShouldThrowException()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        // Sign in as a different user to try and disable admin (which is the last active user)
        session.SignIn(new AuthenticatedUserDto(
            99,
            "temp-user",
            "Temp User",
            PermissionCatalog.All.Select(p => p.Key).ToArray()));

        var adminUser = await db.Users.FirstAsync(u => u.Username == "admin");

        // Act & Assert
        Func<Task> act = async () => await userManagementService.SetActiveAsync(adminUser.Id, false);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*مسؤول نظام نشط واحد على الأقل*");
    }

    [Fact]
    public async Task SetActive_DisableSelf_ShouldThrowException()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var adminUser = await db.Users.FirstAsync(u => u.Username == "admin");
        
        // Add another active user so it's not the last active user check failing
        db.Users.Add(new User { Username = "other", FullName = "Other", PasswordHash = "hash", IsActive = true });
        await db.SaveChangesAsync();

        session.SignIn(new AuthenticatedUserDto(
            adminUser.Id,
            "admin",
            "Administrator",
            PermissionCatalog.All.Select(p => p.Key).ToArray()));

        // Act & Assert
        Func<Task> act = async () => await userManagementService.SetActiveAsync(adminUser.Id, false);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*الحساب الذي تستخدمه حالياً*");
    }

    [Fact]
    public async Task DeleteUser_DeleteSelf_ShouldThrowException()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var adminUser = await db.Users.FirstAsync(u => u.Username == "admin");

        session.SignIn(new AuthenticatedUserDto(
            adminUser.Id,
            "admin",
            "Administrator",
            PermissionCatalog.All.Select(p => p.Key).ToArray()));

        // Act & Assert
        Func<Task> act = async () => await userManagementService.DeleteAsync(adminUser.Id);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*الحساب الذي تستخدمه حالياً*");
    }

    [Fact]
    public async Task Service_AuthorizationGuard_ShouldThrowException_WhenMissingPermission()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var userManagementService = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        // Sign in a user with absolutely no permissions
        session.SignIn(new AuthenticatedUserDto(
            2,
            "limited-user",
            "Limited User",
            Array.Empty<string>()));

        // Act & Assert
        Func<Task> act = async () => await userManagementService.SearchAsync(null);
        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("ليس لديك صلاحية لتنفيذ هذا الإجراء.");
    }

    [Fact]
    public async Task AuthService_Login_ShouldSucceed_ForActiveUserWithCorrectPassword()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        var request = new LoginRequest("admin", "admin123-test-only");

        // Act
        var result = await authService.LoginAsync(request);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.User.Should().NotBeNull();
        result.User!.Username.Should().Be("admin");
        result.User.Permissions.Should().Contain(PermissionCatalog.All.Select(p => p.Key));
    }

    [Fact]
    public async Task AuthService_Login_ShouldSetCurrentBranch_WhenUserHasSingleBranch()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var branchContext = scope.ServiceProvider.GetRequiredService<IBranchContext>();
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);
        await seeder.SeedAsync();

        // Act
        var result = await authService.LoginAsync(new LoginRequest("admin", "admin123-test-only"));

        // Assert
        result.Succeeded.Should().BeTrue();
        result.AvailableBranches.Should().NotBeNull();
        branchContext.CurrentBranch.Should().NotBeNull();
        branchContext.CurrentBranchId.Should().Be(result.AvailableBranches!.Single().Id);
    }

    [Fact]
    public async Task AuthService_Login_ShouldFail_ForInactiveUser()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new User
        {
            Username = "inactive",
            FullName = "Inactive User",
            PasswordHash = passwordHasher.HashPassword("password123"),
            IsActive = false
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var request = new LoginRequest("inactive", "password123");

        // Act
        var result = await authService.LoginAsync(request);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DefaultDataSeeder_ShouldRecreateAdmin_WhenNoUsersExist()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var systemSafeService = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seeder = new DefaultDataSeeder(db, passwordHasher, systemSafeService);

        // Verify database is completely empty
        (await db.Users.CountAsync()).Should().Be(0);

        // Act
        await seeder.SeedAsync();

        // Assert
        var adminUser = await db.Users.Include(u => u.UserPermissions).FirstOrDefaultAsync(u => u.Username == "admin");
        adminUser.Should().NotBeNull();
        adminUser!.FullName.Should().Be("مسؤول النظام");
        adminUser.IsActive.Should().BeTrue();
        adminUser.UserPermissions.Count.Should().Be(PermissionCatalog.All.Count);
    }
}
