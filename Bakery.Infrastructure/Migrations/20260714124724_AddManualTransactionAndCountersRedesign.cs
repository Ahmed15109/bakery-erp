using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManualTransactionAndCountersRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentPath",
                table: "SafeMovements",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BalanceAfter",
                table: "SafeMovements",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BalanceBefore",
                table: "SafeMovements",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "SafeMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserName",
                table: "SafeMovements",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "SafeMovements",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "System");

            migrationBuilder.AddColumn<int>(
                name: "OriginalTransactionId",
                table: "SafeMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "SafeMovements",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "SafeMovements",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReverseReason",
                table: "SafeMovements",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReverseTransactionId",
                table: "SafeMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReversedAt",
                table: "SafeMovements",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReversedBy",
                table: "SafeMovements",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransactionNumber",
                table: "SafeMovements",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TransactionNumberCounters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchId = table.Column<int>(type: "int", nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastValue = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionNumberCounters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionNumberCounters_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SafeMovements_OriginalTransactionId",
                table: "SafeMovements",
                column: "OriginalTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionNumberCounters_BranchId_Prefix",
                table: "TransactionNumberCounters",
                columns: new[] { "BranchId", "Prefix" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransactionNumberCounters");

            migrationBuilder.DropIndex(
                name: "IX_SafeMovements_OriginalTransactionId",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "AttachmentPath",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "BalanceAfter",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "BalanceBefore",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "CreatedByUserName",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "OriginalTransactionId",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "ReverseReason",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "ReverseTransactionId",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "ReversedAt",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "ReversedBy",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "TransactionNumber",
                table: "SafeMovements");
        }
    }
}
