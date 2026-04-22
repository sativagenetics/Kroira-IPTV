#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Models;

namespace Kroira.App.Services
{
    public sealed class CompanionRelayRequest
    {
        public int SourceProfileId { get; set; }
        public SourceType SourceType { get; set; }
        public SourceNetworkPurpose Purpose { get; set; }
        public CatchupPlaybackMode PlaybackMode { get; set; } = CatchupPlaybackMode.Live;
        public string UpstreamUrl { get; set; } = string.Empty;
        public string ProviderSummary { get; set; } = string.Empty;
    }

    public interface ICompanionRelayService
    {
        Task<CompanionRelayDecision> ApplyAsync(
            SourceCredential? credential,
            CompanionRelayRequest request,
            CancellationToken cancellationToken = default);
    }

    public sealed class CompanionRelayService : ICompanionRelayService
    {
        private static readonly TimeSpan AvailabilityTtl = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan AvailabilityTimeout = TimeSpan.FromMilliseconds(900);
        private readonly ConcurrentDictionary<string, AvailabilityCacheEntry> _availabilityCache = new(StringComparer.OrdinalIgnoreCase);

        public async Task<CompanionRelayDecision> ApplyAsync(
            SourceCredential? credential,
            CompanionRelayRequest request,
            CancellationToken cancellationToken = default)
        {
            var upstreamUrl = request?.UpstreamUrl?.Trim() ?? string.Empty;
            var scope = credential?.CompanionScope ?? SourceCompanionScope.Disabled;
            var mode = credential?.CompanionMode ?? SourceCompanionRelayMode.Buffered;

            if (request == null || string.IsNullOrWhiteSpace(upstreamUrl))
            {
                return CompanionRelayDecision.Skipped(
                    scope,
                    mode,
                    upstreamUrl,
                    "Direct provider path",
                    "Companion relay was skipped because no upstream stream URL was available.");
            }

            if (credential == null || !ShouldUseCompanion(scope, request.Purpose))
            {
                return CompanionRelayDecision.Skipped(
                    scope,
                    mode,
                    upstreamUrl,
                    "Direct provider path",
                    "Companion relay is disabled for this source or for this request type.");
            }

            var companionUrl = NormalizeCompanionUrl(credential.CompanionUrl);
            if (string.IsNullOrWhiteSpace(companionUrl) || !Uri.TryCreate(companionUrl, UriKind.Absolute, out var companionUri))
            {
                return CompanionRelayDecision.FallbackDirect(
                    scope,
                    mode,
                    companionUrl,
                    upstreamUrl,
                    "Companion relay configured but invalid. Direct provider path kept.",
                    "The companion endpoint is missing or invalid, so KROIRA kept the direct provider path.");
            }

            var availability = await CheckAvailabilityAsync(companionUri, cancellationToken);
            if (!availability.IsAvailable)
            {
                return CompanionRelayDecision.FallbackDirect(
                    scope,
                    mode,
                    companionUrl,
                    upstreamUrl,
                    "Companion relay unavailable. Direct provider path kept.",
                    availability.Message);
            }

            var relayUrl = BuildRelayUrl(companionUri, request, mode);
            var modeLabel = mode == SourceCompanionRelayMode.Buffered ? "Buffered companion relay" : "Companion relay";
            var scopeLabel = scope == SourceCompanionScope.PlaybackAndProbing ? "playback + probes" : "playback";
            return CompanionRelayDecision.Applied(
                scope,
                mode,
                companionUrl,
                upstreamUrl,
                relayUrl,
                $"{modeLabel} via {companionUrl}",
                $"{modeLabel} is active for {scopeLabel}.");
        }

