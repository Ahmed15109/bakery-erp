using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkingDaySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InvoiceCount",
                table: "WorkingDays",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalExpenses",
                table: "WorkingDays",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalInventoryAdjustments",
                table: "WorkingDays",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalPurchases",
                table: "WorkingDays",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalSafeMovements",
                table: "WorkingDays",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalSales",
                table: "WorkingDays",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalWages",
                table: "WorkingDays",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AttendanceCount",
                table: "EmployeeSettlements",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DailyRate",
                table: "EmployeeSettlements",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlySalary",
                table: "EmployeeSettlements",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "WageTypeSnapshot",
                table: "EmployeeSettlements",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceCount",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "TotalExpenses",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "TotalInventoryAdjustments",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "TotalPurchases",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "TotalSafeMovements",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "TotalSales",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "TotalWages",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "AttendanceCount",
                table: "EmployeeSettlements");

            migrationBuilder.DropColumn(
                name: "DailyRate",
                table: "EmployeeSettlements");

            migrationBuilder.DropColumn(
                name: "MonthlySalary",
                table: "EmployeeSettlements");

            migrationBuilder.DropColumn(
                name: "WageTypeSnapshot",
                table: "EmployeeSettlements");
        }
    }
}
