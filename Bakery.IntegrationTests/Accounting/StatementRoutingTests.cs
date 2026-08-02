using System.IO;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public class StatementRoutingTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public StatementRoutingTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetStatement_ForCustomer_ShouldQueryPartyLedgerEntries()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var statementService = scope.ServiceProvider.GetRequiredService<IStatementService>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        // Seed a working day
        var activeDay = new WorkingDay 
        { 
            BusinessDate = DateOnly.FromDateTime(DateTime.Today), 
            Status = WorkingDayStatus.Open, 
            OpenedAt = DateTime.UtcNow,
            OpenedBy = "admin"
        };
        db.WorkingDays.Add(activeDay);
        await db.SaveChangesAsync();

        // 1. Create a customer party
        var customer = new Party { Name = "Test Customer", Type = PartyType.Customer, IsActive = true };
        db.Parties.Add(customer);
        await db.SaveChangesAsync();

        // 2. Add some ledger entries
        var entry1 = new PartyLedgerEntry
        {
            PartyId = customer.Id,
            WorkingDayId = activeDay.Id,
            Amount = 1000,
            Debit = 1000,
            Credit = 0,
            ReferenceType = "SaleInvoice",
            ReferenceId = 123,
            Description = "Test invoice write-up",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        var entry2 = new PartyLedgerEntry
        {
            PartyId = customer.Id,
            WorkingDayId = activeDay.Id,
            Amount = -400,
            Debit = 0,
            Credit = 400,
            ReferenceType = "Payment",
            ReferenceId = 456,
            Description = "Customer payment",
            CreatedAt = DateTime.UtcNow
        };
        db.PartyLedgerEntries.AddRange(entry1, entry2);
        await db.SaveChangesAsync();

        // 3. Call statement service
        var lines = await statementService.GetStatementAsync(customer.Id);

        // 4. Verify results
        lines.Should().HaveCount(2);
        lines[0].Description.Should().Contain("بيع آجل فاتورة #0123");
        lines[0].Increase.Should().Be(1000);
        lines[0].Decrease.Should().Be(0);
        lines[0].RunningBalance.Should().Be(1000);

        lines[1].Description.Should().Contain("دفعة من العميل");
        lines[1].Increase.Should().Be(0);
        lines[1].Decrease.Should().Be(400);
        lines[1].RunningBalance.Should().Be(600);
    }

    [Fact]
    public async Task GetStatement_ForEmployee_ShouldQueryEmployeeTransactions()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var statementService = scope.ServiceProvider.GetRequiredService<IStatementService>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        // Seed a working day
        var activeDay = new WorkingDay 
        { 
            BusinessDate = DateOnly.FromDateTime(DateTime.Today), 
            Status = WorkingDayStatus.Open, 
            OpenedAt = DateTime.UtcNow,
            OpenedBy = "admin"
        };
        db.WorkingDays.Add(activeDay);
        await db.SaveChangesAsync();

        // 1. Create employee party and employee entity
        var party = new Party { Name = "Test Employee Party", Type = PartyType.Employee, IsActive = true };
        db.Parties.Add(party);
        await db.SaveChangesAsync();

        var jobRole = new JobRole { Name = "Baker", WageType = WageType.Monthly, MonthlySalary = 5000 };
        db.JobRoles.Add(jobRole);
        await db.SaveChangesAsync();

        var employee = new Employee
        {
            PartyId = party.Id,
            Name = "Test Employee Name",
            Code = "EMP001",
            JobRoleId = jobRole.Id,
            HireDate = DateOnly.FromDateTime(DateTime.Today),
            IsActive = true,
            WageType = WageType.Monthly,
            MonthlySalary = 5000
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        // 2. Add some employee transactions directly
        var tx1 = new EmployeeTransaction
        {
            EmployeeId = employee.Id,
            WorkingDayId = activeDay.Id,
            Type = EmployeeTransactionType.Earned,
            Amount = 5000,
            Date = DateTime.UtcNow.AddDays(-1),
            Notes = "شهر يوليو"
        };
        var tx2 = new EmployeeTransaction
        {
            EmployeeId = employee.Id,
            WorkingDayId = activeDay.Id,
            Type = EmployeeTransactionType.Advance,
            Amount = 1500,
            Date = DateTime.UtcNow,
            Notes = "سلفة أول الشهر"
        };
        db.Set<EmployeeTransaction>().AddRange(tx1, tx2);
        await db.SaveChangesAsync();

        // 3. Call statement service using party.Id
        var lines = await statementService.GetStatementAsync(party.Id);

        // 4. Verify results
        lines.Should().HaveCount(2);
        
        lines[0].Description.Should().Be("شهر يوليو");
        lines[0].Increase.Should().Be(5000);
        lines[0].Decrease.Should().Be(0);
        lines[0].RunningBalance.Should().Be(5000);

        lines[1].Description.Should().Be("سحب مقدم / سلفة");
        lines[1].Increase.Should().Be(0);
        lines[1].Decrease.Should().Be(1500);
        lines[1].RunningBalance.Should().Be(3500);
    }

    [Fact]
    public async Task GetStatement_ForEmployeeWithNoTransactions_ShouldReturnEmptyList()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var statementService = scope.ServiceProvider.GetRequiredService<IStatementService>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        // 1. Create employee party and employee entity
        var party = new Party { Name = "Clean Employee", Type = PartyType.Employee, IsActive = true };
        db.Parties.Add(party);
        await db.SaveChangesAsync();

        var jobRole = new JobRole { Name = "Cleaner", WageType = WageType.Monthly, MonthlySalary = 3000 };
        db.JobRoles.Add(jobRole);
        await db.SaveChangesAsync();

        var employee = new Employee
        {
            PartyId = party.Id,
            Name = "Clean Employee",
            Code = "EMP002",
            JobRoleId = jobRole.Id,
            HireDate = DateOnly.FromDateTime(DateTime.Today),
            IsActive = true,
            WageType = WageType.Monthly,
            MonthlySalary = 3000
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        // 2. Call statement service (no transactions added)
        var lines = await statementService.GetStatementAsync(party.Id);

        // 3. Verify results
        lines.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatement_ForEmployeeWithDiverseTransactions_ShouldCalculateRunningBalancesCorrectly()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var statementService = scope.ServiceProvider.GetRequiredService<IStatementService>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        // Seed a working day
        var activeDay = new WorkingDay 
        { 
            BusinessDate = DateOnly.FromDateTime(DateTime.Today), 
            Status = WorkingDayStatus.Open, 
            OpenedAt = DateTime.UtcNow,
            OpenedBy = "admin"
        };
        db.WorkingDays.Add(activeDay);
        await db.SaveChangesAsync();

        // 1. Create employee party and employee entity
        var party = new Party { Name = "Busy Employee", Type = PartyType.Employee, IsActive = true };
        db.Parties.Add(party);
        await db.SaveChangesAsync();

        var jobRole = new JobRole { Name = "Manager", WageType = WageType.Monthly, MonthlySalary = 8000 };
        db.JobRoles.Add(jobRole);
        await db.SaveChangesAsync();

        var employee = new Employee
        {
            PartyId = party.Id,
            Name = "Busy Employee",
            Code = "EMP003",
            JobRoleId = jobRole.Id,
            HireDate = DateOnly.FromDateTime(DateTime.Today),
            IsActive = true,
            WageType = WageType.Monthly,
            MonthlySalary = 8000
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        // 2. Add diverse transactions sequentially
        var txs = new[]
        {
            new EmployeeTransaction { EmployeeId = employee.Id, WorkingDayId = activeDay.Id, Type = EmployeeTransactionType.Earned, Amount = 3000, Date = DateTime.UtcNow.AddMinutes(-5), Notes = "راتب مستحق" },
            new EmployeeTransaction { EmployeeId = employee.Id, WorkingDayId = activeDay.Id, Type = EmployeeTransactionType.Advance, Amount = 500, Date = DateTime.UtcNow.AddMinutes(-4), Notes = "سلفة" },
            new EmployeeTransaction { EmployeeId = employee.Id, WorkingDayId = activeDay.Id, Type = EmployeeTransactionType.Bonus, Amount = 200, Date = DateTime.UtcNow.AddMinutes(-3), Notes = "مكافأة أداء" },
            new EmployeeTransaction { EmployeeId = employee.Id, WorkingDayId = activeDay.Id, Type = EmployeeTransactionType.Deduction, Amount = 100, Date = DateTime.UtcNow.AddMinutes(-2), Notes = "غياب" },
            new EmployeeTransaction { EmployeeId = employee.Id, WorkingDayId = activeDay.Id, Type = EmployeeTransactionType.SalaryPayment, Amount = 2000, Date = DateTime.UtcNow.AddMinutes(-1), Notes = "صرف نقدي" }
        };

        db.Set<EmployeeTransaction>().AddRange(txs);
        await db.SaveChangesAsync();

        // 3. Call statement service
        var lines = await statementService.GetStatementAsync(party.Id);

        // 4. Verify results and running balances step-by-step
        lines.Should().HaveCount(5);

        // 1. Earned (Credit = 3000, Running = 3000)
        lines[0].Increase.Should().Be(3000);
        lines[0].Decrease.Should().Be(0);
        lines[0].RunningBalance.Should().Be(3000);

        // 2. Advance (Debit = 500, Running = 2500)
        lines[1].Increase.Should().Be(0);
        lines[1].Decrease.Should().Be(500);
        lines[1].RunningBalance.Should().Be(2500);

        // 3. Bonus (Credit = 200, Running = 2700)
        lines[2].Increase.Should().Be(200);
        lines[2].Decrease.Should().Be(0);
        lines[2].RunningBalance.Should().Be(2700);

        // 4. Deduction (Debit = 100, Running = 2600)
        lines[3].Increase.Should().Be(0);
        lines[3].Decrease.Should().Be(100);
        lines[3].RunningBalance.Should().Be(2600);

        // 5. SalaryPayment (Debit = 2000, Running = 600)
        lines[4].Increase.Should().Be(0);
        lines[4].Decrease.Should().Be(2000);
        lines[4].RunningBalance.Should().Be(600);
    }

    [Fact]
    public async Task GetStatement_ForEmployeeWithMixedChronologicalOrdering_ShouldSortChronologicallyCorrectly()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var statementService = scope.ServiceProvider.GetRequiredService<IStatementService>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        // Seed a working day
        var activeDay = new WorkingDay 
        { 
            BusinessDate = DateOnly.FromDateTime(DateTime.Today), 
            Status = WorkingDayStatus.Open, 
            OpenedAt = DateTime.UtcNow,
            OpenedBy = "admin"
        };
        db.WorkingDays.Add(activeDay);
        await db.SaveChangesAsync();

        // 1. Create employee party and employee entity
        var party = new Party { Name = "Ordered Employee", Type = PartyType.Employee, IsActive = true };
        db.Parties.Add(party);
        await db.SaveChangesAsync();

        var jobRole = new JobRole { Name = "Baker", WageType = WageType.Monthly, MonthlySalary = 4000 };
        db.JobRoles.Add(jobRole);
        await db.SaveChangesAsync();

        var employee = new Employee
        {
            PartyId = party.Id,
            Name = "Ordered Employee",
            Code = "EMP004",
            JobRoleId = jobRole.Id,
            HireDate = DateOnly.FromDateTime(DateTime.Today),
            IsActive = true,
            WageType = WageType.Monthly,
            MonthlySalary = 4000
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        // 2. Add transactions out of chronological order
        var txToday = new EmployeeTransaction { EmployeeId = employee.Id, WorkingDayId = activeDay.Id, Type = EmployeeTransactionType.SalaryPayment, Amount = 400, Date = DateTime.UtcNow, Notes = "اليوم" };
        var txYesterday = new EmployeeTransaction { EmployeeId = employee.Id, WorkingDayId = activeDay.Id, Type = EmployeeTransactionType.Advance, Amount = 300, Date = DateTime.UtcNow.AddDays(-1), Notes = "أمس" };
        var txTwoDaysAgo = new EmployeeTransaction { EmployeeId = employee.Id, WorkingDayId = activeDay.Id, Type = EmployeeTransactionType.Earned, Amount = 1000, Date = DateTime.UtcNow.AddDays(-2), Notes = "قبل يومين" };

        db.Set<EmployeeTransaction>().AddRange(txToday, txYesterday, txTwoDaysAgo);
        await db.SaveChangesAsync();

        // 3. Call statement service
        var lines = await statementService.GetStatementAsync(party.Id);

        // 4. Verify chronological sorting: TwoDaysAgo -> Yesterday -> Today
        lines.Should().HaveCount(3);
        
        lines[0].Description.Should().Be("قبل يومين");
        lines[0].RunningBalance.Should().Be(1000);

        lines[1].Description.Should().Be("سحب مقدم / سلفة");
        lines[1].RunningBalance.Should().Be(700);

        lines[2].Description.Should().Be("صرف مستحقات الراتب");
        lines[2].RunningBalance.Should().Be(300);
    }

    [Fact]
    public async Task PrintRuntimeStatementComparisonTable()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        var statementService = scope.ServiceProvider.GetRequiredService<IStatementService>();
        var settlementService = scope.ServiceProvider.GetRequiredService<ISettlementService>();

        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var activeDay = new WorkingDay { BusinessDate = DateOnly.FromDateTime(DateTime.Today), Status = WorkingDayStatus.Open, OpenedAt = DateTime.UtcNow, OpenedBy = "admin" };
        db.WorkingDays.Add(activeDay);
        await db.SaveChangesAsync();

        var party = new Party { Name = "E2E Employee", Type = PartyType.Employee, IsActive = true };
        db.Parties.Add(party);
        await db.SaveChangesAsync();

        var jobRole = new JobRole { Name = "Baker", WageType = WageType.Monthly, MonthlySalary = 5000 };
        db.JobRoles.Add(jobRole);
        await db.SaveChangesAsync();

        var employee = new Employee { PartyId = party.Id, Name = "E2E Employee", Code = "EMP999", JobRoleId = jobRole.Id, HireDate = DateOnly.FromDateTime(DateTime.Today), IsActive = true, WageType = WageType.Monthly, MonthlySalary = 5000 };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var txs = new[]
        {
            new EmployeeTransaction { EmployeeId = employee.Id, WorkingDayId = activeDay.Id, Type = EmployeeTransactionType.Earned, Amount = 3000, Date = DateTime.UtcNow.AddMinutes(-5), Notes = "راتب مستحق" },
            new EmployeeTransaction { EmployeeId = employee.Id, WorkingDayId = activeDay.Id, Type = EmployeeTransactionType.Advance, Amount = 500, Date = DateTime.UtcNow.AddMinutes(-4), Notes = "سلفة" },
            new EmployeeTransaction { EmployeeId = employee.Id, WorkingDayId = activeDay.Id, Type = EmployeeTransactionType.Bonus, Amount = 200, Date = DateTime.UtcNow.AddMinutes(-3), Notes = "مكافأة أداء" },
            new EmployeeTransaction { EmployeeId = employee.Id, WorkingDayId = activeDay.Id, Type = EmployeeTransactionType.Deduction, Amount = 100, Date = DateTime.UtcNow.AddMinutes(-2), Notes = "غياب" },
            new EmployeeTransaction { EmployeeId = employee.Id, WorkingDayId = activeDay.Id, Type = EmployeeTransactionType.SalaryPayment, Amount = 2000, Date = DateTime.UtcNow.AddMinutes(-1), Notes = "صرف نقدي" }
        };
        db.Set<EmployeeTransaction>().AddRange(txs);
        await db.SaveChangesAsync();

        // Query via StatementService (Party Statement path)
        var partyStatement = await statementService.GetStatementAsync(party.Id);

        // Query via SettlementService (Employee Ledger path)
        var employeeLedgerRaw = await settlementService.GetEmployeeStatementAsync(employee.Id);
        
        var employeeLedger = new List<(string Type, decimal Debit, decimal Credit, decimal Balance)>();
        decimal runningBalance = 0;
        foreach (var tx in employeeLedgerRaw.OrderBy(t => t.Date).ThenBy(t => t.Id))
        {
            decimal debit = 0;
            decimal credit = 0;
            if (tx.Type == EmployeeTransactionType.Earned || tx.Type == EmployeeTransactionType.Bonus)
            {
                debit = tx.Amount;
                runningBalance += tx.Amount;
            }
            else
            {
                credit = tx.Amount;
                runningBalance -= tx.Amount;
            }
            employeeLedger.Add((tx.Type.ToString(), debit, credit, runningBalance));
        }

        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "BakeryERP",
            "TestArtifacts",
            nameof(StatementRoutingTests));
        Directory.CreateDirectory(outputDirectory);
        var logPath = Path.Combine(outputDirectory, "runtime_comparison_evidence.txt");
        using var sw = new StreamWriter(logPath);
        sw.WriteLine("| Transaction | Ledger Debit | Ledger Credit | Ledger Balance | Party Statement Increase | Party Statement Decrease | Party Statement Balance | Match |");
        sw.WriteLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

        for (int i = 0; i < txs.Length; i++)
        {
            var l = employeeLedger[i];
            var p = partyStatement[i];
            bool match = l.Balance == p.RunningBalance;
            sw.WriteLine($"| {txs[i].Type} | {l.Debit:N0} | {l.Credit:N0} | {l.Balance:N0} | {p.Increase:N0} | {p.Decrease:N0} | {p.RunningBalance:N0} | {(match ? "✔" : "❌")} |");
        }
        sw.WriteLine($"| Final Balance | - | - | {employeeLedger.Last().Balance:N0} | - | - | {partyStatement.Last().RunningBalance:N0} | {(employeeLedger.Last().Balance == partyStatement.Last().RunningBalance ? "✔" : "❌")} |");
    }
}
