using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PhaseA_DefaultCashSafeIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArabicName",
                table: "Safes",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Safes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultCashSafe",
                table: "Safes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "Safes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Safes_Code",
                table: "Safes",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Safes_Code",
                table: "Safes");

            migrationBuilder.DropColumn(
                name: "ArabicName",
                table: "Safes");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Safes");

            migrationBuilder.DropColumn(
                name: "IsDefaultCashSafe",
                table: "Safes");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "Safes");
        }
    }
}
