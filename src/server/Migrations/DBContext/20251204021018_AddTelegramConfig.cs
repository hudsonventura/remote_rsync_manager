using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations.DBContext
{
    /// <inheritdoc />
    public partial class AddTelegramConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "telegram_config",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    botToken = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    webhookUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    isEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_config", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_config");
        }
    }
}
