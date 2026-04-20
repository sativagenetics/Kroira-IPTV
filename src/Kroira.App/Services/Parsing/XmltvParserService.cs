#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        Task ParseAndImportEpgAsync(AppDbContext db, int sourceProfileId);
    }

    public class XmltvParserService : IXmltvParserService
    {
        private readonly IReadOnlyDictionary<SourceType, IEpgSourceDiscoveryService> _discoveryServices;

        public XmltvParserService(IEnumerable<IEpgSourceDiscoveryService> discoveryServices)
        {
            _discoveryServices = discoveryServices.ToDictionary(service => service.SourceType);
        }

        public async Task ParseAndImportEpgAsync(AppDbContext db, int sourceProfileId)
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

                await ParseAndPersistXmltvAsync(db, sourceProfileId, discovered);
            }
            catch (EpgUnavailableException ex)
            {
                await MarkEpgUnavailableAsync(db, sourceProfileId, activeMode, ex.Message);
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

                throw new Exception($"Failed to sync XMLTV EPG: {ex.Message}");
            }
        }

        private static async Task ParseAndPersistXmltvAsync(
            AppDbContext db,
            int sourceProfileId,
            EpgDiscoveryResult discovered)
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
            var xmltvChannels = doc.Descendants("channel")
                .Where(channel => !string.IsNullOrWhiteSpace(channel.Attribute("id")?.Value))
                .GroupBy(channel => channel.Attribute("id")!.Value.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new XmltvChannelInfo(
                    group.Key,
                    group.SelectMany(channel => channel.Elements("display-name"))
                        .Select(element => element.Value.Trim())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()))
                .ToDictionary(channel => channel.Id, StringComparer.OrdinalIgnoreCase);

            ImportRuntimeLogger.Log(
                "EPG SYNC",
                $"source_profile_id={sourceProfileId}; stage=parse_complete; xmltv_channel_count={xmltvChannels.Count}; programme_count={programmeNodes.Count}");

            var channels = await db.Channels
                .Join(
                    db.ChannelCategories.Where(category => category.SourceProfileId == sourceProfileId),
                    channel => channel.ChannelCategoryId,
                    category => category.Id,
                    (channel, category) => channel)
                .ToListAsync();

            var matcher = new EpgChannelMatcher(channels);
            var channelMatches = new Dictionary<string, ChannelMatchOutcome>(StringComparer.OrdinalIgnoreCase);

            foreach (var xmltvChannel in xmltvChannels.Values)
            {
                var outcome = matcher.Match(xmltvChannel);
                channelMatches[xmltvChannel.Id] = outcome;
                LogMatchOutcome(sourceProfileId, xmltvChannel, outcome);
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
                    outcome = matcher.Match(new XmltvChannelInfo(xmltvChannelId, new List<string>()));
                    channelMatches[xmltvChannelId] = outcome;
                    LogMatchOutcome(sourceProfileId, new XmltvChannelInfo(xmltvChannelId, new List<string>()), outcome);
                }

                if (outcome.Channel == null)
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

                epgItems.Add(new EpgProgram
                {
                    ChannelId = outcome.Channel.Id,
                    StartTimeUtc = start.Value,
                    EndTimeUtc = end.Value,
                    Title = string.IsNullOrWhiteSpace(title) ? "Unknown Program" : title,
                    Description = description,
                    Subtitle = string.IsNullOrWhiteSpace(subtitle) ? null : subtitle,
                    Category = string.IsNullOrWhiteSpace(category) ? null : category
                });
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
            IEnumerable<ChannelMatchOutcome> matchOutcomes)
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

        private static string BuildMatchBreakdown(IReadOnlyDictionary<ChannelMatchReason, int> counts, int unmatchedCount)
        {
            static int GetCount(IReadOnlyDictionary<ChannelMatchReason, int> map, ChannelMatchReason reason) =>
                map.TryGetValue(reason, out var count) ? count : 0;

            return $"tvg_id={GetCount(counts, ChannelMatchReason.TvgIdExact)}; normalized_name={GetCount(counts, ChannelMatchReason.NormalizedNameExact)}; alias={GetCount(counts, ChannelMatchReason.AliasExact)}; fuzzy={GetCount(counts, ChannelMatchReason.FuzzyFallback)}; unmatched={unmatchedCount}";
        }

        private static void LogMatchOutcome(int sourceProfileId, XmltvChannelInfo xmltvChannel, ChannelMatchOutcome outcome)
        {
            ImportRuntimeLogger.Log(
                "EPG MATCH",
                $"source_profile_id={sourceProfileId}; xmltv_channel_id={FormatDiagnosticValue(xmltvChannel.Id)}; display_names={FormatDiagnosticValue(string.Join(" | ", xmltvChannel.DisplayNames))}; match_reason={outcome.Reason}; channel_id={(outcome.Channel?.Id ?? 0)}; channel_name={FormatDiagnosticValue(outcome.Channel?.Name ?? string.Empty)}; diagnostics={FormatDiagnosticValue(outcome.Diagnostic)}");
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

        private sealed class XmltvChannelInfo
        {
            public XmltvChannelInfo(string id, List<string> displayNames)
            {
                Id = id;
                DisplayNames = displayNames;
            }

            public string Id { get; }
            public List<string> DisplayNames { get; }
        }

        private enum ChannelMatchReason
        {
            None = 0,
            TvgIdExact = 1,
            NormalizedNameExact = 2,
            AliasExact = 3,
            FuzzyFallback = 4
        }

        private sealed class ChannelMatchOutcome
        {
            public Channel? Channel { get; init; }
            public ChannelMatchReason Reason { get; init; }
            public string Diagnostic { get; init; } = string.Empty;
        }

        private sealed class EpgChannelMatcher
        {
            private static readonly Regex MultiSpaceRegex = new(@"\s{2,}", RegexOptions.Compiled);
            private static readonly Regex QualityRegex = new(@"\b(?:hd|fhd|uhd|sd|4k)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex AliasNoiseRegex = new(@"\b(?:tv|television|channel)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            private static readonly Regex NonAlphaNumericRegex = new(@"[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            private static readonly IReadOnlyDictionary<string, string> NumberWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["one"] = "1",
                ["two"] = "2",
                ["three"] = "3",
                ["four"] = "4",
                ["five"] = "5",
                ["six"] = "6",
                ["seven"] = "7",
                ["eight"] = "8",
                ["nine"] = "9",
                ["ten"] = "10"
            };

            private readonly Dictionary<string, List<Channel>> _byGuideId = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<Channel>> _byNormalizedName = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<Channel>> _byAliasKey = new(StringComparer.OrdinalIgnoreCase);
            private readonly List<ChannelCandidate> _candidates = new();

            public EpgChannelMatcher(IEnumerable<Channel> channels)
            {
                foreach (var channel in channels)
                {
                    AddIndex(_byGuideId, channel.EpgChannelId, channel);
                    AddIndex(_byNormalizedName, NormalizeExactKey(channel.Name), channel);

                    var aliasKeys = BuildAliasKeys(channel.Name)
                        .Concat(BuildAliasKeys(channel.EpgChannelId))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var aliasKey in aliasKeys)
                    {
                        AddIndex(_byAliasKey, aliasKey, channel);
                    }

                    _candidates.Add(new ChannelCandidate(channel, aliasKeys));
                }
            }

            public ChannelMatchOutcome Match(XmltvChannelInfo xmltvChannel)
            {
                if (_byGuideId.TryGetValue(xmltvChannel.Id, out var guideMatches) && guideMatches.Count == 1)
                {
                    return new ChannelMatchOutcome
                    {
                        Channel = guideMatches[0],
                        Reason = ChannelMatchReason.TvgIdExact,
                        Diagnostic = $"Exact tvg-id match on '{xmltvChannel.Id}'."
                    };
                }

                foreach (var candidate in BuildSourceCandidates(xmltvChannel))
                {
                    var normalized = NormalizeExactKey(candidate);
                    if (_byNormalizedName.TryGetValue(normalized, out var nameMatches) && nameMatches.Count == 1)
                    {
                        return new ChannelMatchOutcome
                        {
                            Channel = nameMatches[0],
                            Reason = ChannelMatchReason.NormalizedNameExact,
                            Diagnostic = $"Normalized name match on '{candidate}'."
                        };
                    }
                }

                foreach (var candidate in BuildSourceCandidates(xmltvChannel))
                {
                    foreach (var aliasKey in BuildAliasKeys(candidate))
                    {
                        if (_byAliasKey.TryGetValue(aliasKey, out var aliasMatches) && aliasMatches.Count == 1)
                        {
                            return new ChannelMatchOutcome
                            {
                                Channel = aliasMatches[0],
                                Reason = ChannelMatchReason.AliasExact,
                                Diagnostic = $"Alias key match on '{candidate}' using '{aliasKey}'."
                            };
                        }
                    }
                }

                var fuzzyOutcome = FindFuzzyMatch(xmltvChannel);
                if (fuzzyOutcome != null)
                {
                    return fuzzyOutcome;
                }

                return new ChannelMatchOutcome
                {
                    Channel = null,
                    Reason = ChannelMatchReason.None,
                    Diagnostic = "No exact, alias, or safe fuzzy match was found."
                };
            }

            private ChannelMatchOutcome? FindFuzzyMatch(XmltvChannelInfo xmltvChannel)
            {
                var fuzzyCandidates = BuildSourceCandidates(xmltvChannel)
                    .SelectMany(BuildAliasKeys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(value => value.Length >= 4)
                    .ToList();

                if (fuzzyCandidates.Count == 0)
                {
                    return null;
                }

                var scored = new List<(ChannelCandidate Candidate, double Score, string AliasKey, string SourceValue)>();
                foreach (var sourceValue in fuzzyCandidates)
                {
                    foreach (var candidate in _candidates)
                    {
                        foreach (var aliasKey in candidate.AliasKeys)
                        {
                            var score = ComputeDiceCoefficient(sourceValue, aliasKey);
                            if (score >= 0.92)
                            {
                                scored.Add((candidate, score, aliasKey, sourceValue));
                            }
                        }
                    }
                }

                if (scored.Count == 0)
                {
                    return null;
                }

                var ordered = scored
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.Candidate.Channel.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var best = ordered[0];
                var secondScore = ordered.Skip(1).FirstOrDefault().Score;

                if (best.Score < 0.92 || (secondScore > 0 && best.Score - secondScore < 0.04))
                {
                    return null;
                }

                return new ChannelMatchOutcome
                {
                    Channel = best.Candidate.Channel,
                    Reason = ChannelMatchReason.FuzzyFallback,
                    Diagnostic = $"Safe fuzzy fallback matched '{best.SourceValue}' to alias '{best.AliasKey}' with score {best.Score:0.00}."
                };
            }

            private static IEnumerable<string> BuildSourceCandidates(XmltvChannelInfo xmltvChannel)
            {
                yield return xmltvChannel.Id;
                foreach (var displayName in xmltvChannel.DisplayNames)
                {
                    yield return displayName;
                }
            }

            private static void AddIndex(Dictionary<string, List<Channel>> index, string rawValue, Channel channel)
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return;
                }

                var key = rawValue.Trim();
                if (!index.TryGetValue(key, out var values))
                {
                    values = new List<Channel>();
                    index[key] = values;
                }

                if (!values.Any(item => item.Id == channel.Id))
                {
                    values.Add(channel);
                }
            }

            private static string NormalizeExactKey(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return string.Empty;
                }

                var normalized = ContentClassifier.NormalizeLabel(value).Trim();
                normalized = QualityRegex.Replace(normalized, " ");
                normalized = MultiSpaceRegex.Replace(normalized, " ");
                return normalized.Trim().ToLowerInvariant();
            }

            private static IEnumerable<string> BuildAliasKeys(string value)
            {
                var alias = NormalizeAliasKey(value);
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    yield return alias;
                }
            }

            private static string NormalizeAliasKey(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return string.Empty;
                }

                var normalized = RemoveDiacritics(value).ToLowerInvariant();
                normalized = normalized.Replace("&", " and ");
                normalized = normalized.Replace("+", " plus ");
                normalized = NormalizeNumberWords(normalized);
                normalized = QualityRegex.Replace(normalized, " ");
                normalized = AliasNoiseRegex.Replace(normalized, " ");
                normalized = NonAlphaNumericRegex.Replace(normalized, " ");
                normalized = MultiSpaceRegex.Replace(normalized, " ").Trim();
                return normalized.Replace(" ", string.Empty);
            }

            private static string NormalizeNumberWords(string value)
            {
                var builder = new StringBuilder(value.Length + 8);
                foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(NumberWords.TryGetValue(token, out var mapped) ? mapped : token);
                }

                return builder.ToString();
            }

            private static string RemoveDiacritics(string value)
            {
                var normalized = value.Normalize(NormalizationForm.FormD);
                var builder = new StringBuilder(normalized.Length);
                foreach (var ch in normalized)
                {
                    if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    {
                        builder.Append(ch);
                    }
                }

                return builder.ToString().Normalize(NormalizationForm.FormC);
            }

            private static double ComputeDiceCoefficient(string left, string right)
            {
                if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                {
                    return 0;
                }

                if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                {
                    return 1;
                }

                var leftPairs = BuildBigrams(left);
                var rightPairs = BuildBigrams(right);
                if (leftPairs.Count == 0 || rightPairs.Count == 0)
                {
                    return 0;
                }

                var intersection = leftPairs.Intersect(rightPairs, StringComparer.Ordinal).Count();
                return (2.0 * intersection) / (leftPairs.Count + rightPairs.Count);
            }

            private static List<string> BuildBigrams(string value)
            {
                var compact = value.Replace(" ", string.Empty);
                if (compact.Length < 2)
                {
                    return new List<string>();
                }

                var bigrams = new List<string>(compact.Length - 1);
                for (var i = 0; i < compact.Length - 1; i++)
                {
                    bigrams.Add(compact.Substring(i, 2));
                }

                return bigrams;
            }

            private sealed record ChannelCandidate(Channel Channel, IReadOnlyList<string> AliasKeys);
        }
    }
}