        private async Task<AvailabilityCacheEntry> CheckAvailabilityAsync(Uri companionUri, CancellationToken cancellationToken)
        {
            var cacheKey = NormalizeCompanionUrl(companionUri.ToString());
            if (_availabilityCache.TryGetValue(cacheKey, out var cached) &&
                DateTime.UtcNow - cached.CheckedAtUtc <= AvailabilityTtl)
            {
                return cached;
            }

            AvailabilityCacheEntry entry;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(AvailabilityTimeout);

                using var handler = new HttpClientHandler
                {
                    UseProxy = false,
                    AllowAutoRedirect = true
                };
                using var httpClient = new HttpClient(handler)
                {
                    Timeout = Timeout.InfiniteTimeSpan
                };
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Kroira/1.0");

                var pingUri = BuildServiceUri(companionUri, "ping");
                using var response = await httpClient.GetAsync(pingUri, timeoutCts.Token);
                entry = response.IsSuccessStatusCode
                    ? new AvailabilityCacheEntry(true, "Companion relay is reachable.", DateTime.UtcNow)
                    : new AvailabilityCacheEntry(
                        false,
                        $"The companion relay responded with HTTP {(int)response.StatusCode}, so KROIRA kept the direct provider path.",
                        DateTime.UtcNow);
            }
            catch (OperationCanceledException)
            {
                entry = new AvailabilityCacheEntry(
                    false,
                    "The companion relay timed out, so KROIRA kept the direct provider path.",
                    DateTime.UtcNow);
            }
            catch (HttpRequestException ex)
            {
                entry = new AvailabilityCacheEntry(
                    false,
                    $"The companion relay could not be reached ({Trim(ex.Message, 140)}), so KROIRA kept the direct provider path.",
                    DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                entry = new AvailabilityCacheEntry(
                    false,
                    $"The companion relay check failed ({Trim(ex.Message, 140)}), so KROIRA kept the direct provider path.",
                    DateTime.UtcNow);
            }

            _availabilityCache[cacheKey] = entry;
            return entry;
        }

        private static string BuildRelayUrl(
            Uri companionUri,
            CompanionRelayRequest request,
            SourceCompanionRelayMode mode)
        {
            var relayUri = BuildServiceUri(companionUri, "relay");
            var queryParts = new List<string>
            {
                $"upstream={Uri.EscapeDataString(request.UpstreamUrl)}",
                $"mode={Uri.EscapeDataString(mode == SourceCompanionRelayMode.Buffered ? "buffered" : "relay")}",
                $"purpose={Uri.EscapeDataString(request.Purpose.ToString().ToLowerInvariant())}",
                $"sourceType={Uri.EscapeDataString(request.SourceType.ToString().ToLowerInvariant())}"
            };

            if (request.SourceProfileId > 0)
            {
                queryParts.Add($"sourceId={request.SourceProfileId}");
            }

            if (request.PlaybackMode == CatchupPlaybackMode.Catchup)
            {
                queryParts.Add("timeshift=1");
            }

            if (!string.IsNullOrWhiteSpace(request.ProviderSummary))
            {
                queryParts.Add($"provider={Uri.EscapeDataString(request.ProviderSummary)}");
            }

            return $"{relayUri}?{string.Join("&", queryParts.Where(part => !string.IsNullOrWhiteSpace(part)))}";
        }

        private static Uri BuildServiceUri(Uri companionUri, string leaf)
        {
            var normalized = NormalizeCompanionUrl(companionUri.ToString());
            var builder = new UriBuilder(normalized)
            {
                Query = string.Empty,
                Fragment = string.Empty
            };

            var path = builder.Path.TrimEnd('/');
            if (path.EndsWith("/relay", StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^"/relay".Length];
            }
            else if (path.EndsWith("/ping", StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^"/ping".Length];
            }

            builder.Path = string.IsNullOrWhiteSpace(path) || path == "/"
                ? $"/{leaf}"
                : $"{path}/{leaf}";
            return builder.Uri;
        }

        private static bool ShouldUseCompanion(SourceCompanionScope scope, SourceNetworkPurpose purpose)
        {
            return scope switch
            {
                SourceCompanionScope.PlaybackOnly => purpose == SourceNetworkPurpose.Playback,
                SourceCompanionScope.PlaybackAndProbing => purpose is SourceNetworkPurpose.Playback or SourceNetworkPurpose.Probe,
                _ => false
            };
        }

        private static string NormalizeCompanionUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            {
                return value.Trim();
            }

            var builder = new UriBuilder(uri)
            {
                Query = string.Empty,
                Fragment = string.Empty
            };

            return builder.Uri.ToString().TrimEnd('/');
        }

        private static string Trim(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..(maxLength - 3)] + "...";
        }

        private sealed record AvailabilityCacheEntry(bool IsAvailable, string Message, DateTime CheckedAtUtc);
    }
}
