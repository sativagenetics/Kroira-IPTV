using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kroira.App.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceAcquisitionRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EpgPrograms_ChannelId",
                table: "EpgPrograms");

            migrationBuilder.AddColumn<int>(
                name: "AutoRefreshFailureCount",
                table: "SourceSyncStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

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
                maxLength: 220,
                nullable: false,
                defaultValue: "");

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

            migrationBuilder.AlterColumn<int>(
                name: "M3uImportMode",
                table: "SourceCredentials",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "DetectedEpgUrl",
                table: "SourceCredentials",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EpgMode",
                table: "SourceCredentials",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

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
                maxLength: 600,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BackdropUrl",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CanonicalTitleKey",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContentKind",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DedupFingerprint",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstAirDate",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Genres",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImdbId",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataUpdatedAt",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalLanguage",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Overview",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "Popularity",
                table: "Series",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "RawSourceCategoryName",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RawSourceTitle",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TmdbBackdropPath",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TmdbPosterPath",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "VoteAverage",
                table: "Series",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "ChannelName",
                table: "RecordingJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "RecordingJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "RecordingJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "RecordingJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "RecordingJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxRetryCount",
                table: "RecordingJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAtUtc",
                table: "RecordingJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProfileId",
                table: "RecordingJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedAtUtc",
                table: "RecordingJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "RecordingJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAtUtc",
                table: "RecordingJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreamUrl",
                table: "RecordingJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TempOutputPath",
                table: "RecordingJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "RecordingJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "PlaybackProgresses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationMs",
                table: "PlaybackProgresses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "LogicalContentKey",
                table: "PlaybackProgresses",
                type: "TEXT",
                maxLength: 260,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PreferredSourceProfileId",
                table: "PlaybackProgresses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProfileId",
                table: "PlaybackProgresses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAtUtc",
                table: "PlaybackProgresses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WatchStateOverride",
                table: "PlaybackProgresses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HideLockedContent",
                table: "ParentalControlSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsKidsSafeMode",
                table: "ParentalControlSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LockedSourceIdsJson",
                table: "ParentalControlSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProfileId",
                table: "ParentalControlSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BackdropUrl",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalTitleKey",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentKind",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DedupFingerprint",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Genres",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImdbId",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MetadataUpdatedAt",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalLanguage",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Overview",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Popularity",
                table: "Movies",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "RawSourceCategoryName",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawSourceTitle",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseDate",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TmdbBackdropPath",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TmdbPosterPath",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VoteAverage",
                table: "Movies",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "LogicalContentKey",
                table: "Favorites",
                type: "TEXT",
                maxLength: 220,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PreferredSourceProfileId",
                table: "Favorites",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProfileId",
                table: "Favorites",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAtUtc",
                table: "Favorites",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "EpgSyncLogs",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "ActiveMode",
                table: "EpgSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ActiveXmltvUrl",
                table: "EpgSyncLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentCoverageCount",
                table: "EpgSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FailureStage",
                table: "EpgSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSuccessAtUtc",
                table: "EpgSyncLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchBreakdown",
                table: "EpgSyncLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NextCoverageCount",
                table: "EpgSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ResultCode",
                table: "EpgSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "EpgSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalLiveChannelCount",
                table: "EpgSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnmatchedChannelCount",
                table: "EpgSyncLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "EpgPrograms",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "EpgPrograms",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "DownloadJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxRetryCount",
                table: "DownloadJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAtUtc",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProfileId",
                table: "DownloadJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedAtUtc",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "DownloadJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartedAtUtc",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreamUrl",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subtitle",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TempOutputPath",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "AliasKeys",
                table: "Channels",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "CatchupConfidence",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CatchupDetectedAtUtc",
                table: "Channels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CatchupSource",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CatchupSummary",
                table: "Channels",
                type: "TEXT",
                maxLength: 220,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "CatchupWindowHours",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EnrichedAtUtc",
                table: "Channels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EpgChannelId",
                table: "Channels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "EpgMatchConfidence",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EpgMatchSource",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EpgMatchSummary",
                table: "Channels",
                type: "TEXT",
                maxLength: 220,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LogoConfidence",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LogoSource",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LogoSummary",
                table: "Channels",
                type: "TEXT",
                maxLength: 220,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedIdentityKey",
                table: "Channels",
                type: "TEXT",
                maxLength: 180,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Channels",
                type: "TEXT",
                maxLength: 180,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderCatchupMode",
                table: "Channels",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderCatchupSource",
                table: "Channels",
                type: "TEXT",
                maxLength: 600,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderEpgChannelId",
                table: "Channels",
                type: "TEXT",
                maxLength: 220,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderLogoUrl",
                table: "Channels",
                type: "TEXT",
                maxLength: 600,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SupportsCatchup",
                table: "Channels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AppProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsKidsProfile = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppProfiles", x => x.Id);
                });

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
                name: "SourceAcquisitionProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileKey = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    ProfileLabel = table.Column<string>(type: "TEXT", maxLength: 140, nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    NormalizationSummary = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    MatchingSummary = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    SuppressionSummary = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    ValidationSummary = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    SupportsRegexMatching = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreferProxyDuringValidation = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreferLastKnownGoodRollback = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceAcquisitionProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceAcquisitionProfiles_SourceProfiles_SourceProfileId",
                        column: x => x.SourceProfileId,
                        principalTable: "SourceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceAcquisitionRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Trigger = table.Column<int>(type: "INTEGER", nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProfileKey = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    ProfileLabel = table.Column<string>(type: "TEXT", maxLength: 140, nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    RoutingSummary = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    ValidationRoutingSummary = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 420, nullable: false),
                    CatalogSummary = table.Column<string>(type: "TEXT", maxLength: 280, nullable: false),
                    GuideSummary = table.Column<string>(type: "TEXT", maxLength: 280, nullable: false),
                    ValidationSummary = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    RawItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AcceptedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SuppressedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DemotedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnmatchedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LiveCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MovieCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AliasMatchCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RegexMatchCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FuzzyMatchCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ProbeSuccessCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ProbeFailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WarningCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceAcquisitionRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceAcquisitionRuns_SourceProfiles_SourceProfileId",
                        column: x => x.SourceProfileId,
                        principalTable: "SourceProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceChannelEnrichmentRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    IdentityKey = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                    AliasKeys = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    ProviderEpgChannelId = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    ProviderLogoUrl = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    ResolvedLogoUrl = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    MatchedXmltvChannelId = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    MatchedXmltvDisplayName = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    MatchedXmltvIconUrl = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    EpgMatchSource = table.Column<int>(type: "INTEGER", nullable: false),
                    EpgMatchConfidence = table.Column<int>(type: "INTEGER", nullable: false),
                    EpgMatchSummary = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    LogoSource = table.Column<int>(type: "INTEGER", nullable: false),
                    LogoConfidence = table.Column<int>(type: "INTEGER", nullable: false),
                    LogoSummary = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
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
                    StatusSummary = table.Column<string>(type: "TEXT", maxLength: 280, nullable: false),
                    ImportResultSummary = table.Column<string>(type: "TEXT", maxLength: 280, nullable: false),
                    ValidationSummary = table.Column<string>(type: "TEXT", maxLength: 280, nullable: false),
                    TopIssueSummary = table.Column<string>(type: "TEXT", maxLength: 360, nullable: false),
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

            migrationBuilder.CreateTable(
                name: "SourceAcquisitionEvidence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceAcquisitionRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemKind = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleCode = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    RawName = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    RawCategory = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    NormalizedCategory = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    IdentityKey = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    AliasKeys = table.Column<string>(type: "TEXT", maxLength: 1600, nullable: false),
                    MatchedValue = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    MatchedTarget = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
                    Confidence = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceAcquisitionEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceAcquisitionEvidence_SourceAcquisitionRuns_SourceAcquisitionRunId",
                        column: x => x.SourceAcquisitionRunId,
                        principalTable: "SourceAcquisitionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    Summary = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
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

            migrationBuilder.CreateTable(
                name: "SourceHealthIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceHealthReportId = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 280, nullable: false),
                    AffectedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SampleItems = table.Column<string>(type: "TEXT", maxLength: 280, nullable: false),
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
                    Summary = table.Column<string>(type: "TEXT", maxLength: 220, nullable: false),
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
                name: "IX_Series_CanonicalTitleKey",
                table: "Series",
                column: "CanonicalTitleKey");

            migrationBuilder.CreateIndex(
                name: "IX_Series_DedupFingerprint",
                table: "Series",
                column: "DedupFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_Series_MetadataUpdatedAt",
                table: "Series",
                column: "MetadataUpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Series_TmdbId",
                table: "Series",
                column: "TmdbId");

            migrationBuilder.CreateIndex(
                name: "IX_RecordingJobs_ProfileId_Status_StartTimeUtc",
                table: "RecordingJobs",
                columns: new[] { "ProfileId", "Status", "StartTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackProgresses_ProfileId_ContentType_ContentId",
                table: "PlaybackProgresses",
                columns: new[] { "ProfileId", "ContentType", "ContentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackProgresses_ProfileId_ContentType_LogicalContentKey",
                table: "PlaybackProgresses",
                columns: new[] { "ProfileId", "ContentType", "LogicalContentKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ParentalControlSettings_ProfileId",
                table: "ParentalControlSettings",
                column: "ProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Movies_CanonicalTitleKey",
                table: "Movies",
                column: "CanonicalTitleKey");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_DedupFingerprint",
                table: "Movies",
                column: "DedupFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_MetadataUpdatedAt",
                table: "Movies",
                column: "MetadataUpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_TmdbId",
                table: "Movies",
                column: "TmdbId");

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_ProfileId_ContentType_ContentId",
                table: "Favorites",
                columns: new[] { "ProfileId", "ContentType", "ContentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Favorites_ProfileId_ContentType_LogicalContentKey",
                table: "Favorites",
                columns: new[] { "ProfileId", "ContentType", "LogicalContentKey" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJobs_ProfileId_Status_RequestedAtUtc",
                table: "DownloadJobs",
                columns: new[] { "ProfileId", "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Channels_EpgChannelId",
                table: "Channels",
                column: "EpgChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_NormalizedIdentityKey",
                table: "Channels",
                column: "NormalizedIdentityKey");

            migrationBuilder.CreateIndex(
                name: "IX_AppProfiles_Name",
                table: "AppProfiles",
                column: "Name");

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

            migrationBuilder.CreateIndex(
                name: "IX_SourceAcquisitionEvidence_SourceAcquisitionRunId_SortOrder",
                table: "SourceAcquisitionEvidence",
                columns: new[] { "SourceAcquisitionRunId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceAcquisitionProfiles_SourceProfileId",
                table: "SourceAcquisitionProfiles",
                column: "SourceProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceAcquisitionRuns_SourceProfileId_StartedAtUtc",
                table: "SourceAcquisitionRuns",
                columns: new[] { "SourceProfileId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceChannelEnrichmentRecords_SourceProfileId_IdentityKey",
                table: "SourceChannelEnrichmentRecords",
                columns: new[] { "SourceProfileId", "IdentityKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthComponents_SourceHealthReportId_ComponentType",
                table: "SourceHealthComponents",
                columns: new[] { "SourceHealthReportId", "ComponentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthIssues_SourceHealthReportId",
                table: "SourceHealthIssues",
                column: "SourceHealthReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthProbes_SourceHealthReportId_ProbeType",
                table: "SourceHealthProbes",
                columns: new[] { "SourceHealthReportId", "ProbeType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceHealthReports_SourceProfileId",
                table: "SourceHealthReports",
                column: "SourceProfileId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppProfiles");

            migrationBuilder.DropTable(
                name: "LogicalOperationalCandidates");

            migrationBuilder.DropTable(
                name: "SourceAcquisitionEvidence");

            migrationBuilder.DropTable(
                name: "SourceAcquisitionProfiles");

            migrationBuilder.DropTable(
                name: "SourceChannelEnrichmentRecords");

            migrationBuilder.DropTable(
                name: "SourceHealthComponents");

            migrationBuilder.DropTable(
                name: "SourceHealthIssues");

            migrationBuilder.DropTable(
                name: "SourceHealthProbes");

            migrationBuilder.DropTable(
                name: "LogicalOperationalStates");

            migrationBuilder.DropTable(
                name: "SourceAcquisitionRuns");

            migrationBuilder.DropTable(
                name: "SourceHealthReports");

            migrationBuilder.DropIndex(
                name: "IX_Series_CanonicalTitleKey",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Series_DedupFingerprint",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Series_MetadataUpdatedAt",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Series_TmdbId",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_RecordingJobs_ProfileId_Status_StartTimeUtc",
                table: "RecordingJobs");

            migrationBuilder.DropIndex(
                name: "IX_PlaybackProgresses_ProfileId_ContentType_ContentId",
                table: "PlaybackProgresses");

            migrationBuilder.DropIndex(
                name: "IX_PlaybackProgresses_ProfileId_ContentType_LogicalContentKey",
                table: "PlaybackProgresses");

            migrationBuilder.DropIndex(
                name: "IX_ParentalControlSettings_ProfileId",
                table: "ParentalControlSettings");

            migrationBuilder.DropIndex(
                name: "IX_Movies_CanonicalTitleKey",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_Movies_DedupFingerprint",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_Movies_MetadataUpdatedAt",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_Movies_TmdbId",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "IX_Favorites_ProfileId_ContentType_ContentId",
                table: "Favorites");

            migrationBuilder.DropIndex(
                name: "IX_Favorites_ProfileId_ContentType_LogicalContentKey",
                table: "Favorites");

            migrationBuilder.DropIndex(
                name: "IX_DownloadJobs_ProfileId_Status_RequestedAtUtc",
                table: "DownloadJobs");

            migrationBuilder.DropIndex(
                name: "IX_Channels_EpgChannelId",
                table: "Channels");

            migrationBuilder.DropIndex(
                name: "IX_Channels_NormalizedIdentityKey",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "AutoRefreshFailureCount",
                table: "SourceSyncStates");

            migrationBuilder.DropColumn(
                name: "AutoRefreshState",
                table: "SourceSyncStates");

            migrationBuilder.DropColumn(
                name: "AutoRefreshSummary",
                table: "SourceSyncStates");

            migrationBuilder.DropColumn(
                name: "LastAutoRefreshAttemptAtUtc",
                table: "SourceSyncStates");

            migrationBuilder.DropColumn(
                name: "LastAutoRefreshSuccessAtUtc",
                table: "SourceSyncStates");

            migrationBuilder.DropColumn(
                name: "NextAutoRefreshDueAtUtc",
                table: "SourceSyncStates");

            migrationBuilder.DropColumn(
                name: "DetectedEpgUrl",
                table: "SourceCredentials");

            migrationBuilder.DropColumn(
                name: "EpgMode",
                table: "SourceCredentials");

            migrationBuilder.DropColumn(
                name: "ProxyScope",
                table: "SourceCredentials");

            migrationBuilder.DropColumn(
                name: "ProxyUrl",
                table: "SourceCredentials");

            migrationBuilder.DropColumn(
                name: "BackdropUrl",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "CanonicalTitleKey",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "ContentKind",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "DedupFingerprint",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "FirstAirDate",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Genres",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "ImdbId",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "MetadataUpdatedAt",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "OriginalLanguage",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Overview",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Popularity",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "RawSourceCategoryName",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "RawSourceTitle",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "TmdbBackdropPath",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "TmdbPosterPath",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "VoteAverage",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "ChannelName",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "MaxRetryCount",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "NextRetryAtUtc",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "RequestedAtUtc",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "StartedAtUtc",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "StreamUrl",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "TempOutputPath",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "RecordingJobs");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "PlaybackProgresses");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "PlaybackProgresses");

            migrationBuilder.DropColumn(
                name: "LogicalContentKey",
                table: "PlaybackProgresses");

            migrationBuilder.DropColumn(
                name: "PreferredSourceProfileId",
                table: "PlaybackProgresses");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "PlaybackProgresses");

            migrationBuilder.DropColumn(
                name: "ResolvedAtUtc",
                table: "PlaybackProgresses");

            migrationBuilder.DropColumn(
                name: "WatchStateOverride",
                table: "PlaybackProgresses");

            migrationBuilder.DropColumn(
                name: "HideLockedContent",
                table: "ParentalControlSettings");

            migrationBuilder.DropColumn(
                name: "IsKidsSafeMode",
                table: "ParentalControlSettings");

            migrationBuilder.DropColumn(
                name: "LockedSourceIdsJson",
                table: "ParentalControlSettings");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "ParentalControlSettings");

            migrationBuilder.DropColumn(
                name: "BackdropUrl",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "CanonicalTitleKey",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "ContentKind",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "DedupFingerprint",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Genres",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "ImdbId",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "MetadataUpdatedAt",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "OriginalLanguage",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Overview",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Popularity",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "RawSourceCategoryName",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "RawSourceTitle",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "ReleaseDate",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "TmdbBackdropPath",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "TmdbPosterPath",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "VoteAverage",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "LogicalContentKey",
                table: "Favorites");

            migrationBuilder.DropColumn(
                name: "PreferredSourceProfileId",
                table: "Favorites");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "Favorites");

            migrationBuilder.DropColumn(
                name: "ResolvedAtUtc",
                table: "Favorites");

            migrationBuilder.DropColumn(
                name: "ActiveMode",
                table: "EpgSyncLogs");

            migrationBuilder.DropColumn(
                name: "ActiveXmltvUrl",
                table: "EpgSyncLogs");

            migrationBuilder.DropColumn(
                name: "CurrentCoverageCount",
                table: "EpgSyncLogs");

            migrationBuilder.DropColumn(
                name: "FailureStage",
                table: "EpgSyncLogs");

            migrationBuilder.DropColumn(
                name: "LastSuccessAtUtc",
                table: "EpgSyncLogs");

            migrationBuilder.DropColumn(
                name: "MatchBreakdown",
                table: "EpgSyncLogs");

            migrationBuilder.DropColumn(
                name: "NextCoverageCount",
                table: "EpgSyncLogs");

            migrationBuilder.DropColumn(
                name: "ResultCode",
                table: "EpgSyncLogs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "EpgSyncLogs");

            migrationBuilder.DropColumn(
                name: "TotalLiveChannelCount",
                table: "EpgSyncLogs");

            migrationBuilder.DropColumn(
                name: "UnmatchedChannelCount",
                table: "EpgSyncLogs");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "MaxRetryCount",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "NextRetryAtUtc",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "RequestedAtUtc",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "StartedAtUtc",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "StreamUrl",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "Subtitle",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "TempOutputPath",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "AliasKeys",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "CatchupConfidence",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "CatchupDetectedAtUtc",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "CatchupSource",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "CatchupSummary",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "CatchupWindowHours",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "EnrichedAtUtc",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "EpgChannelId",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "EpgMatchConfidence",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "EpgMatchSource",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "EpgMatchSummary",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "LogoConfidence",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "LogoSource",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "LogoSummary",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "NormalizedIdentityKey",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "ProviderCatchupMode",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "ProviderCatchupSource",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "ProviderEpgChannelId",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "ProviderLogoUrl",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "SupportsCatchup",
                table: "Channels");

            migrationBuilder.AlterColumn<int>(
                name: "M3uImportMode",
                table: "SourceCredentials",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "EpgSyncLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "EpgPrograms",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "EpgPrograms",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_ChannelId",
                table: "EpgPrograms",
                column: "ChannelId");
        }
    }
}
