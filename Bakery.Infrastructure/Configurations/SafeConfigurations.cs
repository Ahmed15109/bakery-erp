using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bakery.Infrastructure.Configurations;

public sealed class SafeConfiguration : IEntityTypeConfiguration<Safe>
{
    public void Configure(EntityTypeBuilder<Safe> builder)
    {
        builder.ToTable("Safes");
        builder.ConfigureBaseEntity();
        builder.Property(safe => safe.Code).HasMaxLength(50);
        builder.Property(safe => safe.Name).HasMaxLength(100).IsRequired();
        builder.Property(safe => safe.ArabicName).HasMaxLength(150);
        builder.Property(safe => safe.Type)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasDefaultValue(SafeType.Normal)
            .HasSentinel((SafeType)0);
        builder.Ignore(safe => safe.IsSystem);
        builder.Ignore(safe => safe.IsDefaultCashSafe);

        builder.HasOne(safe => safe.Branch).WithMany().HasForeignKey(safe => safe.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(safe => new { safe.BranchId, safe.Code }).IsUnique().HasFilter("[IsDeleted] = 0 AND [Code] IS NOT NULL");
        builder.HasIndex(safe => new { safe.BranchId, safe.Name }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class SafeMovementConfiguration : IEntityTypeConfiguration<SafeMovement>
{
    public void Configure(EntityTypeBuilder<SafeMovement> builder)
    {
        builder.ToTable("SafeMovements");
        builder.ConfigureBaseEntity();
        builder.Property(movement => movement.Type).HasConversion<string>().HasMaxLength(50);
        builder.Property(movement => movement.Amount).HasMoneyPrecision();
        builder.Property(movement => movement.Description).HasMaxLength(300).IsRequired();
        builder.Property(movement => movement.ReferenceType).HasMaxLength(100);
        builder.Property(movement => movement.Notes).HasMaxLength(500);
        builder.Property(movement => movement.TransferId);
        builder.Property(movement => movement.IdempotencyKey).HasMaxLength(100);

        // Manual cash columns configuration
        builder.Property(movement => movement.Origin)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasDefaultValue(CashMovementOrigin.System)
            .HasSentinel((CashMovementOrigin)0);
        builder.Property(movement => movement.Reason).HasConversion<string>().HasMaxLength(50);
        builder.Property(movement => movement.TransactionNumber).HasMaxLength(50);
        builder.Property(movement => movement.ReferenceNumber).HasMaxLength(100);
        builder.Property(movement => movement.AttachmentPath).HasMaxLength(500);
        builder.Property(movement => movement.BalanceBefore).HasMoneyPrecision();
        builder.Property(movement => movement.BalanceAfter).HasMoneyPrecision();
        builder.Property(movement => movement.CreatedByUserName).HasMaxLength(100);
        builder.Property(movement => movement.ReversedBy).HasMaxLength(100);
        builder.Property(movement => movement.ReverseReason).HasMaxLength(300);

        builder.HasOne(movement => movement.Branch).WithMany().HasForeignKey(movement => movement.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(movement => new { movement.SafeId, movement.CreatedAt });
        builder.HasIndex(movement => new { movement.BranchId, movement.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [IdempotencyKey] IS NOT NULL");
        builder.HasIndex(movement => movement.OriginalTransactionId)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [OriginalTransactionId] IS NOT NULL");
        builder.HasOne(movement => movement.WorkingDay).WithMany().HasForeignKey(movement => movement.WorkingDayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(movement => movement.Safe).WithMany(safe => safe.Movements).HasForeignKey(movement => movement.SafeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TransactionNumberCounterConfiguration : IEntityTypeConfiguration<TransactionNumberCounter>
{
    public void Configure(EntityTypeBuilder<TransactionNumberCounter> builder)
    {
        builder.ToTable("TransactionNumberCounters");
        builder.ConfigureBaseEntity();
        builder.Property(c => c.Prefix).HasMaxLength(50).IsRequired();
        builder.Property(c => c.LastValue).IsConcurrencyToken();

        builder.HasIndex(c => new { c.BranchId, c.Prefix }).IsUnique();
        builder.HasOne(c => c.Branch).WithMany().HasForeignKey(c => c.BranchId).OnDelete(DeleteBehavior.Restrict);
    }
}
