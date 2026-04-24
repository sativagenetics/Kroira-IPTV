#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Services
{
    public sealed class SourceCreateRequest
    {
        public string Name { get; set; } = string.Empty;
        public SourceType Type { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ManualEpgUrl { get; set; } = string.Empty;
        public string FallbackEpgUrls { get; set; } = string.Empty;
        public EpgActiveMode EpgMode { get; set; } = EpgActiveMode.Detected;
        public M3uImportMode M3uImportMode { get; set; } = M3uImportMode.LiveMoviesAndSeries;
        public SourceProxyScope ProxyScope { get; set; } = SourceProxyScope.Disabled;
        public string ProxyUrl { get; set; } = string.Empty;
        public SourceCompanionScope CompanionScope { get; set; } = SourceCompanionScope.Disabled;
        public SourceCompanionRelayMode CompanionMode { get; set; } = SourceCompanionRelayMode.Buffered;
        public string CompanionUrl { get; set; } = string.Empty;
        public string StalkerMacAddress { get; set; } = string.Empty;
        public string StalkerDeviceId { get; set; } = string.Empty;
        public string StalkerSerialNumber { get; set; } = string.Empty;
        public string StalkerTimezone { get; set; } = string.Empty;
        public string StalkerLocale { get; set; } = string.Empty;
    }

    public sealed class SourceCreateResult
    {
        public bool Success { get; init; }
        public int SourceId { get; init; }
        public string SourceName { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public bool ImportAttempted { get; init; }
        public bool ImportSucceeded { get; init; }
    }

    public sealed class SourceGuideSettingsUpdateRequest
    {
        public int SourceId { get; set; }
        public EpgActiveMode ActiveMode { get; set; } = EpgActiveMode.Detected;
        public string ManualEpgUrl { get; set; } = string.Empty;
        public string FallbackEpgUrls { get; set; } = string.Empty;
        public SourceProxyScope ProxyScope { get; set; } = SourceProxyScope.Disabled;
        public string ProxyUrl { get; set; } = string.Empty;
        public SourceCompanionScope CompanionScope { get; set; } = SourceCompanionScope.Disabled;
        public SourceCompanionRelayMode CompanionMode { get; set; } = SourceCompanionRelayMode.Buffered;
        public string CompanionUrl { get; set; } = string.Empty;
    }

    public sealed class SourceGuideSettingsUpdateResult
    {
        public bool Success { get; init; }
        public bool SyncTriggered { get; init; }
        public bool PreservedExistingGuideData { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public sealed class SourceDeleteResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public string SourceName { get; init; } = string.Empty;
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    public interface ISourceLifecycleService
    {
        Task<SourceCreateResult> CreateSourceAsync(SourceCreateRequest request);
        Task<SourceGuideSettingsUpdateResult> UpdateGuideSettingsAsync(SourceGuideSettingsUpdateRequest request, bool syncNow);
        Task<SourceDeleteResult> DeleteSourceAsync(int sourceProfileId);
    }

    public sealed class SourceLifecycleService : ISourceLifecycleService
    {
        private readonly IServiceProvider _serviceProvider;

        public SourceLifecycleService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task<SourceCreateResult> CreateSourceAsync(SourceCreateRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var refreshService = scope.ServiceProvider.GetRequiredService<ISourceRefreshService>();
            var credentialStore = scope.ServiceProvider.GetRequiredService<ISourceCredentialStore>();

            var normalized = NormalizeCreateRequest(request);
            ValidateCreateRequest(normalized);

            var duplicateHint = await DetectDuplicateHintAsync(db, normalized);
            int sourceId;
            string sourceName;

            using (var transaction = await db.Database.BeginTransactionAsync())
            {
                var profile = new SourceProfile
                {
                    Name = normalized.Name,
                    Type = normalized.Type,
                    LastSync = null
                };

                db.SourceProfiles.Add(profile);
                await db.SaveChangesAsync();

                var credential = new SourceCredential
                {
                    SourceProfileId = profile.Id,
                    Url = normalized.Url,
                    Username = normalized.Username,
                    Password = normalized.Password,
                    ManualEpgUrl = normalized.ManualEpgUrl,
                    FallbackEpgUrls = normalized.FallbackEpgUrls,
                    EpgMode = normalized.EpgMode,
                    M3uImportMode = normalized.M3uImportMode,
                    ProxyScope = normalized.ProxyScope,
                    ProxyUrl = normalized.ProxyUrl,
                    CompanionScope = normalized.CompanionScope,
                    CompanionMode = normalized.CompanionMode,
                    CompanionUrl = normalized.CompanionUrl,
                    StalkerMacAddress = normalized.StalkerMacAddress,
                    StalkerDeviceId = normalized.StalkerDeviceId,
                    StalkerSerialNumber = normalized.StalkerSerialNumber,
                    StalkerTimezone = normalized.StalkerTimezone,
                    StalkerLocale = normalized.StalkerLocale
                };
                db.SourceCredentials.Add(credential);

                db.SourceSyncStates.Add(new SourceSyncState
                {
                    SourceProfileId = profile.Id,
                    LastAttempt = DateTime.UtcNow,
                    HttpStatusCode = 0,
                    ErrorLog = string.Empty
                });

                await db.SaveChangesAsync();
                await credentialStore.ProtectCredentialAsync(db, credential);
                await transaction.CommitAsync();

                sourceId = profile.Id;
                sourceName = profile.Name;
            }

            try
            {
                var refreshResult = await refreshService.RefreshSourceAsync(
                    sourceId,
                    SourceRefreshTrigger.InitialImport,
                    SourceRefreshScope.Full);

                var message = refreshResult.Success
                    ? refreshResult.Message
                    : $"Source saved, but import failed: {refreshResult.Message}";

                if (!string.IsNullOrWhiteSpace(duplicateHint))
                {
                    message = $"{message} {duplicateHint}";
                }

                return new SourceCreateResult
                {
                    Success = true,
                    SourceId = sourceId,
                    SourceName = sourceName,
                    Message = message,
                    ImportAttempted = true,
                    ImportSucceeded = refreshResult.Success
                };
            }
            catch (Exception ex)
            {
                var message = $"Source saved, but import failed: {ex.Message}";
                if (!string.IsNullOrWhiteSpace(duplicateHint))
                {
                    message = $"{message} {duplicateHint}";
                }

                return new SourceCreateResult
                {
                    Success = true,
                    SourceId = sourceId,
                    SourceName = sourceName,
                    Message = message,
                    ImportAttempted = true,
                    ImportSucceeded = false
                };
            }
        }

        public async Task<SourceGuideSettingsUpdateResult> UpdateGuideSettingsAsync(SourceGuideSettingsUpdateRequest request, bool syncNow)
        {
            ArgumentNullException.ThrowIfNull(request);

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var refreshService = scope.ServiceProvider.GetRequiredService<ISourceRefreshService>();
            var sourceHealthService = scope.ServiceProvider.GetRequiredService<ISourceHealthService>();
            var credentialStore = scope.ServiceProvider.GetRequiredService<ISourceCredentialStore>();

            var credential = await credentialStore.GetCredentialAsync(db, request.SourceId);
            if (credential == null)
            {
                throw new InvalidOperationException("Source credentials were not found.");
            }

            var normalized = NormalizeGuideRequest(request);
            ValidateGuideRequest(normalized);

            var previousMode = credential.EpgMode;
            var previousManualUrl = credential.ManualEpgUrl ?? string.Empty;
            var previousFallbackUrls = credential.FallbackEpgUrls ?? string.Empty;
            var previousProxyScope = credential.ProxyScope;
            var previousProxyUrl = credential.ProxyUrl ?? string.Empty;
            var previousCompanionScope = credential.CompanionScope;
            var previousCompanionMode = credential.CompanionMode;
            var previousCompanionUrl = credential.CompanionUrl ?? string.Empty;

            credential.EpgMode = normalized.ActiveMode;
            credential.ManualEpgUrl = normalized.ManualEpgUrl;
            credential.FallbackEpgUrls = normalized.FallbackEpgUrls;
            credential.ProxyScope = normalized.ProxyScope;
            credential.ProxyUrl = normalized.ProxyUrl;
            credential.CompanionScope = normalized.CompanionScope;
            credential.CompanionMode = normalized.CompanionMode;
            credential.CompanionUrl = normalized.CompanionUrl;
            await db.SaveChangesAsync();
            await credentialStore.ProtectCredentialAsync(db, credential);

            var guideBindingChanged = previousMode != credential.EpgMode ||
                                      !string.Equals(previousManualUrl, credential.ManualEpgUrl, StringComparison.OrdinalIgnoreCase) ||
                                      !string.Equals(previousFallbackUrls, credential.FallbackEpgUrls, StringComparison.OrdinalIgnoreCase);
            var routingChanged = previousProxyScope != credential.ProxyScope ||
                                 !string.Equals(previousProxyUrl, credential.ProxyUrl, StringComparison.OrdinalIgnoreCase) ||
                                 previousCompanionScope != credential.CompanionScope ||
                                 previousCompanionMode != credential.CompanionMode ||
                                 !string.Equals(previousCompanionUrl, credential.CompanionUrl, StringComparison.OrdinalIgnoreCase);

            if (syncNow || normalized.ActiveMode == EpgActiveMode.None)
            {
                var result = await refreshService.RefreshSourceAsync(
                    request.SourceId,
                    SourceRefreshTrigger.Manual,
                    SourceRefreshScope.EpgOnly);

                return new SourceGuideSettingsUpdateResult
                {
                    Success = result.Success,
                    SyncTriggered = true,
                    PreservedExistingGuideData = result.Success || normalized.ActiveMode != EpgActiveMode.None,
                    Message = result.Success ? result.Message : result.Message
                };
            }

            if (!guideBindingChanged && !routingChanged)
            {
                return new SourceGuideSettingsUpdateResult
                {
                    Success = true,
                    SyncTriggered = false,
                    PreservedExistingGuideData = false,
                    Message = "Guide settings were unchanged."
                };
            }

            var preservedGuideData = false;
            var message = routingChanged && !guideBindingChanged
                ? "Routing saved. Existing guide data is unchanged until the next sync."
                : "Guide settings saved. Existing guide data is still available until the next sync.";

            if (guideBindingChanged)
            {
                preservedGuideData = await MarkGuideSettingsPendingAsync(db, request.SourceId, credential);
            }

            await sourceHealthService.RefreshSourceHealthAsync(db, request.SourceId);

            return new SourceGuideSettingsUpdateResult
            {
                Success = true,
                SyncTriggered = false,
                PreservedExistingGuideData = preservedGuideData,
                Message = message
            };
        }

        public async Task<SourceDeleteResult> DeleteSourceAsync(int sourceProfileId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var browsePreferencesService = scope.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
            var logicalCatalogStateService = scope.ServiceProvider.GetRequiredService<ILogicalCatalogStateService>();
            var contentOperationalService = scope.ServiceProvider.GetRequiredService<IContentOperationalService>();
            var autoRefreshService = scope.ServiceProvider.GetRequiredService<ISourceAutoRefreshService>();

            var profile = await db.SourceProfiles.FirstOrDefaultAsync(item => item.Id == sourceProfileId);
            if (profile == null)
            {
                return new SourceDeleteResult
                {
                    Success = false,
                    Message = "Source not found."
                };
            }

            var warnings = new List<string>();
            using (var transaction = await db.Database.BeginTransactionAsync())
            {
                var categoryIds = await db.ChannelCategories
                    .Where(category => category.SourceProfileId == sourceProfileId)
                    .Select(category => category.Id)
                    .ToListAsync();
                var channelIds = categoryIds.Count == 0
                    ? new List<int>()
                    : await db.Channels
                        .Where(channel => categoryIds.Contains(channel.ChannelCategoryId))
                        .Select(channel => channel.Id)
                        .ToListAsync();

                var epgMappingDecisions = await db.EpgMappingDecisions
                    .Where(decision => decision.SourceProfileId == sourceProfileId ||
                                       channelIds.Contains(decision.ChannelId))
                    .ToListAsync();
                if (epgMappingDecisions.Count > 0)
                {
                    db.EpgMappingDecisions.RemoveRange(epgMappingDecisions);
                }

                if (channelIds.Count > 0)
                {
                    var epgPrograms = await db.EpgPrograms
                        .Where(program => channelIds.Contains(program.ChannelId))
                        .ToListAsync();
                    if (epgPrograms.Count > 0)
                    {
                        db.EpgPrograms.RemoveRange(epgPrograms);
                    }

                    var channels = await db.Channels
                        .Where(channel => channelIds.Contains(channel.Id))
                        .ToListAsync();
                    db.Channels.RemoveRange(channels);
                }

                if (categoryIds.Count > 0)
                {
                    var categories = await db.ChannelCategories
                        .Where(category => categoryIds.Contains(category.Id))
                        .ToListAsync();
                    db.ChannelCategories.RemoveRange(categories);
                }

                var existingSeries = await db.Series
                    .Include(series => series.Seasons!)
                    .ThenInclude(season => season.Episodes!)
                    .Where(series => series.SourceProfileId == sourceProfileId)
                    .ToListAsync();
                foreach (var series in existingSeries)
                {
                    if (series.Seasons == null)
                    {
                        continue;
                    }

                    foreach (var season in series.Seasons)
                    {
                        if (season.Episodes != null && season.Episodes.Count > 0)
                        {
                            db.Episodes.RemoveRange(season.Episodes);
                        }
                    }

                    db.Seasons.RemoveRange(series.Seasons);
                }

                if (existingSeries.Count > 0)
                {
                    db.Series.RemoveRange(existingSeries);
                }

                var movies = await db.Movies
                    .Where(movie => movie.SourceProfileId == sourceProfileId)
                    .ToListAsync();
                if (movies.Count > 0)
                {
                    db.Movies.RemoveRange(movies);
                }

                var credentials = await db.SourceCredentials
                    .Where(credential => credential.SourceProfileId == sourceProfileId)
                    .ToListAsync();
                if (credentials.Count > 0)
                {
                    db.SourceCredentials.RemoveRange(credentials);
                }

                var protectedCredentials = await db.SourceProtectedCredentialSecrets
                    .Where(secret => secret.SourceProfileId == sourceProfileId)
                    .ToListAsync();
                if (protectedCredentials.Count > 0)
                {
                    db.SourceProtectedCredentialSecrets.RemoveRange(protectedCredentials);
                }

                var syncStates = await db.SourceSyncStates
                    .Where(state => state.SourceProfileId == sourceProfileId)
                    .ToListAsync();
                if (syncStates.Count > 0)
                {
                db.SourceSyncStates.RemoveRange(syncStates);
                }

                var stalkerSnapshots = await db.StalkerPortalSnapshots
                    .Where(item => item.SourceProfileId == sourceProfileId)
                    .ToListAsync();
                if (stalkerSnapshots.Count > 0)
                {
                    db.StalkerPortalSnapshots.RemoveRange(stalkerSnapshots);
                }

                var epgLogs = await db.EpgSyncLogs
                    .Where(log => log.SourceProfileId == sourceProfileId)
                    .ToListAsync();
                if (epgLogs.Count > 0)
                {
                    db.EpgSyncLogs.RemoveRange(epgLogs);
                }

                var healthReports = await db.SourceHealthReports
                    .Include(report => report.Components)
                    .Include(report => report.Probes)
                    .Include(report => report.Issues)
                    .Where(report => report.SourceProfileId == sourceProfileId)
                    .ToListAsync();
                foreach (var report in healthReports)
                {
                    if (report.Components.Count > 0)
                    {
                        db.SourceHealthComponents.RemoveRange(report.Components);
                    }

                    if (report.Probes.Count > 0)
                    {
                        db.SourceHealthProbes.RemoveRange(report.Probes);
                    }

                    if (report.Issues.Count > 0)
                    {
                        db.SourceHealthIssues.RemoveRange(report.Issues);
                    }
                }

                if (healthReports.Count > 0)
                {
                    db.SourceHealthReports.RemoveRange(healthReports);
                }

                var enrichmentRecords = await db.SourceChannelEnrichmentRecords
                    .Where(record => record.SourceProfileId == sourceProfileId)
                    .ToListAsync();
                if (enrichmentRecords.Count > 0)
                {
                    db.SourceChannelEnrichmentRecords.RemoveRange(enrichmentRecords);
                }

                var operationalStates = await db.LogicalOperationalStates
                    .Include(state => state.Candidates)
                    .Where(state => state.Candidates.Any(candidate => candidate.SourceProfileId == sourceProfileId))
                    .ToListAsync();
                foreach (var state in operationalStates)
                {
                    var doomedCandidates = state.Candidates
                        .Where(candidate => candidate.SourceProfileId == sourceProfileId)
                        .ToList();
                    if (doomedCandidates.Count > 0)
                    {
                        db.LogicalOperationalCandidates.RemoveRange(doomedCandidates);
                    }

                    if (state.Candidates.Count == doomedCandidates.Count)
                    {
                        db.LogicalOperationalStates.Remove(state);
                    }
                }

                db.SourceProfiles.Remove(profile);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            db.ChangeTracker.Clear();

            await TryPostDeleteRepairAsync(
                warnings,
                "browse references",
                () => browsePreferencesService.RepairSourceReferencesAsync(db, sourceProfileId),
                sourceProfileId);
            await TryPostDeleteRepairAsync(
                warnings,
                "logical state reconciliation",
                () => logicalCatalogStateService.ReconcilePersistentStateAsync(db),
                sourceProfileId);
            await TryPostDeleteRepairAsync(
                warnings,
                "operational mirror rebuild",
                () => contentOperationalService.RefreshOperationalStateAsync(db),
                sourceProfileId);
            await TryPostDeleteRepairAsync(
                warnings,
                "auto-refresh runtime repair",
                () => autoRefreshService.RepairRuntimeStateAsync(db),
                sourceProfileId);

            var message = warnings.Count == 0
                ? $"Deleted source '{profile.Name}'."
                : $"Deleted source '{profile.Name}'. Some cleanup was deferred.";

            return new SourceDeleteResult
            {
                Success = true,
                Message = message,
                SourceName = profile.Name,
                Warnings = warnings
            };
        }

        private static SourceCreateRequest NormalizeCreateRequest(SourceCreateRequest request)
        {
            var url = NormalizeUrl(request.Url, request.Type);
            var resolvedName = string.IsNullOrWhiteSpace(request.Name)
                ? DeriveSourceName(request.Type, url)
                : request.Name.Trim();

            return new SourceCreateRequest
            {
                Name = resolvedName,
                Type = request.Type,
                Url = url,
                Username = request.Type == SourceType.Xtream ? (request.Username?.Trim() ?? string.Empty) : string.Empty,
                Password = request.Type == SourceType.Xtream ? (request.Password ?? string.Empty) : string.Empty,
                ManualEpgUrl = NormalizeOptionalUrl(request.ManualEpgUrl),
                FallbackEpgUrls = NormalizeGuideUrlList(request.FallbackEpgUrls),
                EpgMode = request.EpgMode,
                M3uImportMode = request.M3uImportMode,
                ProxyScope = request.ProxyScope,
                ProxyUrl = NormalizeOptionalUrl(request.ProxyUrl),
                CompanionScope = request.CompanionScope,
                CompanionMode = request.CompanionMode,
                CompanionUrl = NormalizeCompanionUrl(request.CompanionUrl),
                StalkerMacAddress = request.Type == SourceType.Stalker
                    ? NormalizeMacAddress(request.StalkerMacAddress)
                    : string.Empty,
                StalkerDeviceId = request.Type == SourceType.Stalker
                    ? NormalizeOptionalToken(request.StalkerDeviceId)
                    : string.Empty,
                StalkerSerialNumber = request.Type == SourceType.Stalker
                    ? NormalizeOptionalToken(request.StalkerSerialNumber)
                    : string.Empty,
                StalkerTimezone = request.Type == SourceType.Stalker
                    ? NormalizeOptionalToken(request.StalkerTimezone, ResolveDefaultTimezone())
                    : string.Empty,
                StalkerLocale = request.Type == SourceType.Stalker
                    ? NormalizeOptionalToken(request.StalkerLocale, CultureInfo.CurrentCulture.Name)
                    : string.Empty
            };
        }

        private static SourceGuideSettingsUpdateRequest NormalizeGuideRequest(SourceGuideSettingsUpdateRequest request)
        {
            return new SourceGuideSettingsUpdateRequest
            {
                SourceId = request.SourceId,
                ActiveMode = request.ActiveMode,
                ManualEpgUrl = NormalizeOptionalUrl(request.ManualEpgUrl),
                FallbackEpgUrls = NormalizeGuideUrlList(request.FallbackEpgUrls),
                ProxyScope = request.ProxyScope,
                ProxyUrl = NormalizeOptionalUrl(request.ProxyUrl),
                CompanionScope = request.CompanionScope,
                CompanionMode = request.CompanionMode,
                CompanionUrl = NormalizeCompanionUrl(request.CompanionUrl)
            };
        }

        private static void ValidateCreateRequest(SourceCreateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new InvalidOperationException("Source name could not be resolved.");
            }

            if (request.Type == SourceType.M3U && string.IsNullOrWhiteSpace(request.Url))
            {
                throw new InvalidOperationException("M3U URL or file path is required.");
            }

            if (request.Type == SourceType.Xtream &&
                (string.IsNullOrWhiteSpace(request.Url) ||
                 string.IsNullOrWhiteSpace(request.Username) ||
                 string.IsNullOrWhiteSpace(request.Password)))
            {
                throw new InvalidOperationException("Server URL, username, and password are required for Xtream.");
            }

            if (request.Type == SourceType.Stalker &&
                (string.IsNullOrWhiteSpace(request.Url) || string.IsNullOrWhiteSpace(request.StalkerMacAddress)))
            {
                throw new InvalidOperationException("Portal URL and MAC address are required for Stalker.");
            }

            ValidateGuideRequest(new SourceGuideSettingsUpdateRequest
            {
                ActiveMode = request.EpgMode,
                ManualEpgUrl = request.ManualEpgUrl,
                FallbackEpgUrls = request.FallbackEpgUrls,
                ProxyScope = request.ProxyScope,
                ProxyUrl = request.ProxyUrl,
                CompanionScope = request.CompanionScope,
                CompanionMode = request.CompanionMode,
                CompanionUrl = request.CompanionUrl
            });
        }

        private static void ValidateGuideRequest(SourceGuideSettingsUpdateRequest request)
        {
            if (request.ActiveMode == EpgActiveMode.Manual && string.IsNullOrWhiteSpace(request.ManualEpgUrl))
            {
                throw new InvalidOperationException("Manual XMLTV mode requires a manual XMLTV URL.");
            }

            if (request.ProxyScope != SourceProxyScope.Disabled && string.IsNullOrWhiteSpace(request.ProxyUrl))
            {
                throw new InvalidOperationException("Proxy routing requires a proxy URL.");
            }

            if (request.CompanionScope != SourceCompanionScope.Disabled && string.IsNullOrWhiteSpace(request.CompanionUrl))
            {
                throw new InvalidOperationException("Companion relay mode requires a companion endpoint URL.");
            }
        }

        private static string NormalizeUrl(string value, SourceType type)
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            if ((type == SourceType.Xtream || type == SourceType.Stalker) &&
                Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                var builder = new UriBuilder(uri)
                {
                    Query = string.Empty,
                    Fragment = string.Empty,
                    Path = uri.AbsolutePath.TrimEnd('/')
                };

                return builder.Uri.ToString().TrimEnd('/');
            }

            return trimmed;
        }

        private static string NormalizeOptionalUrl(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeGuideUrlList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return string.Join(
                Environment.NewLine,
                value.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static string NormalizeCompanionUrl(string value)
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return trimmed;
            }

            var builder = new UriBuilder(uri)
            {
                Query = string.Empty,
                Fragment = string.Empty,
                Path = uri.AbsolutePath.TrimEnd('/')
            };

            return builder.Uri.ToString().TrimEnd('/');
        }

        private static string NormalizeOptionalToken(string? value, string fallback = "")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string NormalizeMacAddress(string? value)
        {
            var raw = new string((value ?? string.Empty).Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
            if (raw.Length != 12)
            {
                return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
            }

            return string.Join(":", Enumerable.Range(0, 6).Select(index => raw.Substring(index * 2, 2)));
        }

        private static string DeriveSourceName(SourceType type, string primaryUrl)
        {
            if (!string.IsNullOrWhiteSpace(primaryUrl))
            {
                if (Uri.TryCreate(primaryUrl, UriKind.Absolute, out var uri))
                {
                    var host = uri.Host?.Trim();
                    if (!string.IsNullOrWhiteSpace(host))
                    {
                        return host;
                    }

                    var segment = uri.Segments.LastOrDefault()?.Trim('/').Trim();
                    if (!string.IsNullOrWhiteSpace(segment))
                    {
                        return segment;
                    }
                }

                var fileName = Path.GetFileName(primaryUrl.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    return fileName;
                }
            }

            return type switch
            {
                SourceType.M3U => "M3U Source",
                SourceType.Stalker => "Stalker Source",
                _ => "Xtream Source"
            };
        }

        private static async Task<string> DetectDuplicateHintAsync(AppDbContext db, SourceCreateRequest request)
        {
            var duplicateIdentity = request.Type == SourceType.Stalker
                ? request.StalkerMacAddress
                : request.Username;
            var normalizedEndpoint = BuildDuplicateEndpointKey(request.Type, request.Url, duplicateIdentity);
            if (string.IsNullOrWhiteSpace(normalizedEndpoint))
            {
                return string.Empty;
            }

            var existing = await db.SourceProfiles
                .AsNoTracking()
                .Where(profile => profile.Type == request.Type)
                .Join(
                    db.SourceCredentials.AsNoTracking(),
                    profile => profile.Id,
                    credential => credential.SourceProfileId,
                    (profile, credential) => new
                    {
                        profile.Name,
                        profile.Type,
                        credential.Url,
                        Identity = profile.Type == SourceType.Stalker
                            ? credential.StalkerMacAddress
                            : credential.Username
                    })
                .ToListAsync();

            var hasSimilarSource = existing.Any(item =>
                string.Equals(
                    BuildDuplicateEndpointKey(item.Type, item.Url, item.Identity),
                    normalizedEndpoint,
                    StringComparison.OrdinalIgnoreCase));

            return hasSimilarSource
                ? "A similar source is already configured. Overlapping items will be treated as mirrored candidates."
                : string.Empty;
        }

        private static string BuildDuplicateEndpointKey(SourceType type, string? url, string? identity)
        {
            var normalizedUrl = NormalizeUrl(url ?? string.Empty, type);
            if (string.IsNullOrWhiteSpace(normalizedUrl))
            {
                return string.Empty;
            }

            return type == SourceType.Xtream
                ? $"{normalizedUrl}|{identity?.Trim().ToLowerInvariant() ?? string.Empty}"
                : type == SourceType.Stalker
                    ? $"{normalizedUrl}|{NormalizeMacAddress(identity)}"
                    : normalizedUrl.ToLowerInvariant();
        }

        private static string ResolveDefaultTimezone()
        {
            try
            {
                return TimeZoneInfo.Local.Id;
            }
            catch
            {
                return "UTC";
            }
        }

        private static async Task<bool> MarkGuideSettingsPendingAsync(
            AppDbContext db,
            int sourceProfileId,
            SourceCredential credential)
        {
            var channelIds = await db.ChannelCategories
                .Where(category => category.SourceProfileId == sourceProfileId)
                .Join(
                    db.Channels,
                    category => category.Id,
                    channel => channel.ChannelCategoryId,
                    (category, channel) => channel.Id)
                .ToListAsync();

            var matchedChannelIds = channelIds.Count == 0
                ? new HashSet<int>()
                : (await db.EpgPrograms
                    .Where(program => channelIds.Contains(program.ChannelId))
                    .Select(program => program.ChannelId)
                    .Distinct()
                    .ToListAsync())
                    .ToHashSet();
            var nowUtc = DateTime.UtcNow;
            var currentCoverageCount = channelIds.Count == 0
                ? 0
                : await db.EpgPrograms
                    .Where(program => channelIds.Contains(program.ChannelId) &&
                                      program.StartTimeUtc <= nowUtc &&
                                      program.EndTimeUtc > nowUtc)
                    .Select(program => program.ChannelId)
                    .Distinct()
                    .CountAsync();
            var nextCoverageCount = channelIds.Count == 0
                ? 0
                : await db.EpgPrograms
                    .Where(program => channelIds.Contains(program.ChannelId) &&
                                      program.StartTimeUtc > nowUtc &&
                                      program.StartTimeUtc <= nowUtc.AddHours(24))
                    .Select(program => program.ChannelId)
                    .Distinct()
                    .CountAsync();
            var programmeCount = channelIds.Count == 0
                ? 0
                : await db.EpgPrograms.CountAsync(program => channelIds.Contains(program.ChannelId));

            var epgLog = await db.EpgSyncLogs.FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId);
            if (epgLog == null)
            {
                epgLog = new EpgSyncLog
                {
                    SourceProfileId = sourceProfileId
                };
                db.EpgSyncLogs.Add(epgLog);
            }

            var hasGuideData = matchedChannelIds.Count > 0 || programmeCount > 0 || epgLog.LastSuccessAtUtc.HasValue;
            epgLog.SyncedAtUtc = nowUtc;
            epgLog.IsSuccess = false;
            epgLog.Status = hasGuideData ? EpgStatus.Stale : EpgStatus.Unknown;
            if (!hasGuideData)
            {
                epgLog.ResultCode = EpgSyncResultCode.None;
            }

            epgLog.FailureStage = EpgFailureStage.None;
            epgLog.ActiveMode = credential.EpgMode;
            epgLog.ActiveXmltvUrl = ResolveActiveXmltvUrl(credential);
            epgLog.MatchedChannelCount = matchedChannelIds.Count;
            epgLog.UnmatchedChannelCount = Math.Max(0, channelIds.Count - matchedChannelIds.Count);
            epgLog.CurrentCoverageCount = currentCoverageCount;
            epgLog.NextCoverageCount = nextCoverageCount;
            epgLog.TotalLiveChannelCount = channelIds.Count;
            epgLog.ProgrammeCount = programmeCount;
            epgLog.MatchBreakdown = hasGuideData
                ? epgLog.MatchBreakdown
                : string.Empty;
            epgLog.FailureReason = hasGuideData
                ? "Guide settings changed. Sync pending; last successful guide data is still available."
                : "Guide settings updated. Sync pending.";
            epgLog.GuideWarningSummary = epgLog.FailureReason;
            epgLog.GuideSourceStatusJson = BuildPendingGuideSourceStatusJson(credential, nowUtc);

            if (!hasGuideData)
            {
                epgLog.XmltvChannelCount = 0;
                epgLog.ExactMatchCount = 0;
                epgLog.NormalizedMatchCount = 0;
                epgLog.ApprovedMatchCount = 0;
                epgLog.WeakMatchCount = 0;
            }

            await db.SaveChangesAsync();
            return hasGuideData;
        }

        private static string ResolveActiveXmltvUrl(SourceCredential credential)
        {
            return credential.EpgMode switch
            {
                EpgActiveMode.Manual => credential.ManualEpgUrl?.Trim() ?? string.Empty,
                EpgActiveMode.None => string.Empty,
                _ => !string.IsNullOrWhiteSpace(credential.DetectedEpgUrl)
                    ? credential.DetectedEpgUrl.Trim()
                    : FirstGuideUrl(credential.FallbackEpgUrls)
            };
        }

        private static string FirstGuideUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? string.Empty;
        }

        private static string BuildPendingGuideSourceStatusJson(SourceCredential credential, DateTime checkedAtUtc)
        {
            var snapshots = new List<EpgGuideSourceStatusSnapshot>();
            var priority = 0;
            if (credential.EpgMode == EpgActiveMode.Manual)
            {
                if (!string.IsNullOrWhiteSpace(credential.ManualEpgUrl))
                {
                    snapshots.Add(BuildPendingGuideSource(
                        "Manual XMLTV override",
                        credential.ManualEpgUrl.Trim(),
                        EpgGuideSourceKind.Manual,
                        isOptional: false,
                        priority++,
                        checkedAtUtc));
                }
            }
            else if (credential.EpgMode != EpgActiveMode.None &&
                     !string.IsNullOrWhiteSpace(credential.DetectedEpgUrl))
            {
                snapshots.Add(BuildPendingGuideSource(
                    "Provider XMLTV",
                    credential.DetectedEpgUrl.Trim(),
                    EpgGuideSourceKind.Provider,
                    isOptional: false,
                    priority++,
                    checkedAtUtc));
            }

            foreach (var url in SplitGuideUrls(credential.FallbackEpgUrls))
            {
                var kind = EpgPublicGuideCatalog.ClassifyFallbackUrl(url);
                snapshots.Add(BuildPendingGuideSource(
                    EpgPublicGuideCatalog.BuildGuideSourceLabel(url, kind, "Fallback XMLTV"),
                    url,
                    kind,
                    isOptional: true,
                    priority++,
                    checkedAtUtc));
            }

            return snapshots.Count == 0 ? string.Empty : JsonSerializer.Serialize(snapshots);
        }

        private static EpgGuideSourceStatusSnapshot BuildPendingGuideSource(
            string label,
            string url,
            EpgGuideSourceKind kind,
            bool isOptional,
            int priority,
            DateTime checkedAtUtc)
        {
            return new EpgGuideSourceStatusSnapshot
            {
                Label = label,
                Url = url,
                Kind = kind,
                Status = EpgGuideSourceStatus.Pending,
                IsOptional = isOptional,
                Priority = priority,
                CheckedAtUtc = checkedAtUtc,
                Message = "Configured. Sync pending."
            };
        }

        private static IReadOnlyList<string> SplitGuideUrls(string? value)
        {
            return EpgPublicGuideCatalog.SplitGuideUrls(value);
        }

        private static async Task TryPostDeleteRepairAsync(
            ICollection<string> warnings,
            string label,
            Func<Task> action,
            int sourceProfileId)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                RuntimeEventLogger.Log("SOURCE-LIFECYCLE", ex, $"source_id={sourceProfileId} {label} deferred");
                warnings.Add($"{UppercaseFirst(label)} was deferred.");
            }
        }

        private static string UppercaseFirst(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : char.ToUpperInvariant(value[0]) + value[1..];
        }
    }
}
