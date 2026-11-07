using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations.LogDbContext
{
    /// <inheritdoc />
    public partial class AddExtensionExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "extension_execution",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    extensionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    extensionName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    command = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    startDateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    endDateTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    exitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    output = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    isAutomatic = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extension_execution", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_extension_execution_extensionId_startDateTime",
                table: "extension_execution",
                columns: new[] { "extensionId", "startDateTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "extension_execution");
        }
    }
}

