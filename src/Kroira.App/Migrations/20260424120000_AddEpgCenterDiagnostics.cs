using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    [Migration("20260424120000_AddEpgCenterDiagnostics")]
    public partial class AddEpgCenterDiagnostics : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FallbackEpgUrls",
                table: "SourceCredentials",
                type: "TEXT",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ExactMatchCount",
                table: "EpgSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "GuideSourceStatusJson",
                table: "EpgSyncLogs",
                type: "TEXT",
                maxLength: 8000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GuideWarningSummary",
                table: "EpgSyncLogs",
                type: "TEXT",
                maxLength: 800,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "NormalizedMatchCount",
                table: "EpgSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WeakMatchCount",
                table: "EpgSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "XmltvChannelCount",
                table: "EpgSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "FallbackEpgUrls", table: "SourceCredentials");
            migrationBuilder.DropColumn(name: "ExactMatchCount", table: "EpgSyncLogs");
            migrationBuilder.DropColumn(name: "GuideSourceStatusJson", table: "EpgSyncLogs");
            migrationBuilder.DropColumn(name: "GuideWarningSummary", table: "EpgSyncLogs");
            migrationBuilder.DropColumn(name: "NormalizedMatchCount", table: "EpgSyncLogs");
            migrationBuilder.DropColumn(name: "WeakMatchCount", table: "EpgSyncLogs");
            migrationBuilder.DropColumn(name: "XmltvChannelCount", table: "EpgSyncLogs");
        }
    }
}
