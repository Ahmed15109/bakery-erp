using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "Subtotal",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "SaleInvoiceLines");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "Subtotal",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "PurchaseInvoiceLines");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "SaleInvoices",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Subtotal",
                table: "SaleInvoices",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "SaleInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "PurchaseInvoices",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Subtotal",
                table: "PurchaseInvoices",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "PurchaseInvoiceLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
