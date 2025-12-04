using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace server.Migrations.DBContext
{
    /// <inheritdoc />
    public partial class AddTelegramNotificationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notificationChatId",
                table: "telegram_config",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "notificationsEnabled",
                table: "telegram_config",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notificationChatId",
                table: "telegram_config");

            migrationBuilder.DropColumn(
                name: "notificationsEnabled",
                table: "telegram_config");
        }
    }
}
