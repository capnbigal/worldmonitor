using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldMonitor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityWatchlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FollowedCountries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowedCountries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Variant = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SyncVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    LocaleTag = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: true),
                    LocalePrimary = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Timezone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FollowedCountries_Country",
                table: "FollowedCountries",
                column: "Country");

            migrationBuilder.CreateIndex(
                name: "UX_FollowedCountries_User_Country",
                table: "FollowedCountries",
                columns: new[] { "UserId", "Country" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_UserPreferences_User_Variant",
                table: "UserPreferences",
                columns: new[] { "UserId", "Variant" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail",
                table: "Users",
                column: "NormalizedEmail");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FollowedCountries");

            migrationBuilder.DropTable(
                name: "UserPreferences");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
