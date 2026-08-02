using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bakery.Infrastructure.Configurations;

public sealed class WasteEntryConfiguration : IEntityTypeConfiguration<WasteEntry>
{
    public void Configure(EntityTypeBuilder<WasteEntry> builder)
    {
        builder.ToTable("WasteEntries");
        builder.ConfigureBaseEntity();
        builder.Property(entry => entry.Quantity).HasQuantityPrecision();
        builder.Property(entry => entry.UnitCost).HasMoneyPrecision();
        builder.Property(entry => entry.WasteCost).HasMoneyPrecision();
        builder.Property(entry => entry.Reason).HasMaxLength(300).IsRequired();
        builder.Property(entry => entry.WasteType).HasConversion<string>().HasMaxLength(30);
        builder.Property(entry => entry.Notes).HasMaxLength(500);

        builder.HasOne(entry => entry.Branch).WithMany().HasForeignKey(entry => entry.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(entry => entry.WorkingDay).WithMany().HasForeignKey(entry => entry.WorkingDayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entry => entry.Item).WithMany().HasForeignKey(entry => entry.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entry => entry.Unit).WithMany().HasForeignKey(entry => entry.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses");
        builder.ConfigureBaseEntity();
        builder.Property(expense => expense.Category).HasMaxLength(100).IsRequired();
        builder.Property(expense => expense.Description).HasMaxLength(300).IsRequired();
        builder.Property(expense => expense.Amount).HasMoneyPrecision();

        builder.HasOne(expense => expense.Branch).WithMany().HasForeignKey(expense => expense.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(expense => expense.WorkingDay).WithMany().HasForeignKey(expense => expense.WorkingDayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(expense => expense.Safe).WithMany().HasForeignKey(expense => expense.SafeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class JobRoleConfiguration : IEntityTypeConfiguration<JobRole>
{
    public void Configure(EntityTypeBuilder<JobRole> builder)
    {
        builder.ToTable("JobRoles");
        builder.ConfigureBaseEntity();
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.WageType).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.WageAmount).HasMoneyPrecision();
        builder.Property(r => r.DailyRate).HasMoneyPrecision();
        builder.Property(r => r.MonthlySalary).HasMoneyPrecision();
        builder.Property(r => r.ProductionRate).HasMoneyPrecision();
        builder.Property(r => r.Notes).HasMaxLength(500);

        builder.HasOne(r => r.Branch).WithMany().HasForeignKey(r => r.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.BranchId, r.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");
        builder.ConfigureBaseEntity();
        builder.Property(employee => employee.Code).HasMaxLength(50).IsRequired();
        builder.Property(employee => employee.Name).HasMaxLength(200).IsRequired();
        builder.Property(employee => employee.Phone).HasMaxLength(50);
        builder.Property(employee => employee.Address).HasMaxLength(500);
        builder.Property(employee => employee.NationalId).HasMaxLength(50);

        builder.HasOne(employee => employee.Branch).WithMany().HasForeignKey(employee => employee.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(employee => new { employee.BranchId, employee.Code }).IsUnique().HasFilter("[IsDeleted] = 0");

        builder.HasOne(employee => employee.Party).WithMany().HasForeignKey(employee => employee.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(employee => employee.JobRole).WithMany(role => role.Employees).HasForeignKey(employee => employee.JobRoleId).OnDelete(DeleteBehavior.Restrict);

        // Wage fields — owned by Employee, independent of JobRole after creation
        builder.Property(employee => employee.WageType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(employee => employee.MonthlySalary).HasMoneyPrecision().HasDefaultValue(0m);
        builder.Property(employee => employee.DailyRate).HasMoneyPrecision().HasDefaultValue(0m);
        builder.Property(employee => employee.ProductionRate).HasMoneyPrecision().HasDefaultValue(0m);

        // Wage metadata
        builder.Property(employee => employee.WageEffectiveFrom).IsRequired();
        builder.Property(employee => employee.WageLastUpdatedAt);
        builder.Property(employee => employee.WageLastUpdatedBy).HasMaxLength(100);
    }
}

public sealed class EmployeeWageConfiguration : IEntityTypeConfiguration<EmployeeWage>
{
    public void Configure(EntityTypeBuilder<EmployeeWage> builder)
    {
        builder.ToTable("EmployeeWages");
        builder.ConfigureBaseEntity();
        builder.Property(wage => wage.Amount).HasMoneyPrecision();
        builder.Property(wage => wage.WageTypeSnapshot).HasConversion<string>().HasMaxLength(30);
        builder.Property(wage => wage.WageAmountSnapshot).HasMoneyPrecision();
        builder.Property(wage => wage.Notes).HasMaxLength(300);

        builder.HasOne(wage => wage.Branch).WithMany().HasForeignKey(wage => wage.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(wage => wage.WorkingDay).WithMany().HasForeignKey(wage => wage.WorkingDayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(wage => wage.Employee).WithMany(employee => employee.Wages).HasForeignKey(wage => wage.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(wage => wage.Safe).WithMany().HasForeignKey(wage => wage.SafeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AttendanceConfiguration : IEntityTypeConfiguration<Attendance>
{
    public void Configure(EntityTypeBuilder<Attendance> builder)
    {
        builder.ToTable("Attendances");
        builder.ConfigureBaseEntity();
        builder.Property(a => a.Notes).HasMaxLength(300);

        builder.HasOne(a => a.Branch).WithMany().HasForeignKey(a => a.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.WorkingDay).WithMany().HasForeignKey(a => a.WorkingDayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Employee).WithMany(e => e.Attendances).HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PayrollPeriodConfiguration : IEntityTypeConfiguration<PayrollPeriod>
{
    public void Configure(EntityTypeBuilder<PayrollPeriod> builder)
    {
        builder.ToTable("PayrollPeriods");
        builder.ConfigureBaseEntity();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.TotalNetAmount).HasMoneyPrecision();
        builder.Property(p => p.Notes).HasMaxLength(500);

        builder.HasOne(p => p.Branch).WithMany().HasForeignKey(p => p.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.BranchId, p.StartDate, p.EndDate }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.ToTable(t => t.HasCheckConstraint("CK_PayrollPeriod_TotalNetAmount", "[TotalNetAmount] >= 0"));
    }
}

public sealed class EmployeeSettlementConfiguration : IEntityTypeConfiguration<EmployeeSettlement>
{
    public void Configure(EntityTypeBuilder<EmployeeSettlement> builder)
    {
        builder.ToTable("EmployeeSettlements");
        builder.ConfigureBaseEntity();
        builder.Property(e => e.SettlementDate).IsRequired();
        builder.Property(e => e.ProductionQuantity).HasQuantityPrecision();
        builder.Property(e => e.ProductionRate).HasMoneyPrecision();
        builder.Property(e => e.BaseAmount).HasMoneyPrecision();
        builder.Property(e => e.Bonuses).HasMoneyPrecision();
        builder.Property(e => e.Deductions).HasMoneyPrecision();
        builder.Property(e => e.Advances).HasMoneyPrecision();
        builder.Property(e => e.NetAmount).HasMoneyPrecision();
        builder.Property(e => e.PaidAmount).HasMoneyPrecision();
        builder.Property(e => e.RemainingAmount).HasMoneyPrecision();
        builder.Property(e => e.DailyRate).HasMoneyPrecision();
        builder.Property(e => e.AttendanceCount).HasQuantityPrecision();
        builder.Property(e => e.MonthlySalary).HasMoneyPrecision();
        builder.Property(e => e.Notes).HasMaxLength(500);

        builder.HasOne(e => e.Branch).WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Restrict);
        
        builder.HasOne(e => e.PayrollPeriod).WithMany(p => p.Entries).HasForeignKey(e => e.PayrollPeriodId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        
        builder.ToTable(t => 
        {
            t.HasCheckConstraint("CK_Settlement_NetAmount", "[NetAmount] >= 0");
            t.HasCheckConstraint("CK_Settlement_PaidAmount", "[PaidAmount] >= 0");
            t.HasCheckConstraint("CK_Settlement_RemainingAmount", "[RemainingAmount] >= 0");
        });
    }
}

public sealed class EmployeeTransactionConfiguration : IEntityTypeConfiguration<EmployeeTransaction>
{
    public void Configure(EntityTypeBuilder<EmployeeTransaction> builder)
    {
        builder.ToTable("EmployeeTransactions");
        builder.ConfigureBaseEntity();
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Amount).HasMoneyPrecision();
        builder.Property(t => t.Notes).HasMaxLength(500);

        builder.HasOne(t => t.Branch).WithMany().HasForeignKey(t => t.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Employee).WithMany().HasForeignKey(t => t.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.WorkingDay).WithMany().HasForeignKey(t => t.WorkingDayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Settlement).WithMany(e => e.Transactions).HasForeignKey(t => t.SettlementId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(t => t.SafeMovement).WithMany().HasForeignKey(t => t.SafeMovementId);

        builder.ToTable(t => t.HasCheckConstraint("CK_EmployeeTransaction_Amount", "[Amount] >= 0"));
    }
}
