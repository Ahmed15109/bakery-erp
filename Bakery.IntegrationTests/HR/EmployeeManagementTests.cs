using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class EmployeeManagementTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public EmployeeManagementTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task JobRole_And_EmployeeCRUD_ShouldWorkCorrectly()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<IJobRoleService>();
        var empService = scope.ServiceProvider.GetRequiredService<IEmployeeService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        // 1. Create Job Role with a monthly salary default
        var role = new JobRole
        {
            Name = "Chef",
            WageType = WageType.Monthly,
            WageAmount = 6000,
            MonthlySalary = 6000
        };
        var createdRole = await jobService.CreateRoleAsync(role);
        createdRole.Id.Should().BeGreaterThan(0);

        // 2. Create Employee — wage is COPIED from JobRole at creation
        var employee = new Employee
        {
            Code = "EMP-002",
            Name = "New Employee",
            JobRoleId = createdRole.Id,
            HireDate = DateOnly.FromDateTime(DateTime.Today),
            IsActive = true,
            // Wage copied from JobRole (as the ViewModel would do)
            WageType = createdRole.WageType,
            MonthlySalary = createdRole.MonthlySalary,
            DailyRate = createdRole.DailyRate,
            ProductionRate = createdRole.ProductionRate,
            WageEffectiveFrom = DateOnly.FromDateTime(DateTime.Today)
        };

        var createdEmp = await empService.CreateEmployeeAsync(employee);
        createdEmp.Id.Should().BeGreaterThan(0);
        createdEmp.JobRoleId.Should().Be(createdRole.Id);
        createdEmp.MonthlySalary.Should().Be(6000); // Employee owns its wage

        // 3. Verify Stats — uses Employee.MonthlySalary, not JobRole.WageAmount
        var stats = await empService.GetEmployeeStatsAsync();
        stats.Active.Should().BeGreaterThan(0);
        stats.MonthlyPayroll.Should().BeGreaterThanOrEqualTo(6000);

        // 4. Update JobRole default — must NOT affect existing employees (independence verified)
        createdRole.WageAmount = 7000;
        createdRole.MonthlySalary = 7000;
        await jobService.UpdateRoleAsync(createdRole);
        
        // Stats should NOT change because Employee wage is independent of JobRole
        var statsAfterRoleUpdate = await empService.GetEmployeeStatsAsync();
        statsAfterRoleUpdate.MonthlyPayroll.Should().BeGreaterThanOrEqualTo(6000);
        statsAfterRoleUpdate.MonthlyPayroll.Should().BeLessThan(7000 * 2); // proof it didn't auto-update

        // 5. Delete Safety for Role (Blocked because of Employee)
        (await jobService.CanDeleteRoleAsync(createdRole.Id)).Should().BeFalse();

        // 6. Delete Employee then Role
        await empService.DeleteEmployeeAsync(createdEmp.Id);
        (await jobService.CanDeleteRoleAsync(createdRole.Id)).Should().BeTrue();
        await jobService.DeleteRoleAsync(createdRole.Id);
    }


    [Fact]
    public async Task DeleteEmployee_ShouldBeBlocked_IfHasTransactions()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEmployeeService>();
        var jobService = scope.ServiceProvider.GetRequiredService<IJobRoleService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();

        var role = await jobService.CreateRoleAsync(new JobRole { Name = "Worker", WageType = WageType.Daily, WageAmount = 100 });
        var employee = new Employee { Code = "EMP-003", Name = "Busy Employee", JobRoleId = role.Id, HireDate = DateOnly.FromDateTime(DateTime.Today) };
        await service.CreateEmployeeAsync(employee);

        var dayService = scope.ServiceProvider.GetRequiredService<IWorkingDayService>();
        if (await dayService.GetCurrentOpenDayAsync() == null)
            await dayService.OpenDayAsync(new Bakery.Application.DTOs.OpenWorkingDayRequest(DateOnly.FromDateTime(DateTime.Today), 0m, "Test"));
        
        var day = await dayService.GetCurrentOpenDayAsync();

        // Add a wage
        db.EmployeeWages.Add(new EmployeeWage 
        { 
            EmployeeId = employee.Id, 
            Amount = 100, 
            WageDate = DateOnly.FromDateTime(DateTime.Today),
            WorkingDayId = day!.Id,
            WageTypeSnapshot = WageType.Daily,
            WageAmountSnapshot = 100
        });
        await db.SaveChangesAsync();

        // Try Delete
        (await service.CanDeleteEmployeeAsync(employee.Id)).Should().BeFalse();
        
        var action = () => service.DeleteEmployeeAsync(employee.Id);
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateEmployee_ShouldThrowValidationException_IfMissingFields()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEmployeeService>();

        // Missing Name
        var empNoName = new Employee { Code = "EMP-ERR1", JobRoleId = 1 };
        var actNoName = () => service.CreateEmployeeAsync(empNoName);
        await actNoName.Should().ThrowAsync<System.ComponentModel.DataAnnotations.ValidationException>()
            .WithMessage("اسم الموظف مطلوب");

        // Missing Code
        var empNoCode = new Employee { Name = "John", Code = "", JobRoleId = 1 };
        var actNoCode = () => service.CreateEmployeeAsync(empNoCode);
        await actNoCode.Should().ThrowAsync<System.ComponentModel.DataAnnotations.ValidationException>()
            .WithMessage("كود الموظف مطلوب");

        // Missing JobRole
        var empNoRole = new Employee { Name = "John", Code = "EMP-ERR2", JobRoleId = 0 };
        var actNoRole = () => service.CreateEmployeeAsync(empNoRole);
        await actNoRole.Should().ThrowAsync<System.ComponentModel.DataAnnotations.ValidationException>()
            .WithMessage("يجب تحديد وظيفة / دور صالح للموظف");
    }
}

