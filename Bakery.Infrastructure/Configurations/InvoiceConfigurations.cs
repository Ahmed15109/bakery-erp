using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bakery.Infrastructure.Configurations;

public sealed class PurchaseInvoiceConfiguration : IEntityTypeConfiguration<PurchaseInvoice>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoice> builder)
    {
        builder.ToTable("PurchaseInvoices");
        builder.ConfigureBaseEntity();
        ConfigureInvoiceHeader(builder);

        builder.HasOne(invoice => invoice.Branch).WithMany().HasForeignKey(invoice => invoice.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(invoice => new { invoice.BranchId, invoice.InvoiceNumber }).IsUnique().HasFilter("[IsDeleted] = 0");

        builder.HasOne(invoice => invoice.WorkingDay).WithMany().HasForeignKey(invoice => invoice.WorkingDayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(invoice => invoice.Party).WithMany(party => party.PurchaseInvoices).HasForeignKey(invoice => invoice.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(invoice => invoice.Safe).WithMany().HasForeignKey(invoice => invoice.SafeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(invoice => invoice.Lines).WithOne(line => line.PurchaseInvoice).HasForeignKey(line => line.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureInvoiceHeader(EntityTypeBuilder<PurchaseInvoice> builder)
    {
        builder.Property(invoice => invoice.InvoiceNumber).HasMaxLength(50).IsRequired();
        builder.Property(invoice => invoice.PaymentType).HasConversion<string>().HasMaxLength(30);
        builder.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(invoice => invoice.TaxAmount).HasMoneyPrecision();
        builder.Property(invoice => invoice.TotalAmount).HasMoneyPrecision();
        builder.Property(invoice => invoice.PaidAmount).HasMoneyPrecision();
        builder.Property(invoice => invoice.RemainingAmount).HasMoneyPrecision();
        builder.Property(invoice => invoice.CancellationReason).HasMaxLength(500);
        builder.Property(invoice => invoice.Notes).HasMaxLength(500);
    }
}

public sealed class PurchaseInvoiceLineConfiguration : IEntityTypeConfiguration<PurchaseInvoiceLine>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoiceLine> builder)
    {
        builder.ToTable("PurchaseInvoiceLines");
        builder.ConfigureBaseEntity();
        builder.Property(line => line.Quantity).HasQuantityPrecision();
        builder.Property(line => line.UnitPrice).HasMoneyPrecision();
        builder.Property(line => line.TaxAmount).HasMoneyPrecision();
        builder.Property(line => line.LineTotal).HasMoneyPrecision();

        builder.HasOne(line => line.Branch).WithMany().HasForeignKey(line => line.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(line => line.Item).WithMany().HasForeignKey(line => line.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Unit).WithMany().HasForeignKey(line => line.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SaleInvoiceConfiguration : IEntityTypeConfiguration<SaleInvoice>
{
    public void Configure(EntityTypeBuilder<SaleInvoice> builder)
    {
        builder.ToTable("SaleInvoices");
        builder.ConfigureBaseEntity();
        ConfigureInvoiceHeader(builder);

        builder.HasOne(invoice => invoice.Branch).WithMany().HasForeignKey(invoice => invoice.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(invoice => new { invoice.BranchId, invoice.InvoiceNumber }).IsUnique().HasFilter("[IsDeleted] = 0");

        builder.HasOne(invoice => invoice.WorkingDay).WithMany().HasForeignKey(invoice => invoice.WorkingDayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(invoice => invoice.Party).WithMany(party => party.SaleInvoices).HasForeignKey(invoice => invoice.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(invoice => invoice.Safe).WithMany().HasForeignKey(invoice => invoice.SafeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(invoice => invoice.Lines).WithOne(line => line.SaleInvoice).HasForeignKey(line => line.SaleInvoiceId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureInvoiceHeader(EntityTypeBuilder<SaleInvoice> builder)
    {
        builder.Property(invoice => invoice.InvoiceNumber).HasMaxLength(50).IsRequired();
        builder.Property(invoice => invoice.PaymentType).HasConversion<string>().HasMaxLength(30);
        builder.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(invoice => invoice.TaxAmount).HasMoneyPrecision();
        builder.Property(invoice => invoice.TotalAmount).HasMoneyPrecision();
        builder.Property(invoice => invoice.PaidAmount).HasMoneyPrecision();
        builder.Property(invoice => invoice.RemainingAmount).HasMoneyPrecision();
        builder.Property(invoice => invoice.CancellationReason).HasMaxLength(500);
        builder.Property(invoice => invoice.Notes).HasMaxLength(500);
    }
}

public sealed class SaleInvoiceLineConfiguration : IEntityTypeConfiguration<SaleInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SaleInvoiceLine> builder)
    {
        builder.ToTable("SaleInvoiceLines");
        builder.ConfigureBaseEntity();
        builder.Property(line => line.Quantity).HasQuantityPrecision();
        builder.Property(line => line.UnitPrice).HasMoneyPrecision();
        builder.Property(line => line.TaxAmount).HasMoneyPrecision();
        builder.Property(line => line.LineTotal).HasMoneyPrecision();

        builder.HasOne(line => line.Branch).WithMany().HasForeignKey(line => line.BranchId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(line => line.Item).WithMany().HasForeignKey(line => line.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Unit).WithMany().HasForeignKey(line => line.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
