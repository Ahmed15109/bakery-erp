using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkingDayTreasuryCarryOver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CarryOverBalance",
                table: "WorkingDays",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TransferredToMainSafe",
                table: "WorkingDays",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CarryOverBalance",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "TransferredToMainSafe",
                table: "WorkingDays");
        }
    }
}
