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
                    headline: L("SourceGuidance_Setup_NeedsAttention"),
                    summary: validationIssues[0].Detail,
                    connection: L("SourceGuidance_Setup_CouldNotVerify"),
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
                        headline: L("SourceGuidance_Setup_UnsupportedFormatHeadline"),
                        summary: L("SourceGuidance_Setup_UnsupportedFormatSummary"),
                        connection: L("SourceGuidance_Setup_NoValidationPath"),
                        typeHint: string.Empty,
                        capabilities: Array.Empty<SourceGuidanceCapability>(),
                        issues: new[]
                        {
                            new SourceGuidanceIssue
                            {
                                Title = L("SourceGuidance_Issue_UnsupportedSourceType_Title"),
                                Detail = L("SourceGuidance_Issue_UnsupportedSourceType_Detail"),
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
                        Title = L("SourceGuidance_Issue_ConnectionTestFailed_Title"),
                        Detail = SanitizeText(ex.Message),
                        Tone = SourceActivityTone.Failed
                    }
                };

                return BuildValidationSnapshot(
                    normalized.Type,
                    DetectTypeHint(normalized),
                    canSave: false,
                    headline: L("SourceGuidance_Setup_NeedsAttention"),
                    summary: SanitizeText(ex.Message),
                    connection: L("SourceGuidance_Setup_ConnectionTestIncomplete"),
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
                                throw new InvalidOperationException(L("SourceLifecycle_Error_CredentialsNotFound"));
                            }

                            if (credential.CompanionScope == SourceCompanionScope.Disabled)
                            {
                                detailBuilder.Add(L("SourceGuidance_Repair_Detail_CompanionAlreadyDisabled"));
                            }
                            else
                            {
                                var update = await lifecycleService.UpdateGuideSettingsAsync(
                                    new SourceGuideSettingsUpdateRequest
                                    {
                                        SourceId = sourceId,
                                        ActiveMode = credential.EpgMode,
                                        ManualEpgUrl = credential.ManualEpgUrl,
                                        FallbackEpgUrls = credential.FallbackEpgUrls,
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
                            detailBuilder.Add(L("SourceGuidance_Repair_Detail_StateRebuilt"));
                            break;
                        }

                    case SourceRepairActionType.RunStreamProbe:
                        {
                            await sourceHealthService.RefreshSourceHealthAsync(db, sourceId, forceProbe: true);
                            detailBuilder.Add(L("SourceGuidance_Repair_Detail_StreamProbeRan"));
                            break;
                        }

                    default:
                        throw new InvalidOperationException(L("SourceGuidance_Repair_Error_ActionNotDirect"));
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
                    HeadlineText = L("SourceGuidance_Repair_Result_NeedsReview"),
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
                ? (afterSnapshot?.IsStable == true ? L("SourceGuidance_Repair_Result_AppliedCleanly") : L("SourceGuidance_Repair_Result_AppliedNeedsAttention"))
                : L("SourceGuidance_Repair_Result_NeedsReview");

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
                throw new InvalidOperationException(L("SourceGuidance_M3u_Error_NotPlaylist"));
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
                throw new InvalidOperationException(L("SourceGuidance_M3u_Error_NoEntries"));
            }

            var capabilities = new List<SourceGuidanceCapability>
            {
                new()
                {
                    Label = L("SourceGuidance_Capability_Connection_Label"),
                    Value = L("SourceGuidance_Capability_PlaylistReachable"),
                    Detail = F("SourceGuidance_M3u_Detail_SampledEntries", sampledEntries),
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = L("SourceGuidance_Capability_Catalog_Label"),
                    Value = BuildCatalogCapabilityLabel(liveCount, movieCount, episodeCount > 0),
                    Detail = F("SourceGuidance_M3u_Detail_CatalogCounts", liveCount, movieCount, episodeCount),
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = L("SourceGuidance_Capability_Guide_Label"),
                    Value = header.XmltvUrls.Count > 0
                        ? L("SourceGuidance_Capability_GuideFound")
                        : draft.EpgMode == EpgActiveMode.Manual
                            ? L("SourceGuidance_Capability_ManualGuideSet")
                            : L("SourceGuidance_Capability_NoGuideAdvertised"),
                    Detail = header.XmltvUrls.Count > 0
                        ? SanitizeText(header.XmltvUrls[0])
                        : draft.EpgMode == EpgActiveMode.Manual
                            ? SanitizeText(draft.ManualEpgUrl)
                            : L("SourceGuidance_M3u_Detail_NoXmltvAdvertised"),
                    Tone = header.XmltvUrls.Count > 0 || draft.EpgMode == EpgActiveMode.Manual
                        ? SourceActivityTone.Healthy
                        : SourceActivityTone.Warning
                },
                new()
                {
                    Label = L("SourceGuidance_Capability_Catchup_Label"),
                    Value = catchupCount > 0 ? F("SourceGuidance_Capability_Hints", catchupCount) : L("SourceGuidance_Capability_NoClearHints"),
                    Detail = catchupCount > 0
                        ? L("SourceGuidance_M3u_Detail_CatchupDetected")
                        : L("SourceGuidance_M3u_Detail_CatchupNotAdvertised"),
                    Tone = catchupCount > 0 ? SourceActivityTone.Healthy : SourceActivityTone.Neutral
                }
            };
            capabilities.AddRange(BuildRoutingCapabilities(draft));

            var issues = new List<SourceGuidanceIssue>(BuildRoutingIssues(draft));
            if (header.XmltvUrls.Count == 0 && draft.EpgMode == EpgActiveMode.Detected && liveCount > 0)
            {
                issues.Add(new SourceGuidanceIssue
                {
                    Title = L("SourceGuidance_Issue_NoGuideUrlDetected_Title"),
                    Detail = L("SourceGuidance_Issue_NoGuideUrlDetected_Detail"),
                    Tone = SourceActivityTone.Warning
                });
            }

            return BuildValidationSnapshot(
                draft.Type,
                SourceType.M3U,
                canSave: true,
                headline: L("SourceGuidance_M3u_Headline_Importable"),
                summary: F("SourceGuidance_M3u_Summary_SampledCatalog", BuildCatalogCapabilityLabel(liveCount, movieCount, episodeCount > 0)),
                connection: L("SourceGuidance_M3u_Connection_Success"),
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
                throw new InvalidOperationException(L("SourceGuidance_Xtream_Error_NoCategories"));
            }

            var capabilities = new List<SourceGuidanceCapability>
            {
                new()
                {
                    Label = L("SourceGuidance_Capability_Connection_Label"),
                    Value = L("SourceGuidance_Xtream_Value_CredentialsAccepted"),
                    Detail = F("SourceGuidance_Xtream_Detail_LiveRowsReachable", liveStreamCount),
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = L("SourceGuidance_Capability_Catalog_Label"),
                    Value = BuildXtreamCatalogLabel(liveCategoryCount, vodCategoryCount, seriesCategoryCount),
                    Detail = F("SourceGuidance_Xtream_Detail_CategoryCounts", liveCategoryCount, vodCategoryCount, seriesCategoryCount),
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = L("SourceGuidance_Capability_Guide_Label"),
                    Value = L("SourceGuidance_Xtream_Value_GuideDerived"),
                    Detail = SanitizeText($"{baseUrl}/xmltv.php{authQuery}"),
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = L("SourceGuidance_Capability_Catchup_Label"),
                    Value = catchupCount > 0 ? F("SourceGuidance_Xtream_Value_ArchiveHints", catchupCount) : L("SourceGuidance_Capability_NoClearHints"),
                    Detail = catchupCount > 0
                        ? L("SourceGuidance_Xtream_Detail_ArchiveDetected")
                        : L("SourceGuidance_Xtream_Detail_ArchiveNotAdvertised"),
                    Tone = catchupCount > 0 ? SourceActivityTone.Healthy : SourceActivityTone.Neutral
                }
            };
            capabilities.AddRange(BuildRoutingCapabilities(draft));

            var issues = new List<SourceGuidanceIssue>(BuildRoutingIssues(draft));
            if (vodCategoryCount == 0)
            {
                issues.Add(new SourceGuidanceIssue
                {
                    Title = L("SourceGuidance_Issue_MovieCatalogNotConfirmed_Title"),
                    Detail = L("SourceGuidance_Issue_MovieCatalogNotConfirmed_Detail"),
                    Tone = SourceActivityTone.Warning
                });
            }

            if (seriesCategoryCount == 0)
            {
                issues.Add(new SourceGuidanceIssue
                {
                    Title = L("SourceGuidance_Issue_SeriesCatalogNotConfirmed_Title"),
                    Detail = L("SourceGuidance_Issue_SeriesCatalogNotConfirmed_Detail"),
                    Tone = SourceActivityTone.Neutral
                });
            }

            return BuildValidationSnapshot(
                draft.Type,
                SourceType.Xtream,
                canSave: true,
                headline: L("SourceGuidance_Xtream_Headline_Reachable"),
                summary: F("SourceGuidance_Xtream_Summary_PlayerApiConfirmed", BuildXtreamCatalogLabel(liveCategoryCount, vodCategoryCount, seriesCategoryCount)),
                connection: L("SourceGuidance_Xtream_Connection_Success"),
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
                    Label = L("SourceGuidance_Capability_Connection_Label"),
                    Value = L("SourceGuidance_Stalker_Value_PortalReachable"),
                    Detail = string.IsNullOrWhiteSpace(catalog.DiscoveredApiUrl)
                        ? L("SourceGuidance_Stalker_Detail_HandshakeCompleted")
                        : SanitizeText(catalog.DiscoveredApiUrl),
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = L("SourceGuidance_Capability_Catalog_Label"),
                    Value = BuildStalkerCatalogLabel(catalog),
                    Detail = F("SourceGuidance_Stalker_Detail_CatalogCounts", catalog.LiveChannels.Count, catalog.Movies.Count, catalog.Series.Count),
                    Tone = catalog.SupportsLive || catalog.SupportsMovies || catalog.SupportsSeries
                        ? SourceActivityTone.Healthy
                        : SourceActivityTone.Warning
                },
                new()
                {
                    Label = L("SourceGuidance_Capability_PortalProfile_Label"),
                    Value = string.IsNullOrWhiteSpace(catalog.ProfileName) ? L("SourceGuidance_Stalker_Value_ProfileDiscovered") : catalog.ProfileName,
                    Detail = string.IsNullOrWhiteSpace(catalog.PortalName)
                        ? L("SourceGuidance_Stalker_Detail_PortalHandshakeCompleted")
                        : catalog.PortalName,
                    Tone = SourceActivityTone.Healthy
                },
                new()
                {
                    Label = L("SourceGuidance_Capability_Guide_Label"),
                    Value = draft.EpgMode == EpgActiveMode.Manual && !string.IsNullOrWhiteSpace(draft.ManualEpgUrl)
                        ? L("SourceGuidance_Capability_ManualGuideSet")
                        : L("SourceGuidance_Stalker_Value_ManualGuideOptional"),
                    Detail = draft.EpgMode == EpgActiveMode.Manual && !string.IsNullOrWhiteSpace(draft.ManualEpgUrl)
                        ? SanitizeText(draft.ManualEpgUrl)
                        : L("SourceGuidance_Stalker_Detail_ManualGuideOptional"),
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
                    Title = L("SourceGuidance_Issue_PartialCatalog_Title"),
                    Detail = SanitizeText(warning),
                    Tone = SourceActivityTone.Warning
                });
            }

            if (!catalog.SupportsLive && !catalog.SupportsMovies && !catalog.SupportsSeries)
            {
                issues.Add(new SourceGuidanceIssue
                {
                    Title = L("SourceGuidance_Issue_NoCatalogFamilies_Title"),
                    Detail = L("SourceGuidance_Issue_NoCatalogFamilies_Detail"),
                    Tone = SourceActivityTone.Warning
                });
            }

            return BuildValidationSnapshot(
                draft.Type,
                SourceType.Stalker,
                canSave: true,
                headline: L("SourceGuidance_Stalker_Headline_HandshakeCompleted"),
                summary: F("SourceGuidance_Stalker_Summary_PreviewConfirmed", BuildStalkerCatalogLabel(catalog)),
                connection: L("SourceGuidance_Stalker_Connection_Success"),
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
                    Label = L("SourceGuidance_Capability_Catalog_Label"),
                    Value = BuildCatalogCapabilityLabel(diagnostics.LiveChannelCount, diagnostics.MovieCount, diagnostics.SeriesCount > 0),
                    Detail = F("SourceGuidance_Repair_Detail_CatalogCounts", diagnostics.LiveChannelCount, diagnostics.MovieCount, diagnostics.SeriesCount),
                    Tone = diagnostics.LiveChannelCount + diagnostics.MovieCount + diagnostics.SeriesCount > 0
                        ? SourceActivityTone.Healthy
                        : SourceActivityTone.Warning
                },
                new()
                {
                    Label = L("SourceGuidance_Capability_Guide_Label"),
                    Value = diagnostics.ActiveEpgMode == EpgActiveMode.None
                        ? L("SourceGuidance_Capability_GuideDisabled")
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
                    Label = L("SourceGuidance_Capability_Catchup_Label"),
                    Value = diagnostics.CatchupChannelCount > 0 ? F("SourceGuidance_Capability_Channels", diagnostics.CatchupChannelCount) : L("SourceGuidance_Capability_NoAdvertisedSupport"),
                    Detail = SanitizeText(diagnostics.CatchupStatusText),
                    Tone = diagnostics.CatchupChannelCount > 0 ? SourceActivityTone.Healthy : SourceActivityTone.Neutral
                },
                new()
                {
                    Label = L("SourceGuidance_Capability_Routing_Label"),
                    Value = credential == null
                        ? L("SourceGuidance_Routing_Direct")
                        : credential.CompanionScope != SourceCompanionScope.Disabled
                            ? L("SourceGuidance_Routing_CompanionEnabled")
                            : credential.ProxyScope != SourceProxyScope.Disabled
                                ? L("SourceGuidance_Routing_ProxyEnabled")
                                : L("SourceGuidance_Routing_Direct"),
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
                    Title = string.IsNullOrWhiteSpace(activity.HeadlineText) ? L("SourceGuidance_Issue_SourceStable_Title") : SanitizeText(activity.HeadlineText),
                    Detail = string.IsNullOrWhiteSpace(activity.CurrentStateText)
                        ? SanitizeText(diagnostics.StatusSummary)
                        : SanitizeText(activity.CurrentStateText),
                    Tone = IsHealthyOrGood(diagnostics.HealthLabel)
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
                    Title = L("SourceGuidance_Action_DisableCompanion_Title"),
                    Summary = L("SourceGuidance_Action_DisableCompanion_Summary"),
                    ButtonText = L("SourceGuidance_Action_DisableCompanion_Button"),
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
                    Title = L("SourceGuidance_Action_RefreshPortalProfile_Title"),
                    Summary = L("SourceGuidance_Action_RefreshPortalProfile_Summary"),
                    ButtonText = L("SourceGuidance_Action_RefreshPortalProfile_Button"),
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
                        Title = L("SourceGuidance_Action_RetestGuide_Title"),
                        Summary = L("SourceGuidance_Action_RetestGuide_Summary"),
                        ButtonText = L("SourceGuidance_Action_RetestGuide_Button"),
                        IsPrimary = actions.Count == 0,
                        Tone = SourceActivityTone.Warning
                    });
                }

                actions.Add(new SourceRepairAction
                {
                    ActionType = SourceRepairActionType.ReviewGuideSettings,
                    Kind = SourceRepairActionKind.Review,
                    Title = L("SourceGuidance_Action_ReviewGuideSettings_Title"),
                    Summary = L("SourceGuidance_Action_ReviewGuideSettings_Summary"),
                    ButtonText = L("SourceGuidance_Action_ReviewGuideSettings_Button"),
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
                    Title = profile.Type == SourceType.Stalker ? L("SourceGuidance_Action_RetestPortal_Title") : L("SourceGuidance_Action_RetestSource_Title"),
                    Summary = profile.Type == SourceType.Stalker
                        ? L("SourceGuidance_Action_RetestPortal_Summary")
                        : L("SourceGuidance_Action_RetestSource_Summary"),
                    ButtonText = profile.Type == SourceType.Stalker ? L("SourceGuidance_Action_RetestPortal_Button") : L("SourceGuidance_Action_RetestSource_Button"),
                    IsPrimary = actions.Count == 0,
                    Tone = setupProblem ? SourceActivityTone.Warning : SourceActivityTone.Neutral
                });
            }

            if (diagnostics.LiveChannelCount + diagnostics.MovieCount > 0 || diagnostics.EpisodeCount > 0)
            {
                actions.Add(new SourceRepairAction
                {
                    ActionType = SourceRepairActionType.RunStreamProbe,
                    Kind = SourceRepairActionKind.Apply,
                    Title = L("SourceGuidance_Action_RunStreamProbe_Title"),
                    Summary = L("SourceGuidance_Action_RunStreamProbe_Summary"),
                    ButtonText = L("SourceGuidance_Action_RunStreamProbe_Button"),
                    IsPrimary = actions.Count == 0,
                    Tone = SourceActivityTone.Neutral
                });
            }

            if (staleProblem)
            {
                actions.Add(new SourceRepairAction
                {
                    ActionType = SourceRepairActionType.RepairRuntimeState,
                    Kind = SourceRepairActionKind.Apply,
                    Title = L("SourceGuidance_Action_RepairRuntimeState_Title"),
                    Summary = L("SourceGuidance_Action_RepairRuntimeState_Summary"),
                    ButtonText = L("SourceGuidance_Action_RepairRuntimeState_Button"),
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
                return L("SourceGuidance_Repair_Headline_CompanionProblem");
            }

            if (profile.Type == SourceType.Stalker && !string.IsNullOrWhiteSpace(diagnostics.StalkerPortalErrorText))
            {
                return L("SourceGuidance_Repair_Headline_PortalNeedsAttention");
            }

            if (HasGuideFailureSignal(diagnostics))
            {
                return L("SourceGuidance_Repair_Headline_GuideNeedsAttention");
            }

            if (diagnostics.LiveChannelCount + diagnostics.MovieCount + diagnostics.SeriesCount == 0 &&
                !string.IsNullOrWhiteSpace(diagnostics.FailureSummaryText))
            {
                return L("SourceGuidance_Repair_Headline_FreshConnectionTest");
            }

            if (diagnostics.HealthLabel.Equals("Outdated", StringComparison.OrdinalIgnoreCase) ||
                diagnostics.StatusSummary.Contains("stale", StringComparison.OrdinalIgnoreCase))
            {
                return L("SourceGuidance_Repair_Headline_StoredStateNeedsRepair");
            }

            return L("SourceGuidance_Repair_Headline_ConnectedUsable");
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
                FallbackEpgUrls = NormalizeGuideUrlList(draft.FallbackEpgUrls),
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
                    issues.Add(CreateIssue(L("SourceGuidance_Validation_PlaylistUrlRequired"), SourceActivityTone.Failed));
                    break;
                case SourceType.Xtream when string.IsNullOrWhiteSpace(draft.Url) ||
                                            string.IsNullOrWhiteSpace(draft.Username) ||
                                            string.IsNullOrWhiteSpace(draft.Password):
                    issues.Add(CreateIssue(L("SourceGuidance_Validation_XtreamRequired"), SourceActivityTone.Failed));
                    break;
                case SourceType.Stalker when string.IsNullOrWhiteSpace(draft.Url) ||
                                             string.IsNullOrWhiteSpace(draft.StalkerMacAddress):
                    issues.Add(CreateIssue(L("SourceGuidance_Validation_StalkerRequired"), SourceActivityTone.Failed));
                    break;
            }

            if (draft.EpgMode == EpgActiveMode.Manual && string.IsNullOrWhiteSpace(draft.ManualEpgUrl))
            {
                issues.Add(CreateIssue(L("SourceGuidance_Validation_ManualXmltvRequired"), SourceActivityTone.Failed));
            }

            if (draft.ProxyScope != SourceProxyScope.Disabled && string.IsNullOrWhiteSpace(draft.ProxyUrl))
            {
                issues.Add(CreateIssue(L("SourceGuidance_Validation_ProxyUrlRequired"), SourceActivityTone.Failed));
            }

            if (draft.CompanionScope != SourceCompanionScope.Disabled && string.IsNullOrWhiteSpace(draft.CompanionUrl))
            {
                issues.Add(CreateIssue(L("SourceGuidance_Validation_CompanionEndpointRequired"), SourceActivityTone.Failed));
            }

            if (!string.IsNullOrWhiteSpace(draft.ProxyUrl) &&
                draft.ProxyScope != SourceProxyScope.Disabled &&
                !LooksLikeAbsoluteOrLocalPath(draft.ProxyUrl))
            {
                issues.Add(CreateIssue(L("SourceGuidance_Validation_ProxyUrlAbsolute"), SourceActivityTone.Failed));
            }

            if (!string.IsNullOrWhiteSpace(draft.CompanionUrl) &&
                draft.CompanionScope != SourceCompanionScope.Disabled &&
                !Uri.TryCreate(draft.CompanionUrl, UriKind.Absolute, out _))
            {
                issues.Add(CreateIssue(L("SourceGuidance_Validation_CompanionEndpointAbsolute"), SourceActivityTone.Failed));
            }

            if (draft.Type != SourceType.M3U && !string.IsNullOrWhiteSpace(draft.Url) && !Uri.TryCreate(draft.Url, UriKind.Absolute, out _))
            {
                issues.Add(CreateIssue(L("SourceGuidance_Validation_SourceTypeAbsoluteUrl"), SourceActivityTone.Failed));
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
                    Label = L("SourceGuidance_Capability_Proxy_Label"),
                    Value = draft.ProxyScope switch
                    {
                        SourceProxyScope.PlaybackOnly => L("SourceGuidance_Routing_PlaybackOnly"),
                        SourceProxyScope.PlaybackAndProbing => L("SourceGuidance_Routing_PlaybackProbes"),
                        SourceProxyScope.AllRequests => L("SourceGuidance_Routing_AllRequests"),
                        _ => L("General_Disabled")
                    },
                    Detail = string.IsNullOrWhiteSpace(draft.ProxyUrl) ? L("SourceGuidance_Routing_ProxyConfigured") : draft.ProxyUrl,
                    Tone = SourceActivityTone.Neutral
                });
            }

            if (draft.CompanionScope != SourceCompanionScope.Disabled)
            {
                capabilities.Add(new SourceGuidanceCapability
                {
                    Label = L("SourceGuidance_Capability_Companion_Label"),
                    Value = draft.CompanionMode == SourceCompanionRelayMode.Buffered ? L("SourceGuidance_Routing_BufferedRelay") : L("SourceGuidance_Routing_PassThroughRelay"),
                    Detail = string.IsNullOrWhiteSpace(draft.CompanionUrl) ? L("SourceGuidance_Routing_CompanionConfigured") : draft.CompanionUrl,
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
                    Title = L("SourceGuidance_Issue_CompanionOptional_Title"),
                    Detail = L("SourceGuidance_Issue_CompanionOptional_Detail"),
                    Tone = SourceActivityTone.Neutral
                });
            }

            if (draft.ProxyScope == SourceProxyScope.AllRequests)
            {
                issues.Add(new SourceGuidanceIssue
                {
                    Title = L("SourceGuidance_Issue_AllRequestsProxyBroad_Title"),
                    Detail = L("SourceGuidance_Issue_AllRequestsProxyBroad_Detail"),
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
                FallbackEpgUrls = draft.FallbackEpgUrls,
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
                families.Add(L("SourceGuidance_Catalog_Live"));
            }

            if (movieCount > 0)
            {
                families.Add(L("SourceGuidance_Catalog_Movies"));
            }

            if (hasSeries)
            {
                families.Add(L("SourceGuidance_Catalog_Series"));
            }

            return families.Count == 0 ? L("SourceGuidance_Catalog_NoneConfirmed") : string.Join(" + ", families);
        }

        private static string BuildXtreamCatalogLabel(int liveCategories, int vodCategories, int seriesCategories)
        {
            var families = new List<string>();
            if (liveCategories > 0)
            {
                families.Add(L("SourceGuidance_Catalog_Live"));
            }

            if (vodCategories > 0)
            {
                families.Add(L("SourceGuidance_Catalog_Movies"));
            }

            if (seriesCategories > 0)
            {
                families.Add(L("SourceGuidance_Catalog_Series"));
            }

            return families.Count == 0 ? L("SourceGuidance_Catalog_NoneConfirmed") : string.Join(" + ", families);
        }

        private static string BuildStalkerCatalogLabel(StalkerPortalCatalog catalog)
        {
            var families = new List<string>();
            if (catalog.SupportsLive)
            {
                families.Add(L("SourceGuidance_Catalog_Live"));
            }

            if (catalog.SupportsMovies)
            {
                families.Add(L("SourceGuidance_Catalog_Movies"));
            }

            if (catalog.SupportsSeries)
            {
                families.Add(L("SourceGuidance_Catalog_Series"));
            }

            return families.Count == 0 ? L("SourceGuidance_Catalog_NoneConfirmed") : string.Join(" + ", families);
        }

        private static string BuildTypeHintText(SourceType requestedType, SourceType? detectedType, bool consistent)
        {
            if (!detectedType.HasValue)
            {
                return L("SourceGuidance_TypeHint_Unclassified");
            }

            if (consistent || detectedType.Value == requestedType)
            {
                return F("SourceGuidance_TypeHint_Consistent", requestedType);
            }

            return F("SourceGuidance_TypeHint_Mismatch", detectedType.Value, requestedType);
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
                return L("SourceGuidance_Routing_DirectRouting");
            }

            if (credential.CompanionScope != SourceCompanionScope.Disabled)
            {
                var companionMode = credential.CompanionMode == SourceCompanionRelayMode.Buffered ? L("SourceGuidance_Routing_BufferedRelayLower") : L("SourceGuidance_Routing_PassThroughRelayLower");
                return string.IsNullOrWhiteSpace(credential.CompanionUrl)
                    ? F("SourceGuidance_Routing_CompanionEnabledMode", companionMode)
                    : F("SourceGuidance_Routing_CompanionEnabledVia", companionMode, credential.CompanionUrl);
            }

            if (credential.ProxyScope != SourceProxyScope.Disabled)
            {
                return string.IsNullOrWhiteSpace(credential.ProxyUrl)
                    ? diagnostics.ProxyStatusText
                    : $"{diagnostics.ProxyStatusText}.";
            }

            return L("SourceGuidance_Routing_DirectRouting");
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

        private static bool IsHealthyOrGood(string? healthLabel)
        {
            return healthLabel != null &&
                   (healthLabel.Equals("Healthy", StringComparison.OrdinalIgnoreCase) ||
                    healthLabel.Equals("Good", StringComparison.OrdinalIgnoreCase));
        }

        private string BuildSetupSafeReport(SourceSetupValidationSnapshot snapshot)
        {
            var builder = new StringBuilder();
            builder.AppendLine(L("SourceGuidance_Report_SetupTitle"));
            builder.AppendLine(L("SourceGuidance_Report_RedactedNotice"));
            builder.AppendLine();
            builder.AppendLine(F("SourceGuidance_Report_RequestedType", snapshot.RequestedType));
            builder.AppendLine(F("SourceGuidance_Report_Headline", snapshot.HeadlineText));
            builder.AppendLine(F("SourceGuidance_Report_Summary", snapshot.SummaryText));
            builder.AppendLine(F("SourceGuidance_Report_Connection", snapshot.ConnectionText));
            if (!string.IsNullOrWhiteSpace(snapshot.TypeHintText))
            {
                builder.AppendLine(F("SourceGuidance_Report_TypeHint", snapshot.TypeHintText));
            }

            if (snapshot.Capabilities.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(L("SourceGuidance_Report_Capabilities"));
                foreach (var capability in snapshot.Capabilities)
                {
                    builder.AppendLine($"{capability.Label}: {capability.Value} - {SanitizeText(capability.Detail)}");
                }
            }

            if (snapshot.Issues.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(L("SourceGuidance_Report_Issues"));
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
            builder.AppendLine(L("SourceGuidance_Report_RepairTitle"));
            builder.AppendLine(L("SourceGuidance_Report_RedactedNotice"));
            builder.AppendLine();
            builder.AppendLine(F("SourceGuidance_Report_Source", SanitizeText(profile.Name)));
            builder.AppendLine(F("SourceGuidance_Report_Type", profile.Type));
            builder.AppendLine(F("SourceGuidance_Report_Headline", snapshot.HeadlineText));
            builder.AppendLine(F("SourceGuidance_Report_Summary", snapshot.SummaryText));
            builder.AppendLine(F("SourceGuidance_Report_CurrentStatus", SanitizeText(snapshot.StatusText)));
            builder.AppendLine(F("SourceGuidance_Report_ActivityFocus", SanitizeText(activity.CurrentStateText)));
            builder.AppendLine(F("SourceGuidance_Report_LatestAttempt", SanitizeText(activity.LatestAttemptText)));
            builder.AppendLine(F("SourceGuidance_Report_LastSuccess", SanitizeText(activity.LastSuccessText)));
            builder.AppendLine(F("SourceGuidance_Report_Health", SanitizeText(diagnostics.HealthLabel), diagnostics.HealthScore));

            if (snapshot.Capabilities.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(L("SourceGuidance_Report_Capabilities"));
                foreach (var capability in snapshot.Capabilities)
                {
                    builder.AppendLine($"{capability.Label}: {capability.Value} - {SanitizeText(capability.Detail)}");
                }
            }

            if (snapshot.Issues.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(L("SourceGuidance_Report_CurrentIssues"));
                foreach (var issue in snapshot.Issues)
                {
                    builder.AppendLine($"{issue.Title}: {SanitizeText(issue.Detail)}");
                }
            }

            if (snapshot.Actions.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(L("SourceGuidance_Report_SuggestedActions"));
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
            builder.AppendLine(L("SourceGuidance_Report_RepairExecutionTitle"));
            builder.AppendLine(L("SourceGuidance_Report_RedactedNotice"));
            builder.AppendLine();
            builder.AppendLine(F("SourceGuidance_Report_Action", actionType));
            builder.AppendLine(F("SourceGuidance_Report_Result", success ? L("SourceGuidance_Report_Completed") : L("SourceGuidance_Report_NeedsReview")));
            if (!string.IsNullOrWhiteSpace(detailText))
            {
                builder.AppendLine(F("SourceGuidance_Report_Detail", SanitizeText(detailText)));
            }

            if (!string.IsNullOrWhiteSpace(changeText))
            {
                builder.AppendLine(F("SourceGuidance_Report_Change", SanitizeText(changeText)));
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
                changes.Add(F("SourceGuidance_Repair_Change_HealthMoved", before.HealthScore, after.HealthScore, delta >= 0 ? "+" : string.Empty, delta));
            }

            if (!string.Equals(before.EpgStatusText, after.EpgStatusText, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(F("SourceGuidance_Repair_Change_GuideState", SanitizeText(before.EpgStatusText), SanitizeText(after.EpgStatusText)));
            }

            if (!string.Equals(before.CompanionStatusText, after.CompanionStatusText, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(F("SourceGuidance_Repair_Change_CompanionPolicy", SanitizeText(before.CompanionStatusText), SanitizeText(after.CompanionStatusText)));
            }

            if (beforeSnapshot != null &&
                afterSnapshot != null &&
                !string.Equals(beforeSnapshot.HeadlineText, afterSnapshot.HeadlineText, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(F("SourceGuidance_Repair_Change_TopFocus", SanitizeText(beforeSnapshot.HeadlineText), SanitizeText(afterSnapshot.HeadlineText)));
            }

            if (changes.Count == 0)
            {
                return L("SourceGuidance_Repair_Change_NoMaterialMovement");
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
                Title = L("SourceGuidance_Setup_InputNeedsAttention"),
                Detail = detail,
                Tone = tone
            };
        }

        private static string L(string key)
        {
            return LocalizedStrings.Get(key);
        }

        private static string F(string key, params object?[] args)
        {
            return LocalizedStrings.Format(key, args);
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

        private static string NormalizeGuideUrlList(string? value)
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
