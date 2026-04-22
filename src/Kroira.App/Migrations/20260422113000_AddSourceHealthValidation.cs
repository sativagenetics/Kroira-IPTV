using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    /// <inheritdoc />
    [Migration("20260422113000_AddSourceHealthValidation")]
    public partial class AddSourceHealthValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceHealthReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSyncAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSuccessfulSyncAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HealthScore = table.Column<int>(type: "INTEGER", nullable: false),
                    HealthState = table.Column<int>(type: "INTEGER", nullable: false),
                    StatusSummary = table.Column<string>(type: "TEXT", nullable: false),
                    ImportResultSummary = table.Column<string>(type: "TEXT", nullable: false),
                    ValidationSummary = table.Column<string>(type: "TEXT", nullable: false),
                    TopIssueSummary = table.Column<string>(type: "TEXT", nullable: false),
                    TotalChannelCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalMovieCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalSeriesCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    InvalidStreamCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelsWithEpgMatchCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelsWithCurrentProgramCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelsWithNextProgramCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelsWithLogoCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SuspiciousEntryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceHealthReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceHealthReports_SourceProfiles_SourceProfileId",
                        column: x => x.SourceProfileId,
                        principalTable: "SourceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceHealthIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceHealthReportId = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    AffectedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SampleItems = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceHealthIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceHealthIssues_SourceHealthReports_SourceHealthReportId",
                        column: x => x.SourceHealthReportId,
                        principalTable: "SourceHealthReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthIssues_SourceHealthReportId",
                table: "SourceHealthIssues",
                column: "SourceHealthReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthReports_SourceProfileId",
                table: "SourceHealthReports",
                column: "SourceProfileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SourceHealthIssues");
            migrationBuilder.DropTable(name: "SourceHealthReports");
        }
    }
}
