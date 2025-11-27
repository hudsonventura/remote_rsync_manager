using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations
{
    /// <inheritdoc />
    public partial class AddRsyncToBackupPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "rsyncHost",
                table: "backup_plan",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rsyncPort",
                table: "backup_plan",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "rsyncSshKeyPath",
                table: "backup_plan",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rsyncUser",
                table: "backup_plan",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rsyncHost",
                table: "backup_plan");

            migrationBuilder.DropColumn(
                name: "rsyncPort",
                table: "backup_plan");

            migrationBuilder.DropColumn(
                name: "rsyncSshKeyPath",
                table: "backup_plan");

            migrationBuilder.DropColumn(
                name: "rsyncUser",
                table: "backup_plan");
        }
    }
}
