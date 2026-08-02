using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Inventory;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using Bakery.Infrastructure.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Bakery.IntegrationTests;

public class BranchManagementTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public BranchManagementTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateBranch_ShouldCreateDefaultSafesAndSettings()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var branchService = scope.ServiceProvider.GetRequiredService<IBranchService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        var req = new CreateBranchRequest("ALEX", "فرع الإسكندرية", "ملاحظات الإسكندرية");

        // Act
        var created = await branchService.CreateAsync(req);

        // Assert
        Assert.NotNull(created);
        Assert.Equal("ALEX", created.Code);

        // Check that safes were provisioned
        var safes = await db.Safes.IgnoreQueryFilters().Where(x => x.BranchId == created.Id).ToListAsync();
        Assert.Equal(3, safes.Count);
        Assert.Contains(safes, s => s.Type == SafeType.Main);
        Assert.Contains(safes, s => s.Type == SafeType.Daily);
        Assert.Contains(safes, s => s.Type == SafeType.Private);

        // Check that default settings were provisioned
        var settings = await db.AppSettings.IgnoreQueryFilters().Where(x => x.BranchId == created.Id).ToListAsync();
        Assert.True(settings.Count >= 3);
        Assert.Contains(settings, s => s.Key == "UiCulture" && s.Value == "ar-EG");
        Assert.Contains(settings, s => s.Key == "Inventory.AllowNegativeStock" && s.Value == "false");
    }

    [Fact]
    public async Task DeleteBranch_ShouldSoftDelete_WhenNoDependencies()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var branchService = scope.ServiceProvider.GetRequiredService<IBranchService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        var req = new CreateBranchRequest("ASWAN", "فرع أسوان", "ملاحظات أسوان");
        var branch = await branchService.CreateAsync(req);

        // Act
        var canDelete = await branchService.CanDeleteAsync(branch.Id);
        Assert.True(canDelete);

        await branchService.DeleteAsync(branch.Id);

        // Assert
        var deletedBranch = await db.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == branch.Id);
        Assert.NotNull(deletedBranch);
        Assert.True(deletedBranch.IsDeleted);

        // Associated safes and settings should be deleted/cleaned up
        var safes = await db.Safes.IgnoreQueryFilters().Where(x => x.BranchId == branch.Id).ToListAsync();
        Assert.All(safes, s => Assert.True(s.IsDeleted));
 
        var settings = await db.AppSettings.IgnoreQueryFilters().Where(x => x.BranchId == branch.Id).ToListAsync();
        Assert.All(settings, s => Assert.True(s.IsDeleted));
    }

    [Fact]
    public async Task DeleteBranch_ShouldPreventDeletion_WhenHasMasterOrTransactionalData()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var branchService = scope.ServiceProvider.GetRequiredService<IBranchService>();
        var branchContext = (IInternalBranchContext)scope.ServiceProvider.GetRequiredService<IBranchContext>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        var req = new CreateBranchRequest("LUXOR", "فرع الأقصر", "");
        var branch = await branchService.CreateAsync(req);

        // Set context to Luxor to add an item
        var originalBranch = branchContext.CurrentBranch;
        branchContext.ConfigureBranch(branch);

        var unit = await db.Units.FirstOrDefaultAsync();
        if (unit is null)
        {
            unit = new Unit { Name = "Piece", Symbol = "pcs" };
            db.Units.Add(unit);
            await db.SaveChangesAsync();
        }
        db.Items.Add(new Item { Code = "LUXOR_ITEM", Name = "Luxor Bread", Type = ItemType.FinishedProduct, BaseUnitId = unit.Id });
        await db.SaveChangesAsync();

        // Switch back context
        branchContext.ConfigureBranch(originalBranch!);

        // Act
        var canDelete = await branchService.CanDeleteAsync(branch.Id);
        
        // Assert
        Assert.False(canDelete);
        await Assert.ThrowsAnyAsync<Exception>(() => branchService.DeleteAsync(branch.Id));
    }

    [Fact]
    public async Task BranchIsolation_VerifyAcrossAllModules()
    {
        // Arrange
        using var scope = _fixture.ServiceProvider.CreateScope();
        var branchService = scope.ServiceProvider.GetRequiredService<IBranchService>();
        var branchContext = (IInternalBranchContext)scope.ServiceProvider.GetRequiredService<IBranchContext>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        // Create two new branches
        var b1 = await branchService.CreateAsync(new CreateBranchRequest("ISOL1", "العزل 1", ""));
        var b2 = await branchService.CreateAsync(new CreateBranchRequest("ISOL2", "العزل 2", ""));

        var unit = await db.Units.FirstOrDefaultAsync();
        if (unit is null)
        {
            unit = new Unit { Name = "Piece", Symbol = "pcs" };
            db.Units.Add(unit);
            await db.SaveChangesAsync();
        }

        // Populate ISOL1
        branchContext.ConfigureBranch(b1);

        var item1 = new Item { Code = "ISOL1_ITEM", Name = "Isol1 Item", Type = ItemType.FinishedProduct, BaseUnitId = unit.Id };
        var party1 = new Party { Name = "Isol1 Party", Type = PartyType.Customer };
        var jobRole1 = new JobRole { Name = "Isol1 Role", WageType = WageType.Production };
        db.Items.Add(item1);
        db.Parties.Add(party1);
        db.JobRoles.Add(jobRole1);
        await db.SaveChangesAsync();

        var emp1 = new Employee { Code = "ISOL1_EMP", Name = "Isol1 Emp", PartyId = party1.Id, JobRoleId = jobRole1.Id, HireDate = DateOnly.FromDateTime(DateTime.Today) };
        db.Employees.Add(emp1);
        await db.SaveChangesAsync();

        // Populate ISOL2
        branchContext.ConfigureBranch(b2);

        var item2 = new Item { Code = "ISOL2_ITEM", Name = "Isol2 Item", Type = ItemType.FinishedProduct, BaseUnitId = unit.Id };
        var party2 = new Party { Name = "Isol2 Party", Type = PartyType.Customer };
        var jobRole2 = new JobRole { Name = "Isol2 Role", WageType = WageType.Production };
        db.Items.Add(item2);
        db.Parties.Add(party2);
        db.JobRoles.Add(jobRole2);
        await db.SaveChangesAsync();

        var emp2 = new Employee { Code = "ISOL2_EMP", Name = "Isol2 Emp", PartyId = party2.Id, JobRoleId = jobRole2.Id, HireDate = DateOnly.FromDateTime(DateTime.Today) };
        db.Employees.Add(emp2);
        await db.SaveChangesAsync();

        // Assert query isolation for ISOL1
        branchContext.ConfigureBranch(b1);
        var isol1Items = await db.Items.ToListAsync();
        Assert.Single(isol1Items);
        Assert.Equal("ISOL1_ITEM", isol1Items[0].Code);

        var isol1Parties = await db.Parties.ToListAsync();
        Assert.Single(isol1Parties);
        Assert.Equal("Isol1 Party", isol1Parties[0].Name);

        var isol1Employees = await db.Employees.ToListAsync();
        Assert.Single(isol1Employees);
        Assert.Equal("Isol1 Emp", isol1Employees[0].Name);

        // Assert query isolation for ISOL2
        branchContext.ConfigureBranch(b2);
        var isol2Items = await db.Items.ToListAsync();
        Assert.Single(isol2Items);
        Assert.Equal("ISOL2_ITEM", isol2Items[0].Code);

        var isol2Parties = await db.Parties.ToListAsync();
        Assert.Single(isol2Parties);
        Assert.Equal("Isol2 Party", isol2Parties[0].Name);

        var isol2Employees = await db.Employees.ToListAsync();
        Assert.Single(isol2Employees);
        Assert.Equal("Isol2 Emp", isol2Employees[0].Name);
    }

    [Fact]
    public async Task ProductionReadinessE2EIsolationTest()
    {
        // 1. Arrange & Setup Services
        using var scope = _fixture.ServiceProvider.CreateScope();
        var branchService = scope.ServiceProvider.GetRequiredService<IBranchService>();
        var branchContext = (IInternalBranchContext)scope.ServiceProvider.GetRequiredService<IBranchContext>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        // 2. Create Branch A and Branch B
        var branchA = await branchService.CreateAsync(new CreateBranchRequest("BR_A", "فرع أ", ""));
        var branchB = await branchService.CreateAsync(new CreateBranchRequest("BR_B", "فرع ب", ""));

        var unit = await db.Units.FirstOrDefaultAsync();
        if (unit is null)
        {
            unit = new Unit { Name = "Piece", Symbol = "pcs" };
            db.Units.Add(unit);
            await db.SaveChangesAsync();
        }

        // 3. Switch to Branch A and create entities
        branchContext.ConfigureBranch(branchA);

        // a. Product
        var product = new Item { Code = "PRODUCT_A", Name = "Product A", Type = ItemType.FinishedProduct, BaseUnitId = unit.Id };
        db.Items.Add(product);

        // b. Customer
        var customer = new Party { Name = "Customer A", Type = PartyType.Customer };
        db.Parties.Add(customer);

        // c. Employee
        var jobRole = new JobRole { Name = "Baker A", WageType = WageType.Production };
        db.JobRoles.Add(jobRole);
        await db.SaveChangesAsync();

        var employee = new Employee { Code = "EMP_A", Name = "Employee A", PartyId = customer.Id, JobRoleId = jobRole.Id, HireDate = DateOnly.FromDateTime(DateTime.Today) };
        db.Employees.Add(employee);

        // d. Safe
        var safe = new Safe { Name = "Safe A", IsActive = true, Type = SafeType.Main };
        db.Safes.Add(safe);
        await db.SaveChangesAsync();

        // f. Sale Invoice
        var workingDay = new WorkingDay { BusinessDate = DateOnly.FromDateTime(DateTime.Today), Status = WorkingDayStatus.Open, OpenedBy = "test-admin", OpeningCash = 100m };
        db.WorkingDays.Add(workingDay);
        await db.SaveChangesAsync();

        // e. Safe Transaction
        var transaction = new SafeMovement
        {
            SafeId = safe.Id,
            Amount = 500m,
            Type = SafeMovementType.Adjustment,
            ReferenceType = "Manual",
            ReferenceId = 1,
            Description = "Deposit A",
            WorkingDayId = workingDay.Id
        };
        db.SafeMovements.Add(transaction);

        var sale = new SaleInvoice
        {
            InvoiceNumber = "SALE_A_1",
            InvoiceDate = DateTime.UtcNow,
            PartyId = customer.Id,
            WorkingDayId = workingDay.Id,
            Status = InvoiceStatus.Draft,
            TotalAmount = 150m,
            TaxAmount = 0m,
            PaidAmount = 0m
        };
        db.SaleInvoices.Add(sale);
        await db.SaveChangesAsync();

        // 4. Switch to Branch B and Verify Isolation (none of Branch A data should be visible)
        branchContext.ConfigureBranch(branchB);

        var itemsB = await db.Items.ToListAsync();
        Assert.Empty(itemsB);

        var partiesB = await db.Parties.ToListAsync();
        Assert.Empty(partiesB);

        var employeesB = await db.Employees.ToListAsync();
        Assert.Empty(employeesB);

        var safesB = await db.Safes.ToListAsync();
        // Branch B should only have its own provisioned safes, not Safe A
        Assert.DoesNotContain(safesB, s => s.Name == "Safe A");

        var movementsB = await db.SafeMovements.ToListAsync();
        Assert.Empty(movementsB);

        var workingDaysB = await db.WorkingDays.ToListAsync();
        Assert.Empty(workingDaysB);

        var salesB = await db.SaleInvoices.ToListAsync();
        Assert.Empty(salesB);

        // 5. Switch back to Branch A and Verify everything is visible again
        branchContext.ConfigureBranch(branchA);

        var itemsA = await db.Items.ToListAsync();
        Assert.Contains(itemsA, i => i.Code == "PRODUCT_A");

        var partiesA = await db.Parties.ToListAsync();
        Assert.Contains(partiesA, p => p.Name == "Customer A");

        var employeesA = await db.Employees.ToListAsync();
        Assert.Contains(employeesA, e => e.Code == "EMP_A");

        var safesA = await db.Safes.ToListAsync();
        Assert.Contains(safesA, s => s.Name == "Safe A");

        var movementsA = await db.SafeMovements.ToListAsync();
        Assert.Contains(movementsA, m => m.Description == "Deposit A");

        var workingDaysA = await db.WorkingDays.ToListAsync();
        Assert.Contains(workingDaysA, w => w.Id == workingDay.Id);

        var salesA = await db.SaleInvoices.ToListAsync();
        Assert.Contains(salesA, s => s.InvoiceNumber == "SALE_A_1");
    }
}
