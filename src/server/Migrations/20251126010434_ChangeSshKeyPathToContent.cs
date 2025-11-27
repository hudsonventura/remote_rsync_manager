using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations
{
    /// <inheritdoc />
    public partial class ChangeSshKeyPathToContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "rsyncSshKeyPath",
                table: "backup_plan",
                newName: "rsyncSshKey");

            migrationBuilder.RenameColumn(
                name: "rsyncSshKeyPath",
                table: "agent",
                newName: "rsyncSshKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "rsyncSshKey",
                table: "backup_plan",
                newName: "rsyncSshKeyPath");

            migrationBuilder.RenameColumn(
                name: "rsyncSshKey",
                table: "agent",
                newName: "rsyncSshKeyPath");
        }
    }
}
