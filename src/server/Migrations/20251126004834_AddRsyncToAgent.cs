using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations
{
    /// <inheritdoc />
    public partial class AddRsyncToAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "rsyncPort",
                table: "agent",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "rsyncSshKeyPath",
                table: "agent",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rsyncUser",
                table: "agent",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rsyncPort",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "rsyncSshKeyPath",
                table: "agent");

            migrationBuilder.DropColumn(
                name: "rsyncUser",
                table: "agent");
        }
    }
}
