#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Models;

namespace Kroira.App.Services
{
    public interface ISourceProbeService
    {
        Task<SourceProbeRunResult> ProbeAsync(
            SourceHealthProbeType probeType,
            IReadOnlyList<SourceProbeCandidate> candidates,
            SourceRoutingDecision? routing = null);
    }

    public sealed class SourceProbeCandidate
    {
        public string Name { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public SourceRoutingDecision? Routing { get; set; }
        public string RouteSummary { get; set; } = string.Empty;
    }

    public sealed class SourceProbeRunResult
    {
        public SourceHealthProbeStatus Status { get; set; }
        public DateTime? ProbedAtUtc { get; set; }
        public int CandidateCount { get; set; }
        public int SampleSize { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public int TimeoutCount { get; set; }
        public int HttpErrorCount { get; set; }
        public int TransportErrorCount { get; set; }
        public string Summary { get; set; } = string.Empty;
    }

    public sealed class SourceProbeService : ISourceProbeService
    {
        private const int MaxSampleSize = 4;
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);
        public async Task<SourceProbeRunResult> ProbeAsync(
            SourceHealthProbeType probeType,
            IReadOnlyList<SourceProbeCandidate> candidates,
            SourceRoutingDecision? routing = null)
        {
            var probeable = candidates
                .Where(candidate => TryCreateHttpUri(candidate.StreamUrl, out _))
                .GroupBy(
                    candidate => $"{NormalizeUrl(candidate.StreamUrl)}|{BuildRoutingKey(candidate.Routing ?? routing)}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.StreamUrl, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (probeable.Count == 0)
            {
                return new SourceProbeRunResult
                {
                    Status = SourceHealthProbeStatus.Skipped,
                    CandidateCount = 0,
                    Summary = probeType == SourceHealthProbeType.Live
                        ? "Live health is currently structure-only because no HTTP sample was available for probing."
                        : "VOD health is currently structure-only because no HTTP sample was available for probing."
                };
            }

            var sample = SelectSample(probeable, MaxSampleSize);
            var clients = new Dictionary<string, HttpClient>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var results = await Task.WhenAll(sample.Select(candidate =>
                {
                    var effectiveRouting = candidate.Routing ?? routing;
                    var routeKey = BuildRoutingKey(effectiveRouting);
                    if (!clients.TryGetValue(routeKey, out var httpClient))
                    {
                        httpClient = CreateHttpClient(effectiveRouting);
                        clients[routeKey] = httpClient;
                    }

                    return ProbeCandidateAsync(httpClient, candidate);
                }));

                var successCount = results.Count(result => result.Kind == ProbeResultKind.Success);
                var timeoutCount = results.Count(result => result.Kind == ProbeResultKind.Timeout);
                var httpErrorCount = results.Count(result => result.Kind == ProbeResultKind.HttpError);
                var transportErrorCount = results.Count(result => result.Kind == ProbeResultKind.TransportError);
                var failureCount = sample.Count - successCount;

                return new SourceProbeRunResult
                {
                    Status = SourceHealthProbeStatus.Completed,
                    ProbedAtUtc = DateTime.UtcNow,
                    CandidateCount = probeable.Count,
                    SampleSize = sample.Count,
                    SuccessCount = successCount,
                    FailureCount = failureCount,
                    TimeoutCount = timeoutCount,
                    HttpErrorCount = httpErrorCount,
                    TransportErrorCount = transportErrorCount,
                    Summary = BuildSummary(
                        probeType,
                        probeable.Count,
                        sample.Count,
                        successCount,
                        failureCount,
                        timeoutCount,
                        httpErrorCount,
                        transportErrorCount,
                        BuildRouteSummaries(sample, routing))
                };
            }
            finally
            {
                foreach (var client in clients.Values)
                {
                    client.Dispose();
                }
            }
        }

