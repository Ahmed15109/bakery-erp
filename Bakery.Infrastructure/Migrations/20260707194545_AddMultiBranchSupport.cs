using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiBranchSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkingDays_BusinessDate",
                table: "WorkingDays");

            migrationBuilder.DropIndex(
                name: "IX_WorkingDays_Status",
                table: "WorkingDays");

            migrationBuilder.DropIndex(
                name: "IX_Units_Symbol",
                table: "Units");

            migrationBuilder.DropIndex(
                name: "IX_SaleInvoices_InvoiceNumber",
                table: "SaleInvoices");

            migrationBuilder.DropIndex(
                name: "IX_Safes_Code",
                table: "Safes");

            migrationBuilder.DropIndex(
                name: "IX_Safes_Name",
                table: "Safes");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_InvoiceNumber",
                table: "PurchaseInvoices");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_ProductionNumber",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_PayrollPeriods_StartDate_EndDate",
                table: "PayrollPeriods");

            migrationBuilder.DropIndex(
                name: "IX_JobRoles_Name",
                table: "JobRoles");

            migrationBuilder.DropIndex(
                name: "IX_Items_Barcode",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_Code",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Employees_Code",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_AppSettings_Key",
                table: "AppSettings");

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "WorkingDays",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "WasteEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "UserSafePermissions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Units",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "StockCountSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "StockCountLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "SaleInvoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "SaleInvoiceLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Safes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "SafeMovements",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Recipes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "RecipeItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "PurchaseInvoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "PurchaseInvoiceLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "ProductionProducedItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "ProductionOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "ProductionOrderEmployees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "ProductionConsumedItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "PayrollPeriods",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "PartyLedgerEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Parties",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "JobRoles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "ItemUnits",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "InventoryMovements",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Expenses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "EmployeeWages",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "EmployeeTransactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "EmployeeSettlements",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Employees",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "AuditLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Attendances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "AppSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_Branches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserBranches",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBranches", x => new { x.UserId, x.BranchId });
                    table.ForeignKey(
                        name: "FK_UserBranches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBranches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM Branches WHERE Code = 'MAIN')
                BEGIN
                    INSERT INTO Branches (Code, [Name], IsActive, CreatedAt, IsDeleted) 
                    VALUES ('MAIN', N'الفرع الرئيسي', 1, SYSUTCDATETIME(), 0);
                END
            ");

            migrationBuilder.Sql(@"
                DECLARE @DefaultBranchId INT;
                SELECT TOP 1 @DefaultBranchId = Id FROM Branches WHERE Code = 'MAIN';

                IF @DefaultBranchId IS NOT NULL
                BEGIN
                    UPDATE WorkingDays SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE WasteEntries SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE UserSafePermissions SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE Units SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE StockCountSessions SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE StockCountLines SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE SaleInvoices SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE SaleInvoiceLines SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE Safes SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE SafeMovements SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE Recipes SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE RecipeItems SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE PurchaseInvoices SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE PurchaseInvoiceLines SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE ProductionProducedItems SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE ProductionOrders SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE ProductionOrderEmployees SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE ProductionConsumedItems SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE PayrollPeriods SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE PartyLedgerEntries SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE Parties SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE JobRoles SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE ItemUnits SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE Items SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE InventoryMovements SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE Expenses SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE EmployeeWages SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE EmployeeTransactions SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE EmployeeSettlements SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE Employees SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE AuditLogs SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE Attendances SET BranchId = @DefaultBranchId WHERE BranchId = 0;
                    UPDATE AppSettings SET BranchId = @DefaultBranchId WHERE BranchId = 0;

                    INSERT INTO UserBranches (UserId, BranchId)
                    SELECT Id, @DefaultBranchId FROM Users WHERE Id NOT IN (SELECT UserId FROM UserBranches WHERE BranchId = @DefaultBranchId);
                END
            ");

            migrationBuilder.CreateIndex(
                name: "IX_WorkingDays_BranchId_BusinessDate",
                table: "WorkingDays",
                columns: new[] { "BranchId", "BusinessDate" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_WorkingDays_BranchId_Status",
                table: "WorkingDays",
                columns: new[] { "BranchId", "Status" },
                unique: true,
                filter: "[Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_WasteEntries_BranchId",
                table: "WasteEntries",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSafePermissions_BranchId",
                table: "UserSafePermissions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Units_BranchId_Symbol",
                table: "Units",
                columns: new[] { "BranchId", "Symbol" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountSessions_BranchId",
                table: "StockCountSessions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_BranchId",
                table: "StockCountLines",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_BranchId_InvoiceNumber",
                table: "SaleInvoices",
                columns: new[] { "BranchId", "InvoiceNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoiceLines_BranchId",
                table: "SaleInvoiceLines",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Safes_BranchId_Code",
                table: "Safes",
                columns: new[] { "BranchId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Safes_BranchId_Name",
                table: "Safes",
                columns: new[] { "BranchId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SafeMovements_BranchId",
                table: "SafeMovements",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_BranchId_Name",
                table: "Recipes",
                columns: new[] { "BranchId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeItems_BranchId",
                table: "RecipeItems",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_BranchId_InvoiceNumber",
                table: "PurchaseInvoices",
                columns: new[] { "BranchId", "InvoiceNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_BranchId",
                table: "PurchaseInvoiceLines",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionProducedItems_BranchId",
                table: "ProductionProducedItems",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_BranchId_ProductionNumber",
                table: "ProductionOrders",
                columns: new[] { "BranchId", "ProductionNumber" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderEmployees_BranchId",
                table: "ProductionOrderEmployees",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionConsumedItems_BranchId",
                table: "ProductionConsumedItems",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPeriods_BranchId_StartDate_EndDate",
                table: "PayrollPeriods",
                columns: new[] { "BranchId", "StartDate", "EndDate" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PartyLedgerEntries_BranchId",
                table: "PartyLedgerEntries",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Parties_BranchId_Name",
                table: "Parties",
                columns: new[] { "BranchId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_JobRoles_BranchId_Name",
                table: "JobRoles",
                columns: new[] { "BranchId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnits_BranchId",
                table: "ItemUnits",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_BranchId_Barcode",
                table: "Items",
                columns: new[] { "BranchId", "Barcode" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Barcode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Items_BranchId_Code",
                table: "Items",
                columns: new[] { "BranchId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_BranchId",
                table: "InventoryMovements",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BranchId",
                table: "Expenses",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeWages_BranchId",
                table: "EmployeeWages",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTransactions_BranchId",
                table: "EmployeeTransactions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeSettlements_BranchId",
                table: "EmployeeSettlements",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_BranchId_Code",
                table: "Employees",
                columns: new[] { "BranchId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_BranchId",
                table: "AuditLogs",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_BranchId",
                table: "Attendances",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_BranchId_Key",
                table: "AppSettings",
                columns: new[] { "BranchId", "Key" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Code",
                table: "Branches",
                column: "Code",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_UserBranches_BranchId",
                table: "UserBranches",
                column: "BranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppSettings_Branches_BranchId",
                table: "AppSettings",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Branches_BranchId",
                table: "Attendances",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Branches_BranchId",
                table: "AuditLogs",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Branches_BranchId",
                table: "Employees",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeSettlements_Branches_BranchId",
                table: "EmployeeSettlements",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeTransactions_Branches_BranchId",
                table: "EmployeeTransactions",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeeWages_Branches_BranchId",
                table: "EmployeeWages",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Branches_BranchId",
                table: "Expenses",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_Branches_BranchId",
                table: "InventoryMovements",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Branches_BranchId",
                table: "Items",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ItemUnits_Branches_BranchId",
                table: "ItemUnits",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JobRoles_Branches_BranchId",
                table: "JobRoles",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Parties_Branches_BranchId",
                table: "Parties",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PartyLedgerEntries_Branches_BranchId",
                table: "PartyLedgerEntries",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollPeriods_Branches_BranchId",
                table: "PayrollPeriods",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionConsumedItems_Branches_BranchId",
                table: "ProductionConsumedItems",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionOrderEmployees_Branches_BranchId",
                table: "ProductionOrderEmployees",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionOrders_Branches_BranchId",
                table: "ProductionOrders",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionProducedItems_Branches_BranchId",
                table: "ProductionProducedItems",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoiceLines_Branches_BranchId",
                table: "PurchaseInvoiceLines",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoices_Branches_BranchId",
                table: "PurchaseInvoices",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecipeItems_Branches_BranchId",
                table: "RecipeItems",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Recipes_Branches_BranchId",
                table: "Recipes",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SafeMovements_Branches_BranchId",
                table: "SafeMovements",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Safes_Branches_BranchId",
                table: "Safes",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleInvoiceLines_Branches_BranchId",
                table: "SaleInvoiceLines",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleInvoices_Branches_BranchId",
                table: "SaleInvoices",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockCountLines_Branches_BranchId",
                table: "StockCountLines",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockCountSessions_Branches_BranchId",
                table: "StockCountSessions",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Units_Branches_BranchId",
                table: "Units",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserSafePermissions_Branches_BranchId",
                table: "UserSafePermissions",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WasteEntries_Branches_BranchId",
                table: "WasteEntries",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkingDays_Branches_BranchId",
                table: "WorkingDays",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppSettings_Branches_BranchId",
                table: "AppSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Branches_BranchId",
                table: "Attendances");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Branches_BranchId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Branches_BranchId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeSettlements_Branches_BranchId",
                table: "EmployeeSettlements");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeTransactions_Branches_BranchId",
                table: "EmployeeTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_EmployeeWages_Branches_BranchId",
                table: "EmployeeWages");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Branches_BranchId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_Branches_BranchId",
                table: "InventoryMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_Items_Branches_BranchId",
                table: "Items");

            migrationBuilder.DropForeignKey(
                name: "FK_ItemUnits_Branches_BranchId",
                table: "ItemUnits");

            migrationBuilder.DropForeignKey(
                name: "FK_JobRoles_Branches_BranchId",
                table: "JobRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_Parties_Branches_BranchId",
                table: "Parties");

            migrationBuilder.DropForeignKey(
                name: "FK_PartyLedgerEntries_Branches_BranchId",
                table: "PartyLedgerEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollPeriods_Branches_BranchId",
                table: "PayrollPeriods");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionConsumedItems_Branches_BranchId",
                table: "ProductionConsumedItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionOrderEmployees_Branches_BranchId",
                table: "ProductionOrderEmployees");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionOrders_Branches_BranchId",
                table: "ProductionOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionProducedItems_Branches_BranchId",
                table: "ProductionProducedItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoiceLines_Branches_BranchId",
                table: "PurchaseInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoices_Branches_BranchId",
                table: "PurchaseInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_RecipeItems_Branches_BranchId",
                table: "RecipeItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Recipes_Branches_BranchId",
                table: "Recipes");

            migrationBuilder.DropForeignKey(
                name: "FK_SafeMovements_Branches_BranchId",
                table: "SafeMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_Safes_Branches_BranchId",
                table: "Safes");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleInvoiceLines_Branches_BranchId",
                table: "SaleInvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleInvoices_Branches_BranchId",
                table: "SaleInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_StockCountLines_Branches_BranchId",
                table: "StockCountLines");

            migrationBuilder.DropForeignKey(
                name: "FK_StockCountSessions_Branches_BranchId",
                table: "StockCountSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Units_Branches_BranchId",
                table: "Units");

            migrationBuilder.DropForeignKey(
                name: "FK_UserSafePermissions_Branches_BranchId",
                table: "UserSafePermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_WasteEntries_Branches_BranchId",
                table: "WasteEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkingDays_Branches_BranchId",
                table: "WorkingDays");

            migrationBuilder.DropTable(
                name: "UserBranches");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_WorkingDays_BranchId_BusinessDate",
                table: "WorkingDays");

            migrationBuilder.DropIndex(
                name: "IX_WorkingDays_BranchId_Status",
                table: "WorkingDays");

            migrationBuilder.DropIndex(
                name: "IX_WasteEntries_BranchId",
                table: "WasteEntries");

            migrationBuilder.DropIndex(
                name: "IX_UserSafePermissions_BranchId",
                table: "UserSafePermissions");

            migrationBuilder.DropIndex(
                name: "IX_Units_BranchId_Symbol",
                table: "Units");

            migrationBuilder.DropIndex(
                name: "IX_StockCountSessions_BranchId",
                table: "StockCountSessions");

            migrationBuilder.DropIndex(
                name: "IX_StockCountLines_BranchId",
                table: "StockCountLines");

            migrationBuilder.DropIndex(
                name: "IX_SaleInvoices_BranchId_InvoiceNumber",
                table: "SaleInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SaleInvoiceLines_BranchId",
                table: "SaleInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_Safes_BranchId_Code",
                table: "Safes");

            migrationBuilder.DropIndex(
                name: "IX_Safes_BranchId_Name",
                table: "Safes");

            migrationBuilder.DropIndex(
                name: "IX_SafeMovements_BranchId",
                table: "SafeMovements");

            migrationBuilder.DropIndex(
                name: "IX_Recipes_BranchId_Name",
                table: "Recipes");

            migrationBuilder.DropIndex(
                name: "IX_RecipeItems_BranchId",
                table: "RecipeItems");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_BranchId_InvoiceNumber",
                table: "PurchaseInvoices");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceLines_BranchId",
                table: "PurchaseInvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_ProductionProducedItems_BranchId",
                table: "ProductionProducedItems");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_BranchId_ProductionNumber",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrderEmployees_BranchId",
                table: "ProductionOrderEmployees");

            migrationBuilder.DropIndex(
                name: "IX_ProductionConsumedItems_BranchId",
                table: "ProductionConsumedItems");

            migrationBuilder.DropIndex(
                name: "IX_PayrollPeriods_BranchId_StartDate_EndDate",
                table: "PayrollPeriods");

            migrationBuilder.DropIndex(
                name: "IX_PartyLedgerEntries_BranchId",
                table: "PartyLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_Parties_BranchId_Name",
                table: "Parties");

            migrationBuilder.DropIndex(
                name: "IX_JobRoles_BranchId_Name",
                table: "JobRoles");

            migrationBuilder.DropIndex(
                name: "IX_ItemUnits_BranchId",
                table: "ItemUnits");

            migrationBuilder.DropIndex(
                name: "IX_Items_BranchId_Barcode",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_BranchId_Code",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_BranchId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_BranchId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeWages_BranchId",
                table: "EmployeeWages");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeTransactions_BranchId",
                table: "EmployeeTransactions");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeSettlements_BranchId",
                table: "EmployeeSettlements");

            migrationBuilder.DropIndex(
                name: "IX_Employees_BranchId_Code",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_BranchId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_Attendances_BranchId",
                table: "Attendances");

            migrationBuilder.DropIndex(
                name: "IX_AppSettings_BranchId_Key",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "WorkingDays");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "WasteEntries");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "UserSafePermissions");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "StockCountSessions");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "StockCountLines");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "SaleInvoices");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "SaleInvoiceLines");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Safes");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "SafeMovements");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "RecipeItems");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "PurchaseInvoiceLines");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "ProductionProducedItems");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "ProductionOrderEmployees");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "ProductionConsumedItems");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "PayrollPeriods");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "PartyLedgerEntries");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Parties");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "JobRoles");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "ItemUnits");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "EmployeeWages");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "EmployeeTransactions");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "EmployeeSettlements");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "AppSettings");

            migrationBuilder.CreateIndex(
                name: "IX_WorkingDays_BusinessDate",
                table: "WorkingDays",
                column: "BusinessDate",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_WorkingDays_Status",
                table: "WorkingDays",
                column: "Status",
                unique: true,
                filter: "[Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_Units_Symbol",
                table: "Units",
                column: "Symbol",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SaleInvoices_InvoiceNumber",
                table: "SaleInvoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Safes_Code",
                table: "Safes",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Safes_Name",
                table: "Safes",
                column: "Name",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_InvoiceNumber",
                table: "PurchaseInvoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_ProductionNumber",
                table: "ProductionOrders",
                column: "ProductionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollPeriods_StartDate_EndDate",
                table: "PayrollPeriods",
                columns: new[] { "StartDate", "EndDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobRoles_Name",
                table: "JobRoles",
                column: "Name",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Barcode",
                table: "Items",
                column: "Barcode");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Code",
                table: "Items",
                column: "Code",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Code",
                table: "Employees",
                column: "Code",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_Key",
                table: "AppSettings",
                column: "Key",
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
