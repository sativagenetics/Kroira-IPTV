#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
            XDocument doc;
            try
            {
                doc = XDocument.Parse(discovered.XmlContent);
            }
            catch (Exception ex)
            {
                throw new EpgSyncFailureException(
                    $"XMLTV parse failed: {ex.Message}",
                    discovered.ActiveMode,
                    EpgSyncResultCode.ParseFailed,
                    EpgFailureStage.Parse,
                    discovered.ActiveXmltvUrl,
                    ex);
            }

            var programmeNodes = doc.Descendants("programme").ToList();
            var programmeChannelIds = programmeNodes
                .Select(programme => programme.Attribute("channel")?.Value?.Trim())
                .Where(channelId => !string.IsNullOrWhiteSpace(channelId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var xmltvChannels = doc.Descendants("channel")
                .Where(channel => !string.IsNullOrWhiteSpace(channel.Attribute("id")?.Value))
                .GroupBy(channel => channel.Attribute("id")!.Value.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new XmltvChannelDescriptor
                {
                    Id = group.Key,
                    DisplayNames = group.SelectMany(channel => channel.Elements("display-name"))
                        .Select(element => element.Value.Trim())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    IconUrls = group.SelectMany(channel => channel.Elements("icon"))
                        .Select(element => element.Attribute("src")?.Value?.Trim())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Cast<string>()
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .ToDictionary(channel => channel.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var programmeChannelId in programmeChannelIds)
            {
                if (!xmltvChannels.ContainsKey(programmeChannelId!))
                {
                    xmltvChannels[programmeChannelId!] = new XmltvChannelDescriptor
                    {
                        Id = programmeChannelId!,
                        DisplayNames = Array.Empty<string>(),
                        IconUrls = Array.Empty<string>()
                    };
                }
            }

            ImportRuntimeLogger.Log(
                "EPG SYNC",
                $"source_profile_id={sourceProfileId}; stage=parse_complete; xmltv_channel_count={xmltvChannels.Count}; programme_count={programmeNodes.Count}");

            var orderedXmltvChannels = xmltvChannels.Values
                .OrderByDescending(channel => programmeChannelIds.Contains(channel.Id))
                .ThenBy(channel => channel.Id, StringComparer.OrdinalIgnoreCase)
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

            foreach (var programme in programmeNodes)
            {
                var xmltvChannelId = programme.Attribute("channel")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(xmltvChannelId))
                {
                    continue;
                }

                if (!channelMatches.TryGetValue(xmltvChannelId, out var outcome))
                {
                    continue;
                }

                if (outcome.Channels.Count == 0)
                {
                    continue;
                }

                var startString = programme.Attribute("start")?.Value;
                var stopString = programme.Attribute("stop")?.Value;
                if (string.IsNullOrWhiteSpace(startString) || string.IsNullOrWhiteSpace(stopString))
                {
                    continue;
                }

                var start = ParseXmltvDate(startString);
                var end = ParseXmltvDate(stopString);
                if (start == null || end == null || end <= start)
                {
                    continue;
                }

                var title = programme.Element("title")?.Value?.Trim();
                var description = programme.Element("desc")?.Value?.Trim() ?? string.Empty;
                var subtitle = programme.Element("sub-title")?.Value?.Trim();
                var category = programme.Element("category")?.Value?.Trim();

                foreach (var matchedChannel in outcome.Channels)
                {
                    epgItems.Add(new EpgProgram
                    {
                        ChannelId = matchedChannel.Id,
                        StartTimeUtc = start.Value,
                        EndTimeUtc = end.Value,
                        Title = string.IsNullOrWhiteSpace(title) ? "Unknown Program" : title,
                        Description = description,
                        Subtitle = string.IsNullOrWhiteSpace(subtitle) ? null : subtitle,
                        Category = string.IsNullOrWhiteSpace(category) ? null : category
                    });
                }
            }

            var metrics = BuildSyncMetrics(channels, epgItems, channelMatches.Values);
            ImportRuntimeLogger.Log(
                "EPG SYNC",
                $"source_profile_id={sourceProfileId}; stage=match_complete; total_live_channels={metrics.TotalLiveChannelCount}; matched_channels={metrics.MatchedChannelCount}; unmatched_channels={metrics.UnmatchedChannelCount}; current_coverage={metrics.CurrentCoverageCount}; next_coverage={metrics.NextCoverageCount}; match_breakdown={FormatDiagnosticValue(metrics.MatchBreakdown)}");

            await PersistGuideDataAsync(db, sourceProfileId, discovered, channels, epgItems, metrics);
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
                epgLog.MatchBreakdown = metrics.MatchBreakdown;
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
                epgLog.MatchBreakdown = string.Empty;
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
            IEnumerable<ChannelEpgMatchOutcome> matchOutcomes)
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

            var matchCounts = matchOutcomes
                .GroupBy(outcome => outcome.Reason)
                .ToDictionary(group => group.Key, group => group.Count());

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
                resultCode,
                BuildMatchBreakdown(matchCounts, unmatchedCount));
        }

        private static string BuildMatchBreakdown(IReadOnlyDictionary<ChannelEpgMatchSource, int> counts, int unmatchedCount)
        {
            static int GetCount(IReadOnlyDictionary<ChannelEpgMatchSource, int> map, ChannelEpgMatchSource reason) =>
                map.TryGetValue(reason, out var count) ? count : 0;

            return $"provider_id={GetCount(counts, ChannelEpgMatchSource.Provider)}; reused={GetCount(counts, ChannelEpgMatchSource.Previous)}; normalized={GetCount(counts, ChannelEpgMatchSource.Normalized)}; alias={GetCount(counts, ChannelEpgMatchSource.Alias)}; regex={GetCount(counts, ChannelEpgMatchSource.Regex)}; fuzzy={GetCount(counts, ChannelEpgMatchSource.Fuzzy)}; unmatched={unmatchedCount}";
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
                $"source_profile_id={sourceProfileId}; xmltv_channel_id={FormatDiagnosticValue(xmltvChannel.Id)}; display_names={FormatDiagnosticValue(string.Join(" | ", xmltvChannel.DisplayNames))}; match_reason={outcome.Reason}; confidence={outcome.Confidence}; matched_channel_count={outcome.Channels.Count}; channel_ids={FormatDiagnosticValue(matchedChannelIds)}; channel_names={FormatDiagnosticValue(matchedChannelNames)}; matched_value={FormatDiagnosticValue(outcome.MatchedValue)}; matched_key={FormatDiagnosticValue(outcome.MatchedKey)}; diagnostics={FormatDiagnosticValue(outcome.Diagnostic)}");
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
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return $"\"{value.Replace("\"", "'")}\"";
        }

        private sealed record EpgSyncMetrics(
            int TotalLiveChannelCount,
            int MatchedChannelCount,
            int UnmatchedChannelCount,
            int CurrentCoverageCount,
            int NextCoverageCount,
            EpgSyncResultCode ResultCode,
            string MatchBreakdown);

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
