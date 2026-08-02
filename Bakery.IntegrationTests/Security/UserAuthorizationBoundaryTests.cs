using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Seeders;
using Bakery.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class UserAuthorizationBoundaryTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public UserAuthorizationBoundaryTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(BakeryDbContext Db, IUserManagementService Service, IUserSessionService Session, int BranchId)> PrepareAsync(
        IServiceProvider serviceProvider)
    {
        var db = serviceProvider.GetRequiredService<BakeryDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        await new DefaultDataSeeder(
            db,
            serviceProvider.GetRequiredService<IPasswordHasher>(),
            serviceProvider.GetRequiredService<ISystemSafeService>()).SeedAsync();

        var branch = await db.Branches.IgnoreQueryFilters().OrderBy(item => item.Id).FirstAsync();
        ((IInternalBranchContext)serviceProvider.GetRequiredService<IBranchContext>())
            .ConfigureBranch(new BranchDto(branch.Id, branch.Code, branch.Name, branch.IsActive, branch.Notes));
        var admin = await db.Users.IgnoreQueryFilters().SingleAsync(item => item.IsSuperAdmin);
        var session = serviceProvider.GetRequiredService<IUserSessionService>();
        session.SignIn(new AuthenticatedUserDto(
            admin.Id,
            admin.Username,
            admin.FullName,
            PermissionCatalog.All.Select(item => item.Key).ToArray(),
            true,
            admin.SecurityStamp));
        return (db, serviceProvider.GetRequiredService<IUserManagementService>(), session, branch.Id);
    }

    [Fact]
    public async Task EditOnlyUser_CanUpdateBasicProfile_WithoutReplacingSecurityAssignments()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, service, session, branchId) = await PrepareAsync(scope.ServiceProvider);
        var target = await service.CreateAsync(new SaveUserRequest(
            null, "profile-target", "Original Name", "StrongPassword!123", true,
            [PermissionKeys.SalesView], [branchId]));
        var editor = await service.CreateAsync(new SaveUserRequest(
            null, "profile-editor", "Profile Editor", "StrongPassword!123", true,
            [PermissionKeys.UsersView, PermissionKeys.UsersEdit], [branchId]));
        var editorEntity = await db.Users.AsNoTracking().SingleAsync(item => item.Id == editor.Id);
        session.SignIn(new AuthenticatedUserDto(
            editor.Id, editor.Username, editor.FullName,
            [PermissionKeys.UsersView, PermissionKeys.UsersEdit],
            false, editorEntity.SecurityStamp));

        var updated = await service.UpdateAsync(new SaveUserRequest(
            target.Id, target.Username, "Updated Profile Name", null, true,
            null, null, null, null, target.RowVersion));

        updated.FullName.Should().Be("Updated Profile Name");
        updated.PermissionKeys.Should().BeEquivalentTo(target.PermissionKeys);
        updated.BranchIds.Should().BeEquivalentTo(target.BranchIds);
        updated.RoleIds.Should().BeEquivalentTo(target.RoleIds);
    }

    [Fact]
    public async Task EditOnlyUser_CannotChangePermissionsBranchesRolesOrSafes()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, service, session, branchId) = await PrepareAsync(scope.ServiceProvider);
        var target = await service.CreateAsync(new SaveUserRequest(
            null, "security-target", "Security Target", "StrongPassword!123", true,
            [PermissionKeys.SalesView], [branchId]));
        var editor = await service.CreateAsync(new SaveUserRequest(
            null, "basic-editor", "Basic Editor", "StrongPassword!123", true,
            [PermissionKeys.UsersView, PermissionKeys.UsersEdit], [branchId]));
        var editorEntity = await db.Users.AsNoTracking().SingleAsync(item => item.Id == editor.Id);
        session.SignIn(new AuthenticatedUserDto(
            editor.Id, editor.Username, editor.FullName,
            [PermissionKeys.UsersView, PermissionKeys.UsersEdit],
            false, editorEntity.SecurityStamp));

        var action = () => service.UpdateAsync(new SaveUserRequest(
            target.Id, target.Username, target.FullName, null, true,
            [PermissionKeys.SalesView, PermissionKeys.SalesCreate], [branchId],
            null, null, target.RowVersion));

        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        db.ChangeTracker.Clear();
        var savedKeys = await db.UserPermissions
            .Where(item => item.UserId == target.Id)
            .Select(item => item.Permission.Key)
            .ToListAsync();
        savedKeys.Should().BeEquivalentTo([PermissionKeys.SalesView]);
    }

    [Fact]
    public async Task EditUserPassword_RequiresDedicatedResetPasswordPermission()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, service, session, branchId) = await PrepareAsync(scope.ServiceProvider);
        var target = await service.CreateAsync(new SaveUserRequest(
            null, "password-target", "Password Target", "StrongPassword!123", true,
            [PermissionKeys.SalesView], [branchId]));
        var originalHash = await db.Users.Where(item => item.Id == target.Id)
            .Select(item => item.PasswordHash).SingleAsync();
        var editor = await service.CreateAsync(new SaveUserRequest(
            null, "password-editor", "Password Editor", "StrongPassword!123", true,
            [PermissionKeys.UsersView, PermissionKeys.UsersEdit], [branchId]));
        var editorEntity = await db.Users.AsNoTracking().SingleAsync(item => item.Id == editor.Id);
        session.SignIn(new AuthenticatedUserDto(
            editor.Id, editor.Username, editor.FullName,
            [PermissionKeys.UsersView, PermissionKeys.UsersEdit],
            false, editorEntity.SecurityStamp));

        var action = () => service.UpdateAsync(new SaveUserRequest(
            target.Id, target.Username, target.FullName, "AnotherStrong!123", true,
            null, null, null, null, target.RowVersion));

        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        db.ChangeTracker.Clear();
        (await db.Users.Where(item => item.Id == target.Id)
            .Select(item => item.PasswordHash).SingleAsync()).Should().Be(originalHash);
    }

    [Fact]
    public async Task AssigningRole_RequiresDedicatedRoleAssignmentPermission()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, service, session, branchId) = await PrepareAsync(scope.ServiceProvider);
        var roleService = scope.ServiceProvider.GetRequiredService<IRoleManagementService>();
        var role = await roleService.CreateAsync(new SaveRoleRequest(
            null, "Sales Role", null, [PermissionKeys.SalesView]));
        var target = await service.CreateAsync(new SaveUserRequest(
            null, "role-target", "Role Target", "StrongPassword!123", true,
            [PermissionKeys.SalesView], [branchId]));
        var editorPermissions = new[]
        {
            PermissionKeys.UsersView,
            PermissionKeys.UsersEdit,
            PermissionKeys.UsersChangePermissions
        };
        var editor = await service.CreateAsync(new SaveUserRequest(
            null, "security-editor", "Security Editor", "StrongPassword!123", true,
            editorPermissions, [branchId]));
        var editorEntity = await db.Users.AsNoTracking().SingleAsync(item => item.Id == editor.Id);
        session.SignIn(new AuthenticatedUserDto(
            editor.Id, editor.Username, editor.FullName,
            editorPermissions, false, editorEntity.SecurityStamp));

        var action = () => service.UpdateAsync(new SaveUserRequest(
            target.Id, target.Username, target.FullName, null, true,
            null, null, [role.Id], null, target.RowVersion));

        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        (await db.UserRoles.AnyAsync(item => item.UserId == target.Id)).Should().BeFalse();
    }
}
