using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bakery.Infrastructure.Configurations;

internal static class ConfigurationHelpers
{
    public static void ConfigureBaseEntity<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : BaseEntity
    {
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(entity => entity.CreatedBy).HasMaxLength(100);
        builder.Property(entity => entity.UpdatedBy).HasMaxLength(100);
        builder.Property(entity => entity.IsDeleted).HasDefaultValue(false);
        builder.Property(entity => entity.DeletedBy).HasMaxLength(100);
        builder.HasQueryFilter(entity => !entity.IsDeleted);

        // Concurrency Protection (RowVersion)
        builder.Property(entity => entity.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();
    }

    public static PropertyBuilder<decimal> HasMoneyPrecision(this PropertyBuilder<decimal> builder)
    {
        return builder.HasColumnType("decimal(18,2)");
    }

    public static PropertyBuilder<decimal?> HasMoneyPrecision(this PropertyBuilder<decimal?> builder)
    {
        return builder.HasColumnType("decimal(18,2)");
    }

    public static PropertyBuilder<decimal> HasQuantityPrecision(this PropertyBuilder<decimal> builder)
    {
        return builder.HasColumnType("decimal(18,3)");
    }

    public static PropertyBuilder<decimal?> HasQuantityPrecision(this PropertyBuilder<decimal?> builder)
    {
        return builder.HasColumnType("decimal(18,3)");
    }
}
