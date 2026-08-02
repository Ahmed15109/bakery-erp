using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPartyPaymentReversalLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceSafeMovementId",
                table: "PartyLedgerEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartyLedgerEntries_SourceSafeMovementId",
                table: "PartyLedgerEntries",
                column: "SourceSafeMovementId",
                unique: true,
                filter: "[IsDeleted] = 0 AND [SourceSafeMovementId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PartyLedgerEntries_SafeMovements_SourceSafeMovementId",
                table: "PartyLedgerEntries",
                column: "SourceSafeMovementId",
                principalTable: "SafeMovements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PartyLedgerEntries_SafeMovements_SourceSafeMovementId",
                table: "PartyLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_PartyLedgerEntries_SourceSafeMovementId",
                table: "PartyLedgerEntries");

            migrationBuilder.DropColumn(
                name: "SourceSafeMovementId",
                table: "PartyLedgerEntries");
        }
    }
}
