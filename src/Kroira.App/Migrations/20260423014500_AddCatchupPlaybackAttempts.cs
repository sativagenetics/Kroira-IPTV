using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    [Migration("20260423014500_AddCatchupPlaybackAttempts")]
    public partial class AddCatchupPlaybackAttempts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatchupPlaybackAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    LogicalContentKey = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    RequestKind = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProgramTitle = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    ProgramStartTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProgramEndTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WindowHours = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    RoutingSummary = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    ResolvedStreamUrl = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    ProviderMode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProviderSource = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatchupPlaybackAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatchupPlaybackAttempts_SourceProfiles_SourceProfileId",
                        column: x => x.SourceProfileId,
                        principalTable: "SourceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatchupPlaybackAttempts_ChannelId_RequestedAtUtc",
                table: "CatchupPlaybackAttempts",
                columns: new[] { "ChannelId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CatchupPlaybackAttempts_SourceProfileId_RequestedAtUtc",
                table: "CatchupPlaybackAttempts",
                columns: new[] { "SourceProfileId", "RequestedAtUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatchupPlaybackAttempts");
        }
    }
}
