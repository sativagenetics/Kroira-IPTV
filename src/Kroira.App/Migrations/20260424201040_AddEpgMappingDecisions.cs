using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    /// <inheritdoc />
    public partial class AddEpgMappingDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovedMatchCount",
                table: "EpgSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "EpgMappingDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelIdentityKey = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    ChannelName = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    CategoryName = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    ProviderEpgChannelId = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    StreamUrlHash = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    XmltvChannelId = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    XmltvDisplayName = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    Decision = table.Column<int>(type: "INTEGER", nullable: false),
                    SuggestedMatchSource = table.Column<int>(type: "INTEGER", nullable: false),
                    SuggestedConfidence = table.Column<int>(type: "INTEGER", nullable: false),
                    ReasonSummary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpgMappingDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpgMappingDecisions_SourceProfiles_SourceProfileId",
                        column: x => x.SourceProfileId,
                        principalTable: "SourceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EpgMappingDecisions_SourceProfileId_ChannelId_XmltvChannelId",
                table: "EpgMappingDecisions",
                columns: new[] { "SourceProfileId", "ChannelId", "XmltvChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EpgMappingDecisions_SourceProfileId_StreamUrlHash_XmltvChannelId",
                table: "EpgMappingDecisions",
                columns: new[] { "SourceProfileId", "StreamUrlHash", "XmltvChannelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EpgMappingDecisions");

            migrationBuilder.DropColumn(
                name: "ApprovedMatchCount",
                table: "EpgSyncLogs");
        }
    }
}
