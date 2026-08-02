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
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class SecurityProductionHardeningTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public SecurityProductionHardeningTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(BakeryDbContext Db, User Admin, Branch Branch)> PrepareAsync(
        IServiceProvider serviceProvider)
    {
        var db = serviceProvider.GetRequiredService<BakeryDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        var seeder = new DefaultDataSeeder(
            db,
            serviceProvider.GetRequiredService<IPasswordHasher>(),
            serviceProvider.GetRequiredService<ISystemSafeService>());
        await seeder.SeedAsync();

        var branch = await db.Branches.IgnoreQueryFilters().OrderBy(item => item.Id).FirstAsync();
        ((IInternalBranchContext)serviceProvider.GetRequiredService<IBranchContext>())
            .ConfigureBranch(new BranchDto(branch.Id, branch.Code, branch.Name, branch.IsActive, branch.Notes));
        var admin = await db.Users.IgnoreQueryFilters().SingleAsync(item => item.IsSuperAdmin);
        return (db, admin, branch);
    }

    [Fact]
    public async Task RoleLifecycle_ShouldPersistPermissions_AndWriteAuditHistory()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, admin, _) = await PrepareAsync(scope.ServiceProvider);
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        session.SignIn(new AuthenticatedUserDto(
            admin.Id,
            admin.Username,
            admin.FullName,
            PermissionCatalog.All.Select(item => item.Key).ToArray(),
            true,
            admin.SecurityStamp));
        var service = scope.ServiceProvider.GetRequiredService<IRoleManagementService>();

        var created = await service.CreateAsync(new SaveRoleRequest(
            null,
            "Sales operator",
            "Sales desk role",
            [PermissionKeys.SalesView, PermissionKeys.SalesCreate]));
        created.PermissionKeys.Should().BeEquivalentTo(
            [PermissionKeys.SalesView, PermissionKeys.SalesCreate]);

        var updated = await service.UpdateAsync(new SaveRoleRequest(
            created.Id,
            created.Name,
            "Updated role",
            [PermissionKeys.SalesView, PermissionKeys.SalesCreate, PermissionKeys.SalesPrint],
            created.RowVersion));
        updated.PermissionKeys.Should().Contain(PermissionKeys.SalesPrint);

        await service.DeleteAsync(updated.Id);

        (await db.Roles.IgnoreQueryFilters().SingleAsync(item => item.Id == updated.Id))
            .IsDeleted.Should().BeTrue();
        var actions = await db.AuditLogs.IgnoreQueryFilters()
            .Where(item => item.EntityName == nameof(Role) && item.EntityId == updated.Id)
            .Select(item => item.Action)
            .ToListAsync();
        actions.Should().Contain(["RoleCreated", "RoleUpdated", "RoleDeleted"]);
    }

    [Fact]
    public async Task AuditQuery_ShouldRequirePermission_AndRespectCurrentBranch()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, admin, branch) = await PrepareAsync(scope.ServiceProvider);
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var service = scope.ServiceProvider.GetRequiredService<IAuditQueryService>();

        session.SignIn(new AuthenticatedUserDto(
            admin.Id,
            admin.Username,
            admin.FullName,
            [PermissionKeys.SalesView],
            false,
            admin.SecurityStamp));
        await FluentActions.Invoking(() => service.SearchAsync(new AuditSearchRequest()))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        (await db.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(item => item.Action == "AuthorizationDenied" && item.EntityName == "Permission"))
            .Should().BeTrue();

        var otherBranch = new Branch { Code = "OTHER", Name = "Other branch", IsActive = true };
        db.Branches.Add(otherBranch);
        await db.SaveChangesAsync();
        var marker = $"SecurityTest-{Guid.NewGuid():N}";
        db.AuditLogs.AddRange(
            new AuditLog { BranchId = branch.Id, Action = marker, EntityName = "SecurityTest" },
            new AuditLog { BranchId = otherBranch.Id, Action = marker, EntityName = "SecurityTest" });
        await db.SaveChangesAsync();

        session.SignIn(new AuthenticatedUserDto(
            admin.Id,
            admin.Username,
            admin.FullName,
            [PermissionKeys.AuditView],
            false,
            admin.SecurityStamp));
        var rows = await service.SearchAsync(new AuditSearchRequest(marker));

        rows.Should().ContainSingle();
        rows[0].Action.Should().Be(marker);
    }

    [Fact]
    public async Task PreAuthenticationUserLookup_ShouldReturnActiveUsersForSelectedBranch()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (_, _, branch) = await PrepareAsync(scope.ServiceProvider);
        scope.ServiceProvider.GetRequiredService<IUserSessionService>().SignOut();

        var users = await scope.ServiceProvider.GetRequiredService<IAuthService>()
            .GetUsersForBranchAsync(branch.Id);

        users.Should().ContainSingle(user => user.Username == "admin");
    }

    [Fact]
    public async Task ConcurrentWithdrawals_ShouldNeverOverdrawTheSafe()
    {
        int safeId;
        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var (db, admin, _) = await PrepareAsync(setupScope.ServiceProvider);
            _fixture.ServiceProvider.GetRequiredService<IUserSessionService>().SignIn(
                new AuthenticatedUserDto(
                    admin.Id,
                    admin.Username,
                    admin.FullName,
                    PermissionCatalog.All.Select(item => item.Key).ToArray(),
                    true,
                    admin.SecurityStamp));
            var safe = await db.Safes.FirstAsync(item => item.IsActive);
            safeId = safe.Id;
            var day = await setupScope.ServiceProvider.GetRequiredService<IWorkingDayService>()
                .EnsureActiveWorkingDayAsync();
            db.SafeMovements.Add(new SafeMovement
            {
                SafeId = safeId,
                Amount = 500m,
                Description = "Opening balance",
                WorkingDayId = day.Id
            });
            await db.SaveChangesAsync();
        }

        async Task<bool> TryWithdrawAsync()
        {
            using var scope = _fixture.ServiceProvider.CreateScope();
            try
            {
                return await scope.ServiceProvider.GetRequiredService<ISafeService>()
                    .WithdrawAsync(safeId, 400m, "Concurrent withdrawal");
            }
            catch (ValidationException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TryWithdrawAsync(), TryWithdrawAsync());

        results.Count(result => result).Should().Be(1);
        using var verificationScope = _fixture.ServiceProvider.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var finalBalance = await verificationDb.SafeMovements
            .Where(item => item.SafeId == safeId)
            .SumAsync(item => item.Amount);
        finalBalance.Should().Be(100m);
    }

    [Fact]
    public async Task Login_ShouldUnionDirectPermissionsAcrossMultipleRoles()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (_, admin, branch) = await PrepareAsync(scope.ServiceProvider);
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        session.SignIn(new AuthenticatedUserDto(
            admin.Id, admin.Username, admin.FullName,
            PermissionCatalog.All.Select(item => item.Key).ToArray(),
            true, admin.SecurityStamp));
        var roleService = scope.ServiceProvider.GetRequiredService<IRoleManagementService>();
        var viewer = await roleService.CreateAsync(new SaveRoleRequest(
            null, "Sales Viewer", null, [PermissionKeys.SalesView]));
        var operatorRole = await roleService.CreateAsync(new SaveRoleRequest(
            null, "Sales Operator", null,
            [PermissionKeys.SalesView, PermissionKeys.SalesCreate]));
        await scope.ServiceProvider.GetRequiredService<IUserManagementService>().CreateAsync(
            new SaveUserRequest(
                null, "multi-role-user", "Multi Role User", "StrongPassword!123", true,
                [PermissionKeys.ProductsView], [branch.Id],
                [viewer.Id, operatorRole.Id]));

        var result = await scope.ServiceProvider.GetRequiredService<IAuthService>()
            .LoginAsync(new LoginRequest("multi-role-user", "StrongPassword!123"));

        result.Succeeded.Should().BeTrue();
        result.User.Should().NotBeNull();
        result.User!.Permissions.Should().BeEquivalentTo(
            [PermissionKeys.ProductsView, PermissionKeys.SalesView, PermissionKeys.SalesCreate]);
        result.User.Roles.Should().BeEquivalentTo(["Sales Viewer", "Sales Operator"]);
    }

    [Fact]
    public async Task UpdatingAssignedRole_ShouldImmediatelyInvalidateCurrentAffectedSession()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, admin, branch) = await PrepareAsync(scope.ServiceProvider);
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        session.SignIn(new AuthenticatedUserDto(
            admin.Id, admin.Username, admin.FullName,
            PermissionCatalog.All.Select(item => item.Key).ToArray(),
            true, admin.SecurityStamp));
        var permissions = new[]
        {
            PermissionKeys.UsersView,
            PermissionKeys.UsersChangePermissions,
            PermissionKeys.RolesView,
            PermissionKeys.RolesEdit
        };
        var roleService = scope.ServiceProvider.GetRequiredService<IRoleManagementService>();
        var role = await roleService.CreateAsync(new SaveRoleRequest(
            null, "Security Operator", "Before", permissions));
        var user = await scope.ServiceProvider.GetRequiredService<IUserManagementService>()
            .CreateAsync(new SaveUserRequest(
                null, "assigned-role-user", "Assigned Role User", "StrongPassword!123", true,
                [], [branch.Id], [role.Id]));
        var entity = await db.Users.AsNoTracking().SingleAsync(item => item.Id == user.Id);
        session.SignIn(new AuthenticatedUserDto(
            user.Id, user.Username, user.FullName, permissions, false,
            entity.SecurityStamp, true, [role.Name]));

        await roleService.UpdateAsync(new SaveRoleRequest(
            role.Id, role.Name, "After", permissions, role.RowVersion));

        session.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task StaleUserEdit_FromSeparateDbContext_ShouldBeRejected()
    {
        int userId;
        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var (_, admin, branch) = await PrepareAsync(setupScope.ServiceProvider);
            var session = setupScope.ServiceProvider.GetRequiredService<IUserSessionService>();
            session.SignIn(new AuthenticatedUserDto(
                admin.Id, admin.Username, admin.FullName,
                PermissionCatalog.All.Select(item => item.Key).ToArray(),
                true, admin.SecurityStamp));
            userId = (await setupScope.ServiceProvider.GetRequiredService<IUserManagementService>()
                .CreateAsync(new SaveUserRequest(
                    null, "concurrent-user", "Concurrent User", "StrongPassword!123", true,
                    [PermissionKeys.SalesView], [branch.Id]))).Id;
        }

        using var firstScope = _fixture.ServiceProvider.CreateScope();
        using var secondScope = _fixture.ServiceProvider.CreateScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<IUserManagementService>();
        var firstCopy = await firstService.GetByIdAsync(userId);
        var staleCopy = await secondService.GetByIdAsync(userId);

        await firstService.UpdateAsync(new SaveUserRequest(
            userId, firstCopy!.Username, "First Edit", null, true,
            null, null, null, null, firstCopy.RowVersion));
        var staleAction = () => secondService.UpdateAsync(new SaveUserRequest(
            userId, staleCopy!.Username, "Stale Edit", null, true,
            null, null, null, null, staleCopy.RowVersion));

        await staleAction.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StaleRoleEdit_FromSeparateDbContext_ShouldBeRejected()
    {
        int roleId;
        using (var setupScope = _fixture.ServiceProvider.CreateScope())
        {
            var (_, admin, _) = await PrepareAsync(setupScope.ServiceProvider);
            var session = setupScope.ServiceProvider.GetRequiredService<IUserSessionService>();
            session.SignIn(new AuthenticatedUserDto(
                admin.Id, admin.Username, admin.FullName,
                PermissionCatalog.All.Select(item => item.Key).ToArray(),
                true, admin.SecurityStamp));
            roleId = (await setupScope.ServiceProvider.GetRequiredService<IRoleManagementService>()
                .CreateAsync(new SaveRoleRequest(
                    null, "Concurrent Role", "Initial", [PermissionKeys.SalesView]))).Id;
        }

        using var firstScope = _fixture.ServiceProvider.CreateScope();
        using var secondScope = _fixture.ServiceProvider.CreateScope();
        var firstService = firstScope.ServiceProvider.GetRequiredService<IRoleManagementService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<IRoleManagementService>();
        var firstCopy = await firstService.GetByIdAsync(roleId);
        var staleCopy = await secondService.GetByIdAsync(roleId);

        await firstService.UpdateAsync(new SaveRoleRequest(
            roleId, firstCopy!.Name, "First Edit", firstCopy.PermissionKeys, firstCopy.RowVersion));
        var staleAction = () => secondService.UpdateAsync(new SaveRoleRequest(
            roleId, staleCopy!.Name, "Stale Edit", staleCopy.PermissionKeys, staleCopy.RowVersion));

        await staleAction.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task LowLevelMutationServices_ShouldEnforcePermissions_WhenResolvedDirectly()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, admin, branch) = await PrepareAsync(scope.ServiceProvider);
        scope.ServiceProvider.GetRequiredService<IUserSessionService>().SignIn(
            new AuthenticatedUserDto(
                admin.Id,
                admin.Username,
                admin.FullName,
                [PermissionKeys.SalesView],
                false));

        var branchProvisioning = scope.ServiceProvider.GetRequiredService<IBranchProvisioningService>();
        var systemSafes = scope.ServiceProvider.GetRequiredService<ISystemSafeService>();
        var attachments = scope.ServiceProvider.GetRequiredService<IAttachmentStorageService>();
        var validation = scope.ServiceProvider.GetRequiredService<IValidationService>();
        var settingsBefore = await db.AppSettings.IgnoreQueryFilters()
            .CountAsync(item => item.BranchId == branch.Id);

        await FluentActions.Invoking(() => branchProvisioning.ProvisionBranchAsync(branch.Id))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Invoking(() => systemSafes.EnsureSystemSafesAsync())
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Invoking(() => attachments.SaveAttachmentAsync("missing-attachment.pdf"))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Invoking(() => validation.IsUsernameUsedAsync(admin.Username))
            .Should().ThrowAsync<UnauthorizedAccessException>();

        var customer = new Party
        {
            Name = "Low-level boundary customer",
            Type = Bakery.Domain.Enums.PartyType.Customer,
            IsActive = true
        };
        db.Parties.Add(customer);
        await db.SaveChangesAsync();
        await FluentActions.Invoking(() => scope.ServiceProvider
                .GetRequiredService<IPartyLookupService>()
                .GetPartyRoutingInfoAsync(customer.Id))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Invoking(() => scope.ServiceProvider
                .GetRequiredService<IPartyStatementProvider>()
                .GetStatementAsync(customer.Id))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Invoking(() => scope.ServiceProvider
                .GetRequiredService<IEmployeeStatementProvider>()
                .GetStatementAsync(12345))
            .Should().ThrowAsync<UnauthorizedAccessException>();

        (await db.AppSettings.IgnoreQueryFilters()
                .CountAsync(item => item.BranchId == branch.Id))
            .Should().Be(settingsBefore);

    }

    [Fact]
    public async Task EmployeeStatement_ShouldRequireSalaryVisibility_NotOnlyEmployeeView()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, admin, _) = await PrepareAsync(scope.ServiceProvider);
        var party = new Party
        {
            Name = "Salary visibility employee",
            Type = Bakery.Domain.Enums.PartyType.Employee,
            IsActive = true
        };
        var jobRole = new JobRole
        {
            Name = "Salary visibility role",
            WageType = Bakery.Domain.Enums.WageType.Monthly,
            MonthlySalary = 1000m,
            IsActive = true
        };
        db.Parties.Add(party);
        db.JobRoles.Add(jobRole);
        await db.SaveChangesAsync();
        var employee = new Employee
        {
            PartyId = party.Id,
            JobRoleId = jobRole.Id,
            Name = party.Name,
            Code = "SALARY-VISIBILITY",
            HireDate = DateOnly.FromDateTime(DateTime.Today),
            WageType = Bakery.Domain.Enums.WageType.Monthly,
            MonthlySalary = 1000m,
            IsActive = true
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var statementService = scope.ServiceProvider.GetRequiredService<IStatementService>();

        session.SignIn(new AuthenticatedUserDto(
            admin.Id, admin.Username, admin.FullName,
            [PermissionKeys.EmployeesView], false));
        await FluentActions.Invoking(() => statementService.GetStatementAsync(employee.PartyId))
            .Should().ThrowAsync<UnauthorizedAccessException>();

        session.SignIn(new AuthenticatedUserDto(
            admin.Id, admin.Username, admin.FullName,
            [PermissionKeys.EmployeesView, PermissionKeys.EmployeesViewSalary], false));
        await FluentActions.Invoking(() => statementService.GetStatementAsync(employee.PartyId))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task AssignedRoleDeletion_ShouldBeRejected_WhileUserSessionRemainsValid()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var (db, admin, branch) = await PrepareAsync(scope.ServiceProvider);
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        session.SignIn(new AuthenticatedUserDto(
            admin.Id, admin.Username, admin.FullName,
            PermissionCatalog.All.Select(item => item.Key).ToArray(),
            true, admin.SecurityStamp));
        var roleService = scope.ServiceProvider.GetRequiredService<IRoleManagementService>();
        var rolePermissions = new[] { PermissionKeys.RolesView, PermissionKeys.RolesDelete };
        var role = await roleService.CreateAsync(new SaveRoleRequest(
            null, "Active Session Role", null, rolePermissions));
        var user = await scope.ServiceProvider.GetRequiredService<IUserManagementService>()
            .CreateAsync(new SaveUserRequest(
                null, "active-role-user", "Active Role User", "StrongPassword!123", true,
                [], [branch.Id], [role.Id]));
        var entity = await db.Users.AsNoTracking().SingleAsync(item => item.Id == user.Id);
        session.SignIn(new AuthenticatedUserDto(
            user.Id, user.Username, user.FullName, rolePermissions, false,
            entity.SecurityStamp, true, [role.Name]));

        await FluentActions.Invoking(() => roleService.DeleteAsync(role.Id))
            .Should().ThrowAsync<InvalidOperationException>();

        session.IsAuthenticated.Should().BeTrue();
        (await db.Roles.AsNoTracking().AnyAsync(item => item.Id == role.Id)).Should().BeTrue();
    }
}
