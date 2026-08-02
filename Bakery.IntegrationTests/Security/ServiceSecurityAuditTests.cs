using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class ServiceSecurityAuditTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public ServiceSecurityAuditTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<BakeryDbContext> InitializeTestDbAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<BakeryDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        // Seed default branch
        var branch = new Branch { Code = "MAIN", Name = "MAIN BRANCH", IsActive = true };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        db.Safes.AddRange(
            new Safe { BranchId = branch.Id, Code = "MAIN_SAFE", Name = "Main Safe", Type = SafeType.Main, IsActive = true },
            new Safe { BranchId = branch.Id, Code = "PRIVATE_SAFE", Name = "Private Safe", Type = SafeType.Private, IsActive = true },
            new Safe { BranchId = branch.Id, Code = "DAILY_CASH_SAFE", Name = "Daily Safe", Type = SafeType.Daily, IsActive = true });
        await db.SaveChangesAsync();

        var branchContext = (IInternalBranchContext)sp.GetRequiredService<IBranchContext>();
        branchContext.ConfigureBranch(new BranchDto(branch.Id, branch.Code, branch.Name, branch.IsActive, branch.Notes));

        return db;
    }

    [Fact]
    public async Task StatementService_GetStatement_Customer_ShouldEnforceCustomersView()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await InitializeTestDbAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IStatementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var customer = new Party { Name = "Customer A", Type = PartyType.Customer, IsActive = true };
        db.Parties.Add(customer);
        await db.SaveChangesAsync();

        // 1. No permissions -> should throw
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", []));
        Func<Task> act1 = async () => await service.GetStatementAsync(customer.Id);
        await act1.Should().ThrowAsync<UnauthorizedAccessException>();

        // 2. CustomersView -> should succeed
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", [PermissionKeys.CustomersView]));
        Func<Task> act2 = async () => await service.GetStatementAsync(customer.Id);
        await act2.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StatementService_GetStatement_Supplier_ShouldEnforcePurchasesView()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await InitializeTestDbAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IStatementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var supplier = new Party { Name = "Supplier A", Type = PartyType.Supplier, IsActive = true };
        db.Parties.Add(supplier);
        await db.SaveChangesAsync();

        // 1. No permissions -> should throw
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", []));
        Func<Task> act1 = async () => await service.GetStatementAsync(supplier.Id);
        await act1.Should().ThrowAsync<UnauthorizedAccessException>();

        // 2. PurchasesView -> should succeed
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", [PermissionKeys.PurchasesView]));
        Func<Task> act2 = async () => await service.GetStatementAsync(supplier.Id);
        await act2.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StatementService_GetStatement_Mixed_ShouldEnforceBothCustomersAndPurchasesView()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await InitializeTestDbAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IStatementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var mixed = new Party { Name = "Mixed Party A", Type = PartyType.Mixed, IsActive = true };
        db.Parties.Add(mixed);
        await db.SaveChangesAsync();

        // 1. No permissions -> should throw
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", []));
        Func<Task> act1 = async () => await service.GetStatementAsync(mixed.Id);
        await act1.Should().ThrowAsync<UnauthorizedAccessException>();

        // 2. CustomersView only -> should throw
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", [PermissionKeys.CustomersView]));
        Func<Task> act2 = async () => await service.GetStatementAsync(mixed.Id);
        await act2.Should().ThrowAsync<UnauthorizedAccessException>();

        // 3. PurchasesView only -> should throw
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", [PermissionKeys.PurchasesView]));
        Func<Task> act3 = async () => await service.GetStatementAsync(mixed.Id);
        await act3.Should().ThrowAsync<UnauthorizedAccessException>();

        // 4. Both permissions -> should succeed
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", [PermissionKeys.CustomersView, PermissionKeys.PurchasesView]));
        Func<Task> act4 = async () => await service.GetStatementAsync(mixed.Id);
        await act4.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RecipeService_GetRecipeByProducedItemId_ShouldEnforceProductionView()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await InitializeTestDbAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IRecipeService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var unit = new Unit { Name = "Piece", Symbol = "pcs", IsActive = true };
        db.Units.Add(unit);
        await db.SaveChangesAsync();

        var item = new Item { Code = "ITEM1", Name = "Item 1", Type = ItemType.FinishedProduct, BaseUnitId = unit.Id, IsActive = true };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        // 1. No permissions -> should throw
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", []));
        Func<Task> act1 = async () => await service.GetRecipeByProducedItemIdAsync(item.Id);
        await act1.Should().ThrowAsync<UnauthorizedAccessException>();

        // 2. ProductionView -> should succeed (return null since no recipe exists, but not throw)
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", [PermissionKeys.ProductionView]));
        var recipe = await service.GetRecipeByProducedItemIdAsync(item.Id);
        recipe.Should().BeNull();
    }

    [Fact]
    public async Task StockCalculationService_GetCurrentStock_ShouldFilterByInventoryView()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await InitializeTestDbAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IStockCalculationService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        // 1. No permissions -> should throw
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", []));
        Func<Task> act1 = async () => await service.GetCurrentStockAsync();
        await act1.Should().ThrowAsync<UnauthorizedAccessException>();

        // 2. InventoryView -> should succeed
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", [PermissionKeys.InventoryView]));
        Func<Task> act2 = async () => await service.GetCurrentStockAsync();
        await act2.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PartyService_SearchAndLookup_Customer_ShouldEnforceRespectivePermissions()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await InitializeTestDbAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IPartyService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var customer = new Party { Name = "Customer B", Type = PartyType.Customer, IsActive = true };
        db.Parties.Add(customer);
        await db.SaveChangesAsync();

        // 1. No permissions -> both should return empty
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", []));
        var res1 = await service.SearchAsync(new PartySearchRequest { Type = PartyType.Customer });
        res1.Should().BeEmpty();
        var lookup1 = await service.LookupAsync(new PartySearchRequest { Type = PartyType.Customer });
        lookup1.Should().BeEmpty();

        // 2. SalesCreate only -> SearchAsync should be empty, but LookupAsync should succeed
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", [PermissionKeys.SalesCreate]));
        var res2 = await service.SearchAsync(new PartySearchRequest { Type = PartyType.Customer });
        res2.Should().BeEmpty();
        var lookup2 = await service.LookupAsync(new PartySearchRequest { Type = PartyType.Customer });
        lookup2.Should().NotBeEmpty();

        // 3. CustomersView -> both should succeed
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", [PermissionKeys.CustomersView]));
        var res3 = await service.SearchAsync(new PartySearchRequest { Type = PartyType.Customer });
        res3.Should().NotBeEmpty();
        var lookup3 = await service.LookupAsync(new PartySearchRequest { Type = PartyType.Customer });
        lookup3.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SystemResetService_ResetTransactionalData_ShouldEnforceSettingsResetSystem()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await InitializeTestDbAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<ISystemResetService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();
        var verifier = scope.ServiceProvider.GetRequiredService<IOwnerResetCodeVerifier>();
        var authorization = verifier.Authorize("124578");
        authorization.Should().NotBeNull();

        // 1. General settings permission only -> should throw
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", [PermissionKeys.SettingsSystem]));
        Func<Task> act1 = async () => await service.ResetTransactionalDataAsync(authorization!);
        await act1.Should().ThrowAsync<UnauthorizedAccessException>();

        // 2. The reset permission without Super Administrator status is still denied.
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", [PermissionKeys.SettingsResetSystem]));
        Func<Task> act2 = async () => await service.ResetTransactionalDataAsync(authorization!);
        await act2.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task WasteService_Operations_ShouldEnforceProductionWaste()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await InitializeTestDbAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IWasteService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        // 1. No permissions -> should throw
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", []));
        Func<Task> act1 = async () => await service.GetEntriesAsync(null, null, null, null, null);
        await act1.Should().ThrowAsync<UnauthorizedAccessException>();

        // 2. ProductionWaste -> should succeed (not throw auth exception)
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", [PermissionKeys.ProductionWaste]));
        Func<Task> act2 = async () => await service.GetEntriesAsync(null, null, null, null, null);
        try
        {
            await act2();
        }
        catch (Exception ex)
        {
            ex.Should().NotBeOfType<UnauthorizedAccessException>();
        }
    }

    [Fact]
    public async Task SettlementService_RecordSettlement_ShouldEnforceSalariesAndAdvances()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = await InitializeTestDbAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<ISettlementService>();
        var session = scope.ServiceProvider.GetRequiredService<IUserSessionService>();

        var admin = new User
        {
            Username = "test-admin",
            FullName = "Test Admin",
            PasswordHash = "test",
            IsActive = true,
            IsSuperAdmin = true
        };
        db.Users.Add(admin);

        var jobRole = new JobRole { Name = "Baker", IsActive = true };
        db.JobRoles.Add(jobRole);
        
        var party = new Party { Name = "Employee Party A", Type = PartyType.Employee, IsActive = true };
        db.Parties.Add(party);
        
        await db.SaveChangesAsync();

        var employee = new Employee 
        { 
            Name = "Employee A", 
            WageType = WageType.Monthly, 
            MonthlySalary = 5000, 
            IsActive = true,
            JobRoleId = jobRole.Id,
            PartyId = party.Id
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        // Establish the business prerequisite as an authorized manager before
        // testing the employee-specific permissions below.
        session.SignIn(new AuthenticatedUserDto(admin.Id, "test-admin", "Test Admin", [PermissionKeys.WorkingDayOpen], true));
        var workingDayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        var openResult = await workingDayService.OpenDayAsync(
            new OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today), 0, "Security test prerequisite"));
        openResult.Succeeded.Should().BeTrue(openResult.ErrorMessage);

        var settlementBaseOnly = new EmployeeSettlement
        {
            EmployeeId = employee.Id,
            WageTypeSnapshot = WageType.Monthly,
            MonthlySalary = 1000,
            BaseAmount = 1000,
            SettlementDate = DateTime.Today
        };

        var settlementAdvanceOnly = new EmployeeSettlement
        {
            EmployeeId = employee.Id,
            WageTypeSnapshot = WageType.Monthly,
            Advances = 500,
            SettlementDate = DateTime.Today
        };

        // 1. No permissions -> should throw on both
        session.SignIn(new AuthenticatedUserDto(2, "user", "User", []));
        Func<Task> actBase1 = async () => await service.RecordSettlementAsync(settlementBaseOnly);
        await actBase1.Should().ThrowAsync<UnauthorizedAccessException>();
        Func<Task> actAdv1 = async () => await service.RecordSettlementAsync(settlementAdvanceOnly);
        await actAdv1.Should().ThrowAsync<UnauthorizedAccessException>();

        // 2. Payroll management permission -> should allow base only, throw on advance
        session.SignIn(new AuthenticatedUserDto(2, "user", "User",
            [PermissionKeys.EmployeesView, PermissionKeys.EmployeesViewSalary, PermissionKeys.EmployeesManagePayroll]));
        Func<Task> actBase2 = async () => await service.RecordSettlementAsync(settlementBaseOnly);
        try
        {
            await actBase2();
        }
        catch (Exception ex)
        {
            ex.Should().NotBeOfType<UnauthorizedAccessException>();
        }

        Func<Task> actAdv2 = async () => await service.RecordSettlementAsync(settlementAdvanceOnly);
        await actAdv2.Should().ThrowAsync<UnauthorizedAccessException>();

        // 3. Both permissions -> should allow saving a combined settlement (base + advances)
        session.SignIn(new AuthenticatedUserDto(2, "user", "User",
            [PermissionKeys.EmployeesView, PermissionKeys.EmployeesViewSalary, PermissionKeys.EmployeesManagePayroll, PermissionKeys.EmployeesAdvances]));
        var settlementCombined = new EmployeeSettlement
        {
            EmployeeId = employee.Id,
            WageTypeSnapshot = WageType.Monthly,
            MonthlySalary = 1000,
            BaseAmount = 1000,
            Advances = 500,
            SettlementDate = DateTime.Today
        };
        Func<Task> actCombined = async () => await service.RecordSettlementAsync(settlementCombined);
        try
        {
            await actCombined();
        }
        catch (Exception ex)
        {
            ex.Should().NotBeOfType<UnauthorizedAccessException>();
        }
    }

    [Fact]
    public void PermissionService_EnsurePermission_ShouldLogWarningOnFailure()
    {
        var fakeSession = new FakeUserSessionService();
        var fakeLogger = new FakeLogger();
        var service = new Bakery.Infrastructure.Services.PermissionService(fakeSession, fakeLogger);

        Action act = () => service.EnsurePermission("Some.Permission");

        act.Should().Throw<UnauthorizedAccessException>();
        fakeLogger.LoggedMessages.Should().ContainSingle();
        fakeLogger.LoggedMessages[0].Should().Contain("Warning");
        fakeLogger.LoggedMessages[0].Should().Contain("Security Violation");
        fakeLogger.LoggedMessages[0].Should().Contain("test-user");
        fakeLogger.LoggedMessages[0].Should().Contain("42");
        fakeLogger.LoggedMessages[0].Should().Contain("Some.Permission");
    }

    private class FakeLogger : Microsoft.Extensions.Logging.ILogger<Bakery.Infrastructure.Services.PermissionService>
    {
        public List<string> LoggedMessages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            LoggedMessages.Add($"{logLevel}: {msg}");
        }
    }

    private class FakeUserSessionService : IUserSessionService
    {
        public int? UserId => 42;
        public string Username => "test-user";
        public string FullName => "Test User";
        public IReadOnlyCollection<string> Permissions => [];
        public bool IsAuthenticated => true;
        public bool IsSuperAdmin => false;
        public AuthenticatedUserDto? CurrentUser => new AuthenticatedUserDto(42, "test-user", "Test User", []);

        public bool HasPermission(string key) => false;
        public void SignIn(AuthenticatedUserDto user) { }
        public void SignOut() { }
    }
}
