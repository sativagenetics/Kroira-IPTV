using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kroira.App.Data;
using Kroira.App.Models;
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
            _discoveryServices = discoveryServices.ToDictionary(s => s.SourceType);
        }

        public async Task ParseAndImportEpgAsync(AppDbContext db, int sourceProfileId)
        {
            var profile = await db.SourceProfiles.FirstOrDefaultAsync(p => p.Id == sourceProfileId);
            if (profile == null)
            {
                throw new Exception("Source not found.");
            }

            try
            {
                if (!_discoveryServices.TryGetValue(profile.Type, out var discoveryService))
                {
                    throw new Exception($"EPG discovery is not supported for {profile.Type} sources.");
                }

                var discovered = await discoveryService.DiscoverAsync(db, sourceProfileId);
                await ParseAndPersistXmltvAsync(db, sourceProfileId, discovered);
            }
            catch (Exception ex)
            {
                await MarkEpgFailureAsync(db, sourceProfileId, ex);
                throw new Exception($"Failed to sync XMLTV EPG: {ex.Message}");
            }
        }

        private static async Task ParseAndPersistXmltvAsync(
            AppDbContext db,
            int sourceProfileId,
            EpgDiscoveryResult discovered)
        {
            var doc = XDocument.Parse(discovered.XmlContent);
            var programmes = doc.Descendants("programme").ToList();
            var channelMapping = doc.Descendants("channel")
                .Where(c => !string.IsNullOrWhiteSpace(c.Attribute("id")?.Value))
                .GroupBy(c => c.Attribute("id")!.Value.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => new XmltvChannelInfo(
                        g.Key,
                        g.First()
                            .Elements("display-name")
                            .Select(e => e.Value.Trim())
                            .Where(v => !string.IsNullOrWhiteSpace(v))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()),
                    StringComparer.OrdinalIgnoreCase);

            var channels = await db.Channels
                .Join(db.ChannelCategories.Where(cat => cat.SourceProfileId == sourceProfileId),
                      ch => ch.ChannelCategoryId,
                      cat => cat.Id,
                      (ch, cat) => ch)
                .ToListAsync();

            var matcher = new EpgChannelMatcher(channels);
            var epgItems = new List<EpgProgram>();

            foreach (var p in programmes)
            {
                var chIdNode = p.Attribute("channel")?.Value;
                if (string.IsNullOrWhiteSpace(chIdNode)) continue;

                var xmltvChannelId = chIdNode.Trim();
                channelMapping.TryGetValue(xmltvChannelId, out var xmltvChannel);

                var targetCh = matcher.Match(xmltvChannelId, xmltvChannel);
                if (targetCh == null) continue;

                var startString = p.Attribute("start")?.Value;
                var stopString = p.Attribute("stop")?.Value;
                if (string.IsNullOrWhiteSpace(startString) || string.IsNullOrWhiteSpace(stopString)) continue;

                var start = ParseXmltvDate(startString);
                var end = ParseXmltvDate(stopString);

                if (start == null || end == null || end <= start) continue;

                var titleNode = p.Element("title");
                var descNode = p.Element("desc");
                var subtitleNode = p.Element("sub-title");
                var categoryNode = p.Element("category");

                epgItems.Add(new EpgProgram
                {
                    ChannelId = targetCh.Id,
                    StartTimeUtc = start.Value,
                    EndTimeUtc = end.Value,
                    Title = string.IsNullOrWhiteSpace(titleNode?.Value) ? "Unknown Program" : titleNode!.Value.Trim(),
                    Description = descNode?.Value?.Trim() ?? string.Empty,
                    Subtitle = string.IsNullOrWhiteSpace(subtitleNode?.Value) ? null : subtitleNode!.Value.Trim(),
                    Category = string.IsNullOrWhiteSpace(categoryNode?.Value) ? null : categoryNode!.Value.Trim()
                });
            }

            using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                var chIds = channels.Select(c => c.Id).ToList();

                for (int i = 0; i < chIds.Count; i += 50)
                {
                    var chunk = chIds.Skip(i).Take(50).ToList();
                    var oldEpg = await db.EpgPrograms.Where(e => chunk.Contains(e.ChannelId)).ToListAsync();
                    if (oldEpg.Any())
                    {
                        db.EpgPrograms.RemoveRange(oldEpg);
                    }
                }
                await db.SaveChangesAsync();

                for (int i = 0; i < epgItems.Count; i += 1000)
                {
                    var chunk = epgItems.Skip(i).Take(1000).ToList();
                    db.EpgPrograms.AddRange(chunk);
                    await db.SaveChangesAsync();
                }

                var matchedChannels = epgItems.Select(e => e.ChannelId).Distinct().Count();
                var now = DateTime.UtcNow;

                var epgLog = await db.EpgSyncLogs.FirstOrDefaultAsync(e => e.SourceProfileId == sourceProfileId);
                if (epgLog == null)
                {
                    epgLog = new EpgSyncLog { SourceProfileId = sourceProfileId };
                    db.EpgSyncLogs.Add(epgLog);
                }
                epgLog.SyncedAtUtc = now;
                epgLog.IsSuccess = true;
                epgLog.MatchedChannelCount = matchedChannels;
                epgLog.ProgrammeCount = epgItems.Count;
                epgLog.FailureReason = string.Empty;

                var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
                if (syncState != null)
                {
                    syncState.LastAttempt = now;
                    syncState.HttpStatusCode = 200;
                    syncState.ErrorLog = $"EPG: {epgItems.Count:N0} programs - {matchedChannels} channels matched via {discovered.Description}.";
                }

                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static async Task MarkEpgFailureAsync(AppDbContext db, int sourceProfileId, Exception ex)
        {
            var failedAt = DateTime.UtcNow;
            var shortReason = ex.Message.Length > 200 ? ex.Message.Substring(0, 200) : ex.Message;

            var epgLog = await db.EpgSyncLogs.FirstOrDefaultAsync(e => e.SourceProfileId == sourceProfileId);
            if (epgLog == null)
            {
                epgLog = new EpgSyncLog { SourceProfileId = sourceProfileId };
                db.EpgSyncLogs.Add(epgLog);
            }
            epgLog.SyncedAtUtc = failedAt;
            epgLog.IsSuccess = false;
            epgLog.MatchedChannelCount = 0;
            epgLog.ProgrammeCount = 0;
            epgLog.FailureReason = shortReason;

            var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(s => s.SourceProfileId == sourceProfileId);
            if (syncState != null)
            {
                syncState.LastAttempt = failedAt;
                syncState.HttpStatusCode = 500;
                syncState.ErrorLog = $"EPG failed: {shortReason}";
            }

            try { await db.SaveChangesAsync(); } catch { }
        }

        private static DateTime? ParseXmltvDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return null;
            try
            {
                dateStr = dateStr.Trim();
                if (dateStr.Length >= 14)
                {
                    string formatNode = dateStr.Substring(0, 14);

                    string offsetNode = "+0000";
                    if (dateStr.Length >= 15)
                    {
                        string remainder = dateStr.Substring(14).Trim();
                        if (remainder.Length >= 5)
                            offsetNode = remainder.Substring(0, 5);
                    }

                    if (offsetNode.Length == 5 && !offsetNode.Contains(':'))
                        offsetNode = offsetNode.Insert(3, ":");

                    if (DateTimeOffset.TryParseExact(
                            $"{formatNode} {offsetNode}",
                            "yyyyMMddHHmmss zzz",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out var dto))
                    {
                        return dto.UtcDateTime;
                    }

                    if (DateTime.TryParseExact(
                            formatNode,
                            "yyyyMMddHHmmss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                            out var dt))
                    {
                        return dt;
                    }
                }
            }
            catch { }
            return null;
        }

        private static string NormalizeChannelName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var result = name.Trim();
            var suffixes = new[] { " HD", " SD", " FHD", " 4K", " UHD", " (HD)", " (SD)" };
            foreach (var suffix in suffixes)
            {
                if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffix.Length).TrimEnd();
                }
            }

            return result;
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

        private sealed class EpgChannelMatcher
        {
            private readonly Dictionary<string, Channel> _byGuideId;
            private readonly Dictionary<string, Channel> _byName;
            private readonly Dictionary<string, Channel> _byNormalizedName;

            public EpgChannelMatcher(IEnumerable<Channel> channels)
            {
                _byGuideId = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
                _byName = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
                _byNormalizedName = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);

                foreach (var channel in channels)
                {
                    AddUnique(_byGuideId, channel.EpgChannelId, channel);
                    AddUnique(_byName, channel.Name, channel);
                    AddUnique(_byNormalizedName, NormalizeChannelName(channel.Name), channel);
                }
            }

            public Channel Match(string xmltvChannelId, XmltvChannelInfo xmltvChannel)
            {
                if (_byGuideId.TryGetValue(xmltvChannelId, out var byGuideId))
                {
                    return byGuideId;
                }

                foreach (var candidate in BuildCandidates(xmltvChannelId, xmltvChannel))
                {
                    if (_byName.TryGetValue(candidate, out var byName))
                    {
                        return byName;
                    }
                }

                foreach (var candidate in BuildCandidates(xmltvChannelId, xmltvChannel).Select(NormalizeChannelName))
                {
                    if (_byNormalizedName.TryGetValue(candidate, out var byNormalizedName))
                    {
                        return byNormalizedName;
                    }
                }

                return null;
            }

            private static IEnumerable<string> BuildCandidates(string xmltvChannelId, XmltvChannelInfo xmltvChannel)
            {
                yield return xmltvChannelId;

                if (xmltvChannel == null)
                {
                    yield break;
                }

                yield return xmltvChannel.Id;
                foreach (var displayName in xmltvChannel.DisplayNames)
                {
                    yield return displayName;
                }
            }

            private static void AddUnique(Dictionary<string, Channel> map, string value, Channel channel)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                var key = value.Trim();
                if (!map.ContainsKey(key))
                {
                    map[key] = channel;
                }
            }
        }
    }
}
