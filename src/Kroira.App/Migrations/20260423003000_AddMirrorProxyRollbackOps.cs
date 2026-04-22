using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    [Migration("20260423003000_AddMirrorProxyRollbackOps")]
    public partial class AddMirrorProxyRollbackOps : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProxyScope",
                table: "SourceCredentials",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProxyUrl",
                table: "SourceCredentials",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "LogicalOperationalStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContentType = table.Column<int>(type: "INTEGER", nullable: false),
                    LogicalContentKey = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    CandidateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PreferredContentId = table.Column<int>(type: "INTEGER", nullable: false),
                    PreferredSourceProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    PreferredScore = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectionSummary = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    LastKnownGoodContentId = table.Column<int>(type: "INTEGER", nullable: false),
                    LastKnownGoodSourceProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    LastKnownGoodScore = table.Column<int>(type: "INTEGER", nullable: false),
                    LastKnownGoodAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastPlaybackSuccessAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastPlaybackFailureAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConsecutivePlaybackFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    RecoveryAction = table.Column<int>(type: "INTEGER", nullable: false),
                    RecoverySummary = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    SnapshotEvaluatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PreferredUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogicalOperationalStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogicalOperationalCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LogicalOperationalStateId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSelected = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsLastKnownGood = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsProxy = table.Column<bool>(type: "INTEGER", nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    StreamUrl = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogicalOperationalCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogicalOperationalCandidates_LogicalOperationalStates_LogicalOperationalStateId",
                        column: x => x.LogicalOperationalStateId,
                        principalTable: "LogicalOperationalStates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LogicalOperationalCandidates_LogicalOperationalStateId_ContentId_SourceProfileId",
                table: "LogicalOperationalCandidates",
                columns: new[] { "LogicalOperationalStateId", "ContentId", "SourceProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogicalOperationalCandidates_SourceProfileId_IsSelected",
                table: "LogicalOperationalCandidates",
                columns: new[] { "SourceProfileId", "IsSelected" });

            migrationBuilder.CreateIndex(
                name: "IX_LogicalOperationalStates_ContentType_LogicalContentKey",
                table: "LogicalOperationalStates",
                columns: new[] { "ContentType", "LogicalContentKey" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LogicalOperationalCandidates");

            migrationBuilder.DropTable(
                name: "LogicalOperationalStates");

            migrationBuilder.DropColumn(
                name: "ProxyScope",
                table: "SourceCredentials");

            migrationBuilder.DropColumn(
                name: "ProxyUrl",
                table: "SourceCredentials");
        }
    }
}
