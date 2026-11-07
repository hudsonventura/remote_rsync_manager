using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations.DBContext
{
    /// <inheritdoc />
    public partial class AddEnvironmentVariablesToExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "environmentVariables",
                table: "extension",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "environmentVariables",
                table: "extension");
        }
    }
}

