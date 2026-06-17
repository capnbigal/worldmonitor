using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldMonitor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMlState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CorrelationClusterStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Domain = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClusterKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    Country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    EntityKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Score = table.Column<double>(type: "float", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrelationClusterStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CorrelationStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NewsVelocity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MarketChanges = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PredictionChanges = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrelationStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DedupSeen",
                columns: table => new
                {
                    DedupKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SignalType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SeenAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DedupSeen", x => x.DedupKey);
                });

            migrationBuilder.CreateTable(
                name: "TopicVelocityPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Topic = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Velocity = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopicVelocityPoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vectors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Embedding = table.Column<byte[]>(type: "varbinary(1536)", nullable: false),
                    PubDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IngestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vectors", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_CorrelationClusters_Domain_Key",
                table: "CorrelationClusterStates",
                columns: new[] { "Domain", "ClusterKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DedupSeen_SeenAt",
                table: "DedupSeen",
                column: "SeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_TopicVelocity_Topic_Timestamp",
                table: "TopicVelocityPoints",
                columns: new[] { "Topic", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Vectors_IngestedAt",
                table: "Vectors",
                column: "IngestedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorrelationClusterStates");

            migrationBuilder.DropTable(
                name: "CorrelationStates");

            migrationBuilder.DropTable(
                name: "DedupSeen");

            migrationBuilder.DropTable(
                name: "TopicVelocityPoints");

            migrationBuilder.DropTable(
                name: "Vectors");
        }
    }
}
