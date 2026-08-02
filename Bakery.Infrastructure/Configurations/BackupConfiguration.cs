using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bakery.Infrastructure.Configurations;

public sealed class BackupRecordConfiguration : IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<BackupRecord> builder)
    {
        builder.ToTable("BackupRecords");
        builder.ConfigureBaseEntity();
        builder.Property(item => item.FileName).HasMaxLength(260).IsRequired();
        builder.Property(item => item.LocalPath).HasMaxLength(1_024).IsRequired();
        builder.Property(item => item.BackupType).HasConversion<string>().HasMaxLength(30);
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(item => item.CloudStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(item => item.GoogleDriveFileId).HasMaxLength(256);
        builder.Property(item => item.ApplicationVersion).HasMaxLength(50).IsRequired();
        builder.Property(item => item.DatabaseVersion).HasMaxLength(200).IsRequired();
        builder.Property(item => item.DeviceName).HasMaxLength(100).IsRequired();
        builder.Property(item => item.CreatedByUser).HasMaxLength(100).IsRequired();
        builder.Property(item => item.ErrorSummary).HasMaxLength(500);
        builder.HasIndex(item => item.BackupCreatedAtUtc);
        builder.HasIndex(item => new { item.Status, item.CloudStatus, item.BackupCreatedAtUtc });
        builder.HasIndex(item => item.SourceOperationId)
            .IsUnique()
            .HasFilter("[SourceOperationId] IS NOT NULL AND [IsDeleted] = 0");
    }
}

