using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bakery.Infrastructure.Configurations;

public sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("Items");
        builder.ConfigureBaseEntity();
        builder.Property(item => item.Code).HasMaxLength(50).IsRequired();
        builder.Property(item => item.Barcode).HasMaxLength(100);
        builder.Property(item => item.Name).HasMaxLength(200).IsRequired();
        builder.Property(item => item.Type).HasConversion<string>().HasMaxLength(40);
        builder.Property(item => item.PurchasePrice).HasMoneyPrecision();
        builder.Property(item => item.SalePrice).HasMoneyPrecision();
        builder.Property(item => item.MinStockLevel).HasQuantityPrecision();
        builder.Property(item => item.ReorderLevel).HasQuantityPrecision();
        builder.Property(item => item.Notes).HasMaxLength(500);

        builder.HasOne(item => item.Branch).WithMany().HasForeignKey(item => item.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(item => new { item.BranchId, item.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(item => new { item.BranchId, item.Barcode }).IsUnique().HasFilter("[IsDeleted] = 0 AND [Barcode] IS NOT NULL");
        builder.HasOne(item => item.BaseUnit).WithMany(unit => unit.BaseUnitItems).HasForeignKey(item => item.BaseUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("Units");
        builder.ConfigureBaseEntity();
        builder.Property(unit => unit.Name).HasMaxLength(100).IsRequired();
        builder.Property(unit => unit.Symbol).HasMaxLength(20).IsRequired();

        builder.HasOne(unit => unit.Branch).WithMany().HasForeignKey(unit => unit.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(unit => new { unit.BranchId, unit.Symbol }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class ItemUnitConfiguration : IEntityTypeConfiguration<ItemUnit>
{
    public void Configure(EntityTypeBuilder<ItemUnit> builder)
    {
        builder.ToTable("ItemUnits");
        builder.ConfigureBaseEntity();
        builder.Property(itemUnit => itemUnit.ConversionFactorToBaseUnit).HasQuantityPrecision();
        
        builder.HasOne(itemUnit => itemUnit.Branch).WithMany().HasForeignKey(itemUnit => itemUnit.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(itemUnit => new { itemUnit.ItemId, itemUnit.UnitId }).IsUnique();
        builder.HasOne(itemUnit => itemUnit.Item).WithMany(item => item.ItemUnits).HasForeignKey(itemUnit => itemUnit.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(itemUnit => itemUnit.Unit).WithMany(unit => unit.ItemUnits).HasForeignKey(itemUnit => itemUnit.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("InventoryMovements");
        builder.ConfigureBaseEntity();
        builder.Property(movement => movement.Type).HasConversion<string>().HasMaxLength(50);
        builder.Property(movement => movement.Quantity).HasQuantityPrecision();
        builder.Property(movement => movement.UnitCost).HasMoneyPrecision();
        builder.Property(movement => movement.ReferenceType).HasMaxLength(100);
        builder.Property(movement => movement.Notes).HasMaxLength(500);

        builder.HasOne(movement => movement.Branch).WithMany().HasForeignKey(movement => movement.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(movement => new { movement.ItemId, movement.CreatedAt });
        builder.HasIndex(movement => new { movement.WorkingDayId, movement.Type });
        builder.HasOne(movement => movement.WorkingDay).WithMany().HasForeignKey(movement => movement.WorkingDayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(movement => movement.Item).WithMany(item => item.InventoryMovements).HasForeignKey(movement => movement.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(movement => movement.Unit).WithMany().HasForeignKey(movement => movement.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockCountSessionConfiguration : IEntityTypeConfiguration<StockCountSession>
{
    public void Configure(EntityTypeBuilder<StockCountSession> builder)
    {
        builder.ToTable("StockCountSessions");
        builder.ConfigureBaseEntity();
        builder.Property(session => session.StartedBy).HasMaxLength(100).IsRequired();
        builder.Property(session => session.CompletedBy).HasMaxLength(100);
        builder.Property(session => session.Notes).HasMaxLength(500);

        builder.HasOne(session => session.Branch).WithMany().HasForeignKey(session => session.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(session => session.Lines).WithOne(line => line.StockCountSession).HasForeignKey(line => line.StockCountSessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class StockCountLineConfiguration : IEntityTypeConfiguration<StockCountLine>
{
    public void Configure(EntityTypeBuilder<StockCountLine> builder)
    {
        builder.ToTable("StockCountLines");
        builder.ConfigureBaseEntity();
        builder.Property(line => line.SystemQuantity).HasQuantityPrecision();
        builder.Property(line => line.PhysicalQuantity).HasQuantityPrecision();
        builder.Property(line => line.VarianceQuantity).HasQuantityPrecision();

        builder.HasOne(line => line.Branch).WithMany().HasForeignKey(line => line.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new { line.StockCountSessionId, line.ItemId }).IsUnique();
        builder.HasOne(line => line.Item).WithMany().HasForeignKey(line => line.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Unit).WithMany().HasForeignKey(line => line.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
