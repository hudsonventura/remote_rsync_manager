using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations.LogDbContext
{
    /// <inheritdoc />
    public partial class AddIsAutomaticAndIsSimulationToBackupExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "isAutomatic",
                table: "backup_execution",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "isSimulation",
                table: "backup_execution",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "isAutomatic",
                table: "backup_execution");

            migrationBuilder.DropColumn(
                name: "isSimulation",
                table: "backup_execution");
        }
    }
}
