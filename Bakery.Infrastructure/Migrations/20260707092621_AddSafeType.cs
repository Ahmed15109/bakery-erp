using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSafeType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Safes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.Sql("UPDATE Safes SET Type = 'Daily' WHERE Code = 'DAILY_CASH_SAFE' OR (IsSystem = 1 AND IsDefaultCashSafe = 1);");
            migrationBuilder.Sql("UPDATE Safes SET Type = 'Normal' WHERE Type IS NULL OR Type = '';");

            migrationBuilder.DropColumn(
                name: "IsDefaultCashSafe",
                table: "Safes");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "Safes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.Sql("UPDATE Safes SET IsSystem = 1, IsDefaultCashSafe = 1 WHERE Type = 'Daily';");
            migrationBuilder.Sql("UPDATE Safes SET IsSystem = 1, IsDefaultCashSafe = 0 WHERE Type IN ('Main', 'Private');");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Safes");
        }
    }
}
