#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface ISourceEnrichmentService
    {
        Task PrepareLiveCatalogAsync(AppDbContext db, int sourceProfileId, SourceAcquisitionSession? acquisitionSession = null);
        Task<SourceEpgEnrichmentResult> ApplyXmltvEnrichmentAsync(
            AppDbContext db,
            int sourceProfileId,
            IReadOnlyCollection<XmltvChannelDescriptor> xmltvChannels,
            SourceAcquisitionSession? acquisitionSession = null);
    }

    public sealed class SourceEnrichmentService : ISourceEnrichmentService
    {
        private readonly ILiveChannelIdentityService _identityService;

        public SourceEnrichmentService(ILiveChannelIdentityService identityService)
        {
            _identityService = identityService;
        }

        public async Task PrepareLiveCatalogAsync(AppDbContext db, int sourceProfileId, SourceAcquisitionSession? acquisitionSession = null)
        {
            var channels = await LoadSourceChannelsAsync(db, sourceProfileId);
            var records = await db.SourceChannelEnrichmentRecords
                .Where(record => record.SourceProfileId == sourceProfileId)
                .ToListAsync();
            var decisions = await db.EpgMappingDecisions
                .Where(decision => decision.SourceProfileId == sourceProfileId)
                .ToListAsync();
            var decisionLookup = EpgMappingDecisionLookup.Create(sourceProfileId, channels, decisions);
            if (channels.Count == 0)
            {
                if (records.Count > 0)
                {
                    db.SourceChannelEnrichmentRecords.RemoveRange(records);
                    await db.SaveChangesAsync();
                }

                return;
            }

            var lookup = BuildLookup(records);
            var nowUtc = DateTime.UtcNow;

            foreach (var channel in channels)
            {
                PrepareChannel(db, channel, lookup, records, sourceProfileId, nowUtc, decisionLookup);
            }

            var staleRecords = records
                .Where(record => record.LastSeenAtUtc != nowUtc)
                .ToList();
            if (staleRecords.Count > 0)
            {
                db.SourceChannelEnrichmentRecords.RemoveRange(staleRecords);
            }

            await db.SaveChangesAsync();
        }

        public async Task<SourceEpgEnrichmentResult> ApplyXmltvEnrichmentAsync(
            AppDbContext db,
            int sourceProfileId,
            IReadOnlyCollection<XmltvChannelDescriptor> xmltvChannels,
            SourceAcquisitionSession? acquisitionSession = null)
        {
            await PrepareLiveCatalogAsync(db, sourceProfileId, acquisitionSession);

            var channels = await LoadSourceChannelsAsync(db, sourceProfileId);
            if (channels.Count == 0)
            {
                return new SourceEpgEnrichmentResult();
            }

            var records = await db.SourceChannelEnrichmentRecords
                .Where(record => record.SourceProfileId == sourceProfileId)
                .ToListAsync();
            var decisions = await db.EpgMappingDecisions
                .Where(decision => decision.SourceProfileId == sourceProfileId)
                .ToListAsync();
            var decisionLookup = EpgMappingDecisionLookup.Create(sourceProfileId, channels, decisions);
            var lookup = BuildLookup(records);
            var normalizedXmltvChannels = xmltvChannels
                .Where(channel => !string.IsNullOrWhiteSpace(channel.Id))
                .GroupBy(channel => channel.Id.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            var matcher = new EpgChannelMatcher(_identityService, channels, decisionLookup);
            var matches = matcher.MatchAll(normalizedXmltvChannels);
            var nowUtc = DateTime.UtcNow;

            foreach (var channel in channels)
            {
                PrepareChannel(db, channel, lookup, records, sourceProfileId, nowUtc, decisionLookup);
            }

            foreach (var xmltvChannel in normalizedXmltvChannels)
            {
                if (!matches.TryGetValue(xmltvChannel.Id, out var outcome))
                {
                    continue;
                }

                if (outcome.Channels.Count == 0)
                {
                    continue;
                }

                var xmltvIconUrl = SelectBestLogo(xmltvChannel.IconUrls);
                foreach (var channel in outcome.Channels)
                {
                    if (!outcome.IsSupplemental)
                    {
                        var isActiveGuideAssignment = IsActiveGuideAssignment(outcome);
                        ApplyMatchedGuide(channel, outcome, xmltvChannel, xmltvIconUrl, nowUtc, isActiveGuideAssignment);
                        var record = GetOrCreateRecord(db, channel, lookup, records, sourceProfileId);
                        UpdateRecord(record, channel, xmltvChannel, xmltvIconUrl, nowUtc);
                    }

                    acquisitionSession?.RecordGuideMatch(
                        SourceAcquisitionItemKind.LiveChannel,
                        outcome.Reason,
                        outcome.Confidence,
                        BuildGuideRuleCode(outcome.Reason),
                        outcome.Diagnostic,
                        channel.Name,
                        channel.NormalizedName,
                        channel.NormalizedIdentityKey,
                        channel.AliasKeys,
                        outcome.MatchedValue,
                        string.IsNullOrWhiteSpace(xmltvChannel.PrimaryDisplayName) ? xmltvChannel.Id : xmltvChannel.PrimaryDisplayName,
                        captureDetail: outcome.Reason is not ChannelEpgMatchSource.Provider);
                }
            }

            var matchedChannelIds = matches.Values
                .SelectMany(outcome => outcome.Channels)
                .Select(channel => channel.Id)
                .ToHashSet();
            if (acquisitionSession != null)
            {
                foreach (var channel in channels.Where(channel => !matchedChannelIds.Contains(channel.Id)))
                {
                    acquisitionSession.RecordGuideUnmatched(
                        "epg.unmatched.channel",
                        BuildUnmatchedGuideReason(channel),
                        channel.Name,
                        channel.NormalizedName,
                        channel.NormalizedIdentityKey,
                        channel.AliasKeys);
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
            DateTime nowUtc,
            EpgMappingDecisionLookup? decisionLookup = null)
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

            ApplyStoredFallbacks(channel, record, decisionLookup);
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

        private static void ApplyStoredFallbacks(Channel channel, SourceChannelEnrichmentRecord record, EpgMappingDecisionLookup? decisionLookup)
        {
            if (string.IsNullOrWhiteSpace(channel.ProviderEpgChannelId) &&
                !string.IsNullOrWhiteSpace(record.MatchedXmltvChannelId) &&
                decisionLookup?.IsRejected(channel, record.MatchedXmltvChannelId) != true)
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
            DateTime nowUtc,
            bool isActiveGuideAssignment)
        {
            channel.EpgChannelId = xmltvChannel.Id;
            channel.EpgMatchSource = outcome.Reason;
            channel.EpgMatchConfidence = outcome.Confidence;
            channel.EpgMatchSummary = BuildEpgSummary(outcome, xmltvChannel, isActiveGuideAssignment);
            channel.EnrichedAtUtc = nowUtc;

            if (!string.IsNullOrWhiteSpace(channel.ProviderLogoUrl))
            {
                channel.LogoUrl = channel.ProviderLogoUrl;
                channel.LogoSource = ChannelLogoSource.Provider;
                channel.LogoConfidence = 92;
                channel.LogoSummary = "Using the provider logo.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(xmltvIconUrl) && IsTrustedLogoSource(outcome.Reason))
            {
                channel.LogoUrl = xmltvIconUrl;
                channel.LogoSource = ChannelLogoSource.Xmltv;
                channel.LogoConfidence = outcome.Reason switch
                {
                    ChannelEpgMatchSource.Provider => 88,
                    ChannelEpgMatchSource.Previous => 82,
                    ChannelEpgMatchSource.Normalized => 84,
                    ChannelEpgMatchSource.Regex => 0,
                    ChannelEpgMatchSource.UserApproved => 84,
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
            return BuildEpgSummary(
                new ChannelEpgMatchOutcome
                {
                    Reason = reason,
                    Confidence = reason switch
                    {
                        ChannelEpgMatchSource.Provider => 97,
                        ChannelEpgMatchSource.Normalized => 88,
                        ChannelEpgMatchSource.UserApproved => 93,
                        _ => 0
                    }
                },
                xmltvChannel,
                isActiveGuideAssignment: IsTrustedGuideSource(reason));
        }

        private static string BuildEpgSummary(
            ChannelEpgMatchOutcome outcome,
            XmltvChannelDescriptor xmltvChannel,
            bool isActiveGuideAssignment)
        {
            var label = string.IsNullOrWhiteSpace(xmltvChannel.PrimaryDisplayName)
                ? xmltvChannel.Id
                : xmltvChannel.PrimaryDisplayName;
            if (!isActiveGuideAssignment && IsWeakGuideSource(outcome.Reason))
            {
                return $"Review needed: suggested XMLTV channel {label} ({outcome.Reason}, confidence {outcome.Confidence}). Not used for current/next guide display.";
            }

            return outcome.Reason switch
            {
                ChannelEpgMatchSource.Provider => $"Provider guide metadata matched XMLTV channel {label}.",
                ChannelEpgMatchSource.Previous => $"The previous successful guide mapping still resolves to XMLTV channel {label}.",
                ChannelEpgMatchSource.Normalized => $"Normalized channel identity matched XMLTV channel {label}.",
                ChannelEpgMatchSource.UserApproved => $"User-approved guide mapping matched XMLTV channel {label}.",
                ChannelEpgMatchSource.Alias => $"Alias fallback matched XMLTV channel {label}.",
                ChannelEpgMatchSource.Regex => $"Regex-safe alias matching resolved XMLTV channel {label}.",
                ChannelEpgMatchSource.Fuzzy => $"Safe fuzzy fallback matched XMLTV channel {label}.",
                _ => string.Empty
            };
        }

        private static bool IsActiveGuideAssignment(ChannelEpgMatchOutcome outcome)
        {
            return !outcome.IsSupplemental && IsTrustedGuideSource(outcome.Reason);
        }

        private static bool IsTrustedGuideSource(ChannelEpgMatchSource source)
        {
            return source is ChannelEpgMatchSource.Provider or ChannelEpgMatchSource.Normalized or ChannelEpgMatchSource.Regex or ChannelEpgMatchSource.UserApproved;
        }

        private static bool IsWeakGuideSource(ChannelEpgMatchSource source)
        {
            return source is ChannelEpgMatchSource.Previous or ChannelEpgMatchSource.Alias or ChannelEpgMatchSource.Fuzzy;
        }

        private static bool IsTrustedLogoSource(ChannelEpgMatchSource source)
        {
            return source is ChannelEpgMatchSource.Provider or ChannelEpgMatchSource.Previous or ChannelEpgMatchSource.Normalized or ChannelEpgMatchSource.Regex or ChannelEpgMatchSource.UserApproved;
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
                ChannelEpgMatchSource.Regex => $"Logo recovered from the matched XMLTV channel {label} using regex-safe alias fallback.",
                _ => $"Logo recovered from the matched XMLTV channel {label}."
            };
        }

        private static string BuildGuideRuleCode(ChannelEpgMatchSource reason) => reason switch
        {
            ChannelEpgMatchSource.Provider => "epg.provider_id",
            ChannelEpgMatchSource.Previous => "epg.previous_match",
            ChannelEpgMatchSource.Normalized => "epg.normalized_alias",
            ChannelEpgMatchSource.UserApproved => "epg.user_approved",
            ChannelEpgMatchSource.Alias => "epg.alias_match",
            ChannelEpgMatchSource.Regex => "epg.regex_alias",
            ChannelEpgMatchSource.Fuzzy => "epg.fuzzy_fallback",
            _ => "epg.no_match"
        };

        private static string BuildUnmatchedGuideReason(Channel channel)
        {
            if (!string.IsNullOrWhiteSpace(channel.ProviderEpgChannelId))
            {
                return "No XMLTV provider id, alias, or regex candidate matched this channel.";
            }

            if (!string.IsNullOrWhiteSpace(channel.AliasKeys))
            {
                return "No XMLTV alias, regex, or fuzzy candidate matched the normalized channel identity.";
            }

            return "No reusable guide identity was available for this channel.";
        }

        private static SourceChannelEnrichmentRecord GetOrCreateRecord(
            AppDbContext db,
            Channel channel,
            EnrichmentLookup lookup,
            ICollection<SourceChannelEnrichmentRecord> records,
            int sourceProfileId)
        {
            var record = lookup.ResolveIdentity(channel.NormalizedIdentityKey);
            if (record != null)
            {
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

            public SourceChannelEnrichmentRecord? ResolveIdentity(string identityKey)
            {
                if (!string.IsNullOrWhiteSpace(identityKey) && _byIdentity.TryGetValue(identityKey, out var direct))
                {
                    return direct;
                }

                return null;
            }
        }

        private sealed class EpgMappingDecisionLookup
        {
            private readonly Dictionary<string, List<EpgMappingDecision>> _approvedByXmltvChannelId;
            private readonly HashSet<string> _rejectedChannelKeys;
            private readonly HashSet<string> _rejectedStreamKeys;

            private EpgMappingDecisionLookup(
                Dictionary<string, List<EpgMappingDecision>> approvedByXmltvChannelId,
                HashSet<string> rejectedChannelKeys,
                HashSet<string> rejectedStreamKeys)
            {
                _approvedByXmltvChannelId = approvedByXmltvChannelId;
                _rejectedChannelKeys = rejectedChannelKeys;
                _rejectedStreamKeys = rejectedStreamKeys;
            }

            public static EpgMappingDecisionLookup Create(
                int sourceProfileId,
                IEnumerable<Channel> channels,
                IEnumerable<EpgMappingDecision> decisions)
            {
                var approvedByXmltvChannelId = new Dictionary<string, List<EpgMappingDecision>>(StringComparer.OrdinalIgnoreCase);
                var rejectedChannelKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var rejectedStreamKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var channelIds = channels.Select(channel => channel.Id).ToHashSet();

                foreach (var decision in decisions.Where(decision => decision.SourceProfileId == sourceProfileId))
                {
                    var xmltvChannelId = NormalizeXmltvChannelId(decision.XmltvChannelId);
                    if (string.IsNullOrWhiteSpace(xmltvChannelId))
                    {
                        continue;
                    }

                    if (decision.Decision == EpgMappingDecisionState.Approved)
                    {
                        if (!approvedByXmltvChannelId.TryGetValue(xmltvChannelId, out var approved))
                        {
                            approved = new List<EpgMappingDecision>();
                            approvedByXmltvChannelId[xmltvChannelId] = approved;
                        }

                        approved.Add(decision);
                    }
                    else if (decision.Decision == EpgMappingDecisionState.Rejected)
                    {
                        if (channelIds.Contains(decision.ChannelId))
                        {
                            rejectedChannelKeys.Add(BuildChannelDecisionKey(decision.ChannelId, xmltvChannelId));
                        }

                        if (!string.IsNullOrWhiteSpace(decision.StreamUrlHash))
                        {
                            rejectedStreamKeys.Add(BuildStreamDecisionKey(decision.StreamUrlHash, xmltvChannelId));
                        }
                    }
                }

                return new EpgMappingDecisionLookup(approvedByXmltvChannelId, rejectedChannelKeys, rejectedStreamKeys);
            }

            public bool IsRejected(Channel channel, string xmltvChannelId)
            {
                var normalizedXmltvChannelId = NormalizeXmltvChannelId(xmltvChannelId);
                if (string.IsNullOrWhiteSpace(normalizedXmltvChannelId))
                {
                    return false;
                }

                if (_rejectedChannelKeys.Contains(BuildChannelDecisionKey(channel.Id, normalizedXmltvChannelId)))
                {
                    return true;
                }

                var streamHash = EpgMappingDecisionIdentity.ComputeStreamUrlHash(channel.StreamUrl);
                return !string.IsNullOrWhiteSpace(streamHash) &&
                       _rejectedStreamKeys.Contains(BuildStreamDecisionKey(streamHash, normalizedXmltvChannelId));
            }

            public IReadOnlyList<Channel> ResolveApprovedChannels(
                string xmltvChannelId,
                IReadOnlyDictionary<int, Channel> channelsById,
                IReadOnlyDictionary<string, List<Channel>> channelsByStreamUrlHash,
                ISet<int> assignedChannelIds)
            {
                var normalizedXmltvChannelId = NormalizeXmltvChannelId(xmltvChannelId);
                if (string.IsNullOrWhiteSpace(normalizedXmltvChannelId) ||
                    !_approvedByXmltvChannelId.TryGetValue(normalizedXmltvChannelId, out var decisions))
                {
                    return Array.Empty<Channel>();
                }

                var channels = new List<Channel>();
                foreach (var decision in decisions)
                {
                    if (channelsById.TryGetValue(decision.ChannelId, out var channel))
                    {
                        AddApprovedChannel(channel);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(decision.StreamUrlHash) &&
                        channelsByStreamUrlHash.TryGetValue(decision.StreamUrlHash, out var streamMatches))
                    {
                        foreach (var streamMatch in streamMatches)
                        {
                            AddApprovedChannel(streamMatch);
                        }
                    }
                }

                return channels
                    .GroupBy(channel => channel.Id)
                    .Select(group => group.First())
                    .ToList();

                void AddApprovedChannel(Channel channel)
                {
                    if (assignedChannelIds.Contains(channel.Id) ||
                        IsRejected(channel, normalizedXmltvChannelId))
                    {
                        return;
                    }

                    channels.Add(channel);
                }
            }

            private static string NormalizeXmltvChannelId(string value)
            {
                return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            }

            private static string BuildChannelDecisionKey(int channelId, string xmltvChannelId)
            {
                return $"{channelId}|{xmltvChannelId}";
            }

            private static string BuildStreamDecisionKey(string streamUrlHash, string xmltvChannelId)
            {
                return $"{streamUrlHash.Trim()}|{xmltvChannelId}";
            }
        }

        private sealed class EpgChannelMatcher
        {
            private const int ExpensiveWeakMatchXmltvChannelLimit = 6000;

            private readonly ILiveChannelIdentityService _identityService;
            private readonly EpgMappingDecisionLookup _decisionLookup;
            private readonly Dictionary<string, List<Channel>> _byProviderGuideId = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<Channel>> _byPreviousGuideId = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<Channel>> _byNormalizedValue = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, List<Channel>> _byAliasKey = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<int, Channel> _byChannelId = new();
            private readonly Dictionary<string, List<Channel>> _byStreamUrlHash = new(StringComparer.OrdinalIgnoreCase);
            private readonly List<ChannelCandidate> _candidates = new();

            public EpgChannelMatcher(
                ILiveChannelIdentityService identityService,
                IEnumerable<Channel> channels,
                EpgMappingDecisionLookup decisionLookup)
            {
                _identityService = identityService;
                _decisionLookup = decisionLookup;

                foreach (var channel in channels)
                {
                    _byChannelId[channel.Id] = channel;
                    AddStreamHashIndex(channel);
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

                    _candidates.Add(new ChannelCandidate(channel, aliasKeys, BuildRegexValues(channel, aliasKeys)));
                }
            }

            public IReadOnlyDictionary<string, ChannelEpgMatchOutcome> MatchAll(IReadOnlyCollection<XmltvChannelDescriptor> xmltvChannels)
            {
                var orderedChannels = xmltvChannels
                    .OrderBy(channel => channel.SourcePriority)
                    .ThenBy(channel => channel.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var outcomes = new Dictionary<string, ChannelEpgMatchOutcome>(StringComparer.OrdinalIgnoreCase);
                var assignedChannelIds = new HashSet<int>();

                MatchUsingExactIds(orderedChannels, outcomes, assignedChannelIds, _byProviderGuideId, ChannelEpgMatchSource.Provider, 97, "Provider guide id", respectReviewRejections: false);
                MatchUsingNormalizedValues(orderedChannels, outcomes, assignedChannelIds);
                MatchUsingApprovedDecisions(orderedChannels, outcomes, assignedChannelIds);
                MatchUsingExactIds(orderedChannels, outcomes, assignedChannelIds, _byPreviousGuideId, ChannelEpgMatchSource.Previous, 91, "Previous guide mapping", respectReviewRejections: true);
                MatchUsingAliasKeys(orderedChannels, outcomes, assignedChannelIds);
                if (orderedChannels.Count <= ExpensiveWeakMatchXmltvChannelLimit)
                {
                    MatchUsingRegexAliases(orderedChannels, outcomes, assignedChannelIds);
                    MatchUsingFuzzyFallback(orderedChannels, outcomes, assignedChannelIds);
                }
                MatchSupplementalChannels(orderedChannels, outcomes, assignedChannelIds);

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
                string label,
                bool respectReviewRejections)
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
                    if (respectReviewRejections)
                    {
                        availableMatches = FilterRejectedWeakMatches(xmltvChannel, availableMatches);
                    }

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
                        $"{label} matched XMLTV channel '{xmltvChannel.Id}'.",
                        xmltvChannel.Id,
                        xmltvChannel.Id);
                }
            }

            private void MatchUsingApprovedDecisions(
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

                    var approvedMatches = _decisionLookup.ResolveApprovedChannels(
                        xmltvChannel.Id,
                        _byChannelId,
                        _byStreamUrlHash,
                        assignedChannelIds);
                    if (approvedMatches.Count == 0)
                    {
                        continue;
                    }

                    AssignOutcome(
                        outcomes,
                        assignedChannelIds,
                        xmltvChannel.Id,
                        approvedMatches,
                        ChannelEpgMatchSource.UserApproved,
                        93,
                        $"User-approved EPG mapping matched XMLTV channel '{xmltvChannel.Id}'.",
                        xmltvChannel.PrimaryDisplayName,
                        xmltvChannel.Id);
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
                        var normalized = _identityService.NormalizeForEpgScheduleMatch(candidate);
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
                            $"Normalized channel identity matched '{candidate}'.",
                            candidate,
                            normalized);
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

                            availableMatches = FilterRejectedWeakMatches(xmltvChannel, availableMatches);
                            if (availableMatches.Count == 0 ||
                                !IsWeakMatchIdentitySafe(xmltvChannel, availableMatches, candidate, aliasKey, out var weakDiagnostic))
                            {
                                continue;
                            }

                            AssignOutcome(
                                outcomes,
                                assignedChannelIds,
                                xmltvChannel.Id,
                                availableMatches,
                                ChannelEpgMatchSource.Alias,
                                72,
                                $"Review-needed alias suggestion matched '{candidate}' using '{aliasKey}'. {weakDiagnostic}",
                                candidate,
                                aliasKey);
                            break;
                        }

                        if (outcomes.ContainsKey(xmltvChannel.Id))
                        {
                            break;
                        }
                    }
                }
            }

            private void MatchUsingRegexAliases(
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

                    var regexOutcome = FindRegexAliasMatch(xmltvChannel, assignedChannelIds);
                    if (regexOutcome == null)
                    {
                        continue;
                    }

                    var availableMatches = FilterRejectedWeakMatches(xmltvChannel, regexOutcome.Channels);
                    if (availableMatches.Count == 0 ||
                        !IsWeakMatchIdentitySafe(xmltvChannel, availableMatches, regexOutcome.MatchedValue, regexOutcome.MatchedKey, out var weakDiagnostic))
                    {
                        continue;
                    }

                    AssignOutcome(
                        outcomes,
                        assignedChannelIds,
                        xmltvChannel.Id,
                        availableMatches,
                        regexOutcome.Reason,
                        regexOutcome.Confidence,
                        regexOutcome.Diagnostic,
                        regexOutcome.MatchedValue,
                        regexOutcome.MatchedKey);
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

                    var availableMatches = FilterRejectedWeakMatches(xmltvChannel, fuzzyOutcome.Channels);
                    if (availableMatches.Count == 0 ||
                        !IsWeakMatchIdentitySafe(xmltvChannel, availableMatches, fuzzyOutcome.MatchedValue, fuzzyOutcome.MatchedKey, out var weakDiagnostic))
                    {
                        continue;
                    }

                    AssignOutcome(
                        outcomes,
                        assignedChannelIds,
                        xmltvChannel.Id,
                        availableMatches,
                        fuzzyOutcome.Reason,
                        fuzzyOutcome.Confidence,
                        $"{fuzzyOutcome.Diagnostic} {weakDiagnostic}",
                        fuzzyOutcome.MatchedValue,
                        fuzzyOutcome.MatchedKey);
                }
            }

            private void MatchSupplementalChannels(
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

                    var supplemental = FindSupplementalMatch(xmltvChannel, assignedChannelIds);
                    if (supplemental == null)
                    {
                        continue;
                    }

                    outcomes[xmltvChannel.Id] = supplemental;
                }
            }

            private ChannelEpgMatchOutcome? FindSupplementalMatch(XmltvChannelDescriptor xmltvChannel, ISet<int> assignedChannelIds)
            {
                if (_byProviderGuideId.TryGetValue(xmltvChannel.Id, out var exactMatches))
                {
                    var assignedMatches = exactMatches
                        .Where(channel => assignedChannelIds.Contains(channel.Id))
                        .GroupBy(channel => channel.Id)
                        .Select(group => group.First())
                        .ToList();
                    if (assignedMatches.Count == 1)
                    {
                        return BuildSupplementalOutcome(
                            assignedMatches,
                            ChannelEpgMatchSource.Provider,
                            94,
                            $"Supplemental XMLTV source matched provider guide id '{xmltvChannel.Id}'.",
                            xmltvChannel.Id,
                            xmltvChannel.Id);
                    }
                }

                foreach (var candidate in BuildSourceCandidates(xmltvChannel))
                {
                    var normalized = _identityService.NormalizeForEpgScheduleMatch(candidate);
                    if (!string.IsNullOrWhiteSpace(normalized) &&
                        _byNormalizedValue.TryGetValue(normalized, out var normalizedMatches))
                    {
                        var assignedMatches = normalizedMatches
                            .Where(channel => assignedChannelIds.Contains(channel.Id))
                            .GroupBy(channel => channel.Id)
                            .Select(group => group.First())
                            .ToList();
                        if (assignedMatches.Count == 1)
                        {
                            return BuildSupplementalOutcome(
                                assignedMatches,
                                ChannelEpgMatchSource.Normalized,
                                82,
                                $"Supplemental XMLTV source normalized to '{candidate}'.",
                                candidate,
                                normalized);
                        }
                    }

                    foreach (var aliasKey in _identityService.BuildAliasKeys(candidate))
                    {
                        if (!_byAliasKey.TryGetValue(aliasKey, out var aliasMatches))
                        {
                            continue;
                        }

                        var assignedMatches = aliasMatches
                            .Where(channel => assignedChannelIds.Contains(channel.Id))
                            .GroupBy(channel => channel.Id)
                            .Select(group => group.First())
                            .ToList();
                        assignedMatches = FilterRejectedWeakMatches(xmltvChannel, assignedMatches);
                        if (assignedMatches.Count == 1 &&
                            IsWeakMatchIdentitySafe(xmltvChannel, assignedMatches, candidate, aliasKey, out var weakDiagnostic))
                        {
                            return BuildSupplementalOutcome(
                                assignedMatches,
                                ChannelEpgMatchSource.Alias,
                                60,
                                $"Review-needed supplemental alias suggestion matched '{candidate}'. {weakDiagnostic}",
                                candidate,
                                aliasKey);
                        }
                    }
                }

                return null;
            }

            private ChannelEpgMatchOutcome? FindRegexAliasMatch(XmltvChannelDescriptor xmltvChannel, ISet<int> assignedChannelIds)
            {
                foreach (var candidate in BuildSourceCandidates(xmltvChannel))
                {
                    foreach (var pattern in BuildRegexAliasPatterns(candidate))
                    {
                        var matches = _candidates
                            .Where(item => !assignedChannelIds.Contains(item.Channel.Id) &&
                                           item.RegexValues.Any(value => pattern.Regex.IsMatch(value)))
                            .Select(item => item.Channel)
                            .Distinct()
                            .ToList();
                        if (matches.Count != 1)
                        {
                            continue;
                        }

                        return new ChannelEpgMatchOutcome
                        {
                            Channels = matches,
                            Reason = ChannelEpgMatchSource.Regex,
                            Confidence = 74,
                            Diagnostic = $"Regex-safe alias matched '{candidate}' using pattern '{pattern.Description}'.",
                            MatchedValue = candidate,
                            MatchedKey = pattern.Description
                        };
                    }
                }

                return null;
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
                    Confidence = 58,
                    Diagnostic = $"Review-needed fuzzy suggestion matched '{best.SourceValue}' to '{best.AliasKey}' with score {best.Score:0.00}.",
                    MatchedValue = best.SourceValue,
                    MatchedKey = best.AliasKey
                };
            }

            private static bool IsWeakMatchIdentitySafe(
                XmltvChannelDescriptor xmltvChannel,
                IReadOnlyList<Channel> matchedChannels,
                string matchedValue,
                string matchedKey,
                out string diagnostic)
            {
                diagnostic = "Number and identity suffix guard passed; suggestion remains review-needed.";
                var guideText = string.Join(
                    " ",
                    new[]
                    {
                        xmltvChannel.Id,
                        matchedValue,
                        matchedKey
                    }
                    .Concat(xmltvChannel.DisplayNames)
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

                foreach (var channel in matchedChannels)
                {
                    var channelText = string.Join(
                        " ",
                        new[]
                        {
                            channel.Name,
                            channel.ProviderEpgChannelId,
                            channel.EpgChannelId,
                            channel.NormalizedName
                        }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));

                    if (!HaveCompatibleNumberTokens(channelText, guideText))
                    {
                        diagnostic = $"Rejected weak suggestion because channel numbers differ between '{channel.Name}' and '{matchedValue}'.";
                        return false;
                    }

                    if (!HaveCompatibleIdentitySuffixTokens(channelText, guideText))
                    {
                        diagnostic = $"Rejected weak suggestion because Max/Plus/Extra/Event identity tokens differ between '{channel.Name}' and '{matchedValue}'.";
                        return false;
                    }
                }

                return true;
            }

            private static bool HaveCompatibleNumberTokens(string left, string right)
            {
                var leftNumbers = ExtractNumberTokens(left);
                var rightNumbers = ExtractNumberTokens(right);
                if (leftNumbers.Count == 0 && rightNumbers.Count == 0)
                {
                    return true;
                }

                return leftNumbers.SetEquals(rightNumbers);
            }

            private static bool HaveCompatibleIdentitySuffixTokens(string left, string right)
            {
                var leftTokens = ExtractIdentitySuffixTokens(left);
                var rightTokens = ExtractIdentitySuffixTokens(right);
                return leftTokens.SetEquals(rightTokens);
            }

            private static HashSet<string> ExtractNumberTokens(string value)
            {
                var tokens = ExtractNormalizedTokens(value);
                var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var token in tokens)
                {
                    if (token.All(char.IsDigit))
                    {
                        numbers.Add(token.TrimStart('0').Length == 0 ? "0" : token.TrimStart('0'));
                    }
                    else if (NumberWordTokens.TryGetValue(token, out var mapped))
                    {
                        numbers.Add(mapped);
                    }
                }

                return numbers;
            }

            private static HashSet<string> ExtractIdentitySuffixTokens(string value)
            {
                return ExtractNormalizedTokens(value)
                    .Where(token => IdentitySuffixTokens.Contains(token))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            private static IReadOnlyList<string> ExtractNormalizedTokens(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return Array.Empty<string>();
                }

                return Regex
                    .Matches(RemoveDiacritics(value).ToLowerInvariant(), "[a-z0-9]+")
                    .Select(match => match.Value)
                    .Where(token => !string.IsNullOrWhiteSpace(token))
                    .ToList();
            }

            private static readonly IReadOnlyDictionary<string, string> NumberWordTokens =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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

            private static readonly HashSet<string> IdentitySuffixTokens =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    "max",
                    "plus",
                    "extra",
                    "event",
                    "events"
                };

            private IEnumerable<string> BuildExactValues(Channel channel)
            {
                if (!string.IsNullOrWhiteSpace(channel.NormalizedName))
                {
                    yield return channel.NormalizedName;
                }

                var providerGuide = _identityService.NormalizeForEpgScheduleMatch(channel.ProviderEpgChannelId);
                if (!string.IsNullOrWhiteSpace(providerGuide))
                {
                    yield return providerGuide;
                }
            }

            private static IReadOnlyList<string> BuildRegexValues(Channel channel, IReadOnlyList<string> aliasKeys)
            {
                var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                void AddValue(string? value)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value.Trim());
                    }
                }

                AddValue(channel.Name);
                AddValue(channel.NormalizedName);
                AddValue(channel.ProviderEpgChannelId);
                AddValue(channel.EpgChannelId);
                AddValue(channel.NormalizedIdentityKey);

                foreach (var aliasKey in aliasKeys)
                {
                    AddValue(aliasKey);
                }

                return values.ToList();
            }

            private static IEnumerable<string> BuildSourceCandidates(XmltvChannelDescriptor xmltvChannel)
            {
                yield return xmltvChannel.Id;
                foreach (var displayName in xmltvChannel.DisplayNames)
                {
                    yield return displayName;
                }
            }

            private static IEnumerable<RegexAliasPattern> BuildRegexAliasPatterns(string candidate)
            {
                var tokens = Regex
                    .Matches(RemoveDiacritics(candidate).ToLowerInvariant(), "[a-z0-9]+")
                    .Select(match => match.Value)
                    .Where(token => token.Length > 1 || token.Any(char.IsDigit))
                    .Where(token => token is not "tv" and not "channel" and not "hd" and not "fhd" and not "uhd" and not "sd" and not "us" and not "usa" and not "uk" and not "tr")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (tokens.Count < 2 && tokens.All(token => token.All(char.IsLetter)))
                {
                    yield break;
                }

                var pattern = string.Join(@"\W*", tokens.Select(Regex.Escape));
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    yield break;
                }

                yield return new RegexAliasPattern(
                    new Regex(@"\b" + pattern + @"\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                    string.Join(" ", tokens));
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

            private void AddStreamHashIndex(Channel channel)
            {
                var streamHash = EpgMappingDecisionIdentity.ComputeStreamUrlHash(channel.StreamUrl);
                if (string.IsNullOrWhiteSpace(streamHash))
                {
                    return;
                }

                if (!_byStreamUrlHash.TryGetValue(streamHash, out var channels))
                {
                    channels = new List<Channel>();
                    _byStreamUrlHash[streamHash] = channels;
                }

                if (!channels.Any(item => item.Id == channel.Id))
                {
                    channels.Add(channel);
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

            private List<Channel> FilterRejectedWeakMatches(XmltvChannelDescriptor xmltvChannel, IEnumerable<Channel> matches)
            {
                return matches
                    .Where(channel => !_decisionLookup.IsRejected(channel, xmltvChannel.Id))
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
                string diagnostic,
                string matchedValue,
                string matchedKey)
            {
                outcomes[xmltvChannelId] = new ChannelEpgMatchOutcome
                {
                    Channels = matchedChannels,
                    Reason = reason,
                    Confidence = confidence,
                    Diagnostic = diagnostic,
                    MatchedValue = matchedValue,
                    MatchedKey = matchedKey
                };

                foreach (var channel in matchedChannels)
                {
                    assignedChannelIds.Add(channel.Id);
                }
            }

            private static ChannelEpgMatchOutcome BuildSupplementalOutcome(
                IReadOnlyList<Channel> matchedChannels,
                ChannelEpgMatchSource reason,
                int confidence,
                string diagnostic,
                string matchedValue,
                string matchedKey)
            {
                return new ChannelEpgMatchOutcome
                {
                    Channels = matchedChannels,
                    Reason = reason,
                    Confidence = confidence,
                    Diagnostic = diagnostic,
                    MatchedValue = matchedValue,
                    MatchedKey = matchedKey,
                    IsSupplemental = true
                };
            }

            private static string RemoveDiacritics(string value)
            {
                return value.Normalize(NormalizationForm.FormD)
                    .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    .Aggregate(new StringBuilder(value.Length), (builder, ch) => builder.Append(ch), builder => builder.ToString())
                    .Normalize(NormalizationForm.FormC);
            }

            private sealed record RegexAliasPattern(Regex Regex, string Description);
            private sealed record ChannelCandidate(Channel Channel, IReadOnlyList<string> AliasKeys, IReadOnlyList<string> RegexValues);
        }
    }

    public sealed class XmltvChannelDescriptor
    {
        public string Id { get; init; } = string.Empty;
        public IReadOnlyList<string> DisplayNames { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> IconUrls { get; init; } = Array.Empty<string>();
        public int SourcePriority { get; init; }
        public EpgGuideSourceKind SourceKind { get; init; }
        public string SourceLabel { get; init; } = string.Empty;
        public string SourceUrl { get; init; } = string.Empty;
        public string PrimaryDisplayName => DisplayNames.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    public sealed class ChannelEpgMatchOutcome
    {
        public IReadOnlyList<Channel> Channels { get; init; } = Array.Empty<Channel>();
        public ChannelEpgMatchSource Reason { get; init; }
        public int Confidence { get; init; }
        public string Diagnostic { get; init; } = string.Empty;
        public string MatchedValue { get; init; } = string.Empty;
        public string MatchedKey { get; init; } = string.Empty;
        public bool IsSupplemental { get; init; }
    }

    public sealed class SourceEpgEnrichmentResult
    {
        public IReadOnlyDictionary<string, ChannelEpgMatchOutcome> Matches { get; init; } =
            new Dictionary<string, ChannelEpgMatchOutcome>(StringComparer.OrdinalIgnoreCase);
    }
}
