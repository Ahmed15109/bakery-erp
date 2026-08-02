using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobRolesAndRefactorEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create JobRoles table
            migrationBuilder.CreateTable(
                name: "JobRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WageType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WageAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRoles", x => x.Id);
                });

            // 2. Add JobRoleId to Employees (nullable first to allow migration)
            migrationBuilder.AddColumn<int>(
                name: "JobRoleId",
                table: "Employees",
                type: "int",
                nullable: true);

            // 3. Migrate Data
            // Create roles from existing employee wage configs
            // Daily
            migrationBuilder.Sql(@"
                INSERT INTO JobRoles (Name, WageType, WageAmount, IsActive, CreatedAt, IsDeleted)
                SELECT DISTINCT ISNULL(JobTitle, N'موظف يومية'), N'Daily', DailySalary, 1, SYSUTCDATETIME(), 0
                FROM Employees WHERE WageType = N'Daily' AND DailySalary > 0");

            // Monthly
            migrationBuilder.Sql(@"
                INSERT INTO JobRoles (Name, WageType, WageAmount, IsActive, CreatedAt, IsDeleted)
                SELECT DISTINCT ISNULL(JobTitle, N'موظف شهري'), N'Monthly', MonthlySalary, 1, SYSUTCDATETIME(), 0
                FROM Employees WHERE WageType = N'Monthly' AND MonthlySalary > 0");

            // Piecework
            migrationBuilder.Sql(@"
                INSERT INTO JobRoles (Name, WageType, WageAmount, IsActive, CreatedAt, IsDeleted)
                SELECT DISTINCT ISNULL(JobTitle, N'فني إنتاج'), N'Piecework', PieceworkRate, 1, SYSUTCDATETIME(), 0
                FROM Employees WHERE WageType = N'Piecework' AND PieceworkRate > 0");

            // Link Employees to Roles
            migrationBuilder.Sql(@"
                UPDATE E
                SET E.JobRoleId = R.Id
                FROM Employees E
                JOIN JobRoles R ON (E.JobTitle = R.Name OR (E.JobTitle IS NULL AND R.Name IN (N'موظف يومية', N'موظف شهري', N'فني إنتاج')))
                AND E.WageType = R.WageType");

            // Ensure every employee has a role (fallback for empty/zero salaries)
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM JobRoles WHERE Name = N'موظف عام')
                INSERT INTO JobRoles (Name, WageType, WageAmount, IsActive, CreatedAt, IsDeleted) VALUES (N'موظف عام', N'Daily', 0, 1, SYSUTCDATETIME(), 0);
                
                UPDATE Employees SET JobRoleId = (SELECT TOP 1 Id FROM JobRoles WHERE Name = N'موظف عام') WHERE JobRoleId IS NULL;");

            // 4. Make JobRoleId non-nullable and add snapshots to other tables
            migrationBuilder.AlterColumn<int>(
                name: "JobRoleId",
                table: "Employees",
                nullable: false);

            migrationBuilder.AddColumn<decimal>(
                name: "WageAmountSnapshot",
                table: "ProductionOrderEmployees",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WageTypeSnapshot",
                table: "ProductionOrderEmployees",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "WageAmountSnapshot",
                table: "EmployeeWages",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WageTypeSnapshot",
                table: "EmployeeWages",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            // 5. Drop old columns
            migrationBuilder.DropColumn(
                name: "DailySalary",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "JobTitle",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "MonthlySalary",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "PieceworkRate",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WageType",
                table: "Employees");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_JobRoleId",
                table: "Employees",
                column: "JobRoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_JobRoles_JobRoleId",
                table: "Employees",
                column: "JobRoleId",
                principalTable: "JobRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse logic... simplified (will lose job role links if reverted)
            migrationBuilder.DropForeignKey(
                name: "FK_Employees_JobRoles_JobRoleId",
                table: "Employees");

            migrationBuilder.DropTable(
                name: "JobRoles");

            migrationBuilder.DropIndex(
                name: "IX_Employees_JobRoleId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WageAmountSnapshot",
                table: "ProductionOrderEmployees");

            migrationBuilder.DropColumn(
                name: "WageTypeSnapshot",
                table: "ProductionOrderEmployees");

            migrationBuilder.DropColumn(
                name: "WageAmountSnapshot",
                table: "EmployeeWages");

            migrationBuilder.DropColumn(
                name: "WageTypeSnapshot",
                table: "EmployeeWages");

            migrationBuilder.DropColumn(
                name: "JobRoleId",
                table: "Employees");

            migrationBuilder.AddColumn<decimal>(
                name: "DailySalary",
                table: "Employees",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "JobTitle",
                table: "Employees",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlySalary",
                table: "Employees",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PieceworkRate",
                table: "Employees",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WageType",
                table: "Employees",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }
    }
}
