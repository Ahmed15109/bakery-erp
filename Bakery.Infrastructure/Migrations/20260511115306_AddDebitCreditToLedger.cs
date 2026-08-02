using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDebitCreditToLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Credit",
                table: "PartyLedgerEntries",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Debit",
                table: "PartyLedgerEntries",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Credit",
                table: "PartyLedgerEntries");

            migrationBuilder.DropColumn(
                name: "Debit",
                table: "PartyLedgerEntries");
        }
    }
}
