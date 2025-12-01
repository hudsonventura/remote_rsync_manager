using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations.LogDbContext
{
    /// <inheritdoc />
    public partial class logs1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "backup_execution",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    backupPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    startDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    endDateTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    currentFileName = table.Column<string>(type: "TEXT", nullable: true),
                    currentFilePath = table.Column<string>(type: "TEXT", nullable: true),
                    totalFilesToProcess = table.Column<int>(type: "INTEGER", nullable: true),
                    currentFileIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_execution", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "log_entry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    backupPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    executionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    datetime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    fileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    filePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    size = table.Column<long>(type: "INTEGER", nullable: true),
                    action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_log_entry", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_backup_execution_backupPlanId_startDateTime",
                table: "backup_execution",
                columns: new[] { "backupPlanId", "startDateTime" });

            migrationBuilder.CreateIndex(
                name: "IX_log_entry_backupPlanId_datetime",
                table: "log_entry",
                columns: new[] { "backupPlanId", "datetime" });

            migrationBuilder.CreateIndex(
                name: "IX_log_entry_executionId",
                table: "log_entry",
                column: "executionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backup_execution");

            migrationBuilder.DropTable(
                name: "log_entry");
        }
    }
}
