using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ManualReversalUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS
                (
                    SELECT [OriginalTransactionId]
                    FROM [SafeMovements]
                    WHERE [IsDeleted] = 0 AND [OriginalTransactionId] IS NOT NULL
                    GROUP BY [OriginalTransactionId]
                    HAVING COUNT(*) > 1
                )
                BEGIN
                    THROW 51000, 'Duplicate active safe-movement reversals must be resolved before applying ManualReversalUniqueness.', 1;
                END
                """);

            migrationBuilder.DropIndex(
                name: "IX_SafeMovements_OriginalTransactionId",
                table: "SafeMovements");

            migrationBuilder.CreateIndex(
                name: "IX_SafeMovements_OriginalTransactionId",
                table: "SafeMovements",
                column: "OriginalTransactionId",
                unique: true,
                filter: "[IsDeleted] = 0 AND [OriginalTransactionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SafeMovements_OriginalTransactionId",
                table: "SafeMovements");

            migrationBuilder.CreateIndex(
                name: "IX_SafeMovements_OriginalTransactionId",
                table: "SafeMovements",
                column: "OriginalTransactionId");
        }
    }
}
