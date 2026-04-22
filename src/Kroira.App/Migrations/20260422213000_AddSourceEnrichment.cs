using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    /// <inheritdoc />
    [Migration("20260422213000_AddSourceEnrichment")]
    public partial class AddSourceEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderLogoUrl",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderEpgChannelId",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedIdentityKey",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AliasKeys",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "EpgMatchSource",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EpgMatchConfidence",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EpgMatchSummary",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LogoSource",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LogoConfidence",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LogoSummary",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "EnrichedAtUtc",
                table: "Channels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SourceChannelEnrichmentRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    IdentityKey = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    AliasKeys = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderEpgChannelId = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderLogoUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ResolvedLogoUrl = table.Column<string>(type: "TEXT", nullable: false),
                    MatchedXmltvChannelId = table.Column<string>(type: "TEXT", nullable: false),
                    MatchedXmltvDisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    MatchedXmltvIconUrl = table.Column<string>(type: "TEXT", nullable: false),
                    EpgMatchSource = table.Column<int>(type: "INTEGER", nullable: false),
                    EpgMatchConfidence = table.Column<int>(type: "INTEGER", nullable: false),
                    EpgMatchSummary = table.Column<string>(type: "TEXT", nullable: false),
                    LogoSource = table.Column<int>(type: "INTEGER", nullable: false),
                    LogoConfidence = table.Column<int>(type: "INTEGER", nullable: false),
                    LogoSummary = table.Column<string>(type: "TEXT", nullable: false),
                    LastAppliedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceChannelEnrichmentRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceChannelEnrichmentRecords_SourceProfiles_SourceProfileId",
                        column: x => x.SourceProfileId,
                        principalTable: "SourceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Channels_NormalizedIdentityKey",
                table: "Channels",
                column: "NormalizedIdentityKey");

            migrationBuilder.CreateIndex(
                name: "IX_SourceChannelEnrichmentRecords_SourceProfileId",
                table: "SourceChannelEnrichmentRecords",
                column: "SourceProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceChannelEnrichmentRecords_SourceProfileId_IdentityKey",
                table: "SourceChannelEnrichmentRecords",
                columns: new[] { "SourceProfileId", "IdentityKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SourceChannelEnrichmentRecords");

            migrationBuilder.DropIndex(
                name: "IX_Channels_NormalizedIdentityKey",
                table: "Channels");

            migrationBuilder.DropColumn(name: "ProviderLogoUrl", table: "Channels");
            migrationBuilder.DropColumn(name: "ProviderEpgChannelId", table: "Channels");
            migrationBuilder.DropColumn(name: "NormalizedIdentityKey", table: "Channels");
            migrationBuilder.DropColumn(name: "NormalizedName", table: "Channels");
            migrationBuilder.DropColumn(name: "AliasKeys", table: "Channels");
            migrationBuilder.DropColumn(name: "EpgMatchSource", table: "Channels");
            migrationBuilder.DropColumn(name: "EpgMatchConfidence", table: "Channels");
            migrationBuilder.DropColumn(name: "EpgMatchSummary", table: "Channels");
            migrationBuilder.DropColumn(name: "LogoSource", table: "Channels");
            migrationBuilder.DropColumn(name: "LogoConfidence", table: "Channels");
            migrationBuilder.DropColumn(name: "LogoSummary", table: "Channels");
            migrationBuilder.DropColumn(name: "EnrichedAtUtc", table: "Channels");
        }
    }
}
