using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PiProxyGuard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MinimumSeverity = table.Column<int>(type: "INTEGER", nullable: false),
                    TelegramEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    TelegramBotToken = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    TelegramChatId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    EmailEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmailHost = table.Column<string>(type: "TEXT", maxLength: 253, nullable: true),
                    EmailPort = table.Column<int>(type: "INTEGER", nullable: false),
                    EmailUseSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmailUsername = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    EmailPassword = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    EmailFrom = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    EmailTo = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationSettings");
        }
    }
}
