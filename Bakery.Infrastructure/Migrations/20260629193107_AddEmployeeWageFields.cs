using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeWageFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DailyRate",
                table: "Employees",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlySalary",
                table: "Employees",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProductionRate",
                table: "Employees",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WageEffectiveFrom",
                table: "Employees",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateTime>(
                name: "WageLastUpdatedAt",
                table: "Employees",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WageLastUpdatedBy",
                table: "Employees",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WageType",
                table: "Employees",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Production");

            // Backfill: copy wage defaults from JobRole into each existing Employee.
            // WageEffectiveFrom is set to the employee's HireDate (or today if HireDate is the default).
            migrationBuilder.Sql(@"
                UPDATE e SET
                    e.WageType          = jr.WageType,
                    e.MonthlySalary     = jr.MonthlySalary,
                    e.DailyRate         = jr.DailyRate,
                    e.ProductionRate    = jr.ProductionRate,
                    e.WageEffectiveFrom = CASE
                        WHEN e.HireDate IS NOT NULL AND e.HireDate > '0001-01-01' THEN e.HireDate
                        ELSE CAST(GETDATE() AS DATE)
                    END
                FROM Employees e
                JOIN JobRoles jr ON e.JobRoleId = jr.Id
                WHERE e.IsDeleted = 0 AND jr.IsDeleted = 0
            ");
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyRate",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "MonthlySalary",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "ProductionRate",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WageEffectiveFrom",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WageLastUpdatedAt",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WageLastUpdatedBy",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WageType",
                table: "Employees");
        }
    }
}
