#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services.Parsing
{
    public interface IXmltvParserService
    {
        Task ParseAndImportEpgAsync(
            AppDbContext db,
            int sourceProfileId,
            SourceAcquisitionSession? acquisitionSession = null,
            bool refreshHealth = true);
    }

    public class XmltvParserService : IXmltvParserService
    {
        private static readonly TimeSpan DefaultPastProgrammeWindow = TimeSpan.FromHours(6);
        private static readonly TimeSpan DefaultFutureProgrammeWindow = TimeSpan.FromHours(72);
        private const int SmallGuideUnboundedProgrammeLimit = 500;

        private readonly IReadOnlyDictionary<SourceType, IEpgSourceDiscoveryService> _discoveryServices;
        private readonly ISourceEnrichmentService _sourceEnrichmentService;
        private readonly ISourceHealthService _sourceHealthService;

        public XmltvParserService(
            IEnumerable<IEpgSourceDiscoveryService> discoveryServices,
            ISourceEnrichmentService sourceEnrichmentService,
            ISourceHealthService sourceHealthService)
        {
            _discoveryServices = discoveryServices.ToDictionary(service => service.SourceType);
            _sourceEnrichmentService = sourceEnrichmentService;
            _sourceHealthService = sourceHealthService;
        }

        public async Task ParseAndImportEpgAsync(
            AppDbContext db,
            int sourceProfileId,
            SourceAcquisitionSession? acquisitionSession = null,
            bool refreshHealth = true)
        {
            var profile = await db.SourceProfiles.FirstOrDefaultAsync(p => p.Id == sourceProfileId);
            if (profile == null)
            {
                throw new Exception("Source not found.");
            }

            var credential = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == sourceProfileId);
            if (credential == null)
            {
                throw new Exception("Source credentials were not found.");
            }

            var activeMode = EpgDiscoveryHelpers.ResolveActiveMode(credential);
            ImportRuntimeLogger.Log(
                "EPG SYNC",
                $"source_profile_id={sourceProfileId}; stage=start; mode={activeMode}; manual_xmltv_url={FormatDiagnosticValue(credential.ManualEpgUrl)}; detected_xmltv_url={FormatDiagnosticValue(credential.DetectedEpgUrl)}");

            if (activeMode == EpgActiveMode.None)
            {
                await ClearGuideDataAsync(db, sourceProfileId, activeMode, "Guide mode is set to none for this source.");
                if (refreshHealth)
                {
                    await _sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                }
                return;
            }

            await MarkEpgSyncingAsync(db, sourceProfileId, activeMode);

            if (!_discoveryServices.TryGetValue(profile.Type, out var discoveryService))
            {
                var message = $"EPG discovery is not supported for {profile.Type} sources.";
                await MarkEpgFailureAsync(
                    db,
                    sourceProfileId,
                    activeMode,
                    EpgSyncResultCode.FetchFailed,
                    EpgFailureStage.Discovery,
                    message,
                    string.Empty);
                throw new Exception(message);
            }

            try
            {
                var discovered = await discoveryService.DiscoverAsync(db, sourceProfileId);
                ImportRuntimeLogger.Log(
                    "EPG SYNC",
                    $"source_profile_id={sourceProfileId}; stage=discovery_complete; mode={discovered.ActiveMode}; active_xmltv_url={FormatDiagnosticValue(discovered.ActiveXmltvUrl)}; detected_xmltv_url={FormatDiagnosticValue(discovered.DetectedXmltvUrl)}; description={FormatDiagnosticValue(discovered.Description)}");

                if (await TryReuseCachedGuideDataAsync(db, sourceProfileId, discovered))
                {
                    if (refreshHealth)
                    {
                        await _sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                    }

                    return;
                }

                await ParseAndPersistXmltvAsync(db, sourceProfileId, discovered, _sourceEnrichmentService, acquisitionSession);
                if (refreshHealth)
                {
                    await _sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                }
            }
            catch (EpgUnavailableException ex)
            {
                await MarkEpgUnavailableAsync(db, sourceProfileId, activeMode, ex.Message);
                if (refreshHealth)
                {
                    await _sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                }
            }
            catch (EpgFetchException ex)
            {
                await MarkEpgFailureAsync(
                    db,
                    sourceProfileId,
                    ex.ActiveMode,
                    EpgSyncResultCode.FetchFailed,
                    EpgFailureStage.Fetch,
                    ex.Message,
                    ex.XmltvUrl);

                if (refreshHealth)
                {
                    await _sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                }
                throw new Exception($"Failed to sync XMLTV EPG: {ex.Message}");
            }
            catch (EpgSyncFailureException ex)
            {
                await MarkEpgFailureAsync(
                    db,
                    sourceProfileId,
                    ex.ActiveMode,
                    ex.ResultCode,
                    ex.FailureStage,
                    ex.Message,
                    ex.ActiveXmltvUrl);

                if (refreshHealth)
                {
                    await _sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                }
                throw new Exception($"Failed to sync XMLTV EPG: {ex.Message}");
            }
            catch (Exception ex)
            {
                await MarkEpgFailureAsync(
                    db,
                    sourceProfileId,
                    activeMode,
                    EpgSyncResultCode.FetchFailed,
                    EpgFailureStage.Discovery,
                    ex.Message,
                    string.Empty);

                if (refreshHealth)
                {
                    await _sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                }
                throw new Exception($"Failed to sync XMLTV EPG: {ex.Message}");
            }
        }

        private static async Task ParseAndPersistXmltvAsync(
            AppDbContext db,
            int sourceProfileId,
            EpgDiscoveryResult discovered,
            ISourceEnrichmentService sourceEnrichmentService,
            SourceAcquisitionSession? acquisitionSession)
        {
            var xmltvChannels = new Dictionary<string, XmltvChannelAccumulator>(StringComparer.OrdinalIgnoreCase);
            var parseFailures = new List<string>();
            var nowUtc = DateTime.UtcNow;
            var importWindowStartUtc = nowUtc.Subtract(DefaultPastProgrammeWindow);
            var importWindowEndUtc = nowUtc.Add(DefaultFutureProgrammeWindow);

            foreach (var source in discovered.GuideSources.OrderBy(source => source.Priority))
            {
                if (source.Status != EpgGuideSourceStatus.Ready || string.IsNullOrWhiteSpace(source.XmlContent))
                {
                    continue;
                }

                try
                {
                    var sourceXmltvChannels = new Dictionary<string, XmltvChannelAccumulator>(StringComparer.OrdinalIgnoreCase);
                    var summary = ParseXmltvChannelIndex(source, sourceXmltvChannels);
                    MergeXmltvChannels(xmltvChannels, sourceXmltvChannels.Values);
                    source.XmltvChannelCount = summary.XmltvChannelCount;
                    source.ProgrammeCount = summary.ProgrammeCount;
                    source.Message = $"Parsed {source.XmltvChannelCount:N0} XMLTV channels and found {source.ProgrammeCount:N0} programmes. Programme import waits for matched channels and the active time window.";

                    ImportRuntimeLogger.Log(
                        "EPG SYNC",
                        $"source_profile_id={sourceProfileId}; stage=parse_source_channels_complete; source_kind={source.Kind}; source_label={FormatDiagnosticValue(source.Label)}; xmltv_url={FormatDiagnosticValue(source.Url)}; xmltv_channel_count={source.XmltvChannelCount}; programme_count={source.ProgrammeCount}");
                }
                catch (Exception ex)
                {
                    source.Status = EpgGuideSourceStatus.Failed;
                    source.Message = TrimSummary($"XMLTV parse failed: {ex.Message}", 260);
                    parseFailures.Add($"{source.Label}: {ex.Message}");
                    ImportRuntimeLogger.Log(
                        "EPG SYNC",
                        $"source_profile_id={sourceProfileId}; stage=parse_source_failed; source_kind={source.Kind}; source_label={FormatDiagnosticValue(source.Label)}; xmltv_url={FormatDiagnosticValue(source.Url)}; failure_reason={FormatDiagnosticValue(ex.Message)}");
                }
            }

            if (xmltvChannels.Count == 0)
            {
                throw new EpgSyncFailureException(
                    parseFailures.Count == 0
                        ? "XMLTV parse failed: no usable XMLTV source was available."
                        : $"XMLTV parse failed: {string.Join(" | ", parseFailures.Take(3))}",
                    discovered.ActiveMode,
                    EpgSyncResultCode.ParseFailed,
                    EpgFailureStage.Parse,
                    discovered.ActiveXmltvUrl,
                    new InvalidOperationException("No XMLTV source could be parsed."));
            }

            ImportRuntimeLogger.Log(
                "EPG SYNC",
                $"source_profile_id={sourceProfileId}; stage=parse_channel_index_complete; xmltv_source_count={discovered.GuideSources.Count(source => source.Status == EpgGuideSourceStatus.Ready)}; xmltv_channel_count={xmltvChannels.Count}; programme_count={discovered.GuideSources.Sum(source => source.ProgrammeCount)}; import_window_start_utc={importWindowStartUtc:o}; import_window_end_utc={importWindowEndUtc:o}");

            var orderedXmltvChannels = xmltvChannels.Values
                .OrderBy(channel => channel.SourcePriority)
                .ThenBy(channel => channel.Id, StringComparer.OrdinalIgnoreCase)
                .Select(channel => channel.ToDescriptor())
                .ToList();
            var enrichmentResult = await sourceEnrichmentService.ApplyXmltvEnrichmentAsync(db, sourceProfileId, orderedXmltvChannels, acquisitionSession);
            var channelMatches = enrichmentResult.Matches;
            var channels = await db.Channels
                .Join(
                    db.ChannelCategories.Where(category => category.SourceProfileId == sourceProfileId),
                    channel => channel.ChannelCategoryId,
                    category => category.Id,
                    (channel, category) => channel)
                .ToListAsync();

            foreach (var xmltvChannel in orderedXmltvChannels)
            {
                LogMatchOutcome(sourceProfileId, xmltvChannel, channelMatches[xmltvChannel.Id]);
            }

            var epgItems = new List<EpgProgram>();
            var epgItemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in discovered.GuideSources.OrderBy(source => source.Priority))
            {
                if (source.Status != EpgGuideSourceStatus.Ready || string.IsNullOrWhiteSpace(source.XmlContent))
                {
                    continue;
                }

                try
                {
                    var useDefaultImportWindow = source.ProgrammeCount > SmallGuideUnboundedProgrammeLimit;
                    var sourceImportedProgrammes = AppendMatchedProgrammesFromSource(
                        source,
                        channelMatches,
                        useDefaultImportWindow ? importWindowStartUtc : null,
                        useDefaultImportWindow ? importWindowEndUtc : null,
                        epgItems,
                        epgItemKeys);
                    source.Message = useDefaultImportWindow
                        ? $"{source.Message} Imported {sourceImportedProgrammes:N0} matched programmes inside the default window."
                        : $"{source.Message} Imported {sourceImportedProgrammes:N0} matched programmes from this compact guide.";
                    ImportRuntimeLogger.Log(
                        "EPG SYNC",
                        $"source_profile_id={sourceProfileId}; stage=parse_source_programmes_complete; source_kind={source.Kind}; source_label={FormatDiagnosticValue(source.Label)}; xmltv_url={FormatDiagnosticValue(source.Url)}; imported_programme_count={sourceImportedProgrammes}; import_window_start_utc={importWindowStartUtc:o}; import_window_end_utc={importWindowEndUtc:o}");
                }
                catch (Exception ex)
                {
                    source.Status = EpgGuideSourceStatus.Failed;
                    source.Message = TrimSummary($"XMLTV programme parse failed: {ex.Message}", 260);
                    ImportRuntimeLogger.Log(
                        "EPG SYNC",
                        $"source_profile_id={sourceProfileId}; stage=parse_source_programmes_failed; source_kind={source.Kind}; source_label={FormatDiagnosticValue(source.Label)}; xmltv_url={FormatDiagnosticValue(source.Url)}; failure_reason={FormatDiagnosticValue(ex.Message)}");
                }
            }

            var metrics = BuildSyncMetrics(channels, epgItems, channelMatches.Values, xmltvChannels.Count);
            ImportRuntimeLogger.Log(
                "EPG SYNC",
                $"source_profile_id={sourceProfileId}; stage=match_complete; total_live_channels={metrics.TotalLiveChannelCount}; matched_channels={metrics.MatchedChannelCount}; unmatched_channels={metrics.UnmatchedChannelCount}; exact_matches={metrics.ExactMatchCount}; normalized_matches={metrics.NormalizedMatchCount}; approved_matches={metrics.ApprovedMatchCount}; weak_matches={metrics.WeakMatchCount}; current_coverage={metrics.CurrentCoverageCount}; next_coverage={metrics.NextCoverageCount}; match_breakdown={FormatDiagnosticValue(metrics.MatchBreakdown)}");

            await PersistGuideDataAsync(db, sourceProfileId, discovered, channels, epgItems, metrics);
        }

        private static async Task<bool> TryReuseCachedGuideDataAsync(
            AppDbContext db,
            int sourceProfileId,
            EpgDiscoveryResult discovered)
        {
            var readySources = discovered.GuideSources
                .Where(source => source.Status == EpgGuideSourceStatus.Ready && !string.IsNullOrWhiteSpace(source.XmlContent))
                .ToList();
            if (readySources.Count == 0 ||
                readySources.Any(source => !source.WasContentUnchanged))
            {
                return false;
            }

            var epgLog = await GetOrCreateEpgLogAsync(db, sourceProfileId);
            if (!epgLog.LastSuccessAtUtc.HasValue ||
                epgLog.LastSuccessAtUtc.Value < DateTime.UtcNow.Subtract(TimeSpan.FromHours(6)) ||
                !await HasPersistedGuideDataAsync(db, sourceProfileId))
            {
                return false;
            }

            if (await HasReviewDecisionChangesAsync(db, sourceProfileId, epgLog.LastSuccessAtUtc.Value))
            {
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            ApplyPreviousGuideSourceDiagnostics(discovered, epgLog.GuideSourceStatusJson);
            if (readySources.Any(source => source.XmltvChannelCount == 0 && source.ProgrammeCount == 0))
            {
                return false;
            }

            epgLog.SyncedAtUtc = nowUtc;
            epgLog.IsSuccess = true;
            epgLog.Status = discovered.ActiveMode == EpgActiveMode.Manual
                ? EpgStatus.ManualOverride
                : EpgStatus.Ready;
            epgLog.FailureStage = EpgFailureStage.None;
            epgLog.ActiveMode = discovered.ActiveMode;
            epgLog.ActiveXmltvUrl = discovered.ActiveXmltvUrl;
            epgLog.GuideSourceStatusJson = SerializeGuideSourceStatuses(discovered);
            epgLog.GuideWarningSummary = TrimSummary(
                CombineCachedReuseWarnings(discovered, epgLog.GuideWarningSummary),
                500);
            epgLog.FailureReason = string.Empty;

            await db.SaveChangesAsync();
            ImportRuntimeLogger.Log(
                "EPG SYNC",
                $"source_profile_id={sourceProfileId}; stage=cache_reuse; ready_source_count={readySources.Count}; last_success_utc={epgLog.LastSuccessAtUtc:o}; active_xmltv_url={FormatDiagnosticValue(epgLog.ActiveXmltvUrl)}");
            return true;
        }

        private static async Task<bool> HasReviewDecisionChangesAsync(
            AppDbContext db,
            int sourceProfileId,
            DateTime lastSuccessAtUtc)
        {
            return await db.EpgMappingDecisions
                .AsNoTracking()
                .AnyAsync(decision => decision.SourceProfileId == sourceProfileId &&
                                      decision.UpdatedAtUtc > lastSuccessAtUtc);
        }

        private static void ApplyPreviousGuideSourceDiagnostics(EpgDiscoveryResult discovered, string previousStatusJson)
        {
            if (string.IsNullOrWhiteSpace(previousStatusJson))
            {
                return;
            }

            try
            {
                var previous = JsonSerializer.Deserialize<List<EpgGuideSourceStatusSnapshot>>(previousStatusJson);
                if (previous == null || previous.Count == 0)
                {
                    return;
                }

                var previousByUrl = previous
                    .Where(source => !string.IsNullOrWhiteSpace(source.Url))
                    .GroupBy(source => source.Url.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                foreach (var source in discovered.GuideSources)
                {
                    if (source.XmltvChannelCount > 0 ||
                        string.IsNullOrWhiteSpace(source.Url) ||
                        !previousByUrl.TryGetValue(source.Url.Trim(), out var previousSource))
                    {
                        continue;
                    }

                    source.XmltvChannelCount = previousSource.XmltvChannelCount;
                    source.ProgrammeCount = previousSource.ProgrammeCount;
                    if (string.IsNullOrWhiteSpace(source.ContentHash))
                    {
                        source.ContentHash = previousSource.ContentHash;
                    }
                }
            }
            catch
            {
            }
        }

        private static string CombineCachedReuseWarnings(EpgDiscoveryResult discovered, string previousWarning)
        {
            var warnings = new List<string> { "Guide source content was unchanged; reused the previous import." };
            warnings.AddRange(discovered.GuideSources
                .Where(source => source.Status == EpgGuideSourceStatus.Failed)
                .Select(source => $"{source.Label} failed: {TrimSummary(source.Message, 120)}"));
            if (!string.IsNullOrWhiteSpace(previousWarning))
            {
                warnings.Add(previousWarning);
            }

            return string.Join(" ", warnings.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static XmltvSourceParseSummary ParseXmltvChannelIndex(
            EpgDiscoveredGuideSource source,
            IDictionary<string, XmltvChannelAccumulator> xmltvChannels)
        {
            var sourceXmltvChannelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var programmeCount = 0;
            using var textReader = new StringReader(source.XmlContent);
            using var reader = XmlReader.Create(textReader, CreateXmltvReaderSettings());
            XmltvChannelAccumulator? activeChannel = null;
            var activeChannelDepth = -1;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement &&
                    activeChannel != null &&
                    reader.Depth == activeChannelDepth &&
                    string.Equals(reader.LocalName, "channel", StringComparison.OrdinalIgnoreCase))
                {
                    activeChannel = null;
                    activeChannelDepth = -1;
                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (string.Equals(reader.LocalName, "channel", StringComparison.OrdinalIgnoreCase))
                {
                    var channelId = reader.GetAttribute("id")?.Trim();
                    if (string.IsNullOrWhiteSpace(channelId))
                    {
                        if (!reader.IsEmptyElement)
                        {
                            reader.Skip();
                        }

                        continue;
                    }

                    sourceXmltvChannelIds.Add(channelId);
                    activeChannel = GetOrCreateChannelAccumulator(xmltvChannels, channelId, source);
                    activeChannelDepth = reader.Depth;
                    if (reader.IsEmptyElement)
                    {
                        activeChannel = null;
                        activeChannelDepth = -1;
                    }
                }
                else if (activeChannel != null &&
                         string.Equals(reader.LocalName, "display-name", StringComparison.OrdinalIgnoreCase))
                {
                    var displayName = ReadElementText(reader).Trim();
                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        activeChannel.DisplayNames.Add(displayName);
                    }
                }
                else if (activeChannel != null &&
                         string.Equals(reader.LocalName, "icon", StringComparison.OrdinalIgnoreCase))
                {
                    var iconUrl = reader.GetAttribute("src")?.Trim();
                    if (!string.IsNullOrWhiteSpace(iconUrl))
                    {
                        activeChannel.IconUrls.Add(iconUrl);
                    }
                }
                else if (string.Equals(reader.LocalName, "programme", StringComparison.OrdinalIgnoreCase))
                {
                    programmeCount++;
                    var programmeChannelId = reader.GetAttribute("channel")?.Trim();
                    if (!string.IsNullOrWhiteSpace(programmeChannelId))
                    {
                        sourceXmltvChannelIds.Add(programmeChannelId);
                        GetOrCreateChannelAccumulator(xmltvChannels, programmeChannelId, source);
                    }
                }
            }

            return new XmltvSourceParseSummary(sourceXmltvChannelIds.Count, programmeCount);
        }

        private static void MergeXmltvChannels(
            IDictionary<string, XmltvChannelAccumulator> target,
            IEnumerable<XmltvChannelAccumulator> sourceChannels)
        {
            foreach (var sourceChannel in sourceChannels)
            {
                if (!target.TryGetValue(sourceChannel.Id, out var targetChannel))
                {
                    target[sourceChannel.Id] = sourceChannel;
                    continue;
                }

                if (sourceChannel.SourcePriority < targetChannel.SourcePriority)
                {
                    targetChannel.SourcePriority = sourceChannel.SourcePriority;
                    targetChannel.SourceKind = sourceChannel.SourceKind;
                    targetChannel.SourceLabel = sourceChannel.SourceLabel;
                    targetChannel.SourceUrl = sourceChannel.SourceUrl;
                }

                foreach (var displayName in sourceChannel.DisplayNames)
                {
                    targetChannel.DisplayNames.Add(displayName);
                }

                foreach (var iconUrl in sourceChannel.IconUrls)
                {
                    targetChannel.IconUrls.Add(iconUrl);
                }
            }
        }

        private static int AppendMatchedProgrammesFromSource(
            EpgDiscoveredGuideSource source,
            IReadOnlyDictionary<string, ChannelEpgMatchOutcome> channelMatches,
            DateTime? importWindowStartUtc,
            DateTime? importWindowEndUtc,
            ICollection<EpgProgram> epgItems,
            ISet<string> epgItemKeys)
        {
            var importedProgrammeCount = 0;
            using var textReader = new StringReader(source.XmlContent);
            using var reader = XmlReader.Create(textReader, CreateXmltvReaderSettings());
            MatchedProgrammeAccumulator? activeProgramme = null;
            var activeProgrammeDepth = -1;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement &&
                    activeProgramme != null &&
                    reader.Depth == activeProgrammeDepth &&
                    string.Equals(reader.LocalName, "programme", StringComparison.OrdinalIgnoreCase))
                {
                    importedProgrammeCount += AppendProgramme(activeProgramme, epgItems, epgItemKeys);
                    activeProgramme = null;
                    activeProgrammeDepth = -1;
                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (string.Equals(reader.LocalName, "programme", StringComparison.OrdinalIgnoreCase))
                {
                    activeProgramme = TryCreateMatchedProgramme(reader, channelMatches, importWindowStartUtc, importWindowEndUtc);
                    activeProgrammeDepth = activeProgramme == null ? -1 : reader.Depth;
                    if (reader.IsEmptyElement && activeProgramme != null)
                    {
                        importedProgrammeCount += AppendProgramme(activeProgramme, epgItems, epgItemKeys);
                        activeProgramme = null;
                        activeProgrammeDepth = -1;
                    }

                    continue;
                }

                if (activeProgramme == null)
                {
                    continue;
                }

                if (string.Equals(reader.LocalName, "title", StringComparison.OrdinalIgnoreCase))
                {
                    activeProgramme.Title = ReadElementText(reader).Trim();
                }
                else if (string.Equals(reader.LocalName, "desc", StringComparison.OrdinalIgnoreCase))
                {
                    activeProgramme.Description = ReadElementText(reader).Trim();
                }
                else if (string.Equals(reader.LocalName, "sub-title", StringComparison.OrdinalIgnoreCase))
                {
                    activeProgramme.Subtitle = ReadElementText(reader).Trim();
                }
                else if (string.Equals(reader.LocalName, "category", StringComparison.OrdinalIgnoreCase))
                {
                    activeProgramme.Category = ReadElementText(reader).Trim();
                }
            }

            return importedProgrammeCount;
        }

        private static MatchedProgrammeAccumulator? TryCreateMatchedProgramme(
            XmlReader reader,
            IReadOnlyDictionary<string, ChannelEpgMatchOutcome> channelMatches,
            DateTime? importWindowStartUtc,
            DateTime? importWindowEndUtc)
        {
            var xmltvChannelId = reader.GetAttribute("channel")?.Trim();
            if (string.IsNullOrWhiteSpace(xmltvChannelId) ||
                !channelMatches.TryGetValue(xmltvChannelId, out var outcome) ||
                outcome.Channels.Count == 0 ||
                !IsActiveProgrammeMatch(outcome))
            {
                return null;
            }

            var start = ParseXmltvDate(reader.GetAttribute("start") ?? string.Empty);
            var end = ParseXmltvDate(reader.GetAttribute("stop") ?? string.Empty);
            if (start == null ||
                end == null ||
                end <= start ||
                (importWindowStartUtc.HasValue && end.Value < importWindowStartUtc.Value) ||
                (importWindowEndUtc.HasValue && start.Value > importWindowEndUtc.Value))
            {
                return null;
            }

            return new MatchedProgrammeAccumulator(outcome, start.Value, end.Value);
        }

        private static int AppendProgramme(
            MatchedProgrammeAccumulator programme,
            ICollection<EpgProgram> epgItems,
            ISet<string> epgItemKeys)
        {
            var importedProgrammeCount = 0;
            foreach (var matchedChannel in programme.Outcome.Channels)
            {
                var programTitle = string.IsNullOrWhiteSpace(programme.Title) ? "Unknown Program" : programme.Title;
                var duplicateKey = $"{matchedChannel.Id}|{programme.StartTimeUtc.Ticks}|{programme.EndTimeUtc.Ticks}";
                if (!epgItemKeys.Add(duplicateKey))
                {
                    continue;
                }

                epgItems.Add(new EpgProgram
                {
                    ChannelId = matchedChannel.Id,
                    StartTimeUtc = programme.StartTimeUtc,
                    EndTimeUtc = programme.EndTimeUtc,
                    Title = programTitle,
                    Description = programme.Description,
                    Subtitle = string.IsNullOrWhiteSpace(programme.Subtitle) ? null : programme.Subtitle,
                    Category = string.IsNullOrWhiteSpace(programme.Category) ? null : programme.Category
                });
                importedProgrammeCount++;
            }

            return importedProgrammeCount;
        }

        private static string ReadElementText(XmlReader reader)
        {
            if (reader.IsEmptyElement)
            {
                return string.Empty;
            }

            var depth = reader.Depth;
            var text = string.Empty;
            while (reader.Read())
            {
                if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace)
                {
                    text += reader.Value;
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    break;
                }
            }

            return text;
        }

        private static bool IsActiveProgrammeMatch(ChannelEpgMatchOutcome outcome)
        {
            return outcome.Reason is ChannelEpgMatchSource.Provider or ChannelEpgMatchSource.Normalized or ChannelEpgMatchSource.Regex or ChannelEpgMatchSource.UserApproved;
        }

        private static XmlReaderSettings CreateXmltvReaderSettings()
        {
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                XmlResolver = null,
                MaxCharactersFromEntities = 0
            };
        }

        private static async Task PersistGuideDataAsync(
            AppDbContext db,
            int sourceProfileId,
            EpgDiscoveryResult discovered,
            IReadOnlyCollection<Channel> channels,
            IReadOnlyCollection<EpgProgram> epgItems,
            EpgSyncMetrics metrics)
        {
            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                await RemoveGuideDataAsync(db, channels.Select(channel => channel.Id).ToList());

                if (epgItems.Count > 0)
                {
                    foreach (var chunk in epgItems.Chunk(1000))
                    {
                        db.EpgPrograms.AddRange(chunk);
                        await db.SaveChangesAsync();
                    }
                }

                var nowUtc = DateTime.UtcNow;
                var epgLog = await GetOrCreateEpgLogAsync(db, sourceProfileId);
                epgLog.SyncedAtUtc = nowUtc;
                epgLog.LastSuccessAtUtc = nowUtc;
                epgLog.IsSuccess = true;
                epgLog.Status = discovered.ActiveMode == EpgActiveMode.Manual
                    ? EpgStatus.ManualOverride
                    : EpgStatus.Ready;
                epgLog.ResultCode = metrics.ResultCode;
                epgLog.FailureStage = EpgFailureStage.None;
                epgLog.ActiveMode = discovered.ActiveMode;
                epgLog.ActiveXmltvUrl = discovered.ActiveXmltvUrl;
                epgLog.MatchedChannelCount = metrics.MatchedChannelCount;
                epgLog.UnmatchedChannelCount = metrics.UnmatchedChannelCount;
                epgLog.CurrentCoverageCount = metrics.CurrentCoverageCount;
                epgLog.NextCoverageCount = metrics.NextCoverageCount;
                epgLog.TotalLiveChannelCount = metrics.TotalLiveChannelCount;
                epgLog.ProgrammeCount = epgItems.Count;
                epgLog.XmltvChannelCount = metrics.XmltvChannelCount;
                epgLog.ExactMatchCount = metrics.ExactMatchCount;
                epgLog.NormalizedMatchCount = metrics.NormalizedMatchCount;
                epgLog.ApprovedMatchCount = metrics.ApprovedMatchCount;
                epgLog.WeakMatchCount = metrics.WeakMatchCount;
                epgLog.MatchBreakdown = metrics.MatchBreakdown;
                epgLog.GuideSourceStatusJson = SerializeGuideSourceStatuses(discovered);
                epgLog.GuideWarningSummary = BuildGuideWarningSummary(discovered, metrics);
                epgLog.FailureReason = string.Empty;

                var credential = await db.SourceCredentials.FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId);
                if (credential != null)
                {
                    credential.DetectedEpgUrl = discovered.DetectedXmltvUrl;
                }

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                ImportRuntimeLogger.Log(
                    "EPG SYNC",
                    $"source_profile_id={sourceProfileId}; stage=persist_complete; status={epgLog.Status}; result={epgLog.ResultCode}; programme_count={epgLog.ProgrammeCount}; matched_channels={epgLog.MatchedChannelCount}; current_coverage={epgLog.CurrentCoverageCount}; next_coverage={epgLog.NextCoverageCount}; active_xmltv_url={FormatDiagnosticValue(epgLog.ActiveXmltvUrl)}");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new EpgSyncFailureException(
                    $"XMLTV persistence failed: {ex.Message}",
                    discovered.ActiveMode,
                    EpgSyncResultCode.PersistFailed,
                    EpgFailureStage.Persist,
                    discovered.ActiveXmltvUrl,
                    ex);
            }
        }

        private static async Task ClearGuideDataAsync(
            AppDbContext db,
            int sourceProfileId,
            EpgActiveMode activeMode,
            string reason)
        {
            var channelIds = await db.ChannelCategories
                .Where(category => category.SourceProfileId == sourceProfileId)
                .Join(
                    db.Channels,
                    category => category.Id,
                    channel => channel.ChannelCategoryId,
                    (category, channel) => channel.Id)
                .ToListAsync();

            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                await RemoveGuideDataAsync(db, channelIds);

                var nowUtc = DateTime.UtcNow;
                var epgLog = await GetOrCreateEpgLogAsync(db, sourceProfileId);
                epgLog.SyncedAtUtc = nowUtc;
                epgLog.IsSuccess = false;
                epgLog.Status = EpgStatus.Unknown;
                epgLog.ResultCode = EpgSyncResultCode.None;
                epgLog.FailureStage = EpgFailureStage.None;
                epgLog.ActiveMode = activeMode;
                epgLog.ActiveXmltvUrl = string.Empty;
                epgLog.MatchedChannelCount = 0;
                epgLog.UnmatchedChannelCount = channelIds.Count;
                epgLog.CurrentCoverageCount = 0;
                epgLog.NextCoverageCount = 0;
                epgLog.TotalLiveChannelCount = channelIds.Count;
                epgLog.ProgrammeCount = 0;
                epgLog.XmltvChannelCount = 0;
                epgLog.ExactMatchCount = 0;
                epgLog.NormalizedMatchCount = 0;
                epgLog.ApprovedMatchCount = 0;
                epgLog.WeakMatchCount = 0;
                epgLog.MatchBreakdown = string.Empty;
                epgLog.GuideSourceStatusJson = string.Empty;
                epgLog.GuideWarningSummary = string.Empty;
                epgLog.FailureReason = reason;

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                ImportRuntimeLogger.Log(
                    "EPG SYNC",
                    $"source_profile_id={sourceProfileId}; stage=disabled; mode={activeMode}; cleared_channels={channelIds.Count}; reason={FormatDiagnosticValue(reason)}");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static async Task MarkEpgSyncingAsync(
            AppDbContext db,
            int sourceProfileId,
            EpgActiveMode activeMode)
        {
            var epgLog = await GetOrCreateEpgLogAsync(db, sourceProfileId);
            epgLog.SyncedAtUtc = DateTime.UtcNow;
            epgLog.IsSuccess = false;
            epgLog.Status = EpgStatus.Syncing;
            epgLog.ResultCode = EpgSyncResultCode.None;
            epgLog.FailureStage = EpgFailureStage.None;
            epgLog.ActiveMode = activeMode;
            epgLog.ActiveXmltvUrl = string.Empty;
            epgLog.FailureReason = string.Empty;

            try
            {
                await db.SaveChangesAsync();
            }
            catch
            {
            }

            ImportRuntimeLogger.Log(
                "EPG SYNC",
                $"source_profile_id={sourceProfileId}; stage=syncing; mode={activeMode}");
        }

        private static async Task MarkEpgUnavailableAsync(
            AppDbContext db,
            int sourceProfileId,
            EpgActiveMode activeMode,
            string reason)
        {
            var nowUtc = DateTime.UtcNow;
            var shortReason = TrimSummary(reason, 300);
            var epgLog = await GetOrCreateEpgLogAsync(db, sourceProfileId);
            var hasExistingGuideData = epgLog.LastSuccessAtUtc.HasValue || await HasPersistedGuideDataAsync(db, sourceProfileId);

            epgLog.SyncedAtUtc = nowUtc;
            epgLog.IsSuccess = false;
            epgLog.Status = hasExistingGuideData ? EpgStatus.Stale : EpgStatus.UnavailableNoXmltv;
            epgLog.ResultCode = EpgSyncResultCode.NoXmltvAdvertised;
            epgLog.FailureStage = EpgFailureStage.Discovery;
            epgLog.ActiveMode = activeMode;
            epgLog.ActiveXmltvUrl = string.Empty;
            epgLog.GuideSourceStatusJson = string.Empty;
            epgLog.GuideWarningSummary = shortReason;
            epgLog.FailureReason = shortReason;

            try
            {
                await db.SaveChangesAsync();
            }
            catch
            {
            }

            ImportRuntimeLogger.Log(
                "EPG SYNC",
                $"source_profile_id={sourceProfileId}; stage=unavailable; status={epgLog.Status}; reason={FormatDiagnosticValue(shortReason)}");
        }

        private static async Task MarkEpgFailureAsync(
            AppDbContext db,
            int sourceProfileId,
            EpgActiveMode activeMode,
            EpgSyncResultCode resultCode,
            EpgFailureStage failureStage,
            string reason,
            string activeXmltvUrl)
        {
            var nowUtc = DateTime.UtcNow;
            var shortReason = TrimSummary(reason, 300);
            var epgLog = await GetOrCreateEpgLogAsync(db, sourceProfileId);
            var hasExistingGuideData = epgLog.LastSuccessAtUtc.HasValue || await HasPersistedGuideDataAsync(db, sourceProfileId);

            epgLog.SyncedAtUtc = nowUtc;
            epgLog.IsSuccess = false;
            epgLog.Status = hasExistingGuideData ? EpgStatus.Stale : EpgStatus.FailedFetchOrParse;
            epgLog.ResultCode = resultCode;
            epgLog.FailureStage = failureStage;
            epgLog.ActiveMode = activeMode;
            epgLog.ActiveXmltvUrl = activeXmltvUrl ?? string.Empty;
            epgLog.GuideSourceStatusJson = BuildFailureGuideSourceStatusJson(activeXmltvUrl ?? string.Empty, resultCode, shortReason);
            epgLog.GuideWarningSummary = shortReason;
            epgLog.FailureReason = shortReason;

            try
            {
                await db.SaveChangesAsync();
            }
            catch
            {
            }

            ImportRuntimeLogger.Log(
                "EPG SYNC",
                $"source_profile_id={sourceProfileId}; stage=failure; status={epgLog.Status}; result={resultCode}; failure_stage={failureStage}; active_xmltv_url={FormatDiagnosticValue(activeXmltvUrl ?? string.Empty)}; reason={FormatDiagnosticValue(shortReason)}");
        }

        private static async Task<EpgSyncLog> GetOrCreateEpgLogAsync(AppDbContext db, int sourceProfileId)
        {
            var epgLog = await db.EpgSyncLogs.FirstOrDefaultAsync(log => log.SourceProfileId == sourceProfileId);
            if (epgLog != null)
            {
                return epgLog;
            }

            epgLog = new EpgSyncLog { SourceProfileId = sourceProfileId };
            db.EpgSyncLogs.Add(epgLog);
            return epgLog;
        }

        private static async Task<bool> HasPersistedGuideDataAsync(AppDbContext db, int sourceProfileId)
        {
            return await db.ChannelCategories
                .Where(category => category.SourceProfileId == sourceProfileId)
                .Join(
                    db.Channels,
                    category => category.Id,
                    channel => channel.ChannelCategoryId,
                    (category, channel) => channel.Id)
                .Join(
                    db.EpgPrograms.Select(program => program.ChannelId).Distinct(),
                    channelId => channelId,
                    epgChannelId => epgChannelId,
                    (channelId, epgChannelId) => channelId)
                .AnyAsync();
        }

        private static async Task RemoveGuideDataAsync(AppDbContext db, IReadOnlyCollection<int> channelIds)
        {
            if (channelIds.Count == 0)
            {
                return;
            }

            foreach (var chunk in channelIds.Chunk(50))
            {
                var existing = await db.EpgPrograms.Where(program => chunk.Contains(program.ChannelId)).ToListAsync();
                if (existing.Count > 0)
                {
                    db.EpgPrograms.RemoveRange(existing);
                    await db.SaveChangesAsync();
                }
            }
        }

        private static EpgSyncMetrics BuildSyncMetrics(
            IReadOnlyCollection<Channel> sourceChannels,
            IReadOnlyCollection<EpgProgram> epgItems,
            IEnumerable<ChannelEpgMatchOutcome> matchOutcomes,
            int xmltvChannelCount)
        {
            var nowUtc = DateTime.UtcNow;
            var matchedChannelIds = epgItems
                .Select(item => item.ChannelId)
                .Distinct()
                .ToHashSet();
            var currentCoverage = epgItems
                .Where(item => item.StartTimeUtc <= nowUtc && item.EndTimeUtc > nowUtc)
                .Select(item => item.ChannelId)
                .Distinct()
                .Count();
            var nextCoverage = epgItems
                .Where(item => item.StartTimeUtc > nowUtc)
                .Select(item => item.ChannelId)
                .Distinct()
                .Count();

            var primaryMatchOutcomes = matchOutcomes
                .Where(outcome => !outcome.IsSupplemental)
                .ToList();
            var matchCounts = primaryMatchOutcomes
                .GroupBy(outcome => outcome.Reason)
                .ToDictionary(group => group.Key, group => group.Count());
            var exactMatchCount = primaryMatchOutcomes
                .Where(outcome => outcome.Reason == ChannelEpgMatchSource.Provider)
                .SelectMany(outcome => outcome.Channels)
                .Select(channel => channel.Id)
                .Distinct()
                .Count();
            var normalizedMatchCount = primaryMatchOutcomes
                .Where(outcome => outcome.Reason == ChannelEpgMatchSource.Normalized)
                .SelectMany(outcome => outcome.Channels)
                .Select(channel => channel.Id)
                .Distinct()
                .Count();
            var approvedMatchCount = primaryMatchOutcomes
                .Where(outcome => outcome.Reason == ChannelEpgMatchSource.UserApproved)
                .SelectMany(outcome => outcome.Channels)
                .Select(channel => channel.Id)
                .Distinct()
                .Count();
            var weakMatchCount = primaryMatchOutcomes
                .Where(outcome => outcome.Reason is ChannelEpgMatchSource.Previous or ChannelEpgMatchSource.Alias or ChannelEpgMatchSource.Regex or ChannelEpgMatchSource.Fuzzy)
                .SelectMany(outcome => outcome.Channels)
                .Select(channel => channel.Id)
                .Distinct()
                .Count();

            var matchedCount = matchedChannelIds.Count;
            var totalLiveChannelCount = sourceChannels.Count;
            var unmatchedCount = Math.Max(0, totalLiveChannelCount - matchedCount);

            var resultCode = matchedCount == 0
                ? EpgSyncResultCode.ZeroCoverage
                : unmatchedCount > 0
                    ? EpgSyncResultCode.PartialMatch
                    : EpgSyncResultCode.Ready;

            return new EpgSyncMetrics(
                totalLiveChannelCount,
                matchedCount,
                unmatchedCount,
                currentCoverage,
                nextCoverage,
                xmltvChannelCount,
                exactMatchCount,
                normalizedMatchCount,
                approvedMatchCount,
                weakMatchCount,
                resultCode,
                BuildMatchBreakdown(matchCounts, unmatchedCount));
        }

        private static string BuildMatchBreakdown(IReadOnlyDictionary<ChannelEpgMatchSource, int> counts, int unmatchedCount)
        {
            static int GetCount(IReadOnlyDictionary<ChannelEpgMatchSource, int> map, ChannelEpgMatchSource reason) =>
                map.TryGetValue(reason, out var count) ? count : 0;

            var exact = GetCount(counts, ChannelEpgMatchSource.Provider);
            var normalized = GetCount(counts, ChannelEpgMatchSource.Normalized);

            return $"provider_id={exact}; reused={GetCount(counts, ChannelEpgMatchSource.Previous)}; normalized={normalized}; alias={GetCount(counts, ChannelEpgMatchSource.Alias)}; regex={GetCount(counts, ChannelEpgMatchSource.Regex)}; fuzzy={GetCount(counts, ChannelEpgMatchSource.Fuzzy)}; unmatched={unmatchedCount}";
        }

        private static void LogMatchOutcome(int sourceProfileId, XmltvChannelDescriptor xmltvChannel, ChannelEpgMatchOutcome outcome)
        {
            var matchedChannelIds = outcome.Channels.Count == 0
                ? string.Empty
                : string.Join("|", outcome.Channels.Select(channel => channel.Id));
            var matchedChannelNames = outcome.Channels.Count == 0
                ? string.Empty
                : string.Join(" | ", outcome.Channels.Select(channel => channel.Name));

            ImportRuntimeLogger.Log(
                "EPG MATCH",
                $"source_profile_id={sourceProfileId}; xmltv_channel_id={FormatDiagnosticValue(xmltvChannel.Id)}; display_names={FormatDiagnosticValue(string.Join(" | ", xmltvChannel.DisplayNames))}; source_label={FormatDiagnosticValue(xmltvChannel.SourceLabel)}; match_reason={outcome.Reason}; confidence={outcome.Confidence}; supplemental={outcome.IsSupplemental}; matched_channel_count={outcome.Channels.Count}; channel_ids={FormatDiagnosticValue(matchedChannelIds)}; channel_names={FormatDiagnosticValue(matchedChannelNames)}; matched_value={FormatDiagnosticValue(outcome.MatchedValue)}; matched_key={FormatDiagnosticValue(outcome.MatchedKey)}; diagnostics={FormatDiagnosticValue(outcome.Diagnostic)}");
        }

        private static XmltvChannelAccumulator GetOrCreateChannelAccumulator(
            IDictionary<string, XmltvChannelAccumulator> xmltvChannels,
            string channelId,
            EpgDiscoveredGuideSource source)
        {
            if (!xmltvChannels.TryGetValue(channelId, out var accumulator))
            {
                accumulator = new XmltvChannelAccumulator(channelId, source);
                xmltvChannels[channelId] = accumulator;
            }
            else if (source.Priority < accumulator.SourcePriority)
            {
                accumulator.SourcePriority = source.Priority;
                accumulator.SourceKind = source.Kind;
                accumulator.SourceLabel = source.Label;
                accumulator.SourceUrl = source.Url;
            }

            return accumulator;
        }

        private static string SerializeGuideSourceStatuses(EpgDiscoveryResult discovered)
        {
            try
            {
                return JsonSerializer.Serialize(discovered.BuildStatusSnapshots());
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildFailureGuideSourceStatusJson(string activeXmltvUrl, EpgSyncResultCode resultCode, string reason)
        {
            if (string.IsNullOrWhiteSpace(activeXmltvUrl))
            {
                return string.Empty;
            }

            try
            {
                var snapshot = new[]
                {
                    new EpgGuideSourceStatusSnapshot
                    {
                        Label = "XMLTV",
                        Url = activeXmltvUrl,
                        Status = EpgGuideSourceStatus.Failed,
                        Message = reason,
                        CheckedAtUtc = DateTime.UtcNow,
                        Kind = EpgGuideSourceKind.Provider
                    }
                };

                return JsonSerializer.Serialize(snapshot);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildGuideWarningSummary(EpgDiscoveryResult discovered, EpgSyncMetrics metrics)
        {
            var warnings = new List<string>();
            foreach (var failed in discovered.GuideSources.Where(source => source.Status == EpgGuideSourceStatus.Failed))
            {
                warnings.Add($"{failed.Label} failed: {TrimSummary(failed.Message, 120)}");
            }

            if (metrics.WeakMatchCount > 0)
            {
                warnings.Add($"{metrics.WeakMatchCount:N0} guide matches need review.");
            }

            if (metrics.UnmatchedChannelCount > 0)
            {
                warnings.Add($"{metrics.UnmatchedChannelCount:N0} live channels are unmatched.");
            }

            return TrimSummary(string.Join(" ", warnings.Distinct(StringComparer.OrdinalIgnoreCase)), 500);
        }

        private static DateTime? ParseXmltvDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
            {
                return null;
            }

            try
            {
                dateStr = dateStr.Trim();
                if (dateStr.Length < 14)
                {
                    return null;
                }

                var timestamp = dateStr[..14];
                var offset = "+0000";
                if (dateStr.Length > 14)
                {
                    var remainder = dateStr[14..].Trim();
                    if (remainder.Length >= 5)
                    {
                        offset = remainder[..5];
                    }
                }

                if (offset.Length == 5 && !offset.Contains(':'))
                {
                    offset = offset.Insert(3, ":");
                }

                if (DateTimeOffset.TryParseExact(
                        $"{timestamp} {offset}",
                        "yyyyMMddHHmmss zzz",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dto))
                {
                    return dto.UtcDateTime;
                }

                if (DateTime.TryParseExact(
                        timestamp,
                        "yyyyMMddHHmmss",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dt))
                {
                    return dt;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string TrimSummary(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength
                ? trimmed
                : trimmed[..maxLength] + "...";
        }

        private static string FormatDiagnosticValue(string value)
        {
            return EpgDiagnosticFormatter.Format(value);
        }

        private sealed record EpgSyncMetrics(
            int TotalLiveChannelCount,
            int MatchedChannelCount,
            int UnmatchedChannelCount,
            int CurrentCoverageCount,
            int NextCoverageCount,
            int XmltvChannelCount,
            int ExactMatchCount,
            int NormalizedMatchCount,
            int ApprovedMatchCount,
            int WeakMatchCount,
            EpgSyncResultCode ResultCode,
            string MatchBreakdown);

        private sealed record XmltvSourceParseSummary(int XmltvChannelCount, int ProgrammeCount);

        private sealed class MatchedProgrammeAccumulator
        {
            public MatchedProgrammeAccumulator(ChannelEpgMatchOutcome outcome, DateTime startTimeUtc, DateTime endTimeUtc)
            {
                Outcome = outcome;
                StartTimeUtc = startTimeUtc;
                EndTimeUtc = endTimeUtc;
            }

            public ChannelEpgMatchOutcome Outcome { get; }
            public DateTime StartTimeUtc { get; }
            public DateTime EndTimeUtc { get; }
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Subtitle { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
        }

        private sealed class XmltvChannelAccumulator
        {
            public XmltvChannelAccumulator(string id, EpgDiscoveredGuideSource source)
            {
                Id = id;
                SourcePriority = source.Priority;
                SourceKind = source.Kind;
                SourceLabel = source.Label;
                SourceUrl = source.Url;
            }

            public string Id { get; }
            public int SourcePriority { get; set; }
            public EpgGuideSourceKind SourceKind { get; set; }
            public string SourceLabel { get; set; }
            public string SourceUrl { get; set; }
            public HashSet<string> DisplayNames { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> IconUrls { get; } = new(StringComparer.OrdinalIgnoreCase);

            public XmltvChannelDescriptor ToDescriptor()
            {
                return new XmltvChannelDescriptor
                {
                    Id = Id,
                    DisplayNames = DisplayNames.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                    IconUrls = IconUrls.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList(),
                    SourcePriority = SourcePriority,
                    SourceKind = SourceKind,
                    SourceLabel = SourceLabel,
                    SourceUrl = SourceUrl
                };
            }
        }

        private sealed class EpgSyncFailureException : Exception
        {
            public EpgSyncFailureException(
                string message,
                EpgActiveMode activeMode,
                EpgSyncResultCode resultCode,
                EpgFailureStage failureStage,
                string activeXmltvUrl,
                Exception innerException)
                : base(message, innerException)
            {
                ActiveMode = activeMode;
                ResultCode = resultCode;
                FailureStage = failureStage;
                ActiveXmltvUrl = activeXmltvUrl ?? string.Empty;
            }

            public EpgActiveMode ActiveMode { get; }
            public EpgSyncResultCode ResultCode { get; }
            public EpgFailureStage FailureStage { get; }
            public string ActiveXmltvUrl { get; }
        }
    }
}
