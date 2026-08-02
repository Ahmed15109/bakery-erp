using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bakery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupAndRecoverySystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    LocalPath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    BackupCreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WorkingDayDate = table.Column<DateOnly>(type: "date", nullable: true),
                    WorkingDayId = table.Column<int>(type: "int", nullable: true),
                    SourceOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BackupType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CloudStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GoogleDriveFileId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApplicationVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DatabaseVersion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedByUser = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ErrorSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UploadRetryCount = table.Column<int>(type: "int", nullable: false),
                    LastUploadAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_BackupRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_BackupCreatedAtUtc",
                table: "BackupRecords",
                column: "BackupCreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_SourceOperationId",
                table: "BackupRecords",
                column: "SourceOperationId",
                unique: true,
                filter: "[SourceOperationId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_Status_CloudStatus_BackupCreatedAtUtc",
                table: "BackupRecords",
                columns: new[] { "Status", "CloudStatus", "BackupCreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupRecords");
        }
    }
}
