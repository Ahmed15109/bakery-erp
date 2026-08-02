using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorToSettlementSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeTransactions_PayrollEntries_PayrollEntryId",
                table: "EmployeeTransactions");

            migrationBuilder.DropTable(
                name: "PayrollEntries");

            migrationBuilder.RenameColumn(
                name: "PayrollEntryId",
                table: "EmployeeTransactions",
                newName: "SettlementId");

            migrationBuilder.RenameIndex(
                name: "IX_EmployeeTransactions_PayrollEntryId",
                table: "EmployeeTransactions",
                newName: "IX_EmployeeTransactions_SettlementId");

            migrationBuilder.CreateTable(
                name: "EmployeeSettlements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PayrollPeriodId = table.Column<int>(type: "int", nullable: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    SettlementDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProductionQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    ProductionRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Bonuses = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Deductions = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Advances = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsFullyPaid = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSettlements", x => x.Id);
                    table.CheckConstraint("CK_Settlement_NetAmount", "[NetAmount] >= 0");
                    table.CheckConstraint("CK_Settlement_PaidAmount", "[PaidAmount] >= 0");
                    table.CheckConstraint("CK_Settlement_RemainingAmount", "[RemainingAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_EmployeeSettlements_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeSettlements_PayrollPeriods_PayrollPeriodId",
                        column: x => x.PayrollPeriodId,
                        principalTable: "PayrollPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSettlements_EmployeeId",
                table: "EmployeeSettlements",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSettlements_PayrollPeriodId",
                table: "EmployeeSettlements",
                column: "PayrollPeriodId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeTransactions_EmployeeSettlements_SettlementId",
                table: "EmployeeTransactions",
                column: "SettlementId",
                principalTable: "EmployeeSettlements",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeTransactions_EmployeeSettlements_SettlementId",
                table: "EmployeeTransactions");

            migrationBuilder.DropTable(
                name: "EmployeeSettlements");

            migrationBuilder.RenameColumn(
                name: "SettlementId",
                table: "EmployeeTransactions",
                newName: "PayrollEntryId");

            migrationBuilder.RenameIndex(
                name: "IX_EmployeeTransactions_SettlementId",
                table: "EmployeeTransactions",
                newName: "IX_EmployeeTransactions_PayrollEntryId");

            migrationBuilder.CreateTable(
                name: "PayrollEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    PayrollPeriodId = table.Column<int>(type: "int", nullable: false),
                    Advances = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BaseAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Bonuses = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Deductions = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsFullyPaid = table.Column<bool>(type: "bit", nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SnapshotAttendanceTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SnapshotPaymentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SnapshotProductionTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SnapshotRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SnapshotRoleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollEntries", x => x.Id);
                    table.CheckConstraint("CK_PayrollEntry_NetAmount", "[NetAmount] >= 0");
                    table.CheckConstraint("CK_PayrollEntry_PaidAmount", "[PaidAmount] >= 0");
                    table.CheckConstraint("CK_PayrollEntry_RemainingAmount", "[RemainingAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_PayrollEntries_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollEntries_PayrollPeriods_PayrollPeriodId",
                        column: x => x.PayrollPeriodId,
                        principalTable: "PayrollPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEntries_EmployeeId",
                table: "PayrollEntries",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEntries_PayrollPeriodId_EmployeeId",
                table: "PayrollEntries",
                columns: new[] { "PayrollPeriodId", "EmployeeId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeTransactions_PayrollEntries_PayrollEntryId",
                table: "EmployeeTransactions",
                column: "PayrollEntryId",
                principalTable: "PayrollEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
