#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Services
{
    public sealed class BackupExportResult
    {
        public string FilePath { get; init; } = string.Empty;
        public int SourceCount { get; init; }
        public int ProfileCount { get; init; }
        public int FavoriteCount { get; init; }
        public int WatchStateCount { get; init; }
    }

    public sealed class BackupRestoreResult
    {
        public int SourceCount { get; init; }
        public int ProfileCount { get; init; }
        public int FavoriteCount { get; init; }
        public int FavoriteSkippedCount { get; init; }
        public int WatchStateCount { get; init; }
        public int WatchStateSkippedCount { get; init; }
        public int SourceSyncFailureCount { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    public interface IBackupPackageService
    {
        Task<BackupExportResult> ExportAsync(string filePath);
        Task<BackupRestoreResult> RestoreAsync(string filePath);
    }

    public sealed partial class BackupPackageService : IBackupPackageService
    {
        private const string PackageType = "KroiraBackup";
        private const int PackageSchemaVersion = 1;
        private const long MaxPackageSizeBytes = 16 * 1024 * 1024;
        private readonly IServiceProvider _services;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        public BackupPackageService(IServiceProvider services)
        {
            _services = services;
        }

        public async Task<BackupExportResult> ExportAsync(string filePath)
        {
            return await ExportPackageAsync(filePath);
        }

        public async Task<BackupRestoreResult> RestoreAsync(string filePath)
        {
            return await RestorePackageAsync(filePath);
        }

        private sealed class BackupPackage
        {
            public string PackageType { get; set; } = BackupPackageService.PackageType;
            public int SchemaVersion { get; set; } = BackupPackageService.PackageSchemaVersion;
            public DateTime ExportedAtUtc { get; set; }
            public string AppVersion { get; set; } = string.Empty;
            public List<BackupSourceRecord> Sources { get; set; } = new();
            public List<BackupAppProfileRecord> Profiles { get; set; } = new();
            public List<BackupParentalControlRecord> ParentalControls { get; set; } = new();
            public List<BackupAppSettingRecord> Settings { get; set; } = new();
            public List<BackupFavoriteRecord> Favorites { get; set; } = new();
            public List<BackupWatchStateRecord> WatchStates { get; set; } = new();
        }

        private sealed class BackupSourceRecord
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public SourceType Type { get; set; }
            public DateTime? LastSync { get; set; }
            public BackupSourceCredentialRecord? Credential { get; set; }
            public BackupSourceSyncStateRecord? SyncState { get; set; }
        }

        private sealed class BackupSourceCredentialRecord
        {
            public string Url { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string EpgUrl { get; set; } = string.Empty;
            public string DetectedEpgUrl { get; set; } = string.Empty;
            public EpgActiveMode EpgMode { get; set; } = EpgActiveMode.Detected;
            public M3uImportMode M3uImportMode { get; set; }
        }

        private sealed class BackupSourceSyncStateRecord
        {
            public DateTime LastAttempt { get; set; }
            public int HttpStatusCode { get; set; }
            public string ErrorLog { get; set; } = string.Empty;
        }

        private sealed class BackupAppProfileRecord
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public bool IsKidsProfile { get; set; }
            public DateTime CreatedAtUtc { get; set; }
        }

        private sealed class BackupParentalControlRecord
        {
            public int ProfileId { get; set; }
            public string PinHash { get; set; } = string.Empty;
            public string LockedCategoryIdsJson { get; set; } = string.Empty;
            public string LockedSourceIdsJson { get; set; } = string.Empty;
            public bool IsKidsSafeMode { get; set; }
            public bool HideLockedContent { get; set; }
        }

        private sealed class BackupAppSettingRecord
        {
            public string Key { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        private sealed class BackupFavoriteRecord
        {
            public int ProfileId { get; set; }
            public FavoriteType ContentType { get; set; }
            public BackupContentLocator Locator { get; set; } = new();
        }

        private sealed class BackupWatchStateRecord
        {
            public int ProfileId { get; set; }
            public PlaybackContentType ContentType { get; set; }
            public long PositionMs { get; set; }
            public long DurationMs { get; set; }
            public bool IsCompleted { get; set; }
            public WatchStateOverride WatchStateOverride { get; set; }
            public DateTime LastWatched { get; set; }
            public DateTime? CompletedAtUtc { get; set; }
            public BackupContentLocator Locator { get; set; } = new();
        }

        private sealed class BackupContentLocator
        {
            public int SourceProfileId { get; set; }
            public string Title { get; set; } = string.Empty;
            public string ExternalId { get; set; } = string.Empty;
            public string StreamUrl { get; set; } = string.Empty;
            public string EpgChannelId { get; set; } = string.Empty;
            public string CanonicalTitleKey { get; set; } = string.Empty;
            public string DedupFingerprint { get; set; } = string.Empty;
            public string SeriesTitle { get; set; } = string.Empty;
            public string SeriesExternalId { get; set; } = string.Empty;
            public string SeriesCanonicalTitleKey { get; set; } = string.Empty;
            public string SeriesDedupFingerprint { get; set; } = string.Empty;
            public int SeasonNumber { get; set; }
            public int EpisodeNumber { get; set; }
        }
    }
}
