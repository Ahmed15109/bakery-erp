using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinancialOperationIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SafeMovements_BranchId",
                table: "SafeMovements");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "SafeMovements",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SafeMovements_BranchId_IdempotencyKey",
                table: "SafeMovements",
                columns: new[] { "BranchId", "IdempotencyKey" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [IdempotencyKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SafeMovements_BranchId_IdempotencyKey",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "SafeMovements");

            migrationBuilder.CreateIndex(
                name: "IX_SafeMovements_BranchId",
                table: "SafeMovements",
                column: "BranchId");
        }
    }
}
