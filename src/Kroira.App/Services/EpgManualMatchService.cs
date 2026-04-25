#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface IEpgManualMatchService
    {
        Task<IReadOnlyList<EpgManualMatchChannel>> GetChannelsAsync(
            AppDbContext db,
            int sourceProfileId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<EpgManualMatchCandidate>> SearchXmltvChannelsAsync(
            AppDbContext db,
            int sourceProfileId,
            string searchText,
            int limit = 50,
            CancellationToken cancellationToken = default);

        Task SetOverrideAsync(
            AppDbContext db,
            int sourceProfileId,
            int channelId,
            string xmltvChannelId,
            string xmltvDisplayName,
            CancellationToken cancellationToken = default);

        Task ClearOverrideAsync(
            AppDbContext db,
            int sourceProfileId,
            int channelId,
            CancellationToken cancellationToken = default);
    }

    public sealed class EpgManualMatchService : IEpgManualMatchService
    {
        public async Task<IReadOnlyList<EpgManualMatchChannel>> GetChannelsAsync(
            AppDbContext db,
            int sourceProfileId,
            CancellationToken cancellationToken = default)
        {
            return await db.Channels
                .AsNoTracking()
                .Join(
                    db.ChannelCategories.AsNoTracking().Where(category => category.SourceProfileId == sourceProfileId),
                    channel => channel.ChannelCategoryId,
                    category => category.Id,
                    (channel, category) => new { Channel = channel, Category = category })
                .Join(
                    db.SourceProfiles.AsNoTracking(),
                    row => row.Category.SourceProfileId,
                    source => source.Id,
                    (row, source) => new EpgManualMatchChannel
                    {
                        ChannelId = row.Channel.Id,
                        SourceProfileId = source.Id,
                        SourceName = source.Name,
                        CategoryName = row.Category.Name,
                        ChannelName = row.Channel.Name,
                        ProviderEpgChannelId = row.Channel.ProviderEpgChannelId,
                        CurrentXmltvChannelId = row.Channel.EpgChannelId,
                        MatchSource = row.Channel.EpgMatchSource,
                        MatchConfidence = row.Channel.EpgMatchConfidence,
                        MatchSummary = row.Channel.EpgMatchSummary
                    })
                .OrderBy(channel => channel.CategoryName)
                .ThenBy(channel => channel.ChannelName)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<EpgManualMatchCandidate>> SearchXmltvChannelsAsync(
            AppDbContext db,
            int sourceProfileId,
            string searchText,
            int limit = 50,
            CancellationToken cancellationToken = default)
        {
            var query = searchText?.Trim() ?? string.Empty;
            var candidates = new Dictionary<string, EpgManualMatchCandidate>(StringComparer.OrdinalIgnoreCase);

            void Add(string xmltvChannelId, string displayName, ChannelEpgMatchSource source, int confidence, string detail, bool isCustom = false)
            {
                var id = xmltvChannelId.Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    return;
                }

                if (candidates.TryGetValue(id, out var existing))
                {
                    if (confidence > existing.Confidence)
                    {
                        existing.Confidence = confidence;
                        existing.SuggestedMatchSource = source;
                        existing.DetailText = detail;
                    }

                    if (string.IsNullOrWhiteSpace(existing.DisplayName) && !string.IsNullOrWhiteSpace(displayName))
                    {
                        existing.DisplayName = displayName.Trim();
                    }

                    return;
                }

                candidates[id] = new EpgManualMatchCandidate
                {
                    XmltvChannelId = id,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim(),
                    SuggestedMatchSource = source,
                    Confidence = confidence,
                    DetailText = detail,
                    IsCustomCandidate = isCustom
                };
            }

            var enrichmentRecords = await db.SourceChannelEnrichmentRecords
                .AsNoTracking()
                .Where(record => record.SourceProfileId == sourceProfileId &&
                                 !string.IsNullOrWhiteSpace(record.MatchedXmltvChannelId))
                .Select(record => new
                {
                    record.MatchedXmltvChannelId,
                    record.MatchedXmltvDisplayName,
                    record.EpgMatchSource,
                    record.EpgMatchConfidence,
                    record.ProviderName
                })
                .ToListAsync(cancellationToken);
            foreach (var record in enrichmentRecords)
            {
                Add(
                    record.MatchedXmltvChannelId,
                    record.MatchedXmltvDisplayName,
                    record.EpgMatchSource,
                    record.EpgMatchConfidence,
                    string.IsNullOrWhiteSpace(record.ProviderName)
                        ? "Observed in the latest guide matching data."
                        : $"Observed from {record.ProviderName}.");
            }

            var decisions = await db.EpgMappingDecisions
                .AsNoTracking()
                .Where(decision => decision.SourceProfileId == sourceProfileId &&
                                   !string.IsNullOrWhiteSpace(decision.XmltvChannelId))
                .Select(decision => new
                {
                    decision.XmltvChannelId,
                    decision.XmltvDisplayName,
                    decision.SuggestedMatchSource,
                    decision.SuggestedConfidence,
                    decision.Decision
                })
                .ToListAsync(cancellationToken);
            foreach (var decision in decisions)
            {
                Add(
                    decision.XmltvChannelId,
                    decision.XmltvDisplayName,
                    decision.SuggestedMatchSource,
                    decision.Decision == EpgMappingDecisionState.Approved ? 93 : decision.SuggestedConfidence,
                    decision.Decision == EpgMappingDecisionState.Approved
                        ? "Existing manual override."
                        : "Stored review decision.");
            }

            var channelIds = await db.ChannelCategories
                .AsNoTracking()
                .Where(category => category.SourceProfileId == sourceProfileId)
                .Join(
                    db.Channels.AsNoTracking(),
                    category => category.Id,
                    channel => channel.ChannelCategoryId,
                    (category, channel) => new
                    {
                        channel.EpgChannelId,
                        channel.ProviderEpgChannelId,
                        channel.Name,
                        channel.EpgMatchSource,
                        channel.EpgMatchConfidence
                    })
                .ToListAsync(cancellationToken);
            foreach (var channel in channelIds)
            {
                Add(channel.EpgChannelId, channel.Name, channel.EpgMatchSource, channel.EpgMatchConfidence, "Current channel match.");
                Add(channel.ProviderEpgChannelId, channel.Name, ChannelEpgMatchSource.Provider, 97, "Provider tvg-id.");
            }

            if (!string.IsNullOrWhiteSpace(query) && !candidates.ContainsKey(query))
            {
                Add(query, query, ChannelEpgMatchSource.UserApproved, 93, "Use the typed XMLTV id as a manual override.", isCustom: true);
            }

            return candidates.Values
                .Where(candidate => string.IsNullOrWhiteSpace(query) ||
                                    candidate.XmltvChannelId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                    candidate.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.IsCustomCandidate)
                .ThenByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(limit <= 0 ? 50 : limit)
                .ToList();
        }

        public async Task SetOverrideAsync(
            AppDbContext db,
            int sourceProfileId,
            int channelId,
            string xmltvChannelId,
            string xmltvDisplayName,
            CancellationToken cancellationToken = default)
        {
            var normalizedXmltvId = xmltvChannelId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedXmltvId))
            {
                throw new ArgumentException("XMLTV channel id is required.", nameof(xmltvChannelId));
            }

            var channelContext = await LoadChannelContextAsync(db, sourceProfileId, channelId, cancellationToken)
                ?? throw new InvalidOperationException("Channel was not found for the selected source.");

            var nowUtc = DateTime.UtcNow;
            var existing = await db.EpgMappingDecisions
                .Where(decision => decision.SourceProfileId == sourceProfileId &&
                                   decision.ChannelId == channelId &&
                                   decision.Decision == EpgMappingDecisionState.Approved)
                .ToListAsync(cancellationToken);
            foreach (var previous in existing.Where(decision => !string.Equals(decision.XmltvChannelId, normalizedXmltvId, StringComparison.OrdinalIgnoreCase)))
            {
                db.EpgMappingDecisions.Remove(previous);
            }

            var decision = existing.FirstOrDefault(decision => string.Equals(decision.XmltvChannelId, normalizedXmltvId, StringComparison.OrdinalIgnoreCase));
            if (decision == null)
            {
                decision = new EpgMappingDecision
                {
                    SourceProfileId = sourceProfileId,
                    ChannelId = channelId,
                    XmltvChannelId = normalizedXmltvId,
                    CreatedAtUtc = nowUtc
                };
                db.EpgMappingDecisions.Add(decision);
            }

            decision.ChannelIdentityKey = channelContext.Channel.NormalizedIdentityKey;
            decision.ChannelName = channelContext.Channel.Name;
            decision.CategoryName = channelContext.Category.Name;
            decision.ProviderEpgChannelId = channelContext.Channel.ProviderEpgChannelId;
            decision.StreamUrlHash = EpgMappingDecisionIdentity.ComputeStreamUrlHash(channelContext.Channel.StreamUrl);
            decision.XmltvDisplayName = string.IsNullOrWhiteSpace(xmltvDisplayName) ? normalizedXmltvId : xmltvDisplayName.Trim();
            decision.Decision = EpgMappingDecisionState.Approved;
            decision.SuggestedMatchSource = ChannelEpgMatchSource.UserApproved;
            decision.SuggestedConfidence = 93;
            decision.ReasonSummary = "Manual EPG override set from EPG Center.";
            decision.UpdatedAtUtc = nowUtc;

            channelContext.Channel.EpgChannelId = normalizedXmltvId;
            channelContext.Channel.EpgMatchSource = ChannelEpgMatchSource.UserApproved;
            channelContext.Channel.EpgMatchConfidence = 93;
            channelContext.Channel.EpgMatchSummary = $"Manual XMLTV override: {decision.XmltvDisplayName}.";
            channelContext.Channel.EnrichedAtUtc = nowUtc;

            await db.SaveChangesAsync(cancellationToken);
        }

        public async Task ClearOverrideAsync(
            AppDbContext db,
            int sourceProfileId,
            int channelId,
            CancellationToken cancellationToken = default)
        {
            var channelContext = await LoadChannelContextAsync(db, sourceProfileId, channelId, cancellationToken)
                ?? throw new InvalidOperationException("Channel was not found for the selected source.");
            var decisions = await db.EpgMappingDecisions
                .Where(decision => decision.SourceProfileId == sourceProfileId &&
                                   decision.ChannelId == channelId &&
                                   decision.Decision == EpgMappingDecisionState.Approved)
                .ToListAsync(cancellationToken);
            if (decisions.Count > 0)
            {
                db.EpgMappingDecisions.RemoveRange(decisions);
            }

            if (!string.IsNullOrWhiteSpace(channelContext.Channel.ProviderEpgChannelId))
            {
                channelContext.Channel.EpgChannelId = channelContext.Channel.ProviderEpgChannelId;
                channelContext.Channel.EpgMatchSource = ChannelEpgMatchSource.Provider;
                channelContext.Channel.EpgMatchConfidence = 70;
                channelContext.Channel.EpgMatchSummary = "Using provider guide metadata until XMLTV confirms a better match.";
            }
            else
            {
                channelContext.Channel.EpgChannelId = string.Empty;
                channelContext.Channel.EpgMatchSource = ChannelEpgMatchSource.None;
                channelContext.Channel.EpgMatchConfidence = 0;
                channelContext.Channel.EpgMatchSummary = string.Empty;
            }

            channelContext.Channel.EnrichedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        private static async Task<ManualMatchChannelContext?> LoadChannelContextAsync(
            AppDbContext db,
            int sourceProfileId,
            int channelId,
            CancellationToken cancellationToken)
        {
            return await db.Channels
                .Join(
                    db.ChannelCategories.Where(category => category.SourceProfileId == sourceProfileId),
                    channel => channel.ChannelCategoryId,
                    category => category.Id,
                    (channel, category) => new ManualMatchChannelContext(channel, category))
                .FirstOrDefaultAsync(row => row.Channel.Id == channelId, cancellationToken);
        }

        private sealed record ManualMatchChannelContext(Channel Channel, ChannelCategory Category);
    }
}
