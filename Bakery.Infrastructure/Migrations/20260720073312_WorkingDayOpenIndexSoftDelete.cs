using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkingDayOpenIndexSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkingDays_BranchId_Status",
                table: "WorkingDays");

            migrationBuilder.CreateIndex(
                name: "IX_WorkingDays_BranchId_Status",
                table: "WorkingDays",
                columns: new[] { "BranchId", "Status" },
                unique: true,
                filter: "[Status] = 'Open' AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkingDays_BranchId_Status",
                table: "WorkingDays");

            migrationBuilder.CreateIndex(
                name: "IX_WorkingDays_BranchId_Status",
                table: "WorkingDays",
                columns: new[] { "BranchId", "Status" },
                unique: true,
                filter: "[Status] = 'Open'");
        }
    }
}
