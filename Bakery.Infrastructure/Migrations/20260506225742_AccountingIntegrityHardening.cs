using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountingIntegrityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReversed",
                table: "SafeMovements",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReversalReferenceId",
                table: "SafeMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReversed",
                table: "PartyLedgerEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReversalReferenceId",
                table: "PartyLedgerEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReversed",
                table: "InventoryMovements",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReversalReferenceId",
                table: "InventoryMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReversed",
                table: "EmployeeWages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReversalReferenceId",
                table: "EmployeeWages",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsReversed",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "ReversalReferenceId",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "IsReversed",
                table: "PartyLedgerEntries");

            migrationBuilder.DropColumn(
                name: "ReversalReferenceId",
                table: "PartyLedgerEntries");

            migrationBuilder.DropColumn(
                name: "IsReversed",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "ReversalReferenceId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "IsReversed",
                table: "EmployeeWages");

            migrationBuilder.DropColumn(
                name: "ReversalReferenceId",
                table: "EmployeeWages");
        }
    }
}
