using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    /// <inheritdoc />
    [Migration("20260422171000_AddSourceHealthComponents")]
    public partial class AddSourceHealthComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceHealthComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceHealthReportId = table.Column<int>(type: "INTEGER", nullable: false),
                    ComponentType = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    RelevantCount = table.Column<int>(type: "INTEGER", nullable: false),
                    HealthyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IssueCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceHealthComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceHealthComponents_SourceHealthReports_SourceHealthReportId",
                        column: x => x.SourceHealthReportId,
                        principalTable: "SourceHealthReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthComponents_SourceHealthReportId",
                table: "SourceHealthComponents",
                column: "SourceHealthReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthComponents_SourceHealthReportId_ComponentType",
                table: "SourceHealthComponents",
                columns: new[] { "SourceHealthReportId", "ComponentType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SourceHealthComponents");
        }
    }
}
