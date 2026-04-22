using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    /// <inheritdoc />
    [Migration("20260422235000_AddGlobalFavoritesAutoRefreshCatchup")]
    public partial class AddGlobalFavoritesAutoRefreshCatchup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogicalContentKey",
                table: "Favorites",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PreferredSourceProfileId",
                table: "Favorites",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAtUtc",
                table: "Favorites",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogicalContentKey",
                table: "PlaybackProgresses",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PreferredSourceProfileId",
                table: "PlaybackProgresses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAtUtc",
                table: "PlaybackProgresses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderCatchupMode",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderCatchupSource",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SupportsCatchup",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CatchupWindowHours",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CatchupSource",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CatchupConfidence",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CatchupSummary",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "CatchupDetectedAtUtc",
                table: "Channels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAutoRefreshAttemptAtUtc",
                table: "SourceSyncStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAutoRefreshSuccessAtUtc",
                table: "SourceSyncStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAutoRefreshDueAtUtc",
                table: "SourceSyncStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AutoRefreshState",
                table: "SourceSyncStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AutoRefreshSummary",
                table: "SourceSyncStates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AutoRefreshFailureCount",
                table: "SourceSyncStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_ProfileId_ContentType_LogicalContentKey",
                table: "Favorites",
                columns: new[] { "ProfileId", "ContentType", "LogicalContentKey" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackProgresses_ProfileId_ContentType_LogicalContentKey",
                table: "PlaybackProgresses",
                columns: new[] { "ProfileId", "ContentType", "LogicalContentKey" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceSyncStates_NextAutoRefreshDueAtUtc",
                table: "SourceSyncStates",
                column: "NextAutoRefreshDueAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Favorites_ProfileId_ContentType_LogicalContentKey",
                table: "Favorites");

            migrationBuilder.DropIndex(
                name: "IX_PlaybackProgresses_ProfileId_ContentType_LogicalContentKey",
                table: "PlaybackProgresses");

            migrationBuilder.DropIndex(
                name: "IX_SourceSyncStates_NextAutoRefreshDueAtUtc",
                table: "SourceSyncStates");

            migrationBuilder.DropColumn(name: "LogicalContentKey", table: "Favorites");
            migrationBuilder.DropColumn(name: "PreferredSourceProfileId", table: "Favorites");
            migrationBuilder.DropColumn(name: "ResolvedAtUtc", table: "Favorites");

            migrationBuilder.DropColumn(name: "LogicalContentKey", table: "PlaybackProgresses");
            migrationBuilder.DropColumn(name: "PreferredSourceProfileId", table: "PlaybackProgresses");
            migrationBuilder.DropColumn(name: "ResolvedAtUtc", table: "PlaybackProgresses");

            migrationBuilder.DropColumn(name: "ProviderCatchupMode", table: "Channels");
            migrationBuilder.DropColumn(name: "ProviderCatchupSource", table: "Channels");
            migrationBuilder.DropColumn(name: "SupportsCatchup", table: "Channels");
            migrationBuilder.DropColumn(name: "CatchupWindowHours", table: "Channels");
            migrationBuilder.DropColumn(name: "CatchupSource", table: "Channels");
            migrationBuilder.DropColumn(name: "CatchupConfidence", table: "Channels");
            migrationBuilder.DropColumn(name: "CatchupSummary", table: "Channels");
            migrationBuilder.DropColumn(name: "CatchupDetectedAtUtc", table: "Channels");

            migrationBuilder.DropColumn(name: "LastAutoRefreshAttemptAtUtc", table: "SourceSyncStates");
            migrationBuilder.DropColumn(name: "LastAutoRefreshSuccessAtUtc", table: "SourceSyncStates");
            migrationBuilder.DropColumn(name: "NextAutoRefreshDueAtUtc", table: "SourceSyncStates");
            migrationBuilder.DropColumn(name: "AutoRefreshState", table: "SourceSyncStates");
            migrationBuilder.DropColumn(name: "AutoRefreshSummary", table: "SourceSyncStates");
            migrationBuilder.DropColumn(name: "AutoRefreshFailureCount", table: "SourceSyncStates");
        }
    }
}
