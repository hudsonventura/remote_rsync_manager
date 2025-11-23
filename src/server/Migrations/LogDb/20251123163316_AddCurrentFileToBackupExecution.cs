using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations.LogDb
{
    /// <inheritdoc />
    public partial class AddCurrentFileToBackupExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currentFileName",
                table: "backup_execution",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currentFilePath",
                table: "backup_execution",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "currentFileName",
                table: "backup_execution");

            migrationBuilder.DropColumn(
                name: "currentFilePath",
                table: "backup_execution");
        }
    }
}
