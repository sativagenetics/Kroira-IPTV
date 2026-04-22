#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Services
{
    public interface ISourceGuidanceService
    {
        Task<SourceSetupValidationSnapshot> ValidateDraftAsync(
            SourceSetupDraft draft,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyDictionary<int, SourceRepairSnapshot>> GetRepairSnapshotsAsync(
            AppDbContext db,
            IReadOnlyCollection<int> sourceIds,
            IReadOnlyDictionary<int, SourceDiagnosticsSnapshot>? diagnostics = null,
            IReadOnlyDictionary<int, SourceActivitySnapshot>? activities = null,
            CancellationToken cancellationToken = default);

        Task<SourceRepairExecutionResult> ApplyRepairActionAsync(
            int sourceId,
            SourceRepairActionType actionType,
            CancellationToken cancellationToken = default);
    }

    public sealed class SourceGuidanceService : ISourceGuidanceService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ISourceRoutingService _sourceRoutingService;
        private readonly IStalkerPortalClient _stalkerPortalClient;
        private readonly ISourceDiagnosticsService _sourceDiagnosticsService;
        private readonly ISourceActivityService _sourceActivityService;
        private readonly ISensitiveDataRedactionService _redactionService;
        private readonly IChannelCatchupService _channelCatchupService;

        public SourceGuidanceService(
            IServiceProvider serviceProvider,
            ISourceRoutingService sourceRoutingService,
            IStalkerPortalClient stalkerPortalClient,
            ISourceDiagnosticsService sourceDiagnosticsService,
            ISourceActivityService sourceActivityService,
            ISensitiveDataRedactionService redactionService,
            IChannelCatchupService channelCatchupService)
        {
            _serviceProvider = serviceProvider;
            _sourceRoutingService = sourceRoutingService;
            _stalkerPortalClient = stalkerPortalClient;
            _sourceDiagnosticsService = sourceDiagnosticsService;
            _sourceActivityService = sourceActivityService;
            _redactionService = redactionService;
            _channelCatchupService = channelCatchupService;
        }

        public async Task<SourceSetupValidationSnapshot> ValidateDraftAsync(
            SourceSetupDraft draft,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(draft);

            var normalized = NormalizeDraft(draft);
            var validationIssues = ValidateDraftInputs(normalized);
            if (validationIssues.Count > 0)
            {
                return BuildValidationSnapshot(
                    normalized.Type,
                    DetectTypeHint(normalized),
                    canSave: false,
                    headline: "Source setup needs attention",
                    summary: validationIssues[0].Detail,
                    connection: "KROIRA could not verify this source yet.",
                    typeHint: BuildTypeHintText(normalized.Type, DetectTypeHint(normalized), consistent: false),
                    capabilities: BuildRoutingCapabilities(normalized),
                    issues: validationIssues);
            }

            try
            {
                return normalized.Type switch
                {
                    SourceType.M3U => await ValidateM3uDraftAsync(normalized, cancellationToken),
                    SourceType.Xtream => await ValidateXtreamDraftAsync(normalized, cancellationToken),
                    SourceType.Stalker => await ValidateStalkerDraftAsync(normalized, cancellationToken),
                    _ => BuildValidationSnapshot(
                        normalized.Type,
                        null,
                        canSave: false,
                        headline: "Source format is not supported",
                        summary: "KROIRA could not validate the selected source format.",
                        connection: "No validation path is available.",
                        typeHint: string.Empty,
                        capabilities: Array.Empty<SourceGuidanceCapability>(),
                        issues: new[]
                        {
                            new SourceGuidanceIssue
                            {
                                Title = "Unsupported source type",
                                Detail = "Choose M3U, Xtream, or Stalker.",
                                Tone = SourceActivityTone.Failed
                            }
                        })
                };
            }
            catch (Exception ex)
            {
                var issues = new List<SourceGuidanceIssue>(BuildRoutingIssues(normalized))
                {
                    new()
                    {
                        Title = "Connection test failed",
                        Detail = SanitizeText(ex.Message),
                        Tone = SourceActivityTone.Failed
                    }
                };

                return BuildValidationSnapshot(
                    normalized.Type,
                    DetectTypeHint(normalized),
                    canSave: false,
                    headline: "Source setup needs attention",
                    summary: SanitizeText(ex.Message),
                    connection: "The connection test did not complete successfully.",
                    typeHint: BuildTypeHintText(normalized.Type, DetectTypeHint(normalized), consistent: false),
                    capabilities: BuildRoutingCapabilities(normalized),
                    issues: issues);
            }
        }

        public async Task<IReadOnlyDictionary<int, SourceRepairSnapshot>> GetRepairSnapshotsAsync(
            AppDbContext db,
            IReadOnlyCollection<int> sourceIds,
            IReadOnlyDictionary<int, SourceDiagnosticsSnapshot>? diagnostics = null,
            IReadOnlyDictionary<int, SourceActivitySnapshot>? activities = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(db);

            var ids = sourceIds
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<int, SourceRepairSnapshot>();
            }

            var diagnosticLookup = diagnostics ?? await _sourceDiagnosticsService.GetSnapshotsAsync(db, ids);
            var activityLookup = activities ?? await _sourceActivityService.GetSnapshotsAsync(db, ids, diagnosticLookup, cancellationToken);
            var profiles = await db.SourceProfiles
                .AsNoTracking()
                .Where(profile => ids.Contains(profile.Id))
                .ToDictionaryAsync(profile => profile.Id, cancellationToken);
            var credentials = await db.SourceCredentials
                .AsNoTracking()
                .Where(credential => ids.Contains(credential.SourceProfileId))
                .ToDictionaryAsync(credential => credential.SourceProfileId, cancellationToken);

            var snapshots = new Dictionary<int, SourceRepairSnapshot>(ids.Count);
            foreach (var sourceId in ids)
            {
                if (!profiles.TryGetValue(sourceId, out var profile))
                {
                    continue;
                }

                diagnosticLookup.TryGetValue(sourceId, out var diagnostic);
                diagnostic ??= new SourceDiagnosticsSnapshot
                {
                    SourceProfileId = sourceId,
                    SourceType = profile.Type
                };

                activityLookup.TryGetValue(sourceId, out var activity);
                activity ??= new SourceActivitySnapshot
                {
                    SourceProfileId = sourceId,
                    SourceName = profile.Name,
                    SourceType = profile.Type
                };

                credentials.TryGetValue(sourceId, out var credential);
                snapshots[sourceId] = BuildRepairSnapshot(profile, credential, diagnostic, activity);
            }

            return snapshots;
        }

        public async Task<SourceRepairExecutionResult> ApplyRepairActionAsync(
            int sourceId,
            SourceRepairActionType actionType,
            CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var refreshService = scope.ServiceProvider.GetRequiredService<ISourceRefreshService>();
            var lifecycleService = scope.ServiceProvider.GetRequiredService<ISourceLifecycleService>();
            var browsePreferencesService = scope.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
            var logicalCatalogStateService = scope.ServiceProvider.GetRequiredService<ILogicalCatalogStateService>();
            var contentOperationalService = scope.ServiceProvider.GetRequiredService<IContentOperationalService>();
            var sourceHealthService = scope.ServiceProvider.GetRequiredService<ISourceHealthService>();

            var beforeDiagnostics = await _sourceDiagnosticsService.GetSnapshotsAsync(db, new[] { sourceId });
            var beforeActivity = await _sourceActivityService.GetSnapshotsAsync(db, new[] { sourceId }, beforeDiagnostics, cancellationToken);
            var beforeRepair = await GetRepairSnapshotsAsync(db, new[] { sourceId }, beforeDiagnostics, beforeActivity, cancellationToken);
            beforeDiagnostics.TryGetValue(sourceId, out var beforeDiagnostic);
            beforeRepair.TryGetValue(sourceId, out var beforeSnapshot);

            var detailBuilder = new List<string>();

            try
            {
                switch (actionType)
                {
                    case SourceRepairActionType.RetestSource:
                    case SourceRepairActionType.RefreshPortalProfile:
                        {
                            var result = await refreshService.RefreshSourceAsync(
                                sourceId,
                                SourceRefreshTrigger.Manual,
                                SourceRefreshScope.Full);
                            detailBuilder.Add(SanitizeText(result.Message));
                            break;
                        }

                    case SourceRepairActionType.RetestGuide:
                        {
                            var result = await refreshService.RefreshSourceAsync(
                                sourceId,
                                SourceRefreshTrigger.Manual,
                                SourceRefreshScope.EpgOnly);
                            detailBuilder.Add(SanitizeText(result.Message));
                            break;
                        }

                    case SourceRepairActionType.DisableCompanionRelay:
                        {
                            var credential = await db.SourceCredentials
                                .AsNoTracking()
                                .FirstOrDefaultAsync(item => item.SourceProfileId == sourceId, cancellationToken);
                            if (credential == null)
                            {
                                throw new InvalidOperationException("Source credentials were not found.");
                            }

                            if (credential.CompanionScope == SourceCompanionScope.Disabled)
                            {
                                detailBuilder.Add("Local companion relay was already disabled.");
                            }
                            else
                            {
                                var update = await lifecycleService.UpdateGuideSettingsAsync(
                                    new SourceGuideSettingsUpdateRequest
                                    {
                                        SourceId = sourceId,
                                        ActiveMode = credential.EpgMode,
                                        ManualEpgUrl = credential.ManualEpgUrl,
                                        ProxyScope = credential.ProxyScope,
                                        ProxyUrl = credential.ProxyUrl,
                                        CompanionScope = SourceCompanionScope.Disabled,
                                        CompanionMode = credential.CompanionMode,
                                        CompanionUrl = string.Empty
                                    },
                                    syncNow: false);
                                detailBuilder.Add(SanitizeText(update.Message));
                            }

                            var refresh = await refreshService.RefreshSourceAsync(
                                sourceId,
                                SourceRefreshTrigger.Manual,
                                SourceRefreshScope.Full);
                            detailBuilder.Add(SanitizeText(refresh.Message));
                            break;
                        }

                    case SourceRepairActionType.RepairRuntimeState:
                        {
                            await browsePreferencesService.RepairSourceReferencesAsync(db, sourceId);
                            await logicalCatalogStateService.ReconcilePersistentStateAsync(db);
                            await contentOperationalService.RefreshOperationalStateAsync(db);
                            await sourceHealthService.RefreshSourceHealthAsync(db, sourceId);
                            detailBuilder.Add("Saved state, operational candidates, and source health were rebuilt.");
                            break;
                        }

                    default:
                        throw new InvalidOperationException("This repair action is not applied directly from the assistant.");
                }
            }
            catch (Exception ex)
            {
                var failureDetail = SanitizeText(ex.Message);
                return new SourceRepairExecutionResult
                {
                    SourceId = sourceId,
                    ActionType = actionType,
                    Success = false,
                    HeadlineText = "Repair attempt needs review",
                    DetailText = failureDetail,
                    ChangeText = string.Empty,
                    SafeReportText = BuildRepairExecutionReport(actionType, false, failureDetail, string.Empty)
                };
            }

            db.ChangeTracker.Clear();
            var afterDiagnostics = await _sourceDiagnosticsService.GetSnapshotsAsync(db, new[] { sourceId });
            var afterActivity = await _sourceActivityService.GetSnapshotsAsync(db, new[] { sourceId }, afterDiagnostics, cancellationToken);
            var afterRepair = await GetRepairSnapshotsAsync(db, new[] { sourceId }, afterDiagnostics, afterActivity, cancellationToken);
            afterDiagnostics.TryGetValue(sourceId, out var afterDiagnostic);
            afterRepair.TryGetValue(sourceId, out var afterSnapshot);

            var changeText = BuildRepairChangeText(beforeDiagnostic, afterDiagnostic, beforeSnapshot, afterSnapshot);
            var detailText = string.Join(
                " ",
                detailBuilder
                    .Where(segment => !string.IsNullOrWhiteSpace(segment))
                    .Select(segment => segment.Trim()));
            var success = actionType == SourceRepairActionType.DisableCompanionRelay
                ? afterDiagnostic == null || afterDiagnostic.CompanionStatusText.Contains("off", StringComparison.OrdinalIgnoreCase)
                : true;

            var headlineText = success
                ? (afterSnapshot?.IsStable == true ? "Repair applied cleanly" : "Repair applied, but the source still needs attention")
                : "Repair attempt needs review";

            return new SourceRepairExecutionResult
            {
                SourceId = sourceId,
                ActionType = actionType,
                Success = success,
                HeadlineText = headlineText,
                DetailText = detailText,
                ChangeText = changeText,
                SafeReportText = BuildRepairExecutionReport(actionType, success, detailText, changeText)
            };
        }

        private async Task<SourceSetupValidationSnapshot> ValidateM3uDraftAsync(
            SourceSetupDraft draft,
            CancellationToken cancellationToken)
        {
            var credential = BuildCredential(draft);
            var content = await ReadTextPreviewAsync(draft.Url, credential, SourceNetworkPurpose.Import, cancellationToken);
            if (!LooksLikeM3u(content))
            {
                throw new InvalidOperationException("The endpoint responded, but it did not look like an M3U playlist.");
            }

            var header = M3uMetadataParser.ParseHeaderMetadata(content, draft.Url);
            var liveCount = 0;
            var movieCount = 0;
            var episodeCount = 0;
            var catchupCount = 0;
            var sampledEntries = 0;
            var pendingExtinf = string.Empty;

            foreach (var rawLine in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                {
                    pendingExtinf = line;
                    continue;
                }

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(pendingExtinf))
                {
                    continue;
                }

                var metadata = M3uMetadataParser.ParseExtinf(pendingExtinf);
                var streamUrl = M3uMetadataParser.ResolveUrl(line, draft.Url);
                var groupName = M3uMetadataParser.GetFirstAttributeValue(metadata.Attributes, "group-title");
                var tvgType = M3uMetadataParser.GetFirstAttributeValue(metadata.Attributes, "tvg-type", "type");
                switch (ContentClassifier.ClassifyM3uEntry(metadata.DisplayName, streamUrl, groupName, tvgType))
                {
                    case ContentClassifier.M3uEntryType.Live:
                        liveCount++;
                        break;
                    case ContentClassifier.M3uEntryType.Movie:
                        movieCount++;
                        break;
                    default:
                        episodeCount++;
                        break;
                }

                var tempChannel = new Channel
                {
                    Name = metadata.DisplayName,
                    StreamUrl = streamUrl
                };
                _channelCatchupService.ApplyM3uCatchup(tempChannel, metadata.Attributes);
                if (tempChannel.SupportsCatchup)
                {
                    catchupCount++;
                }

                sampledEntries++;
                pendingExtinf = string.Empty;
                if (sampledEntries >= 200)
                {
                    break;
                }
            }

            if (sampledEntries == 0)
            {
                throw new InvalidOperationException("The playlist was reachable, but it did not expose any playable entries.");
            }

            var capabilities = new List<SourceGuidanceCapability>
            {
                new()
                {
                    Label = "Connection",
                    Value = "Playlist reachable",
                    Detail = $"Sampled {sampledEntries:N0} entries without provider auth errors.",
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = "Catalog",
                    Value = BuildCatalogCapabilityLabel(liveCount, movieCount, episodeCount > 0),
                    Detail = $"{liveCount:N0} live, {movieCount:N0} VOD, {episodeCount:N0} episodic hints in the sampled slice.",
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = "Guide",
                    Value = header.XmltvUrls.Count > 0
                        ? "Guide found"
                        : draft.EpgMode == EpgActiveMode.Manual
                            ? "Manual guide set"
                            : "No guide advertised",
                    Detail = header.XmltvUrls.Count > 0
                        ? SanitizeText(header.XmltvUrls[0])
                        : draft.EpgMode == EpgActiveMode.Manual
                            ? SanitizeText(draft.ManualEpgUrl)
                            : "No XMLTV URL was advertised in the playlist header preview.",
                    Tone = header.XmltvUrls.Count > 0 || draft.EpgMode == EpgActiveMode.Manual
                        ? SourceActivityTone.Healthy
                        : SourceActivityTone.Warning
                },
                new()
                {
                    Label = "Catchup",
                    Value = catchupCount > 0 ? $"{catchupCount:N0} hints" : "No clear hints",
                    Detail = catchupCount > 0
                        ? "Catchup attributes or archive-style stream patterns were found in the sampled slice."
                        : "Catchup support can still appear later, but the sampled slice did not advertise it clearly.",
                    Tone = catchupCount > 0 ? SourceActivityTone.Healthy : SourceActivityTone.Neutral
                }
            };
            capabilities.AddRange(BuildRoutingCapabilities(draft));

            var issues = new List<SourceGuidanceIssue>(BuildRoutingIssues(draft));
            if (header.XmltvUrls.Count == 0 && draft.EpgMode == EpgActiveMode.Detected && liveCount > 0)
            {
                issues.Add(new SourceGuidanceIssue
                {
                    Title = "No guide URL was detected yet",
                    Detail = "The playlist can still be saved, but guide sync will stay off unless you add a manual XMLTV feed.",
                    Tone = SourceActivityTone.Warning
                });
            }

            return BuildValidationSnapshot(
                draft.Type,
                SourceType.M3U,
                canSave: true,
                headline: "Playlist looks importable",
                summary: $"{BuildCatalogCapabilityLabel(liveCount, movieCount, episodeCount > 0)} was detected in the sampled playlist preview.",
                connection: "KROIRA reached the playlist and parsed a representative slice successfully.",
                typeHint: BuildTypeHintText(draft.Type, SourceType.M3U, consistent: true),
                capabilities: capabilities,
                issues: issues);
        }

        private async Task<SourceSetupValidationSnapshot> ValidateXtreamDraftAsync(
            SourceSetupDraft draft,
            CancellationToken cancellationToken)
        {
            var credential = BuildCredential(draft);
            using var client = _sourceRoutingService.CreateHttpClient(credential, SourceNetworkPurpose.Import, TimeSpan.FromSeconds(20));

            var baseUrl = draft.Url.TrimEnd('/');
            var authQuery = $"?username={Uri.EscapeDataString(draft.Username)}&password={Uri.EscapeDataString(draft.Password)}";

            using var liveCategories = await ReadJsonAsync(client, $"{baseUrl}/player_api.php{authQuery}&action=get_live_categories", cancellationToken);
            using var liveStreams = await ReadJsonAsync(client, $"{baseUrl}/player_api.php{authQuery}&action=get_live_streams", cancellationToken);

            var liveCategoryCount = CountJsonItems(liveCategories.RootElement);
            var liveStreamCount = CountJsonItems(liveStreams.RootElement);

            var vodCategoryCount = 0;
            try
            {
                using var vodCategories = await ReadJsonAsync(client, $"{baseUrl}/player_api.php{authQuery}&action=get_vod_categories", cancellationToken);
                vodCategoryCount = CountJsonItems(vodCategories.RootElement);
            }
            catch
            {
            }

            var seriesCategoryCount = 0;
            try
            {
                using var seriesCategories = await ReadJsonAsync(client, $"{baseUrl}/player_api.php{authQuery}&action=get_series_categories", cancellationToken);
                seriesCategoryCount = CountJsonItems(seriesCategories.RootElement);
            }
            catch
            {
            }

            var catchupCount = 0;
            foreach (var element in EnumerateJsonItems(liveStreams.RootElement).Take(120))
            {
                var tempChannel = new Channel
                {
                    Name = GetJsonString(element, "name", "title"),
                    StreamUrl = $"{baseUrl}/live/{Uri.EscapeDataString(draft.Username)}/{Uri.EscapeDataString(draft.Password)}/{GetJsonString(element, "stream_id", "id")}.ts"
                };
                _channelCatchupService.ApplyXtreamCatchup(tempChannel, element);
                if (tempChannel.SupportsCatchup)
                {
                    catchupCount++;
                }
            }

            if (liveCategoryCount == 0 && vodCategoryCount == 0 && seriesCategoryCount == 0)
            {
                throw new InvalidOperationException("The Xtream endpoint responded, but it did not return any catalog categories.");
            }

            var capabilities = new List<SourceGuidanceCapability>
            {
                new()
                {
                    Label = "Connection",
                    Value = "Credentials accepted",
                    Detail = $"{liveStreamCount:N0} live stream rows were reachable during the preview.",
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = "Catalog",
                    Value = BuildXtreamCatalogLabel(liveCategoryCount, vodCategoryCount, seriesCategoryCount),
                    Detail = $"{liveCategoryCount:N0} live categories, {vodCategoryCount:N0} movie categories, {seriesCategoryCount:N0} series categories.",
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = "Guide",
                    Value = "Guide derived",
                    Detail = SanitizeText($"{baseUrl}/xmltv.php{authQuery}"),
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = "Catchup",
                    Value = catchupCount > 0 ? $"{catchupCount:N0} archive hints" : "No clear hints",
                    Detail = catchupCount > 0
                        ? "Archive-capable channels were detected in the sampled live stream list."
                        : "The sampled live stream list did not advertise archive playback clearly.",
                    Tone = catchupCount > 0 ? SourceActivityTone.Healthy : SourceActivityTone.Neutral
                }
            };
            capabilities.AddRange(BuildRoutingCapabilities(draft));

            var issues = new List<SourceGuidanceIssue>(BuildRoutingIssues(draft));
            if (vodCategoryCount == 0)
            {
                issues.Add(new SourceGuidanceIssue
                {
                    Title = "Movie catalog was not confirmed",
                    Detail = "This Xtream provider may still be live-only, or the VOD endpoint may be restricted.",
                    Tone = SourceActivityTone.Warning
                });
            }

            if (seriesCategoryCount == 0)
            {
                issues.Add(new SourceGuidanceIssue
                {
                    Title = "Series catalog was not confirmed",
                    Detail = "Series support will stay optional unless the provider exposes series categories.",
                    Tone = SourceActivityTone.Neutral
                });
            }

            return BuildValidationSnapshot(
                draft.Type,
                SourceType.Xtream,
                canSave: true,
                headline: "Xtream provider looks reachable",
                summary: $"{BuildXtreamCatalogLabel(liveCategoryCount, vodCategoryCount, seriesCategoryCount)} was confirmed through player_api preview calls.",
                connection: "KROIRA reached the Xtream player_api endpoints with the supplied credentials.",
                typeHint: BuildTypeHintText(draft.Type, SourceType.Xtream, consistent: true),
                capabilities: capabilities,
                issues: issues);
        }

        private async Task<SourceSetupValidationSnapshot> ValidateStalkerDraftAsync(
            SourceSetupDraft draft,
            CancellationToken cancellationToken)
        {
            var catalog = await _stalkerPortalClient.LoadCatalogAsync(BuildCredential(draft), cancellationToken);
            var capabilities = new List<SourceGuidanceCapability>
            {
                new()
                {
                    Label = "Connection",
                    Value = "Portal reachable",
                    Detail = string.IsNullOrWhiteSpace(catalog.DiscoveredApiUrl)
                        ? "Handshake and profile discovery completed."
                        : SanitizeText(catalog.DiscoveredApiUrl),
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = "Catalog",
                    Value = BuildStalkerCatalogLabel(catalog),
                    Detail = $"{catalog.LiveChannels.Count:N0} live, {catalog.Movies.Count:N0} movies, {catalog.Series.Count:N0} series items discovered.",
                    Tone = catalog.SupportsLive || catalog.SupportsMovies || catalog.SupportsSeries
                        ? SourceActivityTone.Healthy
                        : SourceActivityTone.Warning
                },
                new()
                {
                    Label = "Portal profile",
                    Value = string.IsNullOrWhiteSpace(catalog.ProfileName) ? "Profile discovered" : catalog.ProfileName,
                    Detail = string.IsNullOrWhiteSpace(catalog.PortalName)
                        ? "Portal handshake completed."
                        : catalog.PortalName,
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = "Guide",
                    Value = draft.EpgMode == EpgActiveMode.Manual && !string.IsNullOrWhiteSpace(draft.ManualEpgUrl)
                        ? "Manual guide set"
                        : "Manual guide optional",
                    Detail = draft.EpgMode == EpgActiveMode.Manual && !string.IsNullOrWhiteSpace(draft.ManualEpgUrl)
                        ? SanitizeText(draft.ManualEpgUrl)
                        : "Stalker portals do not always expose XMLTV directly. Add a manual guide only when you have one.",
                    Tone = draft.EpgMode == EpgActiveMode.Manual && !string.IsNullOrWhiteSpace(draft.ManualEpgUrl)
                        ? SourceActivityTone.Healthy
                        : SourceActivityTone.Neutral
                }
            };
            capabilities.AddRange(BuildRoutingCapabilities(draft));

            var issues = new List<SourceGuidanceIssue>(BuildRoutingIssues(draft));
            foreach (var warning in catalog.Warnings.Take(3))
            {
                issues.Add(new SourceGuidanceIssue
                {
                    Title = "Portal returned a partial catalog",
                    Detail = SanitizeText(warning),
                    Tone = SourceActivityTone.Warning
                });
            }

            if (!catalog.SupportsLive && !catalog.SupportsMovies && !catalog.SupportsSeries)
            {
                issues.Add(new SourceGuidanceIssue
                {
                    Title = "No catalog families were confirmed",
                    Detail = "The portal handshake worked, but it did not return live, movie, or series data in this preview.",
                    Tone = SourceActivityTone.Warning
                });
            }

            return BuildValidationSnapshot(
                draft.Type,
                SourceType.Stalker,
                canSave: true,
                headline: "Portal handshake completed",
                summary: $"{BuildStalkerCatalogLabel(catalog)} was confirmed through the portal preview.",
                connection: "KROIRA completed handshake and profile discovery with the supplied portal identity.",
                typeHint: BuildTypeHintText(draft.Type, SourceType.Stalker, consistent: true),
                capabilities: capabilities,
                issues: issues);
        }

        private SourceRepairSnapshot BuildRepairSnapshot(
            SourceProfile profile,
            SourceCredential? credential,
            SourceDiagnosticsSnapshot diagnostics,
            SourceActivitySnapshot activity)
        {
            var capabilities = BuildRepairCapabilities(diagnostics, credential);
            var issues = BuildRepairIssues(diagnostics, activity);
            var actions = BuildRepairActions(profile, credential, diagnostics, activity);
            var headline = ResolveRepairHeadline(profile, credential, diagnostics, activity);
            var summary = ResolveRepairSummary(profile, credential, diagnostics, activity);

            var snapshot = new SourceRepairSnapshot
            {
                SourceId = profile.Id,
                HeadlineText = headline,
                SummaryText = summary,
                StatusText = string.IsNullOrWhiteSpace(activity.LastSuccessText)
                    ? diagnostics.StatusSummary
                    : $"{activity.LatestAttemptText} {activity.LastSuccessText}".Trim(),
                CapabilitySummaryText = BuildRepairCapabilitySummary(capabilities),
                IsStable = !issues.Any(issue => issue.Tone is SourceActivityTone.Warning or SourceActivityTone.Failed),
                Capabilities = capabilities,
                Issues = issues,
                Actions = actions
            };

            snapshot.SafeReportText = BuildRepairSafeReport(profile, diagnostics, activity, snapshot);
            return snapshot;
        }

        private IReadOnlyList<SourceGuidanceCapability> BuildRepairCapabilities(
            SourceDiagnosticsSnapshot diagnostics,
            SourceCredential? credential)
        {
            var capabilities = new List<SourceGuidanceCapability>
            {
                new()
                {
                    Label = "Catalog",
                    Value = BuildCatalogCapabilityLabel(diagnostics.LiveChannelCount, diagnostics.MovieCount, diagnostics.SeriesCount > 0),
                    Detail = $"{diagnostics.LiveChannelCount:N0} live, {diagnostics.MovieCount:N0} movies, {diagnostics.SeriesCount:N0} series.",
                    Tone = diagnostics.LiveChannelCount + diagnostics.MovieCount + diagnostics.SeriesCount > 0
                        ? SourceActivityTone.Healthy
                        : SourceActivityTone.Warning
                },
                new()
                {
                    Label = "Guide",
                    Value = diagnostics.ActiveEpgMode == EpgActiveMode.None
                        ? "Guide disabled"
                        : diagnostics.EpgStatusText,
                    Detail = SanitizeText(diagnostics.EpgStatusSummary),
                    Tone = diagnostics.EpgStatus switch
                    {
                        EpgStatus.Ready or EpgStatus.ManualOverride => SourceActivityTone.Healthy,
                        EpgStatus.Stale or EpgStatus.FailedFetchOrParse => SourceActivityTone.Warning,
                        _ => SourceActivityTone.Neutral
                    }
                },
                new()
                {
                    Label = "Catchup",
                    Value = diagnostics.CatchupChannelCount > 0 ? $"{diagnostics.CatchupChannelCount:N0} channels" : "No advertised support",
                    Detail = SanitizeText(diagnostics.CatchupStatusText),
                    Tone = diagnostics.CatchupChannelCount > 0 ? SourceActivityTone.Healthy : SourceActivityTone.Neutral
                },
                new()
                {
                    Label = "Routing",
                    Value = credential == null
                        ? "Direct"
                        : credential.CompanionScope != SourceCompanionScope.Disabled
                            ? "Companion enabled"
                            : credential.ProxyScope != SourceProxyScope.Disabled
                                ? "Proxy enabled"
                                : "Direct",
                    Detail = SanitizeText(BuildRoutingDetail(credential, diagnostics)),
                    Tone = credential?.CompanionScope != SourceCompanionScope.Disabled || credential?.ProxyScope != SourceProxyScope.Disabled
                        ? SourceActivityTone.Neutral
                        : SourceActivityTone.Healthy
                }
            };

            return capabilities;
        }

        private IReadOnlyList<SourceGuidanceIssue> BuildRepairIssues(
            SourceDiagnosticsSnapshot diagnostics,
            SourceActivitySnapshot activity)
        {
            var issues = diagnostics.Issues
                .Take(3)
                .Select(issue => new SourceGuidanceIssue
                {
                    Title = SanitizeText(issue.Title),
                    Detail = SanitizeText(string.IsNullOrWhiteSpace(issue.Message) ? diagnostics.StatusSummary : issue.Message),
                    Tone = issue.Severity == SourceHealthIssueSeverity.Error
                        ? SourceActivityTone.Failed
                        : issue.Severity == SourceHealthIssueSeverity.Warning
                            ? SourceActivityTone.Warning
                            : SourceActivityTone.Neutral
                })
                .ToList();

            if (issues.Count == 0)
            {
                issues.Add(new SourceGuidanceIssue
                {
                    Title = string.IsNullOrWhiteSpace(activity.HeadlineText) ? "Source status is stable" : SanitizeText(activity.HeadlineText),
                    Detail = string.IsNullOrWhiteSpace(activity.CurrentStateText)
                        ? SanitizeText(diagnostics.StatusSummary)
                        : SanitizeText(activity.CurrentStateText),
                    Tone = diagnostics.HealthLabel.Equals("Healthy", StringComparison.OrdinalIgnoreCase)
                        ? SourceActivityTone.Healthy
                        : SourceActivityTone.Neutral
                });
            }

            return issues;
        }

        private IReadOnlyList<SourceRepairAction> BuildRepairActions(
            SourceProfile profile,
            SourceCredential? credential,
            SourceDiagnosticsSnapshot diagnostics,
            SourceActivitySnapshot activity)
        {
            var actions = new List<SourceRepairAction>();
            var companionProblem = HasCompanionFailureSignal(credential, diagnostics, activity);
            var guideProblem = HasGuideFailureSignal(diagnostics);
            var portalProblem = profile.Type == SourceType.Stalker && !string.IsNullOrWhiteSpace(diagnostics.StalkerPortalErrorText);
            var setupProblem = diagnostics.LiveChannelCount + diagnostics.MovieCount + diagnostics.SeriesCount == 0 &&
                               !string.IsNullOrWhiteSpace(diagnostics.FailureSummaryText);
            var staleProblem = diagnostics.HealthLabel.Equals("Outdated", StringComparison.OrdinalIgnoreCase) ||
                               diagnostics.StatusSummary.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
                               diagnostics.StatusSummary.Contains("outdated", StringComparison.OrdinalIgnoreCase);

            if (companionProblem)
            {
                actions.Add(new SourceRepairAction
                {
                    ActionType = SourceRepairActionType.DisableCompanionRelay,
                    Kind = SourceRepairActionKind.Apply,
                    Title = "Disable local companion relay",
                    Summary = "Keep the provider path direct for this source, then rerun refresh and validation.",
                    ButtonText = "Disable companion",
                    IsPrimary = true,
                    Tone = SourceActivityTone.Warning
                });
            }

            if (portalProblem)
            {
                actions.Add(new SourceRepairAction
                {
                    ActionType = SourceRepairActionType.RefreshPortalProfile,
                    Kind = SourceRepairActionKind.Apply,
                    Title = "Refresh portal profile",
                    Summary = "Run a fresh Stalker handshake, reload profile data, and retest the portal catalog.",
                    ButtonText = "Refresh portal",
                    IsPrimary = actions.Count == 0,
                    Tone = SourceActivityTone.Warning
                });
            }

            if (guideProblem)
            {
                if (diagnostics.HasEpgUrl)
                {
                    actions.Add(new SourceRepairAction
                    {
                        ActionType = SourceRepairActionType.RetestGuide,
                        Kind = SourceRepairActionKind.Apply,
                        Title = "Retest guide feed",
                        Summary = "Retry XMLTV fetch and rematch without changing catalog settings.",
                        ButtonText = "Retest guide",
                        IsPrimary = actions.Count == 0,
                        Tone = SourceActivityTone.Warning
                    });
                }

                actions.Add(new SourceRepairAction
                {
                    ActionType = SourceRepairActionType.ReviewGuideSettings,
                    Kind = SourceRepairActionKind.Review,
                    Title = "Review guide settings",
                    Summary = "Check detected versus manual XMLTV and routing choices before the next guide sync.",
                    ButtonText = "Guide settings",
                    IsPrimary = false,
                    Tone = SourceActivityTone.Neutral
                });
            }

            if (setupProblem || (!portalProblem && !guideProblem))
            {
                actions.Add(new SourceRepairAction
                {
                    ActionType = profile.Type == SourceType.Stalker
                        ? SourceRepairActionType.RefreshPortalProfile
                        : SourceRepairActionType.RetestSource,
                    Kind = SourceRepairActionKind.Apply,
                    Title = profile.Type == SourceType.Stalker ? "Retest portal sync" : "Retest source",
                    Summary = profile.Type == SourceType.Stalker
                        ? "Run a fresh portal sync and validation pass."
                        : "Run a fresh sync and validation pass for this source.",
                    ButtonText = profile.Type == SourceType.Stalker ? "Retest portal" : "Retest source",
                    IsPrimary = actions.Count == 0,
                    Tone = setupProblem ? SourceActivityTone.Warning : SourceActivityTone.Neutral
                });
            }

            if (staleProblem)
            {
                actions.Add(new SourceRepairAction
                {
                    ActionType = SourceRepairActionType.RepairRuntimeState,
                    Kind = SourceRepairActionKind.Apply,
                    Title = "Repair saved source state",
                    Summary = "Rebuild source health, logical state, and operational candidates without changing provider credentials.",
                    ButtonText = "Repair state",
                    IsPrimary = false,
                    Tone = SourceActivityTone.Neutral
                });
            }

            return actions
                .GroupBy(action => action.ActionType)
                .Select(group => group.First())
                .ToList();
        }

        private string ResolveRepairHeadline(
            SourceProfile profile,
            SourceCredential? credential,
            SourceDiagnosticsSnapshot diagnostics,
            SourceActivitySnapshot activity)
        {
            if (HasCompanionFailureSignal(credential, diagnostics, activity))
            {
                return "Companion routing is getting in the way";
            }

            if (profile.Type == SourceType.Stalker && !string.IsNullOrWhiteSpace(diagnostics.StalkerPortalErrorText))
            {
                return "Portal connection needs attention";
            }

            if (HasGuideFailureSignal(diagnostics))
            {
                return "Guide data needs attention";
            }

            if (diagnostics.LiveChannelCount + diagnostics.MovieCount + diagnostics.SeriesCount == 0 &&
                !string.IsNullOrWhiteSpace(diagnostics.FailureSummaryText))
            {
                return "Source setup needs a fresh connection test";
            }

            if (diagnostics.HealthLabel.Equals("Outdated", StringComparison.OrdinalIgnoreCase) ||
                diagnostics.StatusSummary.Contains("stale", StringComparison.OrdinalIgnoreCase))
            {
                return "Stored source state needs repair";
            }

            return "Source is connected and currently usable";
        }

        private string ResolveRepairSummary(
            SourceProfile profile,
            SourceCredential? credential,
            SourceDiagnosticsSnapshot diagnostics,
            SourceActivitySnapshot activity)
        {
            if (HasCompanionFailureSignal(credential, diagnostics, activity))
            {
                var companionProbe = diagnostics.HealthProbes
                    .Select(probe => probe.Summary)
                    .FirstOrDefault(summary => !string.IsNullOrWhiteSpace(summary) &&
                                               summary.Contains("companion", StringComparison.OrdinalIgnoreCase));
                return SanitizeText(string.IsNullOrWhiteSpace(companionProbe) ? diagnostics.ValidationResultText : companionProbe);
            }

            if (profile.Type == SourceType.Stalker && !string.IsNullOrWhiteSpace(diagnostics.StalkerPortalErrorText))
            {
                return SanitizeText(diagnostics.StalkerPortalErrorText);
            }

            if (HasGuideFailureSignal(diagnostics))
            {
                return SanitizeText(diagnostics.EpgStatusSummary);
            }

            if (diagnostics.LiveChannelCount + diagnostics.MovieCount + diagnostics.SeriesCount == 0 &&
                !string.IsNullOrWhiteSpace(diagnostics.FailureSummaryText))
            {
                return SanitizeText(diagnostics.FailureSummaryText);
            }

            return SanitizeText(string.IsNullOrWhiteSpace(activity.CurrentStateText) ? diagnostics.StatusSummary : activity.CurrentStateText);
        }

        private SourceSetupValidationSnapshot BuildValidationSnapshot(
            SourceType requestedType,
            SourceType? detectedType,
            bool canSave,
            string headline,
            string summary,
            string connection,
            string typeHint,
            IEnumerable<SourceGuidanceCapability> capabilities,
            IEnumerable<SourceGuidanceIssue> issues)
        {
            var snapshot = new SourceSetupValidationSnapshot
            {
                RequestedType = requestedType,
                DetectedTypeHint = detectedType,
                CanSave = canSave,
                HeadlineText = SanitizeText(headline),
                SummaryText = SanitizeText(summary),
                ConnectionText = SanitizeText(connection),
                TypeHintText = SanitizeText(typeHint),
                Capabilities = capabilities
                    .Where(item => !string.IsNullOrWhiteSpace(item.Label))
                    .ToList(),
                Issues = issues
                    .Where(item => !string.IsNullOrWhiteSpace(item.Title))
                    .ToList()
            };

            snapshot.CapabilitySummaryText = BuildCapabilitySummary(snapshot.Capabilities);
            snapshot.SafeReportText = BuildSetupSafeReport(snapshot);
            return snapshot;
        }

        private static SourceSetupDraft NormalizeDraft(SourceSetupDraft draft)
        {
            return new SourceSetupDraft
            {
                Name = draft.Name?.Trim() ?? string.Empty,
                Type = draft.Type,
                Url = NormalizeUrl(draft.Url, draft.Type),
                Username = draft.Type == SourceType.Xtream ? (draft.Username?.Trim() ?? string.Empty) : string.Empty,
                Password = draft.Type == SourceType.Xtream ? (draft.Password ?? string.Empty) : string.Empty,
                ManualEpgUrl = NormalizeOptionalUrl(draft.ManualEpgUrl),
                EpgMode = draft.EpgMode,
                ProxyScope = draft.ProxyScope,
                ProxyUrl = NormalizeOptionalUrl(draft.ProxyUrl),
                CompanionScope = draft.CompanionScope,
                CompanionMode = draft.CompanionMode,
                CompanionUrl = NormalizeCompanionUrl(draft.CompanionUrl),
                StalkerMacAddress = draft.Type == SourceType.Stalker ? NormalizeMacAddress(draft.StalkerMacAddress) : string.Empty,
                StalkerDeviceId = draft.Type == SourceType.Stalker ? NormalizeOptionalToken(draft.StalkerDeviceId) : string.Empty,
                StalkerSerialNumber = draft.Type == SourceType.Stalker ? NormalizeOptionalToken(draft.StalkerSerialNumber) : string.Empty,
                StalkerTimezone = draft.Type == SourceType.Stalker ? NormalizeOptionalToken(draft.StalkerTimezone, ResolveDefaultTimezone()) : string.Empty,
                StalkerLocale = draft.Type == SourceType.Stalker ? NormalizeOptionalToken(draft.StalkerLocale, System.Globalization.CultureInfo.CurrentCulture.Name) : string.Empty
            };
        }

        private static List<SourceGuidanceIssue> ValidateDraftInputs(SourceSetupDraft draft)
        {
            var issues = new List<SourceGuidanceIssue>();

            switch (draft.Type)
            {
                case SourceType.M3U when string.IsNullOrWhiteSpace(draft.Url):
                    issues.Add(CreateIssue("Playlist URL or file path is required.", SourceActivityTone.Failed));
                    break;
                case SourceType.Xtream when string.IsNullOrWhiteSpace(draft.Url) ||
                                            string.IsNullOrWhiteSpace(draft.Username) ||
                                            string.IsNullOrWhiteSpace(draft.Password):
                    issues.Add(CreateIssue("Server URL, username, and password are required for Xtream.", SourceActivityTone.Failed));
                    break;
                case SourceType.Stalker when string.IsNullOrWhiteSpace(draft.Url) ||
                                             string.IsNullOrWhiteSpace(draft.StalkerMacAddress):
                    issues.Add(CreateIssue("Portal URL and MAC address are required for Stalker.", SourceActivityTone.Failed));
                    break;
            }

            if (draft.EpgMode == EpgActiveMode.Manual && string.IsNullOrWhiteSpace(draft.ManualEpgUrl))
            {
                issues.Add(CreateIssue("Manual guide mode requires a manual XMLTV URL.", SourceActivityTone.Failed));
            }

            if (draft.ProxyScope != SourceProxyScope.Disabled && string.IsNullOrWhiteSpace(draft.ProxyUrl))
            {
                issues.Add(CreateIssue("Proxy routing requires a proxy URL.", SourceActivityTone.Failed));
            }

            if (draft.CompanionScope != SourceCompanionScope.Disabled && string.IsNullOrWhiteSpace(draft.CompanionUrl))
            {
                issues.Add(CreateIssue("Companion relay mode requires a companion endpoint URL.", SourceActivityTone.Failed));
            }

            if (!string.IsNullOrWhiteSpace(draft.ProxyUrl) &&
                draft.ProxyScope != SourceProxyScope.Disabled &&
                !LooksLikeAbsoluteOrLocalPath(draft.ProxyUrl))
            {
                issues.Add(CreateIssue("Proxy URL must be an absolute URL.", SourceActivityTone.Failed));
            }

            if (!string.IsNullOrWhiteSpace(draft.CompanionUrl) &&
                draft.CompanionScope != SourceCompanionScope.Disabled &&
                !Uri.TryCreate(draft.CompanionUrl, UriKind.Absolute, out _))
            {
                issues.Add(CreateIssue("Companion endpoint must be an absolute URL.", SourceActivityTone.Failed));
            }

            if (draft.Type != SourceType.M3U && !string.IsNullOrWhiteSpace(draft.Url) && !Uri.TryCreate(draft.Url, UriKind.Absolute, out _))
            {
                issues.Add(CreateIssue("This source type requires an absolute URL.", SourceActivityTone.Failed));
            }

            return issues;
        }

        private static SourceType? DetectTypeHint(SourceSetupDraft draft)
        {
            if (draft.Type == SourceType.Stalker || !string.IsNullOrWhiteSpace(draft.StalkerMacAddress))
            {
                return SourceType.Stalker;
            }

            if (draft.Type == SourceType.Xtream || !string.IsNullOrWhiteSpace(draft.Username))
            {
                return SourceType.Xtream;
            }

            var url = draft.Url ?? string.Empty;
            if (url.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(url))
            {
                return SourceType.M3U;
            }

            if (url.Contains("stalker", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("portal.php", StringComparison.OrdinalIgnoreCase))
            {
                return SourceType.Stalker;
            }

            if (url.Contains("player_api.php", StringComparison.OrdinalIgnoreCase))
            {
                return SourceType.Xtream;
            }

            return draft.Type;
        }

        private static IReadOnlyList<SourceGuidanceCapability> BuildRoutingCapabilities(SourceSetupDraft draft)
        {
            var capabilities = new List<SourceGuidanceCapability>();
            if (draft.ProxyScope != SourceProxyScope.Disabled)
            {
                capabilities.Add(new SourceGuidanceCapability
                {
                    Label = "Proxy",
                    Value = draft.ProxyScope switch
                    {
                        SourceProxyScope.PlaybackOnly => "Playback only",
                        SourceProxyScope.PlaybackAndProbing => "Playback + probes",
                        SourceProxyScope.AllRequests => "All requests",
                        _ => "Disabled"
                    },
                    Detail = string.IsNullOrWhiteSpace(draft.ProxyUrl) ? "Proxy configured." : draft.ProxyUrl,
                    Tone = SourceActivityTone.Neutral
                });
            }

            if (draft.CompanionScope != SourceCompanionScope.Disabled)
            {
                capabilities.Add(new SourceGuidanceCapability
                {
                    Label = "Companion",
                    Value = draft.CompanionMode == SourceCompanionRelayMode.Buffered ? "Buffered relay" : "Pass-through relay",
                    Detail = string.IsNullOrWhiteSpace(draft.CompanionUrl) ? "Companion configured." : draft.CompanionUrl,
                    Tone = SourceActivityTone.Neutral
                });
            }

            return capabilities;
        }

        private IReadOnlyList<SourceGuidanceIssue> BuildRoutingIssues(SourceSetupDraft draft)
        {
            var issues = new List<SourceGuidanceIssue>();
            if (draft.CompanionScope != SourceCompanionScope.Disabled)
            {
                issues.Add(new SourceGuidanceIssue
                {
                    Title = "Companion relay is optional",
                    Detail = "Keep direct playback as the normal path unless this provider behaves better behind a local relay.",
                    Tone = SourceActivityTone.Neutral
                });
            }

            if (draft.ProxyScope == SourceProxyScope.AllRequests)
            {
                issues.Add(new SourceGuidanceIssue
                {
                    Title = "All-requests proxy mode is broad",
                    Detail = "Import, guide, probe, and playback traffic will all route through the proxy. Use it only when the provider truly needs it.",
                    Tone = SourceActivityTone.Warning
                });
            }

            return issues;
        }

        private SourceCredential BuildCredential(SourceSetupDraft draft)
        {
            return new SourceCredential
            {
                Url = draft.Url,
                Username = draft.Username,
                Password = draft.Password,
                ManualEpgUrl = draft.ManualEpgUrl,
                EpgMode = draft.EpgMode,
                ProxyScope = draft.ProxyScope,
                ProxyUrl = draft.ProxyUrl,
                CompanionScope = draft.CompanionScope,
                CompanionMode = draft.CompanionMode,
                CompanionUrl = draft.CompanionUrl,
                StalkerMacAddress = draft.StalkerMacAddress,
                StalkerDeviceId = draft.StalkerDeviceId,
                StalkerSerialNumber = draft.StalkerSerialNumber,
                StalkerTimezone = draft.StalkerTimezone,
                StalkerLocale = draft.StalkerLocale
            };
        }

        private async Task<string> ReadTextPreviewAsync(
            string location,
            SourceCredential credential,
            SourceNetworkPurpose purpose,
            CancellationToken cancellationToken)
        {
            if (File.Exists(location))
            {
                await using var stream = File.OpenRead(location);
                using var reader = new StreamReader(stream);
                var buffer = new char[262144];
                var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
                return new string(buffer, 0, read);
            }

            using var client = _sourceRoutingService.CreateHttpClient(credential, purpose, TimeSpan.FromSeconds(20));
            using var response = await client.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var streamResponse = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var readerResponse = new StreamReader(streamResponse);
            var chars = new char[262144];
            var bytes = await readerResponse.ReadBlockAsync(chars, 0, chars.Length);
            return new string(chars, 0, bytes);
        }

        private static bool LooksLikeM3u(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            return content.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase) ||
                   content.Contains("#EXTINF:", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<JsonDocument> ReadJsonAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "[]" : content);
        }

        private static int CountJsonItems(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Array
                ? element.GetArrayLength()
                : 0;
        }

        private static IEnumerable<JsonElement> EnumerateJsonItems(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Array
                ? element.EnumerateArray()
                : Enumerable.Empty<JsonElement>();
        }

        private static string GetJsonString(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var property))
                {
                    return property.ValueKind switch
                    {
                        JsonValueKind.String => property.GetString() ?? string.Empty,
                        JsonValueKind.Number => property.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => string.Empty
                    };
                }
            }

            return string.Empty;
        }

        private static string BuildCatalogCapabilityLabel(int liveCount, int movieCount, bool hasSeries)
        {
            var families = new List<string>();
            if (liveCount > 0)
            {
                families.Add("Live");
            }

            if (movieCount > 0)
            {
                families.Add("Movies");
            }

            if (hasSeries)
            {
                families.Add("Series");
            }

            return families.Count == 0 ? "No catalog confirmed" : string.Join(" + ", families);
        }

        private static string BuildXtreamCatalogLabel(int liveCategories, int vodCategories, int seriesCategories)
        {
            var families = new List<string>();
            if (liveCategories > 0)
            {
                families.Add("Live");
            }

            if (vodCategories > 0)
            {
                families.Add("Movies");
            }

            if (seriesCategories > 0)
            {
                families.Add("Series");
            }

            return families.Count == 0 ? "No catalog confirmed" : string.Join(" + ", families);
        }

        private static string BuildStalkerCatalogLabel(StalkerPortalCatalog catalog)
        {
            var families = new List<string>();
            if (catalog.SupportsLive)
            {
                families.Add("Live");
            }

            if (catalog.SupportsMovies)
            {
                families.Add("Movies");
            }

            if (catalog.SupportsSeries)
            {
                families.Add("Series");
            }

            return families.Count == 0 ? "No catalog confirmed" : string.Join(" + ", families);
        }

        private static string BuildTypeHintText(SourceType requestedType, SourceType? detectedType, bool consistent)
        {
            if (!detectedType.HasValue)
            {
                return "KROIRA could not confidently classify this source yet.";
            }

            if (consistent || detectedType.Value == requestedType)
            {
                return $"{requestedType} setup and the tested endpoint look consistent.";
            }

            return $"This looks more like {detectedType.Value} than {requestedType}. Review the selected source type before saving.";
        }

        private bool HasGuideFailureSignal(SourceDiagnosticsSnapshot diagnostics)
        {
            if (diagnostics.ActiveEpgMode == EpgActiveMode.None)
            {
                return false;
            }

            return diagnostics.EpgStatus is EpgStatus.Stale or EpgStatus.FailedFetchOrParse ||
                   (diagnostics.LiveChannelCount > 0 && diagnostics.HasEpgUrl && !diagnostics.GuideAvailableForLive) ||
                   diagnostics.GuideWarningCount > 0;
        }

        private bool HasCompanionFailureSignal(
            SourceCredential? credential,
            SourceDiagnosticsSnapshot diagnostics,
            SourceActivitySnapshot activity)
        {
            if (credential == null || credential.CompanionScope == SourceCompanionScope.Disabled)
            {
                return false;
            }

            var texts = new[]
            {
                diagnostics.ValidationResultText,
                diagnostics.FailureSummaryText,
                diagnostics.StatusSummary,
                activity.HeadlineText,
                activity.CurrentStateText,
                activity.LatestAttemptText
            };

            if (texts.Any(text => ContainsCompanionFailureText(text)))
            {
                return true;
            }

            return diagnostics.HealthProbes.Any(probe => ContainsCompanionFailureText(probe.Summary));
        }

        private static bool ContainsCompanionFailureText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Contains("companion relay unavailable", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("companion relay responded", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("kept the direct provider path", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("companion relay timed out", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("companion relay could not be reached", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildRoutingDetail(SourceCredential? credential, SourceDiagnosticsSnapshot diagnostics)
        {
            if (credential == null)
            {
                return "Direct routing";
            }

            if (credential.CompanionScope != SourceCompanionScope.Disabled)
            {
                var companionMode = credential.CompanionMode == SourceCompanionRelayMode.Buffered ? "buffered relay" : "pass-through relay";
                return string.IsNullOrWhiteSpace(credential.CompanionUrl)
                    ? $"Companion enabled ({companionMode})."
                    : $"Companion enabled ({companionMode}) via {credential.CompanionUrl}.";
            }

            if (credential.ProxyScope != SourceProxyScope.Disabled)
            {
                return string.IsNullOrWhiteSpace(credential.ProxyUrl)
                    ? diagnostics.ProxyStatusText
                    : $"{diagnostics.ProxyStatusText}.";
            }

            return "Direct routing";
        }

        private static string BuildCapabilitySummary(IReadOnlyList<SourceGuidanceCapability> capabilities)
        {
            return string.Join(
                " / ",
                capabilities
                    .Take(4)
                    .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                    .Select(item => $"{item.Label}: {item.Value}"));
        }

        private static string BuildRepairCapabilitySummary(IReadOnlyList<SourceGuidanceCapability> capabilities)
        {
            return string.Join(
                " / ",
                capabilities
                    .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                    .Take(4)
                    .Select(item => $"{item.Label}: {item.Value}"));
        }

        private string BuildSetupSafeReport(SourceSetupValidationSnapshot snapshot)
        {
            var builder = new StringBuilder();
            builder.AppendLine("KROIRA source setup report");
            builder.AppendLine("Sensitive values are redacted.");
            builder.AppendLine();
            builder.AppendLine($"Requested type: {snapshot.RequestedType}");
            builder.AppendLine($"Headline: {snapshot.HeadlineText}");
            builder.AppendLine($"Summary: {snapshot.SummaryText}");
            builder.AppendLine($"Connection: {snapshot.ConnectionText}");
            if (!string.IsNullOrWhiteSpace(snapshot.TypeHintText))
            {
                builder.AppendLine($"Type hint: {snapshot.TypeHintText}");
            }

            if (snapshot.Capabilities.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Capabilities");
                foreach (var capability in snapshot.Capabilities)
                {
                    builder.AppendLine($"{capability.Label}: {capability.Value} - {SanitizeText(capability.Detail)}");
                }
            }

            if (snapshot.Issues.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Issues");
                foreach (var issue in snapshot.Issues)
                {
                    builder.AppendLine($"{issue.Title}: {SanitizeText(issue.Detail)}");
                }
            }

            return builder.ToString().Trim();
        }

        private string BuildRepairSafeReport(
            SourceProfile profile,
            SourceDiagnosticsSnapshot diagnostics,
            SourceActivitySnapshot activity,
            SourceRepairSnapshot snapshot)
        {
            var builder = new StringBuilder();
            builder.AppendLine("KROIRA source repair report");
            builder.AppendLine("Sensitive values are redacted.");
            builder.AppendLine();
            builder.AppendLine($"Source: {SanitizeText(profile.Name)}");
            builder.AppendLine($"Type: {profile.Type}");
            builder.AppendLine($"Headline: {snapshot.HeadlineText}");
            builder.AppendLine($"Summary: {snapshot.SummaryText}");
            builder.AppendLine($"Current status: {SanitizeText(snapshot.StatusText)}");
            builder.AppendLine($"Activity focus: {SanitizeText(activity.CurrentStateText)}");
            builder.AppendLine($"Latest attempt: {SanitizeText(activity.LatestAttemptText)}");
            builder.AppendLine($"Last success: {SanitizeText(activity.LastSuccessText)}");
            builder.AppendLine($"Health: {SanitizeText(diagnostics.HealthLabel)} / {diagnostics.HealthScore}/100");

            if (snapshot.Capabilities.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Capabilities");
                foreach (var capability in snapshot.Capabilities)
                {
                    builder.AppendLine($"{capability.Label}: {capability.Value} - {SanitizeText(capability.Detail)}");
                }
            }

            if (snapshot.Issues.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Current issues");
                foreach (var issue in snapshot.Issues)
                {
                    builder.AppendLine($"{issue.Title}: {SanitizeText(issue.Detail)}");
                }
            }

            if (snapshot.Actions.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Suggested actions");
                foreach (var action in snapshot.Actions)
                {
                    builder.AppendLine($"{action.ButtonText}: {SanitizeText(action.Summary)}");
                }
            }

            return builder.ToString().Trim();
        }

        private string BuildRepairExecutionReport(
            SourceRepairActionType actionType,
            bool success,
            string detailText,
            string changeText)
        {
            var builder = new StringBuilder();
            builder.AppendLine("KROIRA repair execution");
            builder.AppendLine("Sensitive values are redacted.");
            builder.AppendLine();
            builder.AppendLine($"Action: {actionType}");
            builder.AppendLine($"Result: {(success ? "Completed" : "Needs review")}");
            if (!string.IsNullOrWhiteSpace(detailText))
            {
                builder.AppendLine($"Detail: {SanitizeText(detailText)}");
            }

            if (!string.IsNullOrWhiteSpace(changeText))
            {
                builder.AppendLine($"Change: {SanitizeText(changeText)}");
            }

            return builder.ToString().Trim();
        }

        private string BuildRepairChangeText(
            SourceDiagnosticsSnapshot? before,
            SourceDiagnosticsSnapshot? after,
            SourceRepairSnapshot? beforeSnapshot,
            SourceRepairSnapshot? afterSnapshot)
        {
            if (before == null || after == null)
            {
                return string.Empty;
            }

            var changes = new List<string>();
            if (before.HealthScore != after.HealthScore)
            {
                var delta = after.HealthScore - before.HealthScore;
                changes.Add($"Health moved from {before.HealthScore}/100 to {after.HealthScore}/100 ({(delta >= 0 ? "+" : string.Empty)}{delta}).");
            }

            if (!string.Equals(before.EpgStatusText, after.EpgStatusText, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add($"Guide state changed from {SanitizeText(before.EpgStatusText)} to {SanitizeText(after.EpgStatusText)}.");
            }

            if (!string.Equals(before.CompanionStatusText, after.CompanionStatusText, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add($"Companion policy changed from {SanitizeText(before.CompanionStatusText)} to {SanitizeText(after.CompanionStatusText)}.");
            }

            if (beforeSnapshot != null &&
                afterSnapshot != null &&
                !string.Equals(beforeSnapshot.HeadlineText, afterSnapshot.HeadlineText, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add($"Top repair focus changed from {SanitizeText(beforeSnapshot.HeadlineText)} to {SanitizeText(afterSnapshot.HeadlineText)}.");
            }

            if (changes.Count == 0)
            {
                return "The repair completed, but the source health summary did not move materially yet.";
            }

            return string.Join(" ", changes);
        }

        private string SanitizeText(string? value)
        {
            return _redactionService.RedactLooseText(value);
        }

        private static SourceGuidanceIssue CreateIssue(string detail, SourceActivityTone tone)
        {
            return new SourceGuidanceIssue
            {
                Title = "Setup input needs attention",
                Detail = detail,
                Tone = tone
            };
        }

        private static bool LooksLikeAbsoluteOrLocalPath(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out _) || Path.IsPathRooted(value);
        }

        private static string NormalizeUrl(string? value, SourceType type)
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

        private static string NormalizeOptionalUrl(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeCompanionUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
                ? uri.ToString().TrimEnd('/')
                : value.Trim();
        }

        private static string NormalizeOptionalToken(string? value, string fallback = "")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string NormalizeMacAddress(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var hex = new string(value.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
            if (hex.Length < 12)
            {
                return value.Trim();
            }

            return string.Join(":", Enumerable.Range(0, 6).Select(index => hex.Substring(index * 2, 2)));
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
    }
}
