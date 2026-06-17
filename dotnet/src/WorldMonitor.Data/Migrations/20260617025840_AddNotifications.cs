using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldMonitor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Variant = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    EventTypes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sensitivity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Channels = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    QuietHoursEnabled = table.Column<bool>(type: "bit", nullable: true),
                    QuietHoursStart = table.Column<int>(type: "int", nullable: true),
                    QuietHoursEnd = table.Column<int>(type: "int", nullable: true),
                    QuietHoursTimezone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    QuietHoursOverride = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DigestMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    DigestHour = table.Column<int>(type: "int", nullable: true),
                    DigestTimezone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AiDigestEnabled = table.Column<bool>(type: "bit", nullable: true),
                    Countries = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ChannelType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Verified = table.Column<bool>(type: "bit", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WebhookEnvelope = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DiscordGuildId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DiscordChannelId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SlackChannelName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SlackTeamName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SlackConfigurationUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChatId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Endpoint = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    P256dh = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Auth = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WebhookLabel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WebhookSecret = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelegramPairingTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Used = table.Column<bool>(type: "bit", nullable: false),
                    Variant = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramPairingTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_Enabled",
                table: "AlertRules",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "UX_AlertRules_User_Variant",
                table: "AlertRules",
                columns: new[] { "UserId", "Variant" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_NotificationChannels_Endpoint",
                table: "NotificationChannels",
                column: "Endpoint",
                unique: true,
                filter: "[Endpoint] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_NotificationChannels_User_Channel",
                table: "NotificationChannels",
                columns: new[] { "UserId", "ChannelType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramPairingTokens_User",
                table: "TelegramPairingTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_TelegramPairingTokens_Token",
                table: "TelegramPairingTokens",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertRules");

            migrationBuilder.DropTable(
                name: "NotificationChannels");

            migrationBuilder.DropTable(
                name: "TelegramPairingTokens");
        }
    }
}
