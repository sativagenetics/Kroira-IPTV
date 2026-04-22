#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface ISourceEnrichmentService
    {
        Task PrepareLiveCatalogAsync(AppDbContext db, int sourceProfileId);
        Task<SourceEpgEnrichmentResult> ApplyXmltvEnrichmentAsync(
            AppDbContext db,
            int sourceProfileId,
            IReadOnlyCollection<XmltvChannelDescriptor> xmltvChannels);
    }

    public sealed class SourceEnrichmentService : ISourceEnrichmentService
    {
        private readonly ILiveChannelIdentityService _identityService;

        public SourceEnrichmentService(ILiveChannelIdentityService identityService)
        {
            _identityService = identityService;
        }

        public async Task PrepareLiveCatalogAsync(AppDbContext db, int sourceProfileId)
        {
            var channels = await LoadSourceChannelsAsync(db, sourceProfileId);
            if (channels.Count == 0)
            {
                return;
            }

            var records = await db.SourceChannelEnrichmentRecords
                .Where(record => record.SourceProfileId == sourceProfileId)
                .ToListAsync();
            var lookup = BuildLookup(records);
            var nowUtc = DateTime.UtcNow;

            foreach (var channel in channels)
            {
                PrepareChannel(db, channel, lookup, records, sourceProfileId, nowUtc);
            }

            await db.SaveChangesAsync();
        }

        public async Task<SourceEpgEnrichmentResult> ApplyXmltvEnrichmentAsync(
            AppDbContext db,
            int sourceProfileId,
            IReadOnlyCollection<XmltvChannelDescriptor> xmltvChannels)
        {
            await PrepareLiveCatalogAsync(db, sourceProfileId);

            var channels = await LoadSourceChannelsAsync(db, sourceProfileId);
            if (channels.Count == 0)
            {
                return new SourceEpgEnrichmentResult();
            }

            var records = await db.SourceChannelEnrichmentRecords
                .Where(record => record.SourceProfileId == sourceProfileId)
                .ToListAsync();
            var lookup = BuildLookup(records);
            var normalizedXmltvChannels = xmltvChannels
                .Where(channel => !string.IsNullOrWhiteSpace(channel.Id))
                .GroupBy(channel => channel.Id.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            var matcher = new EpgChannelMatcher(_identityService, channels);
            var matches = matcher.MatchAll(normalizedXmltvChannels);
            var nowUtc = DateTime.UtcNow;

            foreach (var channel in channels)
            {
                PrepareChannel(db, channel, lookup, records, sourceProfileId, nowUtc);
            }

            foreach (var xmltvChannel in normalizedXmltvChannels)
            {
                if (!matches.TryGetValue(xmltvChannel.Id, out var outcome) || outcome.Channels.Count == 0)
                {
                    continue;
                }

                var xmltvIconUrl = SelectBestLogo(xmltvChannel.IconUrls);
                foreach (var channel in outcome.Channels)
                {
                    ApplyMatchedGuide(channel, outcome, xmltvChannel, xmltvIconUrl, nowUtc);
                    var record = GetOrCreateRecord(db, channel, lookup, records, sourceProfileId);
                    UpdateRecord(record, channel, xmltvChannel, xmltvIconUrl, nowUtc);
                }
            }

            await db.SaveChangesAsync();

            return new SourceEpgEnrichmentResult
            {
                Matches = matches
            };
        }

        private static async Task<List<Channel>> LoadSourceChannelsAsync(AppDbContext db, int sourceProfileId)
        {
            return await db.Channels
                .Join(
                    db.ChannelCategories.Where(category => category.SourceProfileId == sourceProfileId),
                    channel => channel.ChannelCategoryId,
                    category => category.Id,
                    (channel, category) => channel)
                .ToListAsync();
        }

        private void PrepareChannel(
            AppDbContext db,
            Channel channel,
            EnrichmentLookup lookup,
            ICollection<SourceChannelEnrichmentRecord> records,
            int sourceProfileId,
            DateTime nowUtc)
        {
            channel.ProviderLogoUrl = NormalizeValue(string.IsNullOrWhiteSpace(channel.ProviderLogoUrl) ? channel.LogoUrl : channel.ProviderLogoUrl);
            channel.ProviderEpgChannelId = NormalizeValue(string.IsNullOrWhiteSpace(channel.ProviderEpgChannelId) ? channel.EpgChannelId : channel.ProviderEpgChannelId);

            var identity = _identityService.Build(channel.Name, channel.ProviderEpgChannelId);
            channel.NormalizedIdentityKey = identity.IdentityKey;
            channel.NormalizedName = identity.NormalizedName;
            channel.AliasKeys = SerializeAliasKeys(identity.AliasKeys);
            channel.EnrichedAtUtc = nowUtc;

            ApplyProviderDefaults(channel);

            var record = GetOrCreateRecord(db, channel, lookup, records, sourceProfileId);
            record.IdentityKey = channel.NormalizedIdentityKey;
            record.NormalizedName = channel.NormalizedName;
            record.AliasKeys = channel.AliasKeys;
            record.ProviderName = channel.Name;
            record.ProviderEpgChannelId = channel.ProviderEpgChannelId;
            record.ProviderLogoUrl = channel.ProviderLogoUrl;
            record.LastSeenAtUtc = nowUtc;
            if (record.LastAppliedAtUtc == default)
            {
                record.LastAppliedAtUtc = nowUtc;
            }

            ApplyStoredFallbacks(channel, record);
        }

        private static void ApplyProviderDefaults(Channel channel)
        {
            var providerGuideId = NormalizeValue(channel.ProviderEpgChannelId);
            if (!string.IsNullOrWhiteSpace(providerGuideId))
            {
                channel.EpgChannelId = providerGuideId;
                channel.EpgMatchSource = ChannelEpgMatchSource.Provider;
                channel.EpgMatchConfidence = 70;
                channel.EpgMatchSummary = "Using provider guide metadata until XMLTV confirms a better match.";
            }
            else
            {
                channel.EpgChannelId = string.Empty;
                channel.EpgMatchSource = ChannelEpgMatchSource.None;
                channel.EpgMatchConfidence = 0;
                channel.EpgMatchSummary = string.Empty;
            }

            var providerLogoUrl = SelectBestLogo(new[] { channel.ProviderLogoUrl });
            if (!string.IsNullOrWhiteSpace(providerLogoUrl))
            {
                channel.LogoUrl = providerLogoUrl;
                channel.LogoSource = ChannelLogoSource.Provider;
                channel.LogoConfidence = 92;
                channel.LogoSummary = "Using the provider logo.";
            }
            else
            {
                channel.LogoUrl = string.Empty;
                channel.LogoSource = ChannelLogoSource.None;
                channel.LogoConfidence = 0;
                channel.LogoSummary = string.Empty;
            }
        }

        private static void ApplyStoredFallbacks(Channel channel, SourceChannelEnrichmentRecord record)
        {
            if (string.IsNullOrWhiteSpace(channel.ProviderEpgChannelId) &&
                !string.IsNullOrWhiteSpace(record.MatchedXmltvChannelId))
            {
                channel.EpgChannelId = record.MatchedXmltvChannelId;
                channel.EpgMatchSource = ChannelEpgMatchSource.Previous;
                channel.EpgMatchConfidence = Math.Max(record.EpgMatchConfidence - 4, 68);
                channel.EpgMatchSummary = string.IsNullOrWhiteSpace(record.MatchedXmltvDisplayName)
                    ? "Reused the last successful guide match for this channel."
                    : $"Reused the last successful guide match ({record.MatchedXmltvDisplayName}).";
            }

            if (string.IsNullOrWhiteSpace(channel.ProviderLogoUrl))
            {
                var fallbackLogo = SelectBestLogo(new[] { record.ResolvedLogoUrl, record.MatchedXmltvIconUrl });
                if (!string.IsNullOrWhiteSpace(fallbackLogo))
                {
                    channel.LogoUrl = fallbackLogo;
                    channel.LogoSource = ChannelLogoSource.Previous;
                    channel.LogoConfidence = Math.Max(record.LogoConfidence - 6, 66);
                    channel.LogoSummary = "Reused the last successful logo fallback for this channel.";
                }
            }
        }

        private static void ApplyMatchedGuide(
            Channel channel,
            ChannelEpgMatchOutcome outcome,
            XmltvChannelDescriptor xmltvChannel,
            string xmltvIconUrl,
            DateTime nowUtc)
        {
            channel.EpgChannelId = xmltvChannel.Id;
            channel.EpgMatchSource = outcome.Reason;
            channel.EpgMatchConfidence = outcome.Confidence;
            channel.EpgMatchSummary = BuildEpgSummary(outcome.Reason, xmltvChannel);
            channel.EnrichedAtUtc = nowUtc;

            if (!string.IsNullOrWhiteSpace(channel.ProviderLogoUrl))
            {
                channel.LogoUrl = channel.ProviderLogoUrl;
                channel.LogoSource = ChannelLogoSource.Provider;
                channel.LogoConfidence = 92;
                channel.LogoSummary = "Using the provider logo.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(xmltvIconUrl))
            {
                channel.LogoUrl = xmltvIconUrl;
                channel.LogoSource = ChannelLogoSource.Xmltv;
                channel.LogoConfidence = outcome.Reason switch
                {
                    ChannelEpgMatchSource.Provider => 88,
                    ChannelEpgMatchSource.Previous => 82,
                    ChannelEpgMatchSource.Normalized => 84,
                    ChannelEpgMatchSource.Alias => 78,
                    ChannelEpgMatchSource.Fuzzy => 68,
                    _ => 0
                };
                channel.LogoSummary = BuildLogoSummary(outcome.Reason, xmltvChannel);
            }
        }

        private static void UpdateRecord(
            SourceChannelEnrichmentRecord record,
            Channel channel,
            XmltvChannelDescriptor xmltvChannel,
            string xmltvIconUrl,
            DateTime nowUtc)
        {
            record.ProviderName = channel.Name;
            record.ProviderEpgChannelId = channel.ProviderEpgChannelId;
            record.ProviderLogoUrl = channel.ProviderLogoUrl;
            record.ResolvedLogoUrl = channel.LogoUrl;
            record.MatchedXmltvChannelId = channel.EpgChannelId;
            record.MatchedXmltvDisplayName = xmltvChannel.PrimaryDisplayName;
            record.MatchedXmltvIconUrl = xmltvIconUrl;
            record.EpgMatchSource = channel.EpgMatchSource;
            record.EpgMatchConfidence = channel.EpgMatchConfidence;
            record.EpgMatchSummary = channel.EpgMatchSummary;
            record.LogoSource = channel.LogoSource;
            record.LogoConfidence = channel.LogoConfidence;
            record.LogoSummary = channel.LogoSummary;
            record.LastAppliedAtUtc = nowUtc;
            record.LastSeenAtUtc = nowUtc;
        }

        private static string BuildEpgSummary(ChannelEpgMatchSource reason, XmltvChannelDescriptor xmltvChannel)
        {
            var label = string.IsNullOrWhiteSpace(xmltvChannel.PrimaryDisplayName)
                ? xmltvChannel.Id
                : xmltvChannel.PrimaryDisplayName;
            return reason switch
            {
                ChannelEpgMatchSource.Provider => $"Provider guide metadata matched XMLTV channel {label}.",
                ChannelEpgMatchSource.Previous => $"The previous successful guide mapping still resolves to XMLTV channel {label}.",
                ChannelEpgMatchSource.Normalized => $"Normalized channel identity matched XMLTV channel {label}.",
                ChannelEpgMatchSource.Alias => $"Alias fallback matched XMLTV channel {label}.",
                ChannelEpgMatchSource.Fuzzy => $"Safe fuzzy fallback matched XMLTV channel {label}.",
                _ => string.Empty
            };
        }

        private static string BuildLogoSummary(ChannelEpgMatchSource reason, XmltvChannelDescriptor xmltvChannel)
        {
            var label = string.IsNullOrWhiteSpace(xmltvChannel.PrimaryDisplayName)
                ? xmltvChannel.Id
                : xmltvChannel.PrimaryDisplayName;
            return reason switch
            {
                ChannelEpgMatchSource.Fuzzy => $"Logo recovered from the matched XMLTV channel {label} using a conservative fuzzy fallback.",
                ChannelEpgMatchSource.Alias => $"Logo recovered from the matched XMLTV channel {label} using alias fallback.",
                _ => $"Logo recovered from the matched XMLTV channel {label}."
            };
        }

        private static SourceChannelEnrichmentRecord GetOrCreateRecord(
            AppDbContext db,
            Channel channel,
            EnrichmentLookup lookup,
            ICollection<SourceChannelEnrichmentRecord> records,
            int sourceProfileId)
        {
            var record = lookup.Resolve(channel.NormalizedIdentityKey, DeserializeAliasKeys(channel.AliasKeys));
            if (record != null)
            {
                if (!string.Equals(record.IdentityKey, channel.NormalizedIdentityKey, StringComparison.OrdinalIgnoreCase) &&
                    !lookup.ContainsIdentity(channel.NormalizedIdentityKey))
                {
                    lookup.Remove(record);
                    record.IdentityKey = channel.NormalizedIdentityKey;
                    lookup.Add(record);
                }

                return record;
            }

            record = new SourceChannelEnrichmentRecord
            {
                SourceProfileId = sourceProfileId,
                IdentityKey = channel.NormalizedIdentityKey,
                NormalizedName = channel.NormalizedName,
                AliasKeys = channel.AliasKeys
            };

            records.Add(record);
            db.SourceChannelEnrichmentRecords.Add(record);
            lookup.Add(record);
            return record;
        }

        private static string SerializeAliasKeys(IEnumerable<string> values)
        {
            return string.Join("\n", values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<string> DeserializeAliasKeys(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            return value
                .Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string SelectBestLogo(IEnumerable<string?> urls)
        {
            foreach (var url in urls)
            {
                if (!string.IsNullOrWhiteSpace(url) && IsUsableLogoUrl(url))
                {
                    return url.Trim();
                }
            }

            return string.Empty;
        }

        private static bool IsUsableLogoUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return true;
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return false;
            }

            return uri.Scheme is "http" or "https" or "file" or "ms-appx" or "ms-appdata";
        }

        private static EnrichmentLookup BuildLookup(IEnumerable<SourceChannelEnrichmentRecord> records)
        {
            var lookup = new EnrichmentLookup();
            foreach (var record in records)
            {
                lookup.Add(record);
            }

            return lookup;
        }

        private sealed class EnrichmentLookup
        {
            private readonly Dictionary<string, SourceChannelEnrichmentRecord> _byIdentity = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<SourceChannelEnrichmentRecord>> _byAlias = new(StringComparer.OrdinalIgnoreCase);

            public void Add(SourceChannelEnrichmentRecord record)
            {
                if (!string.IsNullOrWhiteSpace(record.IdentityKey))
                {
                    _byIdentity[record.IdentityKey] = record;
                }

                foreach (var alias in DeserializeAliasKeys(record.AliasKeys))
                {
                    if (!_byAlias.TryGetValue(alias, out var records))
                    {
                        records = new List<SourceChannelEnrichmentRecord>();
                        _byAlias[alias] = records;
                    }

                    if (!records.Any(item => item.Id == record.Id && record.Id != 0))
                    {
                        records.Add(record);
                    }
                    else if (record.Id == 0 && !records.Contains(record))
                    {
                        records.Add(record);
                    }
                }
            }

            public void Remove(SourceChannelEnrichmentRecord record)
            {
                if (!string.IsNullOrWhiteSpace(record.IdentityKey))
                {
                    _byIdentity.Remove(record.IdentityKey);
                }

                foreach (var pair in _byAlias.ToList())
                {
                    pair.Value.RemoveAll(item => ReferenceEquals(item, record) || item.Id == record.Id && record.Id != 0);
                    if (pair.Value.Count == 0)
                    {
                        _byAlias.Remove(pair.Key);
                    }
                }
            }

            public bool ContainsIdentity(string identityKey)
            {
                return !string.IsNullOrWhiteSpace(identityKey) && _byIdentity.ContainsKey(identityKey);
            }

            public SourceChannelEnrichmentRecord? Resolve(string identityKey, IReadOnlyList<string> aliasKeys)
            {
                if (!string.IsNullOrWhiteSpace(identityKey) && _byIdentity.TryGetValue(identityKey, out var direct))
                {
                    return direct;
                }

                var candidates = new List<SourceChannelEnrichmentRecord>();
                foreach (var alias in aliasKeys)
                {
                    if (_byAlias.TryGetValue(alias, out var records))
                    {
                        candidates.AddRange(records);
                    }
                }

                return candidates
                    .Distinct()
                    .OrderByDescending(item => item.LastAppliedAtUtc)
                    .ThenByDescending(item => item.EpgMatchConfidence + item.LogoConfidence)
                    .FirstOrDefault();
            }
        }

        private sealed class EpgChannelMatcher
        {
            private readonly ILiveChannelIdentityService _identityService;
            private readonly Dictionary<string, List<Channel>> _byProviderGuideId = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<Channel>> _byPreviousGuideId = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<Channel>> _byNormalizedValue = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<Channel>> _byAliasKey = new(StringComparer.OrdinalIgnoreCase);
            private readonly List<ChannelCandidate> _candidates = new();

            public EpgChannelMatcher(ILiveChannelIdentityService identityService, IEnumerable<Channel> channels)
            {
                _identityService = identityService;

                foreach (var channel in channels)
                {
                    AddIndex(_byProviderGuideId, channel.ProviderEpgChannelId, channel);

                    if (channel.EpgMatchSource == ChannelEpgMatchSource.Previous)
                    {
                        AddIndex(_byPreviousGuideId, channel.EpgChannelId, channel);
                    }

                    foreach (var exactValue in BuildExactValues(channel))
                    {
                        AddIndex(_byNormalizedValue, exactValue, channel);
                    }

                    var aliasKeys = DeserializeAliasKeys(channel.AliasKeys);
                    foreach (var aliasKey in aliasKeys)
                    {
                        AddIndex(_byAliasKey, aliasKey, channel);
                    }

                    _candidates.Add(new ChannelCandidate(channel, aliasKeys));
                }
            }

            public IReadOnlyDictionary<string, ChannelEpgMatchOutcome> MatchAll(IReadOnlyCollection<XmltvChannelDescriptor> xmltvChannels)
            {
                var orderedChannels = xmltvChannels
                    .OrderBy(channel => channel.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var outcomes = new Dictionary<string, ChannelEpgMatchOutcome>(StringComparer.OrdinalIgnoreCase);
                var assignedChannelIds = new HashSet<int>();

                MatchUsingExactIds(orderedChannels, outcomes, assignedChannelIds, _byProviderGuideId, ChannelEpgMatchSource.Provider, 97, "Provider guide id");
                MatchUsingExactIds(orderedChannels, outcomes, assignedChannelIds, _byPreviousGuideId, ChannelEpgMatchSource.Previous, 91, "Previous guide mapping");
                MatchUsingNormalizedValues(orderedChannels, outcomes, assignedChannelIds);
                MatchUsingAliasKeys(orderedChannels, outcomes, assignedChannelIds);
                MatchUsingFuzzyFallback(orderedChannels, outcomes, assignedChannelIds);

                foreach (var xmltvChannel in orderedChannels)
                {
                    if (!outcomes.ContainsKey(xmltvChannel.Id))
                    {
                        outcomes[xmltvChannel.Id] = new ChannelEpgMatchOutcome
                        {
                            Channels = Array.Empty<Channel>(),
                            Reason = ChannelEpgMatchSource.None,
                            Confidence = 0,
                            Diagnostic = "No safe guide match was found."
                        };
                    }
                }

                return outcomes;
            }

            private void MatchUsingExactIds(
                IReadOnlyCollection<XmltvChannelDescriptor> xmltvChannels,
                IDictionary<string, ChannelEpgMatchOutcome> outcomes,
                ISet<int> assignedChannelIds,
                IReadOnlyDictionary<string, List<Channel>> index,
                ChannelEpgMatchSource reason,
                int confidence,
                string label)
            {
                foreach (var xmltvChannel in xmltvChannels)
                {
                    if (outcomes.ContainsKey(xmltvChannel.Id))
                    {
                        continue;
                    }

                    if (!index.TryGetValue(xmltvChannel.Id, out var matches))
                    {
                        continue;
                    }

                    var availableMatches = GetAvailableMatches(matches, assignedChannelIds);
                    if (availableMatches.Count == 0)
                    {
                        continue;
                    }

                    AssignOutcome(
                        outcomes,
                        assignedChannelIds,
                        xmltvChannel.Id,
                        availableMatches,
                        reason,
                        confidence,
                        $"{label} matched XMLTV channel '{xmltvChannel.Id}'.");
                }
            }

            private void MatchUsingNormalizedValues(
                IReadOnlyCollection<XmltvChannelDescriptor> xmltvChannels,
                IDictionary<string, ChannelEpgMatchOutcome> outcomes,
                ISet<int> assignedChannelIds)
            {
                foreach (var xmltvChannel in xmltvChannels)
                {
                    if (outcomes.ContainsKey(xmltvChannel.Id))
                    {
                        continue;
                    }

                    foreach (var candidate in BuildSourceCandidates(xmltvChannel))
                    {
                        var normalized = _identityService.NormalizeExactKey(candidate);
                        if (string.IsNullOrWhiteSpace(normalized))
                        {
                            continue;
                        }

                        if (!_byNormalizedValue.TryGetValue(normalized, out var matches))
                        {
                            continue;
                        }

                        var availableMatches = GetAvailableMatches(matches, assignedChannelIds);
                        if (availableMatches.Count == 0)
                        {
                            continue;
                        }

                        AssignOutcome(
                            outcomes,
                            assignedChannelIds,
                            xmltvChannel.Id,
                            availableMatches,
                            ChannelEpgMatchSource.Normalized,
                            88,
                            $"Normalized channel identity matched '{candidate}'.");
                        break;
                    }
                }
            }

            private void MatchUsingAliasKeys(
                IReadOnlyCollection<XmltvChannelDescriptor> xmltvChannels,
                IDictionary<string, ChannelEpgMatchOutcome> outcomes,
                ISet<int> assignedChannelIds)
            {
                foreach (var xmltvChannel in xmltvChannels)
                {
                    if (outcomes.ContainsKey(xmltvChannel.Id))
                    {
                        continue;
                    }

                    foreach (var candidate in BuildSourceCandidates(xmltvChannel))
                    {
                        foreach (var aliasKey in _identityService.BuildAliasKeys(candidate))
                        {
                            if (!_byAliasKey.TryGetValue(aliasKey, out var matches))
                            {
                                continue;
                            }

                            var availableMatches = GetAvailableMatches(matches, assignedChannelIds);
                            if (availableMatches.Count == 0)
                            {
                                continue;
                            }

                            AssignOutcome(
                                outcomes,
                                assignedChannelIds,
                                xmltvChannel.Id,
                                availableMatches,
                                ChannelEpgMatchSource.Alias,
                                80,
                                $"Alias fallback matched '{candidate}' using '{aliasKey}'.");
                            break;
                        }

                        if (outcomes.ContainsKey(xmltvChannel.Id))
                        {
                            break;
                        }
                    }
                }
            }

            private void MatchUsingFuzzyFallback(
                IReadOnlyCollection<XmltvChannelDescriptor> xmltvChannels,
                IDictionary<string, ChannelEpgMatchOutcome> outcomes,
                ISet<int> assignedChannelIds)
            {
                foreach (var xmltvChannel in xmltvChannels)
                {
                    if (outcomes.ContainsKey(xmltvChannel.Id))
                    {
                        continue;
                    }

                    var fuzzyOutcome = FindFuzzyMatch(xmltvChannel, assignedChannelIds);
                    if (fuzzyOutcome == null)
                    {
                        continue;
                    }

                    AssignOutcome(
                        outcomes,
                        assignedChannelIds,
                        xmltvChannel.Id,
                        fuzzyOutcome.Channels,
                        fuzzyOutcome.Reason,
                        fuzzyOutcome.Confidence,
                        fuzzyOutcome.Diagnostic);
                }
            }

            private ChannelEpgMatchOutcome? FindFuzzyMatch(XmltvChannelDescriptor xmltvChannel, ISet<int> assignedChannelIds)
            {
                var fuzzyCandidates = BuildSourceCandidates(xmltvChannel)
                    .SelectMany(candidate => _identityService.BuildAliasKeys(candidate))
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
                        if (assignedChannelIds.Contains(candidate.Channel.Id))
                        {
                            continue;
                        }

                        foreach (var aliasKey in candidate.AliasKeys)
                        {
                            var score = _identityService.ComputeDiceCoefficient(sourceValue, aliasKey);
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

                return new ChannelEpgMatchOutcome
                {
                    Channels = new[] { best.Candidate.Channel },
                    Reason = ChannelEpgMatchSource.Fuzzy,
                    Confidence = 68,
                    Diagnostic = $"Safe fuzzy fallback matched '{best.SourceValue}' to '{best.AliasKey}' with score {best.Score:0.00}."
                };
            }

            private IEnumerable<string> BuildExactValues(Channel channel)
            {
                if (!string.IsNullOrWhiteSpace(channel.NormalizedName))
                {
                    yield return channel.NormalizedName;
                }

                var providerGuide = _identityService.NormalizeExactKey(channel.ProviderEpgChannelId);
                if (!string.IsNullOrWhiteSpace(providerGuide))
                {
                    yield return providerGuide;
                }
            }

            private static IEnumerable<string> BuildSourceCandidates(XmltvChannelDescriptor xmltvChannel)
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

            private static List<Channel> GetAvailableMatches(IEnumerable<Channel> matches, ISet<int> assignedChannelIds)
            {
                return matches
                    .Where(channel => !assignedChannelIds.Contains(channel.Id))
                    .GroupBy(channel => channel.Id)
                    .Select(group => group.First())
                    .ToList();
            }

            private static void AssignOutcome(
                IDictionary<string, ChannelEpgMatchOutcome> outcomes,
                ISet<int> assignedChannelIds,
                string xmltvChannelId,
                IReadOnlyList<Channel> matchedChannels,
                ChannelEpgMatchSource reason,
                int confidence,
                string diagnostic)
            {
                outcomes[xmltvChannelId] = new ChannelEpgMatchOutcome
                {
                    Channels = matchedChannels,
                    Reason = reason,
                    Confidence = confidence,
                    Diagnostic = diagnostic
                };

                foreach (var channel in matchedChannels)
                {
                    assignedChannelIds.Add(channel.Id);
                }
            }

            private sealed record ChannelCandidate(Channel Channel, IReadOnlyList<string> AliasKeys);
        }
    }

    public sealed class XmltvChannelDescriptor
    {
        public string Id { get; init; } = string.Empty;
        public IReadOnlyList<string> DisplayNames { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> IconUrls { get; init; } = Array.Empty<string>();
        public string PrimaryDisplayName => DisplayNames.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    public sealed class ChannelEpgMatchOutcome
    {
        public IReadOnlyList<Channel> Channels { get; init; } = Array.Empty<Channel>();
        public ChannelEpgMatchSource Reason { get; init; }
        public int Confidence { get; init; }
        public string Diagnostic { get; init; } = string.Empty;
    }

    public sealed class SourceEpgEnrichmentResult
    {
        public IReadOnlyDictionary<string, ChannelEpgMatchOutcome> Matches { get; init; } =
            new Dictionary<string, ChannelEpgMatchOutcome>(StringComparer.OrdinalIgnoreCase);
    }
}
