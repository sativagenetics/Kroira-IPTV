using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    [Migration("20260423031500_AddStalkerPortalSupport")]
    public partial class AddStalkerPortalSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StalkerApiUrl",
                table: "SourceCredentials",
                type: "TEXT",
                maxLength: 600,
                nullable: false,
                defaultValue: string.Empty);

            migrationBuilder.AddColumn<string>(
                name: "StalkerDeviceId",
                table: "SourceCredentials",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: string.Empty);

            migrationBuilder.AddColumn<string>(
                name: "StalkerLocale",
                table: "SourceCredentials",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: string.Empty);

            migrationBuilder.AddColumn<string>(
                name: "StalkerMacAddress",
                table: "SourceCredentials",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: string.Empty);

            migrationBuilder.AddColumn<string>(
                name: "StalkerSerialNumber",
                table: "SourceCredentials",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: string.Empty);

            migrationBuilder.AddColumn<string>(
                name: "StalkerTimezone",
                table: "SourceCredentials",
                type: "TEXT",
                maxLength: 96,
                nullable: false,
                defaultValue: string.Empty);

            migrationBuilder.CreateTable(
                name: "StalkerPortalSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    PortalName = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    PortalVersion = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    ProfileName = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    ProfileId = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    MacAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Timezone = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    DiscoveredApiUrl = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    SupportsLive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsMovies = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsSeries = table.Column<bool>(type: "INTEGER", nullable: false),
                    LiveCategoryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MovieCategoryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesCategoryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastHandshakeAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastProfileSyncAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSummary = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StalkerPortalSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StalkerPortalSnapshots_SourceProfiles_SourceProfileId",
                        column: x => x.SourceProfileId,
                        principalTable: "SourceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StalkerPortalSnapshots_SourceProfileId",
                table: "StalkerPortalSnapshots",
                column: "SourceProfileId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StalkerPortalSnapshots");

            migrationBuilder.DropColumn(
                name: "StalkerApiUrl",
                table: "SourceCredentials");

            migrationBuilder.DropColumn(
                name: "StalkerDeviceId",
                table: "SourceCredentials");

            migrationBuilder.DropColumn(
                name: "StalkerLocale",
                table: "SourceCredentials");

            migrationBuilder.DropColumn(
                name: "StalkerMacAddress",
                table: "SourceCredentials");

            migrationBuilder.DropColumn(
                name: "StalkerSerialNumber",
                table: "SourceCredentials");

            migrationBuilder.DropColumn(
                name: "StalkerTimezone",
                table: "SourceCredentials");
        }
    }
}
