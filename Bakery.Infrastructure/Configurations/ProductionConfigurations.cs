using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bakery.Infrastructure.Configurations;

public sealed class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> builder)
    {
        builder.ToTable("ProductionOrders");
        builder.ConfigureBaseEntity();
        builder.Property(order => order.ProductionNumber).HasMaxLength(50).IsRequired();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(order => order.BatchNumber).HasMaxLength(50);
        builder.Property(order => order.Notes).HasMaxLength(500);

        builder.HasOne(order => order.Branch).WithMany().HasForeignKey(order => order.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(order => new { order.BranchId, order.ProductionNumber }).IsUnique().HasFilter("[IsDeleted] = 0");

        builder.HasOne(order => order.WorkingDay).WithMany().HasForeignKey(order => order.WorkingDayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(order => order.Recipe).WithMany().HasForeignKey(order => order.RecipeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.ConsumedItems).WithOne(item => item.ProductionOrder).HasForeignKey(item => item.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(order => order.ProducedItems).WithOne(item => item.ProductionOrder).HasForeignKey(item => item.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(order => order.Employees).WithOne(emp => emp.ProductionOrder).HasForeignKey(emp => emp.ProductionOrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ProductionOrderEmployeeConfiguration : IEntityTypeConfiguration<ProductionOrderEmployee>
{
    public void Configure(EntityTypeBuilder<ProductionOrderEmployee> builder)
    {
        builder.ToTable("ProductionOrderEmployees");
        builder.ConfigureBaseEntity();
        builder.Property(emp => emp.ContributionPercentage).HasQuantityPrecision();
        builder.Property(emp => emp.WageTypeSnapshot).HasConversion<string>().HasMaxLength(30);
        builder.Property(emp => emp.WageAmountSnapshot).HasMoneyPrecision();
        builder.Property(emp => emp.CalculatedWage).HasMoneyPrecision();
        builder.Property(emp => emp.Notes).HasMaxLength(500);

        builder.HasOne(emp => emp.Branch).WithMany().HasForeignKey(emp => emp.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(emp => emp.Employee).WithMany().HasForeignKey(emp => emp.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductionConsumedItemConfiguration : IEntityTypeConfiguration<ProductionConsumedItem>
{
    public void Configure(EntityTypeBuilder<ProductionConsumedItem> builder)
    {
        builder.ToTable("ProductionConsumedItems");
        builder.ConfigureBaseEntity();
        builder.Property(item => item.Quantity).HasQuantityPrecision();
        builder.Property(item => item.UnitCost).HasMoneyPrecision();

        builder.HasOne(item => item.Branch).WithMany().HasForeignKey(item => item.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.Item).WithMany().HasForeignKey(item => item.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Unit).WithMany().HasForeignKey(item => item.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProductionProducedItemConfiguration : IEntityTypeConfiguration<ProductionProducedItem>
{
    public void Configure(EntityTypeBuilder<ProductionProducedItem> builder)
    {
        builder.ToTable("ProductionProducedItems");
        builder.ConfigureBaseEntity();
        builder.Property(item => item.ExpectedProducedQty).HasQuantityPrecision();
        builder.Property(item => item.ActualProducedQty).HasQuantityPrecision();
        builder.Property(item => item.VarianceQty).HasQuantityPrecision();
        builder.Property(item => item.VarianceReason).HasMaxLength(200);
        builder.Property(item => item.UnitCost).HasMoneyPrecision();

        builder.HasOne(item => item.Branch).WithMany().HasForeignKey(item => item.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.Item).WithMany().HasForeignKey(item => item.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Unit).WithMany().HasForeignKey(item => item.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