        private async Task<ProbeAttemptResult> ProbeCandidateAsync(HttpClient httpClient, SourceProbeCandidate candidate)
        {
            if (!TryCreateHttpUri(candidate.StreamUrl, out var uri))
            {
                return new ProbeAttemptResult(ProbeResultKind.TransportError);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var timeoutCts = new CancellationTokenSource(ProbeTimeout);

            try
            {
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                return (int)response.StatusCode < 400
                    ? new ProbeAttemptResult(ProbeResultKind.Success)
                    : new ProbeAttemptResult(ProbeResultKind.HttpError);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                return new ProbeAttemptResult(ProbeResultKind.Timeout);
            }
            catch (HttpRequestException)
            {
                return new ProbeAttemptResult(ProbeResultKind.TransportError);
            }
            catch
            {
                return new ProbeAttemptResult(ProbeResultKind.TransportError);
            }
        }

        private static List<SourceProbeCandidate> SelectSample(IReadOnlyList<SourceProbeCandidate> candidates, int maxSampleSize)
        {
            if (candidates.Count <= maxSampleSize)
            {
                return candidates.ToList();
            }

            var selected = new List<SourceProbeCandidate>(maxSampleSize);
            var usedIndexes = new HashSet<int>();
            for (var position = 0; position < maxSampleSize; position++)
            {
                var index = (int)Math.Round((double)position * (candidates.Count - 1) / (maxSampleSize - 1), MidpointRounding.AwayFromZero);
                while (!usedIndexes.Add(index) && index < candidates.Count - 1)
                {
                    index++;
                }

                selected.Add(candidates[index]);
            }

            return selected;
        }

        private static bool TryCreateHttpUri(string? streamUrl, out Uri? uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(streamUrl) ||
                !Uri.TryCreate(streamUrl.Trim(), UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            uri = parsed;
            return true;
        }

        private static string NormalizeUrl(string streamUrl)
        {
            return streamUrl.Trim();
        }

        private static string BuildSummary(
            SourceHealthProbeType probeType,
            int candidateCount,
            int sampleSize,
            int successCount,
            int failureCount,
            int timeoutCount,
            int httpErrorCount,
            int transportErrorCount,
            IReadOnlyList<string> routeSummaries)
        {
            var layer = probeType == SourceHealthProbeType.Live ? "Live" : "VOD";
            if (sampleSize <= 0)
            {
                return $"{layer} probing did not run.";
            }

            var detailParts = new List<string>();
            if (timeoutCount > 0) detailParts.Add(timeoutCount == 1 ? "1 timeout" : $"{timeoutCount} timeouts");
            if (httpErrorCount > 0) detailParts.Add(httpErrorCount == 1 ? "1 HTTP failure" : $"{httpErrorCount} HTTP failures");
            if (transportErrorCount > 0) detailParts.Add(transportErrorCount == 1 ? "1 transport error" : $"{transportErrorCount} transport errors");

            var summary = successCount == sampleSize
                ? $"{layer} probe reached {successCount}/{sampleSize} sampled items."
                : $"{layer} probe reached {successCount}/{sampleSize} sampled items and missed {failureCount}.";

            if (candidateCount > sampleSize)
            {
                summary += $" Sampled from {candidateCount} probeable entries.";
            }

            if (detailParts.Count > 0)
            {
                summary += $" {string.Join(", ", detailParts)}.";
            }

            if (routeSummaries.Count > 0)
            {
                summary += routeSummaries.Count == 1
                    ? $" Route: {TrimSentence(routeSummaries[0])}."
                    : $" Routes: {string.Join(" | ", routeSummaries.Select(TrimSentence))}.";
            }

            return summary;
        }

        private static IReadOnlyList<string> BuildRouteSummaries(
            IReadOnlyList<SourceProbeCandidate> sample,
            SourceRoutingDecision? fallbackRouting)
        {
            return sample
                .Select(candidate => FirstNonEmpty(candidate.RouteSummary, candidate.Routing?.Summary, fallbackRouting?.Summary))
                .Where(summary => !string.IsNullOrWhiteSpace(summary) &&
                                  !string.Equals(summary, "Direct routing", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();
        }

        private static string BuildRoutingKey(SourceRoutingDecision? routing)
        {
            if (routing is not { UseProxy: true })
            {
                return "direct";
            }

            return string.IsNullOrWhiteSpace(routing.ProxyUrl)
                ? "proxy"
                : routing.ProxyUrl.Trim();
        }

        private static string TrimSentence(string value)
        {
            var trimmed = value?.Trim() ?? string.Empty;
            return trimmed.TrimEnd('.', ' ');
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
        }

        private static HttpClient CreateHttpClient(SourceRoutingDecision? routing)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                UseCookies = false
            };

            if (routing is { UseProxy: true } && Uri.TryCreate(routing.ProxyUrl, UriKind.Absolute, out var proxyUri))
            {
                handler.Proxy = new WebProxy(proxyUri);
                handler.UseProxy = true;
            }

            var httpClient = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Kroira/1.0");
            return httpClient;
        }

        private sealed record ProbeAttemptResult(ProbeResultKind Kind);

        private enum ProbeResultKind
        {
            Success = 0,
            Timeout = 1,
            HttpError = 2,
            TransportError = 3
        }
    }
}
