using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductionRecipeWasteEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "ProductionProducedItems",
                newName: "VarianceQty");

            migrationBuilder.RenameColumn(
                name: "ProductionDate",
                table: "ProductionOrders",
                newName: "StartedAt");

            migrationBuilder.RenameColumn(
                name: "OrderNumber",
                table: "ProductionOrders",
                newName: "ProductionNumber");

            migrationBuilder.RenameIndex(
                name: "IX_ProductionOrders_OrderNumber",
                table: "ProductionOrders",
                newName: "IX_ProductionOrders_ProductionNumber");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "WasteEntries",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WasteCost",
                table: "WasteEntries",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WasteType",
                table: "WasteEntries",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ActualProducedQty",
                table: "ProductionProducedItems",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedProducedQty",
                table: "ProductionProducedItems",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "VarianceReason",
                table: "ProductionProducedItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                table: "ProductionOrders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "ProductionOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecipeId",
                table: "ProductionOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipeSnapshotJson",
                table: "ProductionOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Employees",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlySalary",
                table: "Employees",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                table: "Employees",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartyId",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PieceworkRate",
                table: "Employees",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WageType",
                table: "Employees",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ProductionOrderEmployees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductionOrderId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    ContributionPercentage = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    CalculatedWage = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrderEmployees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionOrderEmployees_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrderEmployees_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Recipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProducedItemId = table.Column<int>(type: "int", nullable: false),
                    ProducedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recipes_Items_ProducedItemId",
                        column: x => x.ProducedItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecipeItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecipeId = table.Column<int>(type: "int", nullable: false),
                    RawItemId = table.Column<int>(type: "int", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeItems_Items_RawItemId",
                        column: x => x.RawItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecipeItems_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeItems_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_RecipeId",
                table: "ProductionOrders",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_PartyId",
                table: "Employees",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderEmployees_EmployeeId",
                table: "ProductionOrderEmployees",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderEmployees_ProductionOrderId",
                table: "ProductionOrderEmployees",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeItems_RawItemId",
                table: "RecipeItems",
                column: "RawItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeItems_RecipeId",
                table: "RecipeItems",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeItems_UnitId",
                table: "RecipeItems",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_ProducedItemId",
                table: "Recipes",
                column: "ProducedItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Parties_PartyId",
                table: "Employees",
                column: "PartyId",
                principalTable: "Parties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionOrders_Recipes_RecipeId",
                table: "ProductionOrders",
                column: "RecipeId",
                principalTable: "Recipes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Parties_PartyId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionOrders_Recipes_RecipeId",
                table: "ProductionOrders");

            migrationBuilder.DropTable(
                name: "ProductionOrderEmployees");

            migrationBuilder.DropTable(
                name: "RecipeItems");

            migrationBuilder.DropTable(
                name: "Recipes");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_RecipeId",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_Employees_PartyId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "WasteEntries");

            migrationBuilder.DropColumn(
                name: "WasteCost",
                table: "WasteEntries");

            migrationBuilder.DropColumn(
                name: "WasteType",
                table: "WasteEntries");

            migrationBuilder.DropColumn(
                name: "ActualProducedQty",
                table: "ProductionProducedItems");

            migrationBuilder.DropColumn(
                name: "ExpectedProducedQty",
                table: "ProductionProducedItems");

            migrationBuilder.DropColumn(
                name: "VarianceReason",
                table: "ProductionProducedItems");

            migrationBuilder.DropColumn(
                name: "BatchNumber",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "RecipeId",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "RecipeSnapshotJson",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "MonthlySalary",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "NationalId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "PartyId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "PieceworkRate",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WageType",
                table: "Employees");

            migrationBuilder.RenameColumn(
                name: "VarianceQty",
                table: "ProductionProducedItems",
                newName: "Quantity");

            migrationBuilder.RenameColumn(
                name: "StartedAt",
                table: "ProductionOrders",
                newName: "ProductionDate");

            migrationBuilder.RenameColumn(
                name: "ProductionNumber",
                table: "ProductionOrders",
                newName: "OrderNumber");

            migrationBuilder.RenameIndex(
                name: "IX_ProductionOrders_ProductionNumber",
                table: "ProductionOrders",
                newName: "IX_ProductionOrders_OrderNumber");
        }
    }
}
