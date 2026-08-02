using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bakery.Infrastructure.Configurations;

public sealed class RecipeConfiguration : IEntityTypeConfiguration<Recipe>
{
    public void Configure(EntityTypeBuilder<Recipe> builder)
    {
        builder.ToTable("Recipes");
        builder.ConfigureBaseEntity();
        builder.Property(recipe => recipe.Name).HasMaxLength(200).IsRequired();
        builder.Property(recipe => recipe.ProducedQuantity).HasQuantityPrecision();
        builder.Property(recipe => recipe.Notes).HasMaxLength(500);

        builder.HasOne(recipe => recipe.Branch).WithMany().HasForeignKey(recipe => recipe.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(recipe => new { recipe.BranchId, recipe.Name }).IsUnique().HasFilter("[IsDeleted] = 0");

        builder.HasOne(recipe => recipe.ProducedItem).WithMany().HasForeignKey(recipe => recipe.ProducedItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(recipe => recipe.ConsumedItems).WithOne(item => item.Recipe).HasForeignKey(item => item.RecipeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RecipeItemConfiguration : IEntityTypeConfiguration<RecipeItem>
{
    public void Configure(EntityTypeBuilder<RecipeItem> builder)
    {
        builder.ToTable("RecipeItems");
        builder.ConfigureBaseEntity();
        builder.Property(item => item.Quantity).HasQuantityPrecision();

        builder.HasOne(item => item.Branch).WithMany().HasForeignKey(item => item.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.RawItem).WithMany().HasForeignKey(item => item.RawItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Unit).WithMany().HasForeignKey(item => item.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
