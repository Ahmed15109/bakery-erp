using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollPeriod_TotalNetAmount",
                table: "PayrollPeriods",
                sql: "[TotalNetAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollEntry_NetAmount",
                table: "PayrollEntries",
                sql: "[NetAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollEntry_PaidAmount",
                table: "PayrollEntries",
                sql: "[PaidAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayrollEntry_RemainingAmount",
                table: "PayrollEntries",
                sql: "[RemainingAmount] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeeTransaction_Amount",
                table: "EmployeeTransactions",
                sql: "[Amount] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollPeriod_TotalNetAmount",
                table: "PayrollPeriods");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollEntry_NetAmount",
                table: "PayrollEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollEntry_PaidAmount",
                table: "PayrollEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayrollEntry_RemainingAmount",
                table: "PayrollEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeeTransaction_Amount",
                table: "EmployeeTransactions");
        }
    }
}
