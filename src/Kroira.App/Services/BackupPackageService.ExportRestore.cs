#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Services
{
    public sealed partial class BackupPackageService
    {
        private async Task<BackupExportResult> ExportPackageAsync(string filePath)
        {
            var fullPath = NormalizeBackupPath(filePath);
            BackupRuntimeLogger.Log("BACKUP EXPORT", $"export start path='{fullPath}'");

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            BackupRuntimeLogger.Log("BACKUP EXPORT", "snapshot load start");
            var snapshot = await LoadCatalogLocatorSnapshotAsync(db);
            BackupRuntimeLogger.Log("BACKUP EXPORT", $"snapshot load end channels={snapshot.Channels.Count} movies={snapshot.Movies.Count} series={snapshot.Series.Count} episodes={snapshot.Episodes.Count}");

            BackupRuntimeLogger.Log("BACKUP EXPORT", "package projection start");
            var package = new BackupPackage
            {
                ExportedAtUtc = DateTime.UtcNow,
                AppVersion = GetAppVersion(),
                Sources = await ExportSourcesAsync(db),
                Profiles = await db.AppProfiles
                    .AsNoTracking()
                    .OrderBy(profile => profile.Id)
                    .Select(profile => new BackupAppProfileRecord
                    {
                        Id = profile.Id,
                        Name = profile.Name,
                        IsKidsProfile = profile.IsKidsProfile,
                        CreatedAtUtc = NormalizeUtc(profile.CreatedAtUtc)
                    })
                    .ToListAsync(),
                ParentalControls = await db.ParentalControlSettings
                    .AsNoTracking()
                    .OrderBy(setting => setting.ProfileId)
                    .Select(setting => new BackupParentalControlRecord
                    {
                        ProfileId = setting.ProfileId,
                        PinHash = setting.PinHash,
                        LockedCategoryIdsJson = setting.LockedCategoryIdsJson,
                        LockedSourceIdsJson = setting.LockedSourceIdsJson,
                        IsKidsSafeMode = setting.IsKidsSafeMode,
                        HideLockedContent = setting.HideLockedContent
                    })
                    .ToListAsync(),
                Settings = (await db.AppSettings
                        .AsNoTracking()
                        .OrderBy(setting => setting.Id)
                        .ToListAsync())
                    .GroupBy(setting => setting.Key, StringComparer.Ordinal)
                    .Select(group => group.Last())
                    .OrderBy(setting => setting.Key, StringComparer.Ordinal)
                    .Select(setting => new BackupAppSettingRecord
                    {
                        Key = setting.Key,
                        Value = setting.Value
                    })
                    .ToList(),
                Favorites = await ExportFavoritesAsync(db, snapshot),
                WatchStates = await ExportWatchStatesAsync(db, snapshot)
            };
            BackupRuntimeLogger.Log("BACKUP EXPORT", $"package projection end sources={package.Sources.Count} profiles={package.Profiles.Count} settings={package.Settings.Count} favorites={package.Favorites.Count} watch={package.WatchStates.Count}");

            ValidatePackage(package);
            BackupRuntimeLogger.Log("BACKUP EXPORT", "package validation end");

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            BackupRuntimeLogger.Log("BACKUP EXPORT", "serialization start");
            var json = JsonSerializer.Serialize(package, JsonOptions);
            var byteCount = Encoding.UTF8.GetByteCount(json);
            BackupRuntimeLogger.Log("BACKUP EXPORT", $"serialization end bytes={byteCount}");
            if (byteCount > MaxPackageSizeBytes)
            {
                throw new InvalidOperationException($"Backup package is too large ({byteCount / 1024 / 1024.0:0.0} MB).");
            }

            BackupRuntimeLogger.Log("BACKUP EXPORT", "file write start");
            await File.WriteAllTextAsync(fullPath, json, Encoding.UTF8);
            BackupRuntimeLogger.Log("BACKUP EXPORT", "file write end");

            BackupRuntimeLogger.Log("BACKUP EXPORT", "export completed");
            return new BackupExportResult
            {
                FilePath = fullPath,
                SourceCount = package.Sources.Count,
                ProfileCount = package.Profiles.Count,
                FavoriteCount = package.Favorites.Count,
                WatchStateCount = package.WatchStates.Count
            };
        }

        private async Task<BackupRestoreResult> RestorePackageAsync(string filePath)
        {
            var fullPath = NormalizeBackupPath(filePath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Backup package was not found.", fullPath);
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length <= 0)
            {
                throw new InvalidDataException("Backup package is empty.");
            }

            if (fileInfo.Length > MaxPackageSizeBytes)
            {
                throw new InvalidDataException("Backup package exceeds the supported size limit.");
            }

            BackupPackage package;
            await using (var stream = File.OpenRead(fullPath))
            {
                package = await JsonSerializer.DeserializeAsync<BackupPackage>(stream, JsonOptions)
                    ?? throw new InvalidDataException("Backup package could not be read.");
            }

            ValidatePackage(package);

            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await RestoreBaseStateAsync(db, package);
            }

            var warnings = new List<string>();
            var sourceSyncFailureCount = 0;

            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var m3uParser = scope.ServiceProvider.GetRequiredService<IM3uParserService>();
                var xtreamParser = scope.ServiceProvider.GetRequiredService<IXtreamParserService>();
                var xmltvParser = scope.ServiceProvider.GetRequiredService<IXmltvParserService>();
                sourceSyncFailureCount = await ReimportRestoredSourcesAsync(
                    db,
                    package.Sources,
                    m3uParser,
                    xtreamParser,
                    xmltvParser,
                    warnings);
            }

            var favoriteCount = 0;
            var favoriteSkippedCount = 0;
            var watchStateCount = 0;
            var watchStateSkippedCount = 0;

            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var remapResult = await RestoreMappedStateAsync(db, package);
                favoriteCount = remapResult.FavoriteCount;
                favoriteSkippedCount = remapResult.FavoriteSkippedCount;
                watchStateCount = remapResult.WatchStateCount;
                watchStateSkippedCount = remapResult.WatchStateSkippedCount;
            }

            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
                var profiles = await profileService.GetProfilesAsync(db);
                foreach (var profile in profiles)
                {
                    profileService.RelockProfile(profile.Id);
                }

                var activeProfileId = await profileService.GetActiveProfileIdAsync(db);
                await profileService.SwitchProfileAsync(db, activeProfileId);
            }

            if (favoriteSkippedCount > 0)
            {
                warnings.Add($"{favoriteSkippedCount} favorites could not be remapped after restore.");
            }

            if (watchStateSkippedCount > 0)
            {
                warnings.Add($"{watchStateSkippedCount} watch-state records could not be remapped after restore.");
            }

            return new BackupRestoreResult
            {
                SourceCount = package.Sources.Count,
                ProfileCount = package.Profiles.Count,
                FavoriteCount = favoriteCount,
                FavoriteSkippedCount = favoriteSkippedCount,
                WatchStateCount = watchStateCount,
                WatchStateSkippedCount = watchStateSkippedCount,
                SourceSyncFailureCount = sourceSyncFailureCount,
                Warnings = warnings
            };
        }

        private static async Task<List<BackupSourceRecord>> ExportSourcesAsync(AppDbContext db)
        {
            var profiles = await db.SourceProfiles
                .AsNoTracking()
                .OrderBy(profile => profile.Id)
                .ToListAsync();

            var credentials = await db.SourceCredentials
                .AsNoTracking()
                .ToDictionaryAsync(item => item.SourceProfileId);

            var syncStates = await db.SourceSyncStates
                .AsNoTracking()
                .ToDictionaryAsync(item => item.SourceProfileId);

            return profiles.Select(profile =>
            {
                credentials.TryGetValue(profile.Id, out var credential);
                syncStates.TryGetValue(profile.Id, out var syncState);

                return new BackupSourceRecord
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    Type = profile.Type,
                    LastSync = profile.LastSync.HasValue ? NormalizeUtc(profile.LastSync.Value) : null,
                    Credential = credential == null
                        ? null
                        : new BackupSourceCredentialRecord
                        {
                            Url = credential.Url,
                            Username = credential.Username,
                            Password = credential.Password,
                            EpgUrl = credential.EpgUrl,
                            DetectedEpgUrl = credential.DetectedEpgUrl,
                            EpgMode = credential.EpgMode,
                            M3uImportMode = credential.M3uImportMode
                        },
                    SyncState = syncState == null
                        ? null
                        : new BackupSourceSyncStateRecord
                        {
                            LastAttempt = NormalizeUtc(syncState.LastAttempt),
                            HttpStatusCode = syncState.HttpStatusCode,
                            ErrorLog = syncState.ErrorLog
                        }
                };
            }).ToList();
        }

        private static async Task<List<BackupFavoriteRecord>> ExportFavoritesAsync(AppDbContext db, CatalogLocatorSnapshot snapshot)
        {
            var favorites = await db.Favorites
                .AsNoTracking()
                .OrderBy(item => item.ProfileId)
                .ThenBy(item => item.ContentType)
                .ThenBy(item => item.ContentId)
                .ToListAsync();

            var exported = new List<BackupFavoriteRecord>(favorites.Count);
            foreach (var favorite in favorites)
            {
                var locator = favorite.ContentType switch
                {
                    FavoriteType.Channel when snapshot.Channels.TryGetValue(favorite.ContentId, out var channel) => BuildChannelLocator(channel),
                    FavoriteType.Movie when snapshot.Movies.TryGetValue(favorite.ContentId, out var movie) => BuildMovieLocator(movie),
                    FavoriteType.Series when snapshot.Series.TryGetValue(favorite.ContentId, out var series) => BuildSeriesLocator(series),
                    _ => null
                };

                if (locator == null)
                {
                    continue;
                }

                exported.Add(new BackupFavoriteRecord
                {
                    ProfileId = favorite.ProfileId,
                    ContentType = favorite.ContentType,
                    Locator = locator
                });
            }

            return exported;
        }

        private static async Task<List<BackupWatchStateRecord>> ExportWatchStatesAsync(AppDbContext db, CatalogLocatorSnapshot snapshot)
        {
            var progressRows = await db.PlaybackProgresses
                .AsNoTracking()
                .OrderBy(item => item.ProfileId)
                .ThenBy(item => item.ContentType)
                .ThenBy(item => item.ContentId)
                .ToListAsync();

            var exported = new List<BackupWatchStateRecord>(progressRows.Count);
            foreach (var progress in progressRows)
            {
                var locator = progress.ContentType switch
                {
                    PlaybackContentType.Channel when snapshot.Channels.TryGetValue(progress.ContentId, out var channel) => BuildChannelLocator(channel),
                    PlaybackContentType.Movie when snapshot.Movies.TryGetValue(progress.ContentId, out var movie) => BuildMovieLocator(movie),
                    PlaybackContentType.Episode when snapshot.Episodes.TryGetValue(progress.ContentId, out var episode) => BuildEpisodeLocator(episode),
                    _ => null
                };

                if (locator == null)
                {
                    continue;
                }

                exported.Add(new BackupWatchStateRecord
                {
                    ProfileId = progress.ProfileId,
                    ContentType = progress.ContentType,
                    PositionMs = Math.Max(0, progress.PositionMs),
                    DurationMs = Math.Max(0, progress.DurationMs),
                    IsCompleted = progress.IsCompleted,
                    WatchStateOverride = progress.WatchStateOverride,
                    LastWatched = NormalizeUtc(progress.LastWatched),
                    CompletedAtUtc = progress.CompletedAtUtc.HasValue ? NormalizeUtc(progress.CompletedAtUtc.Value) : null,
                    Locator = locator
                });
            }

            return exported;
        }

        private static void ValidatePackage(BackupPackage package)
        {
            if (!string.Equals(package.PackageType, PackageType, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Unsupported backup package type.");
            }

            if (package.SchemaVersion != PackageSchemaVersion)
            {
                throw new InvalidDataException($"Unsupported backup schema version {package.SchemaVersion}.");
            }

            if (package.Sources.Count > 128 || package.Profiles.Count > 32 || package.Settings.Count > 20000 ||
                package.Favorites.Count > 50000 || package.WatchStates.Count > 50000)
            {
                throw new InvalidDataException("Backup package exceeds supported record limits.");
            }

            if (package.Profiles.Count == 0)
            {
                throw new InvalidDataException("Backup package does not contain any profiles.");
            }

            var sourceIds = new HashSet<int>();
            foreach (var source in package.Sources)
            {
                if (source.Id <= 0 || !sourceIds.Add(source.Id))
                {
                    throw new InvalidDataException("Backup package contains invalid or duplicate source ids.");
                }

                if (string.IsNullOrWhiteSpace(source.Name))
                {
                    throw new InvalidDataException("Backup package contains a source with no name.");
                }

                if (source.Credential == null || string.IsNullOrWhiteSpace(source.Credential.Url))
                {
                    throw new InvalidDataException($"Source '{source.Name}' is missing credentials.");
                }

                if (source.Type == SourceType.Xtream &&
                    string.IsNullOrWhiteSpace(source.Credential.Username))
                {
                    throw new InvalidDataException($"Xtream source '{source.Name}' is missing a username.");
                }
            }

            var profileIds = new HashSet<int>();
            foreach (var profile in package.Profiles)
            {
                if (profile.Id <= 0 || !profileIds.Add(profile.Id))
                {
                    throw new InvalidDataException("Backup package contains invalid or duplicate profile ids.");
                }

                if (string.IsNullOrWhiteSpace(profile.Name))
                {
                    throw new InvalidDataException("Backup package contains a profile with no name.");
                }
            }

            var settingKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setting in package.Settings)
            {
                if (string.IsNullOrWhiteSpace(setting.Key) || !settingKeys.Add(setting.Key))
                {
                    throw new InvalidDataException("Backup package contains invalid or duplicate settings.");
                }
            }

            foreach (var control in package.ParentalControls)
            {
                if (!profileIds.Contains(control.ProfileId))
                {
                    throw new InvalidDataException("Backup package contains parental controls for an unknown profile.");
                }
            }

            foreach (var favorite in package.Favorites)
            {
                if (!profileIds.Contains(favorite.ProfileId) || favorite.Locator == null)
                {
                    throw new InvalidDataException("Backup package contains an invalid favorite record.");
                }
            }

            foreach (var watchState in package.WatchStates)
            {
                if (!profileIds.Contains(watchState.ProfileId) || watchState.Locator == null)
                {
                    throw new InvalidDataException("Backup package contains an invalid watch-state record.");
                }
            }
        }

        private static async Task RestoreBaseStateAsync(AppDbContext db, BackupPackage package)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await ClearPackageManagedStateAsync(db);
            await RestoreProfilesAsync(db, package);
            await RestoreSourcesAsync(db, package.Sources);
            await RestoreSettingsAsync(db, package.Settings);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            db.ChangeTracker.Clear();
        }

        private static async Task ClearPackageManagedStateAsync(AppDbContext db)
        {
            await db.EpgPrograms.ExecuteDeleteAsync();
            await db.EpgSyncLogs.ExecuteDeleteAsync();
            await db.Favorites.ExecuteDeleteAsync();
            await db.PlaybackProgresses.ExecuteDeleteAsync();
            await db.Episodes.ExecuteDeleteAsync();
            await db.Seasons.ExecuteDeleteAsync();
            await db.Series.ExecuteDeleteAsync();
            await db.Movies.ExecuteDeleteAsync();
            await db.Channels.ExecuteDeleteAsync();
            await db.ChannelCategories.ExecuteDeleteAsync();
            await db.ParentalControlSettings.ExecuteDeleteAsync();
            await db.SourceSyncStates.ExecuteDeleteAsync();
            await db.SourceCredentials.ExecuteDeleteAsync();
            await db.SourceProfiles.ExecuteDeleteAsync();
            await db.AppSettings.ExecuteDeleteAsync();
            await db.AppProfiles.ExecuteDeleteAsync();
        }

        private static Task RestoreProfilesAsync(AppDbContext db, BackupPackage package)
        {
            db.AppProfiles.AddRange(package.Profiles.Select(profile => new AppProfile
            {
                Id = profile.Id,
                Name = profile.Name,
                IsKidsProfile = profile.IsKidsProfile,
                CreatedAtUtc = NormalizeUtc(profile.CreatedAtUtc)
            }));

            db.ParentalControlSettings.AddRange(package.ParentalControls.Select(control => new ParentalControlSetting
            {
                ProfileId = control.ProfileId,
                PinHash = control.PinHash ?? string.Empty,
                LockedCategoryIdsJson = control.LockedCategoryIdsJson ?? string.Empty,
                LockedSourceIdsJson = control.LockedSourceIdsJson ?? string.Empty,
                IsKidsSafeMode = control.IsKidsSafeMode,
                HideLockedContent = control.HideLockedContent
            }));

            var missingControlIds = package.Profiles
                .Select(profile => profile.Id)
                .Except(package.ParentalControls.Select(control => control.ProfileId))
                .ToList();

            foreach (var profileId in missingControlIds)
            {
                db.ParentalControlSettings.Add(new ParentalControlSetting
                {
                    ProfileId = profileId,
                    HideLockedContent = true
                });
            }

            return Task.CompletedTask;
        }

        private static Task RestoreSourcesAsync(AppDbContext db, IReadOnlyList<BackupSourceRecord> sources)
        {
            db.SourceProfiles.AddRange(sources.Select(source => new SourceProfile
            {
                Id = source.Id,
                Name = source.Name,
                Type = source.Type,
                LastSync = source.LastSync.HasValue ? NormalizeUtc(source.LastSync.Value) : null
            }));

            db.SourceCredentials.AddRange(sources.Select(source => new SourceCredential
            {
                SourceProfileId = source.Id,
                Url = source.Credential?.Url ?? string.Empty,
                Username = source.Credential?.Username ?? string.Empty,
                Password = source.Credential?.Password ?? string.Empty,
                EpgUrl = source.Credential?.EpgUrl ?? string.Empty,
                DetectedEpgUrl = source.Credential?.DetectedEpgUrl ?? string.Empty,
                EpgMode = source.Credential?.EpgMode ?? (string.IsNullOrWhiteSpace(source.Credential?.EpgUrl) ? EpgActiveMode.Detected : EpgActiveMode.Manual),
                M3uImportMode = source.Credential?.M3uImportMode ?? M3uImportMode.LiveMoviesAndSeries
            }));

            db.SourceSyncStates.AddRange(sources.Select(source => new SourceSyncState
            {
                SourceProfileId = source.Id,
                LastAttempt = source.SyncState?.LastAttempt is DateTime lastAttempt
                    ? NormalizeUtc(lastAttempt)
                    : DateTime.UtcNow,
                HttpStatusCode = source.SyncState?.HttpStatusCode ?? 0,
                ErrorLog = source.SyncState?.ErrorLog ?? string.Empty
            }));

            return Task.CompletedTask;
        }

        private static Task RestoreSettingsAsync(AppDbContext db, IReadOnlyList<BackupAppSettingRecord> settings)
        {
            db.AppSettings.AddRange(settings.Select(setting => new AppSetting
            {
                Key = setting.Key,
                Value = setting.Value ?? string.Empty
            }));

            return Task.CompletedTask;
        }

        private static async Task<int> ReimportRestoredSourcesAsync(
            AppDbContext db,
            IReadOnlyList<BackupSourceRecord> sources,
            IM3uParserService m3uParser,
            IXtreamParserService xtreamParser,
            IXmltvParserService xmltvParser,
            ICollection<string> warnings)
        {
            var failureCount = 0;

            foreach (var source in sources.OrderBy(item => item.Id))
            {
                try
                {
                    if (source.Type == SourceType.M3U)
                    {
                        await m3uParser.ParseAndImportM3uAsync(db, source.Id);
                    }
                    else
                    {
                        await xtreamParser.ParseAndImportXtreamAsync(db, source.Id);
                        await xtreamParser.ParseAndImportXtreamVodAsync(db, source.Id);
                    }
                }
                catch (Exception ex)
                {
                    failureCount++;
                    warnings.Add($"Source '{source.Name}' could not be re-imported: {ex.Message}");
                    continue;
                }

                try
                {
                    await xmltvParser.ParseAndImportEpgAsync(db, source.Id);
                }
                catch (Exception ex)
                {
                    warnings.Add($"EPG sync for '{source.Name}' did not complete: {ex.Message}");
                }
            }

            return failureCount;
        }

        private static async Task<(int FavoriteCount, int FavoriteSkippedCount, int WatchStateCount, int WatchStateSkippedCount)>
            RestoreMappedStateAsync(AppDbContext db, BackupPackage package)
        {
            var snapshot = await LoadCatalogLocatorSnapshotAsync(db);

            var favoriteRows = new List<Favorite>();
            var favoriteKeys = new HashSet<(int ProfileId, FavoriteType ContentType, int ContentId)>();
            var favoriteSkipped = 0;

            foreach (var favorite in package.Favorites)
            {
                var contentId = favorite.ContentType switch
                {
                    FavoriteType.Channel => ResolveChannelId(snapshot, favorite.Locator),
                    FavoriteType.Movie => ResolveMovieId(snapshot, favorite.Locator),
                    FavoriteType.Series => ResolveSeriesId(snapshot, favorite.Locator),
                    _ => 0
                };

                if (contentId <= 0)
                {
                    favoriteSkipped++;
                    continue;
                }

                if (!favoriteKeys.Add((favorite.ProfileId, favorite.ContentType, contentId)))
                {
                    continue;
                }

                favoriteRows.Add(new Favorite
                {
                    ProfileId = favorite.ProfileId,
                    ContentType = favorite.ContentType,
                    ContentId = contentId
                });
            }

            var watchRows = new List<PlaybackProgress>();
            var watchKeys = new HashSet<(int ProfileId, PlaybackContentType ContentType, int ContentId)>();
            var watchSkipped = 0;

            foreach (var watchState in package.WatchStates
                .OrderByDescending(item => NormalizeUtc(item.LastWatched)))
            {
                var contentId = watchState.ContentType switch
                {
                    PlaybackContentType.Channel => ResolveChannelId(snapshot, watchState.Locator),
                    PlaybackContentType.Movie => ResolveMovieId(snapshot, watchState.Locator),
                    PlaybackContentType.Episode => ResolveEpisodeId(snapshot, watchState.Locator),
                    _ => 0
                };

                if (contentId <= 0)
                {
                    watchSkipped++;
                    continue;
                }

                if (!watchKeys.Add((watchState.ProfileId, watchState.ContentType, contentId)))
                {
                    continue;
                }

                var durationMs = Math.Max(0, watchState.DurationMs);
                var positionMs = Math.Max(0, watchState.PositionMs);
                if (durationMs > 0 && positionMs > durationMs)
                {
                    positionMs = durationMs;
                }

                watchRows.Add(new PlaybackProgress
                {
                    ProfileId = watchState.ProfileId,
                    ContentType = watchState.ContentType,
                    ContentId = contentId,
                    PositionMs = positionMs,
                    DurationMs = durationMs,
                    IsCompleted = watchState.IsCompleted,
                    WatchStateOverride = watchState.WatchStateOverride,
                    LastWatched = NormalizeUtc(watchState.LastWatched),
                    CompletedAtUtc = watchState.CompletedAtUtc.HasValue ? NormalizeUtc(watchState.CompletedAtUtc.Value) : null
                });
            }

            if (favoriteRows.Count > 0)
            {
                db.Favorites.AddRange(favoriteRows);
            }

            if (watchRows.Count > 0)
            {
                db.PlaybackProgresses.AddRange(watchRows);
            }

            await db.SaveChangesAsync();

            return (favoriteRows.Count, favoriteSkipped, watchRows.Count, watchSkipped);
        }

        private static string NormalizeBackupPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A backup file path is required.", nameof(filePath));
            }

            return Path.GetFullPath(filePath.Trim());
        }

        private static string GetAppVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null
                ? "1.0.0.0"
                : $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }
    }
}
