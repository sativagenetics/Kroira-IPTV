using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    /// <inheritdoc />
    [Migration("20260422193000_AddSourceHealthProbes")]
    public partial class AddSourceHealthProbes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceHealthProbes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceHealthReportId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProbeType = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ProbedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CandidateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SampleSize = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeoutCount = table.Column<int>(type: "INTEGER", nullable: false),
                    HttpErrorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TransportErrorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceHealthProbes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceHealthProbes_SourceHealthReports_SourceHealthReportId",
                        column: x => x.SourceHealthReportId,
                        principalTable: "SourceHealthReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthProbes_SourceHealthReportId",
                table: "SourceHealthProbes",
                column: "SourceHealthReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthProbes_SourceHealthReportId_ProbeType",
                table: "SourceHealthProbes",
                columns: new[] { "SourceHealthReportId", "ProbeType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SourceHealthProbes");
        }
    }
}
