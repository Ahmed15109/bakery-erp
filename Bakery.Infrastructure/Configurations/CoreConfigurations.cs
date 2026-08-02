using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bakery.Infrastructure.Configurations;

public sealed class WorkingDayConfiguration : IEntityTypeConfiguration<WorkingDay>
{
    public void Configure(EntityTypeBuilder<WorkingDay> builder)
    {
        builder.ToTable("WorkingDays");
        builder.ConfigureBaseEntity();
        builder.Property(day => day.BusinessDate).IsRequired();
        builder.Property(day => day.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(day => day.OpenedBy).HasMaxLength(100).IsRequired();
        builder.Property(day => day.ClosedBy).HasMaxLength(100);
        builder.Property(day => day.OpeningCash).HasMoneyPrecision();
        builder.Property(day => day.ClosingCash).HasMoneyPrecision();
        builder.Property(day => day.ExpectedClosingCash).HasMoneyPrecision();
        builder.Property(day => day.CashDifference).HasMoneyPrecision();
        builder.Property(day => day.TransferredToMainSafe).HasMoneyPrecision().HasDefaultValue(0m);
        builder.Property(day => day.CarryOverBalance).HasMoneyPrecision().HasDefaultValue(0m);
        builder.Property(day => day.Notes).HasMaxLength(500);

        builder.Property(day => day.TotalSales).HasMoneyPrecision();
        builder.Property(day => day.TotalPurchases).HasMoneyPrecision();
        builder.Property(day => day.TotalExpenses).HasMoneyPrecision();
        builder.Property(day => day.TotalWages).HasMoneyPrecision();
        builder.Property(day => day.TotalSafeMovements).HasMoneyPrecision();
        builder.Property(day => day.TotalInventoryAdjustments).HasMoneyPrecision();

        builder.HasOne(day => day.Branch).WithMany().HasForeignKey(day => day.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(day => new { day.BranchId, day.BusinessDate }).IsUnique().HasFilter("[IsDeleted] = 0");

        // Single Active Day Constraint (Filtered Unique Index per Branch)
        builder.HasIndex(day => new { day.BranchId, day.Status })
            .IsUnique()
            .HasFilter("[Status] = 'Open' AND [IsDeleted] = 0");
    }
}

public sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("Parties");
        builder.ConfigureBaseEntity();
        builder.Property(party => party.Name).HasMaxLength(200).IsRequired();
        builder.Property(party => party.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(party => party.Phone).HasMaxLength(50);
        builder.Property(party => party.Address).HasMaxLength(500);
        builder.Property(party => party.NationalId).HasMaxLength(50);
        builder.Property(party => party.TaxNumber).HasMaxLength(100);
        builder.Property(party => party.Notes).HasMaxLength(500);

        builder.HasOne(party => party.Branch).WithMany().HasForeignKey(party => party.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(party => new { party.BranchId, party.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class PartyLedgerEntryConfiguration : IEntityTypeConfiguration<PartyLedgerEntry>
{
    public void Configure(EntityTypeBuilder<PartyLedgerEntry> builder)
    {
        builder.ToTable("PartyLedgerEntries");
        builder.ConfigureBaseEntity();
        builder.Property(entry => entry.Amount).HasMoneyPrecision();
        builder.Property(entry => entry.Debit).HasMoneyPrecision();
        builder.Property(entry => entry.Credit).HasMoneyPrecision();
        builder.Property(entry => entry.Description).HasMaxLength(300).IsRequired();
        builder.Property(entry => entry.ReferenceType).HasMaxLength(100);
        
        builder.HasOne(entry => entry.Branch).WithMany().HasForeignKey(entry => entry.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(entry => new { entry.PartyId, entry.CreatedAt });
        builder.HasIndex(entry => entry.SourceSafeMovementId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [SourceSafeMovementId] IS NOT NULL");
        builder.HasOne(entry => entry.WorkingDay).WithMany().HasForeignKey(entry => entry.WorkingDayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entry => entry.Party).WithMany(party => party.LedgerEntries).HasForeignKey(entry => entry.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entry => entry.SourceSafeMovement).WithMany().HasForeignKey(entry => entry.SourceSafeMovementId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");
        builder.ConfigureBaseEntity();
        builder.Property(setting => setting.Key).HasMaxLength(100).IsRequired();
        builder.Property(setting => setting.Value).HasMaxLength(500).IsRequired();
        builder.Property(setting => setting.Description).HasMaxLength(300);

        builder.HasOne(setting => setting.Branch).WithMany().HasForeignKey(setting => setting.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(setting => new { setting.BranchId, setting.Key }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.ConfigureBaseEntity();
        builder.Property(log => log.Action).HasMaxLength(100).IsRequired();
        builder.Property(log => log.EntityName).HasMaxLength(150).IsRequired();
        builder.Property(log => log.OldValues).HasColumnType("nvarchar(max)");
        builder.Property(log => log.NewValues).HasColumnType("nvarchar(max)");
        builder.Property(log => log.MachineName).HasMaxLength(100);
        builder.Property(log => log.IPAddress).HasMaxLength(50);

        builder.HasOne(log => log.Branch).WithMany().HasForeignKey(log => log.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(log => new { log.EntityName, log.EntityId });
        builder.HasIndex(log => new { log.BranchId, log.OccurredAt });
        builder.HasOne(log => log.User).WithMany(user => user.AuditLogs).HasForeignKey(log => log.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}
