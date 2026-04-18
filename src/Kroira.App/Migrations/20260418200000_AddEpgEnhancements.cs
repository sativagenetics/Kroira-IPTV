using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    /// <inheritdoc />
    [Migration("20260418200000_AddEpgEnhancements")]
    public partial class AddEpgEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Subtitle",
                table: "EpgPrograms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "EpgPrograms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_ChannelId_StartTimeUtc",
                table: "EpgPrograms",
                columns: new[] { "ChannelId", "StartTimeUtc" });

            migrationBuilder.CreateTable(
                name: "EpgSyncLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    SyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsSuccess = table.Column<bool>(type: "INTEGER", nullable: false),
                    MatchedChannelCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ProgrammeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpgSyncLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpgSyncLogs_SourceProfiles_SourceProfileId",
                        column: x => x.SourceProfileId,
                        principalTable: "SourceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EpgSyncLogs_SourceProfileId",
                table: "EpgSyncLogs",
                column: "SourceProfileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EpgSyncLogs");

            migrationBuilder.DropIndex(
                name: "IX_EpgPrograms_ChannelId_StartTimeUtc",
                table: "EpgPrograms");

            migrationBuilder.DropColumn(name: "Subtitle", table: "EpgPrograms");
            migrationBuilder.DropColumn(name: "Category", table: "EpgPrograms");
        }
    }
}
