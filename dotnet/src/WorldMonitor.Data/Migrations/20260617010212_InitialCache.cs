using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldMonitor.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CacheEntries",
                columns: table => new
                {
                    CacheKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ByteLength = table.Column<long>(type: "bigint", nullable: false, computedColumnSql: "CAST(DATALENGTH([Value]) AS bigint)", stored: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RecordCount = table.Column<int>(type: "int", nullable: true),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    SourceVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    NewestItemAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxContentAgeMin = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheEntries", x => x.CacheKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CacheEntries_FetchedAt",
                table: "CacheEntries",
                column: "FetchedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CacheEntries");
        }
    }
}
