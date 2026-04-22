#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface IProviderStreamResolverService
    {
        Task<ProviderStreamResolution> ResolveAsync(
            AppDbContext db,
            int preferredSourceProfileId,
            string catalogStreamUrl,
            SourceNetworkPurpose purpose,
            CancellationToken cancellationToken = default);

        Task<ProviderStreamResolution> ResolvePlaybackContextAsync(
            AppDbContext db,
            PlaybackLaunchContext context,
            SourceNetworkPurpose purpose,
            CancellationToken cancellationToken = default);
    }

    public sealed class ProviderStreamResolverService : IProviderStreamResolverService
    {
        private readonly IStalkerPortalClient _stalkerPortalClient;
        private readonly ISourceRoutingService _sourceRoutingService;
        private readonly ICompanionRelayService _companionRelayService;

        public ProviderStreamResolverService(
            IStalkerPortalClient stalkerPortalClient,
            ISourceRoutingService sourceRoutingService,
            ICompanionRelayService companionRelayService)
        {
            _stalkerPortalClient = stalkerPortalClient;
            _sourceRoutingService = sourceRoutingService;
            _companionRelayService = companionRelayService;
        }

        public async Task<ProviderStreamResolution> ResolveAsync(
            AppDbContext db,
            int preferredSourceProfileId,
            string catalogStreamUrl,
            SourceNetworkPurpose purpose,
            CancellationToken cancellationToken = default)
        {
            var effectiveCatalogUrl = string.IsNullOrWhiteSpace(catalogStreamUrl) ? string.Empty : catalogStreamUrl.Trim();
            if (string.IsNullOrWhiteSpace(effectiveCatalogUrl))
            {
                return ProviderStreamResolution.Failed("No catalog stream URL was available.");
            }

            if (!StalkerLocatorCodec.TryParse(effectiveCatalogUrl, out var stalkerLocator))
            {
                var credential = preferredSourceProfileId > 0
                    ? await db.SourceCredentials
                        .AsNoTracking()
                        .FirstOrDefaultAsync(item => item.SourceProfileId == preferredSourceProfileId, cancellationToken)
                    : null;
                var sourceType = await ResolveSourceTypeAsync(db, preferredSourceProfileId, cancellationToken);
                return await FinalizeResolutionAsync(
                    credential,
                    preferredSourceProfileId,
                    sourceType,
                    purpose,
                    effectiveCatalogUrl,
                    effectiveCatalogUrl,
                    "Direct stream URL",
                    cancellationToken);
            }

            var sourceProfileId = stalkerLocator.SourceProfileId > 0
                ? stalkerLocator.SourceProfileId
                : preferredSourceProfileId;
            if (sourceProfileId <= 0)
            {
                return ProviderStreamResolution.Failed("The Stalker locator does not identify a source profile.");
            }

            var sourceCredential = await db.SourceCredentials
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId, cancellationToken);
            if (sourceCredential == null)
            {
                return ProviderStreamResolution.Failed("The source credentials for this Stalker item were not found.");
            }

            try
            {
                var resolution = await _stalkerPortalClient.ResolveStreamAsync(
                    sourceCredential,
                    stalkerLocator,
                    purpose,
                    cancellationToken);
                return await FinalizeResolutionAsync(
                    sourceCredential,
                    sourceProfileId,
                    SourceType.Stalker,
                    purpose,
                    effectiveCatalogUrl,
                    resolution.StreamUrl,
                    $"Stalker {stalkerLocator.ResourceType} link",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                return ProviderStreamResolution.Failed(ex.Message);
            }
        }

        public async Task<ProviderStreamResolution> ResolvePlaybackContextAsync(
            AppDbContext db,
            PlaybackLaunchContext context,
            SourceNetworkPurpose purpose,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                return ProviderStreamResolution.Failed("Playback context is missing.");
            }

            var catalogStreamUrl = string.IsNullOrWhiteSpace(context.CatalogStreamUrl)
                ? context.StreamUrl
                : context.CatalogStreamUrl;
            var resolution = await ResolveAsync(
                db,
                context.PreferredSourceProfileId,
                catalogStreamUrl,
                purpose,
                cancellationToken);
            if (!resolution.Success)
            {
                return resolution;
            }

            context.CatalogStreamUrl = resolution.CatalogStreamUrl;
            context.UpstreamStreamUrl = resolution.UpstreamStreamUrl;
            context.StreamUrl = resolution.StreamUrl;
            context.ProxyScope = resolution.UpstreamRouting.Scope;
            context.ProxyUrl = resolution.UpstreamRouting.UseProxy ? resolution.UpstreamRouting.ProxyUrl : string.Empty;
            context.CompanionScope = resolution.Companion.Scope;
            context.CompanionMode = resolution.Companion.Mode;
            context.CompanionUrl = resolution.Companion.CompanionUrl;
            context.CompanionStatus = resolution.Companion.Status;
            context.CompanionStatusText = resolution.Companion.StatusText;
            context.ProviderSummary = resolution.ProviderSummary;
            context.RoutingSummary = string.IsNullOrWhiteSpace(resolution.RoutingSummary)
                ? resolution.EffectiveRouting.Summary
                : resolution.RoutingSummary;
            if (context.ContentType == PlaybackContentType.Channel &&
                !string.IsNullOrWhiteSpace(resolution.UpstreamStreamUrl))
            {
                context.LiveStreamUrl = resolution.UpstreamStreamUrl;
            }

            return resolution;
        }

        private async Task<ProviderStreamResolution> FinalizeResolutionAsync(
            SourceCredential? credential,
            int sourceProfileId,
            SourceType sourceType,
            SourceNetworkPurpose purpose,
            string catalogStreamUrl,
            string upstreamStreamUrl,
            string providerSummary,
            CancellationToken cancellationToken)
        {
            var upstreamRouting = _sourceRoutingService.Resolve(credential, purpose);
            var companion = await _companionRelayService.ApplyAsync(
                credential,
                new CompanionRelayRequest
                {
                    SourceProfileId = sourceProfileId,
                    SourceType = sourceType,
                    Purpose = purpose,
                    UpstreamUrl = upstreamStreamUrl,
                    ProviderSummary = providerSummary
                },
                cancellationToken);
            var effectiveRouting = companion.UseCompanion
                ? new SourceRoutingDecision
                {
                    Scope = SourceProxyScope.Disabled,
                    Summary = "Direct local companion connection"
                }
                : upstreamRouting;
            var routingSummary = BuildRoutingSummary(upstreamRouting, companion);
            var message = companion.Status == CompanionRelayStatus.FallbackDirect
                ? companion.StatusText
                : string.Empty;

            return ProviderStreamResolution.CreateSuccess(
                catalogStreamUrl,
                upstreamStreamUrl,
                companion.UseCompanion ? companion.RelayUrl : upstreamStreamUrl,
                providerSummary,
                upstreamRouting,
                effectiveRouting,
                companion,
                routingSummary,
                message);
        }

        private static string BuildRoutingSummary(SourceRoutingDecision upstreamRouting, CompanionRelayDecision companion)
        {
            if (companion.UseCompanion)
            {
                return companion.Summary;
            }

            if (companion.Status == CompanionRelayStatus.FallbackDirect)
            {
                return upstreamRouting.UseProxy
                    ? $"{companion.Summary} {upstreamRouting.Summary}."
                    : companion.Summary;
            }

            return upstreamRouting.Summary;
        }

        private static async Task<SourceType> ResolveSourceTypeAsync(
            AppDbContext db,
            int sourceProfileId,
            CancellationToken cancellationToken)
        {
            if (sourceProfileId <= 0)
            {
                return SourceType.M3U;
            }

            var sourceType = await db.SourceProfiles
                .AsNoTracking()
                .Where(item => item.Id == sourceProfileId)
                .Select(item => (SourceType?)item.Type)
                .FirstOrDefaultAsync(cancellationToken);
            return sourceType ?? SourceType.M3U;
        }
    }

    public sealed class ProviderStreamResolution
    {
        public bool Success { get; init; }
        public string CatalogStreamUrl { get; init; } = string.Empty;
        public string UpstreamStreamUrl { get; init; } = string.Empty;
        public string StreamUrl { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public string ProviderSummary { get; init; } = string.Empty;
        public SourceRoutingDecision UpstreamRouting { get; init; } = new();
        public SourceRoutingDecision EffectiveRouting { get; init; } = new();
        public CompanionRelayDecision Companion { get; init; } = new();
        public string RoutingSummary { get; init; } = string.Empty;

        public static ProviderStreamResolution CreateSuccess(
            string catalogStreamUrl,
            string upstreamStreamUrl,
            string streamUrl,
            string providerSummary,
            SourceRoutingDecision upstreamRouting,
            SourceRoutingDecision effectiveRouting,
            CompanionRelayDecision companion,
            string routingSummary,
            string message = "")
        {
            return new ProviderStreamResolution
            {
                Success = true,
                CatalogStreamUrl = catalogStreamUrl,
                UpstreamStreamUrl = upstreamStreamUrl,
                StreamUrl = streamUrl,
                Message = message?.Trim() ?? string.Empty,
                ProviderSummary = providerSummary,
                UpstreamRouting = upstreamRouting,
                EffectiveRouting = effectiveRouting,
                Companion = companion,
                RoutingSummary = routingSummary
            };
        }

        public static ProviderStreamResolution Failed(string message)
        {
            return new ProviderStreamResolution
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(message) ? "Provider stream resolution failed." : message.Trim()
            };
        }
    }
}
