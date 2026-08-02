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
using Bakery.Shared.Helpers;
using Xunit;

namespace Bakery.IntegrationTests;

public class BranchSessionWorkflowTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public BranchSessionWorkflowTests(DatabaseFixture fixture)
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
    public async Task Login_WithOneBranch_Succeeds()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var branchContext = (IInternalBranchContext)scope.ServiceProvider.GetRequiredService<IBranchContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        branchContext.Clear();

        var branch = new Branch { Code = "BR1", Name = "Branch 1", IsActive = true };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var user = new User
        {
            Username = "user-1",
            FullName = "User 1",
            PasswordHash = passwordHasher.HashPassword("pass123"),
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserBranches.Add(new UserBranch { UserId = user.Id, BranchId = branch.Id });
        await db.SaveChangesAsync();

        // Act
        var loginResult = await authService.LoginAsync(new LoginRequest("user-1", "pass123", branch.Id));

        // Assert
        loginResult.Succeeded.Should().BeTrue();
        branchContext.CurrentBranchId.Should().Be(branch.Id);
    }

    [Fact]
    public async Task Login_WithMultipleBranches_Succeeds()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var branchContext = (IInternalBranchContext)scope.ServiceProvider.GetRequiredService<IBranchContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        branchContext.Clear();

        var b1 = new Branch { Code = "B1", Name = "Branch One", IsActive = true };
        var b2 = new Branch { Code = "B2", Name = "Branch Two", IsActive = true };
        db.Branches.AddRange(b1, b2);
        await db.SaveChangesAsync();

        var user = new User
        {
            Username = "multi-user",
            FullName = "Multi User",
            PasswordHash = passwordHasher.HashPassword("password123"),
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserBranches.AddRange(
            new UserBranch { UserId = user.Id, BranchId = b1.Id },
            new UserBranch { UserId = user.Id, BranchId = b2.Id }
        );
        await db.SaveChangesAsync();

        // Act & Assert 1: Login to Branch 2
        var result2 = await authService.LoginAsync(new LoginRequest("multi-user", "password123", b2.Id));
        result2.Succeeded.Should().BeTrue();
        branchContext.CurrentBranchId.Should().Be(b2.Id);

        // Act & Assert 2: Login to Branch 1
        var result1 = await authService.LoginAsync(new LoginRequest("multi-user", "password123", b1.Id));
        result1.Succeeded.Should().BeTrue();
        branchContext.CurrentBranchId.Should().Be(b1.Id);
    }

    [Fact]
    public async Task GetUsersForBranch_ReturnsOnlyAssignedUsers()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        var b1 = new Branch { Code = "B1", Name = "Branch 1", IsActive = true };
        var b2 = new Branch { Code = "B2", Name = "Branch 2", IsActive = true };
        db.Branches.AddRange(b1, b2);
        await db.SaveChangesAsync();

        var u1 = new User { Username = "u1", FullName = "User 1", IsActive = true, PasswordHash = "hash" };
        var u2 = new User { Username = "u2", FullName = "User 2", IsActive = true, PasswordHash = "hash" };
        var u3 = new User { Username = "u3", FullName = "User 3 (Inactive)", IsActive = false, PasswordHash = "hash" };
        var u4 = new User { Username = "u4", FullName = "User 4 (Deleted)", IsActive = true, PasswordHash = "hash" };
        db.Users.AddRange(u1, u2, u3, u4);
        await db.SaveChangesAsync();

        u4.IsDeleted = true;
        await db.SaveChangesAsync();

        db.UserBranches.Add(new UserBranch { UserId = u1.Id, BranchId = b1.Id });
        db.UserBranches.Add(new UserBranch { UserId = u2.Id, BranchId = b2.Id });
        db.UserBranches.Add(new UserBranch { UserId = u3.Id, BranchId = b1.Id }); // assigned but inactive
        db.UserBranches.Add(new UserBranch { UserId = u4.Id, BranchId = b1.Id }); // assigned but deleted
        await db.SaveChangesAsync();

        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        session.SignIn(new AuthenticatedUserDto(u1.Id, u1.Username, u1.FullName, [PermissionKeys.UsersView]));

        // Act
        var usersOfB1 = await authService.GetUsersForBranchAsync(b1.Id);
        var usersOfB2 = await authService.GetUsersForBranchAsync(b2.Id);

        // Assert
        usersOfB1.Should().HaveCount(1);
        usersOfB1.Should().Contain(u => u.Username == "u1");
        usersOfB1.Should().NotContain(u => u.Username == "u2");
        usersOfB1.Should().NotContain(u => u.Username == "u3"); // inactive
        usersOfB1.Should().NotContain(u => u.Username == "u4"); // deleted

        usersOfB2.Should().HaveCount(1);
        usersOfB2.Should().Contain(u => u.Username == "u2");
        usersOfB2.Should().NotContain(u => u.Username == "u1");
    }

    [Fact]
    public async Task Login_WithInvalidPassword_Fails()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var branch = new Branch { Code = "BR", Name = "Branch", IsActive = true };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var user = new User
        {
            Username = "user",
            FullName = "User",
            PasswordHash = passwordHasher.HashPassword("correct-password"),
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserBranches.Add(new UserBranch { UserId = user.Id, BranchId = branch.Id });
        await db.SaveChangesAsync();

        // Act
        var result = await authService.LoginAsync(new LoginRequest("user", "wrong-password", branch.Id));

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be(Loc.ErrInvalidCredentials);
    }

    [Fact]
    public async Task SwitchBranch_RefreshesContextAndData()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var branchContext = (IInternalBranchContext)scope.ServiceProvider.GetRequiredService<IBranchContext>();
        var userSessionService = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var branchService = scope.ServiceProvider.GetRequiredService<IBranchService>();

        branchContext.Clear();

        var b1 = new Branch { Code = "B1", Name = "Branch 1", IsActive = true };
        var b2 = new Branch { Code = "B2", Name = "Branch 2", IsActive = true };
        db.Branches.AddRange(b1, b2);
        await db.SaveChangesAsync();

        var user = new User { Username = "user", FullName = "User", PasswordHash = "hash" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserBranches.AddRange(
            new UserBranch { UserId = user.Id, BranchId = b1.Id },
            new UserBranch { UserId = user.Id, BranchId = b2.Id }
        );
        await db.SaveChangesAsync();

        userSessionService.SignIn(new AuthenticatedUserDto(user.Id, user.Username, user.FullName, [PermissionKeys.BranchesSwitch]));
        branchContext.ConfigureBranch(new BranchDto(b1.Id, b1.Code, b1.Name, b1.IsActive, b1.Notes));

        // Act 1
        var userBranches = await branchService.GetUserBranchesAsync(user.Id);
        userBranches.Should().HaveCount(2);

        // Switch
        branchContext.ConfigureBranch(userBranches.First(b => b.Id == b2.Id));

        // Assert
        branchContext.CurrentBranchId.Should().Be(b2.Id);
        branchContext.CurrentBranch!.Code.Should().Be("B2");
    }

    [Fact]
    public async Task Logout_ClearsStateAndRestoresSession()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var branchContext = (IInternalBranchContext)scope.ServiceProvider.GetRequiredService<IBranchContext>();
        var userSessionService = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var b1 = new Branch { Code = "B1", Name = "Branch 1", IsActive = true };
        db.Branches.Add(b1);
        await db.SaveChangesAsync();

        var user = new User { Username = "u1", FullName = "U 1", PasswordHash = passwordHasher.HashPassword("pass") };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserBranches.Add(new UserBranch { UserId = user.Id, BranchId = b1.Id });
        await db.SaveChangesAsync();

        // Login
        var login = await authService.LoginAsync(new LoginRequest("u1", "pass", b1.Id));
        login.Succeeded.Should().BeTrue();
        userSessionService.IsAuthenticated.Should().BeTrue();
        branchContext.CurrentBranchId.Should().Be(b1.Id);

        // Logout
        await authService.LogoutAsync();

        // Assert cleared
        userSessionService.IsAuthenticated.Should().BeFalse();
        userSessionService.CurrentUser.Should().BeNull();
        branchContext.CurrentBranch.Should().BeNull();
    }

    [Fact]
    public void BranchContext_ClearAndSet_BehavesCorrectly()
    {
        var context = new Bakery.Infrastructure.Services.BranchContext();
        context.CurrentBranch.Should().BeNull();
        context.CurrentBranchId.Should().BeNull();

        var dto = new BranchDto(99, "TEST", "Test Branch", true, null);
        context.ConfigureBranch(dto);

        context.CurrentBranch.Should().Be(dto);
        context.CurrentBranchId.Should().Be(99);

        context.Clear();
        context.CurrentBranch.Should().BeNull();
        context.CurrentBranchId.Should().BeNull();
    }

    [Fact]
    public async Task DataIsolation_AfterBranchSwitch_Workflow()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await PrepareCleanDatabaseAsync(scope.ServiceProvider);
        var branchContext = (IInternalBranchContext)scope.ServiceProvider.GetRequiredService<IBranchContext>();

        var b1 = new Branch { Code = "B1", Name = "Branch 1" };
        var b2 = new Branch { Code = "B2", Name = "Branch 2" };
        db.Branches.AddRange(b1, b2);
        await db.SaveChangesAsync();

        var unit = new Unit { Name = "Piece", Symbol = "pcs" };
        db.Units.Add(unit);
        await db.SaveChangesAsync();

        // Populate Branch 1
        branchContext.ConfigureBranch(new BranchDto(b1.Id, b1.Code, b1.Name, b1.IsActive, b1.Notes));
        db.Items.Add(new Item { Code = "ITEM-B1", Name = "Item Branch 1", Type = ItemType.FinishedProduct, BaseUnitId = unit.Id });
        await db.SaveChangesAsync();

        // Populate Branch 2
        branchContext.ConfigureBranch(new BranchDto(b2.Id, b2.Code, b2.Name, b2.IsActive, b2.Notes));
        db.Items.Add(new Item { Code = "ITEM-B2", Name = "Item Branch 2", Type = ItemType.FinishedProduct, BaseUnitId = unit.Id });
        await db.SaveChangesAsync();

        // Act & Assert 1: Query Branch 1
        branchContext.ConfigureBranch(new BranchDto(b1.Id, b1.Code, b1.Name, b1.IsActive, b1.Notes));
        var items1 = await db.Items.ToListAsync();
        items1.Should().HaveCount(1);
        items1.First().Code.Should().Be("ITEM-B1");

        // Act & Assert 2: Switch to Branch 2
        branchContext.ConfigureBranch(new BranchDto(b2.Id, b2.Code, b2.Name, b2.IsActive, b2.Notes));
        var items2 = await db.Items.ToListAsync();
        items2.Should().HaveCount(1);
        items2.First().Code.Should().Be("ITEM-B2");
    }
}
