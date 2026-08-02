using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuthenticationAndWorkingDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CashDifference",
                table: "WorkingDays",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedBy",
                table: "WorkingDays",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingCash",
                table: "WorkingDays",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedClosingCash",
                table: "WorkingDays",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpenedBy",
                table: "WorkingDays",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningCash",
                table: "WorkingDays",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "IPAddress",
                table: "AuditLogs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MachineName",
                table: "AuditLogs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CashDifference",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "ClosedBy",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "ClosingCash",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "ExpectedClosingCash",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "OpenedBy",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "OpeningCash",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "IPAddress",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "MachineName",
                table: "AuditLogs");
        }
    }
}
