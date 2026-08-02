using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinalProductionPhase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_WorkingDayId",
                table: "InventoryMovements");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_WorkingDayId_Type",
                table: "InventoryMovements",
                columns: new[] { "WorkingDayId", "Type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_WorkingDayId_Type",
                table: "InventoryMovements");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_WorkingDayId",
                table: "InventoryMovements",
                column: "WorkingDayId");
        }
    }
}
