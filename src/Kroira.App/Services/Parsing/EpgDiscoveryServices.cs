using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.EntityFrameworkCore;

#nullable enable

namespace Kroira.App.Services.Parsing
{
    public sealed class EpgUnavailableException : Exception
    {
        public EpgUnavailableException(string message)
            : base(message)
        {
        }
    }

    public sealed class EpgFetchException : Exception
    {
        public EpgFetchException(string message, string xmltvUrl, EpgActiveMode activeMode)
            : base(message)
        {
            XmltvUrl = xmltvUrl ?? string.Empty;
            ActiveMode = activeMode;
        }

        public string XmltvUrl { get; }
        public EpgActiveMode ActiveMode { get; }
    }

    public sealed class EpgDiscoveryResult
    {
        public EpgDiscoveryResult(
            IReadOnlyList<EpgDiscoveredGuideSource> guideSources,
            string description,
            string detectedXmltvUrl,
            EpgActiveMode activeMode)
        {
            GuideSources = guideSources ?? Array.Empty<EpgDiscoveredGuideSource>();
            Description = description;
            DetectedXmltvUrl = detectedXmltvUrl ?? string.Empty;
            ActiveMode = activeMode;
        }

        public IReadOnlyList<EpgDiscoveredGuideSource> GuideSources { get; }
        public string Description { get; }
        public string DetectedXmltvUrl { get; }
        public EpgActiveMode ActiveMode { get; }
        public string ActiveXmltvUrl =>
            GuideSources.FirstOrDefault(source => source.Status == EpgGuideSourceStatus.Ready && !string.IsNullOrWhiteSpace(source.XmlContent))?.Url
            ?? string.Empty;

        public IReadOnlyList<EpgGuideSourceStatusSnapshot> BuildStatusSnapshots()
        {
            return GuideSources
                .OrderBy(source => source.Priority)
                .ThenBy(source => source.Label, StringComparer.OrdinalIgnoreCase)
                .Select(source => source.ToSnapshot())
                .ToList();
        }
    }

    public sealed class EpgDiscoveredGuideSource
    {
        public string Label { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public EpgGuideSourceKind Kind { get; init; }
        public EpgGuideSourceStatus Status { get; set; } = EpgGuideSourceStatus.Pending;
        public bool IsOptional { get; init; }
        public int Priority { get; init; }
        public string XmlContent { get; set; } = string.Empty;
        public int XmltvChannelCount { get; set; }
        public int ProgrammeCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime? CheckedAtUtc { get; set; }
        public long FetchDurationMs { get; set; }
        public bool WasCacheHit { get; set; }
        public bool WasContentUnchanged { get; set; }
        public string ContentHash { get; set; } = string.Empty;

        public EpgGuideSourceStatusSnapshot ToSnapshot()
        {
            return new EpgGuideSourceStatusSnapshot
            {
                Label = Label,
                Url = Url,
                Kind = Kind,
                Status = Status,
                IsOptional = IsOptional,
                Priority = Priority,
                XmltvChannelCount = XmltvChannelCount,
                ProgrammeCount = ProgrammeCount,
                Message = Message,
                CheckedAtUtc = CheckedAtUtc,
                FetchDurationMs = FetchDurationMs,
                WasCacheHit = WasCacheHit,
                WasContentUnchanged = WasContentUnchanged,
                ContentHash = ContentHash
            };
        }
    }

    internal sealed class EpgSourceCandidate
    {
        public string Url { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
        public string Method { get; init; } = string.Empty;
        public EpgGuideSourceKind Kind { get; init; }
        public bool IsOptional { get; init; }
        public int Priority { get; init; }
    }

    public interface IEpgSourceDiscoveryService
    {
        SourceType SourceType { get; }
        Task<EpgDiscoveryResult> DiscoverAsync(AppDbContext db, int sourceProfileId);
    }

    public sealed class M3uEpgDiscoveryService : IEpgSourceDiscoveryService
    {
        private readonly ISourceRoutingService _sourceRoutingService;

        public M3uEpgDiscoveryService(ISourceRoutingService sourceRoutingService)
        {
            _sourceRoutingService = sourceRoutingService;
        }

        public SourceType SourceType => SourceType.M3U;

        public async Task<EpgDiscoveryResult> DiscoverAsync(AppDbContext db, int sourceProfileId)
        {
            var cred = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == sourceProfileId);
            if (cred == null || string.IsNullOrWhiteSpace(cred.Url))
            {
                throw new Exception("M3U source URL or path is empty.");
            }

            var playlistContent = await EpgDiscoveryHelpers.ReadTextAsync(cred.Url, cred, SourceNetworkPurpose.Import, _sourceRoutingService);
            var headerMetadata = M3uMetadataParser.ParseHeaderMetadata(playlistContent, cred.Url);
            LogHeaderDiscovery(sourceProfileId, headerMetadata);

            var discoveryCandidates = new List<EpgSourceCandidate>();
            var priority = 0;
            foreach (var candidateUrl in headerMetadata.XmltvUrls)
            {
                discoveryCandidates.Add(new EpgSourceCandidate
                {
                    Url = candidateUrl,
                    Label = "Provider XMLTV metadata",
                    Method = "header",
                    Kind = EpgGuideSourceKind.Provider,
                    Priority = priority++
                });
            }

            if (discoveryCandidates.Count == 0)
            {
                var derivedUrl = M3uMetadataParser.TryBuildXtreamXmltvUrl(cred.Url);
                if (!string.IsNullOrWhiteSpace(derivedUrl))
                {
                    discoveryCandidates.Add(new EpgSourceCandidate
                    {
                        Url = derivedUrl,
                        Label = "Derived Xtream XMLTV",
                        Method = "xtream_playlist_url",
                        Kind = EpgGuideSourceKind.Provider,
                        Priority = priority++
                    });
                    ImportRuntimeLogger.Log(
                        "EPG DISCOVERY",
                        $"source_profile_id={sourceProfileId}; source_type=M3U; xmltv_url_found=false; fallback_method=xtream_playlist_url; xmltv_candidate={FormatDiagnosticValue(derivedUrl)}");
                }
            }

            cred.DetectedEpgUrl = discoveryCandidates.FirstOrDefault(candidate => candidate.Kind == EpgGuideSourceKind.Provider)?.Url ?? string.Empty;

            var activeMode = EpgDiscoveryHelpers.ResolveActiveMode(cred);
            var candidates = activeMode == EpgActiveMode.Manual
                ? EpgDiscoveryHelpers.BuildManualAndFallbackCandidates(cred, priority)
                : discoveryCandidates
                    .Concat(EpgDiscoveryHelpers.BuildFallbackCandidates(cred, priority))
                    .ToList();

            if (activeMode == EpgActiveMode.Manual)
            {
                if (candidates.All(candidate => candidate.Kind != EpgGuideSourceKind.Manual))
                {
                    throw new EpgUnavailableException("Manual XMLTV URL is not configured.");
                }
            }

            if (candidates.Count == 0)
            {
                throw new EpgUnavailableException("Playlist does not advertise an XMLTV guide URL");
            }

            return await EpgDiscoveryHelpers.FetchGuideSourcesAsync(
                candidates,
                SourceType.M3U,
                sourceProfileId,
                activeMode,
                cred,
                cred.DetectedEpgUrl,
                _sourceRoutingService,
                activeMode == EpgActiveMode.Manual ? "Manual XMLTV override with fallback sources" : "Provider XMLTV with fallback sources");
        }

        private static void LogHeaderDiscovery(int sourceProfileId, M3uHeaderMetadata headerMetadata)
        {
            ImportRuntimeLogger.Log(
                "EPG DISCOVERY",
                $"source_profile_id={sourceProfileId}; source_type=M3U; xmltv_url_found={(headerMetadata.XmltvUrls.Count > 0 ? "true" : "false")}; xmltv_url_value={FormatDiagnosticValue(string.Join(" | ", headerMetadata.XmltvUrls))}; header_preview={FormatDiagnosticValue(headerMetadata.RawHeaderPreview)}; header_attributes={FormatDiagnosticValue(DescribeHeaderAttributes(headerMetadata))}");
        }

        private static string DescribeHeaderAttributes(M3uHeaderMetadata headerMetadata)
        {
            if (headerMetadata.Attributes.Count == 0)
            {
                return "none";
            }

            return string.Join(
                ";",
                headerMetadata.Attributes
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => $"{pair.Key}=[{string.Join("|", pair.Value)}]"));
        }

        private static string FormatDiagnosticValue(string value)
        {
            return EpgDiagnosticFormatter.Format(value);
        }
    }

    public sealed class XtreamEpgDiscoveryService : IEpgSourceDiscoveryService
    {
        private readonly ISourceRoutingService _sourceRoutingService;

        public XtreamEpgDiscoveryService(ISourceRoutingService sourceRoutingService)
        {
            _sourceRoutingService = sourceRoutingService;
        }

        public SourceType SourceType => SourceType.Xtream;

        public async Task<EpgDiscoveryResult> DiscoverAsync(AppDbContext db, int sourceProfileId)
        {
            var cred = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == sourceProfileId);
            if (cred == null || string.IsNullOrWhiteSpace(cred.Url) || string.IsNullOrWhiteSpace(cred.Username))
            {
                throw new Exception("Xtream credentials are incomplete.");
            }

            var providerGuideUrl = EpgDiscoveryHelpers.BuildXtreamProviderXmltvUrl(cred);
            cred.DetectedEpgUrl = providerGuideUrl;

            var activeMode = EpgDiscoveryHelpers.ResolveActiveMode(cred);
            var providerCandidates = new List<EpgSourceCandidate>
            {
                new()
                {
                    Url = providerGuideUrl,
                    Label = "Xtream provider XMLTV",
                    Method = "xmltv.php",
                    Kind = EpgGuideSourceKind.Provider,
                    Priority = 0
                }
            };
            var candidates = activeMode == EpgActiveMode.Manual
                ? EpgDiscoveryHelpers.BuildManualAndFallbackCandidates(cred, 1)
                : providerCandidates
                    .Concat(EpgDiscoveryHelpers.BuildConfiguredEpgUrlFallbackCandidates(cred, 1))
                    .Concat(EpgDiscoveryHelpers.BuildFallbackCandidates(cred, 2))
                    .ToList();

            if (activeMode == EpgActiveMode.Manual &&
                candidates.All(candidate => candidate.Kind != EpgGuideSourceKind.Manual))
            {
                throw new EpgUnavailableException("Manual XMLTV URL is not configured.");
            }

            return await EpgDiscoveryHelpers.FetchGuideSourcesAsync(
                candidates,
                SourceType.Xtream,
                sourceProfileId,
                activeMode,
                cred,
                providerGuideUrl,
                _sourceRoutingService,
                activeMode == EpgActiveMode.Manual ? "Manual XMLTV override with fallback sources" : "Xtream provider XMLTV with fallback sources");
        }

        private static string FormatDiagnosticValue(string value)
        {
            return EpgDiagnosticFormatter.Format(value);
        }
    }

    public sealed class StalkerEpgDiscoveryService : IEpgSourceDiscoveryService
    {
        private readonly ISourceRoutingService _sourceRoutingService;

        public StalkerEpgDiscoveryService(ISourceRoutingService sourceRoutingService)
        {
            _sourceRoutingService = sourceRoutingService;
        }

        public SourceType SourceType => SourceType.Stalker;

        public async Task<EpgDiscoveryResult> DiscoverAsync(AppDbContext db, int sourceProfileId)
        {
            var cred = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == sourceProfileId);
            if (cred == null || string.IsNullOrWhiteSpace(cred.Url))
            {
                throw new Exception("Stalker source credentials are incomplete.");
            }

            var activeMode = EpgDiscoveryHelpers.ResolveActiveMode(cred);
            var candidates = new List<EpgSourceCandidate>();
            if (activeMode == EpgActiveMode.Manual)
            {
                candidates.AddRange(EpgDiscoveryHelpers.BuildManualAndFallbackCandidates(cred, 0));
                if (candidates.All(candidate => candidate.Kind != EpgGuideSourceKind.Manual))
                {
                    throw new EpgUnavailableException("Manual XMLTV URL is not configured.");
                }
            }
            else
            {
                var priority = 0;
                if (!string.IsNullOrWhiteSpace(cred.DetectedEpgUrl))
                {
                    candidates.Add(new EpgSourceCandidate
                    {
                        Url = cred.DetectedEpgUrl.Trim(),
                        Label = "Stalker portal XMLTV",
                        Method = "detected",
                        Kind = EpgGuideSourceKind.Provider,
                        Priority = priority++
                    });
                }

                candidates.AddRange(EpgDiscoveryHelpers.BuildFallbackCandidates(cred, priority));
            }

            if (candidates.Count == 0)
            {
                throw new EpgUnavailableException("The Stalker portal does not currently advertise an XMLTV guide URL.");
            }

            return await EpgDiscoveryHelpers.FetchGuideSourcesAsync(
                candidates,
                SourceType.Stalker,
                sourceProfileId,
                activeMode,
                cred,
                cred.DetectedEpgUrl,
                _sourceRoutingService,
                activeMode == EpgActiveMode.Manual ? "Manual XMLTV override with fallback sources" : "Stalker XMLTV with fallback sources");
        }
    }

    internal static class EpgDiscoveryHelpers
    {
        internal static EpgActiveMode ResolveActiveMode(SourceCredential credential)
        {
            return credential.EpgMode switch
            {
                EpgActiveMode.Manual => EpgActiveMode.Manual,
                EpgActiveMode.None => EpgActiveMode.None,
                _ => EpgActiveMode.Detected
            };
        }

        internal static IReadOnlyList<EpgSourceCandidate> BuildManualAndFallbackCandidates(SourceCredential credential, int startingPriority)
        {
            var candidates = new List<EpgSourceCandidate>();
            var priority = startingPriority;
            if (!string.IsNullOrWhiteSpace(credential.ManualEpgUrl))
            {
                candidates.Add(new EpgSourceCandidate
                {
                    Url = credential.ManualEpgUrl.Trim(),
                    Label = "Manual XMLTV override",
                    Method = "manual",
                    Kind = EpgGuideSourceKind.Manual,
                    Priority = priority++
                });
            }

            candidates.AddRange(BuildFallbackCandidates(credential, priority));
            return candidates;
        }

        internal static IReadOnlyList<EpgSourceCandidate> BuildFallbackCandidates(SourceCredential credential, int startingPriority)
        {
            var urls = ParseGuideUrlList(credential.FallbackEpgUrls);
            var candidates = new List<EpgSourceCandidate>(urls.Count);
            var priority = startingPriority;
            foreach (var url in urls)
            {
                var kind = EpgPublicGuideCatalog.ClassifyFallbackUrl(url);
                candidates.Add(new EpgSourceCandidate
                {
                    Url = url,
                    Label = EpgPublicGuideCatalog.BuildGuideSourceLabel(url, kind, "Fallback XMLTV"),
                    Method = "fallback",
                    Kind = kind,
                    IsOptional = true,
                    Priority = priority++
                });
            }

            return candidates;
        }

        internal static IReadOnlyList<EpgSourceCandidate> BuildConfiguredEpgUrlFallbackCandidates(SourceCredential credential, int startingPriority)
        {
            if (string.IsNullOrWhiteSpace(credential.ManualEpgUrl))
            {
                return Array.Empty<EpgSourceCandidate>();
            }

            return new[]
            {
                new EpgSourceCandidate
                {
                    Url = credential.ManualEpgUrl.Trim(),
                    Label = "Configured XMLTV fallback",
                    Method = "configured_epg_url",
                    Kind = EpgGuideSourceKind.Manual,
                    IsOptional = true,
                    Priority = startingPriority
                }
            };
        }

        internal static string BuildXtreamProviderXmltvUrl(SourceCredential credential)
        {
            var baseUrl = (credential.Url ?? string.Empty).Trim();
            var authQuery = $"?username={Uri.EscapeDataString(credential.Username ?? string.Empty)}&password={Uri.EscapeDataString(credential.Password ?? string.Empty)}";
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            {
                var path = uri.AbsolutePath.TrimEnd('/');
                if (string.IsNullOrWhiteSpace(path) || path == "/")
                {
                    path = "/xmltv.php";
                }
                else
                {
                    var fileName = Path.GetFileName(path);
                    path = string.Equals(fileName, "get.php", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(fileName, "player_api.php", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(fileName, "xmltv.php", StringComparison.OrdinalIgnoreCase)
                        ? ReplaceFileName(path, "xmltv.php")
                        : $"{path}/xmltv.php";
                }

                var builder = new UriBuilder(uri)
                {
                    Path = path,
                    Query = authQuery.TrimStart('?')
                };
                return builder.Uri.ToString();
            }

            return $"{baseUrl.TrimEnd('/')}/xmltv.php{authQuery}";
        }

        private static string ReplaceFileName(string absolutePath, string newFileName)
        {
            var lastSlashIndex = absolutePath.LastIndexOf('/');
            return lastSlashIndex < 0
                ? newFileName
                : absolutePath[..(lastSlashIndex + 1)] + newFileName;
        }

        internal static async Task<EpgDiscoveryResult> FetchGuideSourcesAsync(
            IReadOnlyList<EpgSourceCandidate> candidates,
            SourceType sourceType,
            int sourceProfileId,
            EpgActiveMode activeMode,
            SourceCredential credential,
            string detectedXmltvUrl,
            ISourceRoutingService sourceRoutingService,
            string description)
        {
            var distinctCandidates = candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Url))
                .GroupBy(candidate => candidate.Url.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(candidate => candidate.Priority).First())
                .OrderBy(candidate => candidate.Priority)
                .ToList();

            if (distinctCandidates.Count == 0)
            {
                throw new EpgUnavailableException("No XMLTV guide URL is configured.");
            }

            var primaryCandidates = distinctCandidates
                .Where(candidate => !candidate.IsOptional)
                .ToList();
            var optionalCandidates = distinctCandidates
                .Where(candidate => candidate.IsOptional)
                .ToList();
            var sources = new List<EpgDiscoveredGuideSource>(distinctCandidates.Count);
            foreach (var candidate in primaryCandidates)
            {
                sources.Add(await FetchGuideSourceCandidateAsync(
                    candidate,
                    sourceType,
                    sourceProfileId,
                    activeMode,
                    credential,
                    sourceRoutingService));
            }

            if (optionalCandidates.Count > 0)
            {
                using var throttler = new SemaphoreSlim(5);
                var optionalTasks = optionalCandidates.Select(async candidate =>
                {
                    await throttler.WaitAsync();
                    try
                    {
                        return await FetchGuideSourceCandidateAsync(
                            candidate,
                            sourceType,
                            sourceProfileId,
                            activeMode,
                            credential,
                            sourceRoutingService);
                    }
                    finally
                    {
                        throttler.Release();
                    }
                });
                sources.AddRange(await Task.WhenAll(optionalTasks));
            }

            sources = sources
                .OrderBy(source => source.Priority)
                .ThenBy(source => source.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sources.Any(source => source.Status == EpgGuideSourceStatus.Ready && !string.IsNullOrWhiteSpace(source.XmlContent)))
            {
                return new EpgDiscoveryResult(sources, description, detectedXmltvUrl, activeMode);
            }

            var lastFailedSource = sources
                .Where(source => !string.IsNullOrWhiteSpace(source.Message))
                .OrderByDescending(source => source.Priority)
                .FirstOrDefault();
            var lastFailure = lastFailedSource?.Message;

            if (activeMode == EpgActiveMode.Manual && lastFailedSource != null)
            {
                throw new EpgFetchException(
                    lastFailure ?? "Manual XMLTV URL did not return usable XMLTV.",
                    lastFailedSource.Url,
                    activeMode);
            }

            throw new EpgFetchException(
                $"Discovered {distinctCandidates.Count} XMLTV URL candidate(s) for the {sourceType} source, but none returned usable XMLTV. last_error={lastFailure ?? "unknown"}",
                lastFailedSource?.Url ?? detectedXmltvUrl,
                activeMode);
        }

        private static async Task<EpgDiscoveredGuideSource> FetchGuideSourceCandidateAsync(
            EpgSourceCandidate candidate,
            SourceType sourceType,
            int sourceProfileId,
            EpgActiveMode activeMode,
            SourceCredential credential,
            ISourceRoutingService sourceRoutingService)
        {
            var source = new EpgDiscoveredGuideSource
            {
                Label = candidate.Label,
                Url = candidate.Url.Trim(),
                Kind = candidate.Kind,
                IsOptional = candidate.IsOptional,
                Priority = candidate.Priority,
                CheckedAtUtc = DateTime.UtcNow
            };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var fetchResult = await ReadXmltvAsync(source.Url, activeMode, credential, sourceRoutingService);
                stopwatch.Stop();
                source.XmlContent = fetchResult.Content;
                source.Status = EpgGuideSourceStatus.Ready;
                source.WasCacheHit = fetchResult.WasCacheHit;
                source.WasContentUnchanged = fetchResult.WasContentUnchanged;
                source.ContentHash = fetchResult.ContentHash;
                source.FetchDurationMs = stopwatch.ElapsedMilliseconds;
                source.Message = fetchResult.WasContentUnchanged
                    ? $"Reused cached XMLTV; remote content was unchanged. {FormatDuration(source.FetchDurationMs)}."
                    : fetchResult.WasCacheHit
                        ? $"Fetched XMLTV from cache. {FormatDuration(source.FetchDurationMs)}."
                        : $"Fetched XMLTV. {FormatDuration(source.FetchDurationMs)}.";
                ImportRuntimeLogger.Log(
                    "EPG DISCOVERY",
                    $"source_profile_id={sourceProfileId}; source_type={sourceType}; mode={activeMode}; source_kind={source.Kind}; discovery_method={FormatDiagnosticValue(candidate.Method)}; xmltv_candidate={FormatDiagnosticValue(source.Url)}; optional={source.IsOptional}; fetch_status=success; cache_hit={source.WasCacheHit}; content_unchanged={source.WasContentUnchanged}; duration_ms={source.FetchDurationMs}");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                source.Status = EpgGuideSourceStatus.Failed;
                source.FetchDurationMs = stopwatch.ElapsedMilliseconds;
                source.Message = TrimDiagnostic(ex.Message, 260);
                ImportRuntimeLogger.Log(
                    "EPG DISCOVERY",
                    $"source_profile_id={sourceProfileId}; source_type={sourceType}; mode={activeMode}; source_kind={source.Kind}; discovery_method={FormatDiagnosticValue(candidate.Method)}; xmltv_candidate={FormatDiagnosticValue(source.Url)}; optional={source.IsOptional}; fetch_status=failed; duration_ms={source.FetchDurationMs}; failure_reason={FormatDiagnosticValue(ex.Message)}");
            }

            return source;
        }

        internal static async Task<EpgXmltvFetchResult> ReadXmltvAsync(
            string location,
            EpgActiveMode activeMode,
            SourceCredential? credential,
            ISourceRoutingService sourceRoutingService)
        {
            try
            {
                var result = await ReadTextResultAsync(location, credential, SourceNetworkPurpose.Guide, sourceRoutingService);
                if (!LooksLikeXmltv(result.Content))
                {
                    throw new EpgFetchException("XMLTV URL did not return XMLTV content.", location, activeMode);
                }

                return new EpgXmltvFetchResult(
                    result.Content,
                    result.WasCacheHit,
                    result.WasContentUnchanged,
                    result.ContentHash);
            }
            catch (EpgFetchException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new EpgFetchException(ex.Message, location, activeMode);
            }
        }

        internal static async Task<string> ReadTextAsync(
            string location,
            SourceCredential? credential,
            SourceNetworkPurpose purpose,
            ISourceRoutingService sourceRoutingService)
        {
            var result = await ReadTextResultAsync(location, credential, purpose, sourceRoutingService);
            return result.Content;
        }

        private static async Task<EpgTextReadResult> ReadTextResultAsync(
            string location,
            SourceCredential? credential,
            SourceNetworkPurpose purpose,
            ISourceRoutingService sourceRoutingService)
        {
            if (location.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var client = sourceRoutingService.CreateHttpClient(credential, purpose, TimeSpan.FromSeconds(60));
                if (purpose == SourceNetworkPurpose.Guide)
                {
                    return await ReadHttpTextWithCacheAsync(client, location);
                }

                var bytes = await client.GetByteArrayAsync(location);
                return EpgTextReadResult.FromBytes(bytes, location, wasCacheHit: false, wasContentUnchanged: false);
            }

            if (!File.Exists(location))
            {
                throw new FileNotFoundException("EPG or playlist file was not found.", location);
            }

            var fileBytes = await File.ReadAllBytesAsync(location);
            return EpgTextReadResult.FromBytes(fileBytes, location, wasCacheHit: false, wasContentUnchanged: false);
        }

        internal static bool LooksLikeXmltv(string content)
        {
            return !string.IsNullOrWhiteSpace(content) &&
                   content.IndexOf("<tv", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static async Task<EpgTextReadResult> ReadHttpTextWithCacheAsync(HttpClient client, string location)
        {
            var cachePaths = EpgGuideHttpCachePaths.Create(location);
            var cachedMetadata = await TryReadCacheMetadataAsync(cachePaths.MetadataPath);
            using var request = new HttpRequestMessage(HttpMethod.Get, location);
            if (cachedMetadata != null)
            {
                if (!string.IsNullOrWhiteSpace(cachedMetadata.ETag))
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", cachedMetadata.ETag);
                }

                if (!string.IsNullOrWhiteSpace(cachedMetadata.LastModified))
                {
                    request.Headers.TryAddWithoutValidation("If-Modified-Since", cachedMetadata.LastModified);
                }
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == HttpStatusCode.NotModified &&
                cachedMetadata != null &&
                File.Exists(cachePaths.BodyPath))
            {
                var cachedBytes = await File.ReadAllBytesAsync(cachePaths.BodyPath);
                return EpgTextReadResult.FromBytes(
                    cachedBytes,
                    location,
                    wasCacheHit: true,
                    wasContentUnchanged: true,
                    cachedMetadata.ContentHash);
            }

            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var contentHash = ComputeSha256(bytes);
            var wasContentUnchanged = cachedMetadata != null &&
                                      !string.IsNullOrWhiteSpace(cachedMetadata.ContentHash) &&
                                      string.Equals(cachedMetadata.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase);

            await WriteCacheEntryAsync(
                cachePaths,
                new EpgGuideHttpCacheMetadata
                {
                    Url = location,
                    ETag = response.Headers.ETag?.ToString() ?? string.Empty,
                    LastModified = response.Content.Headers.LastModified?.ToString("R") ?? string.Empty,
                    ContentHash = contentHash,
                    SavedAtUtc = DateTime.UtcNow
                },
                bytes);

            return EpgTextReadResult.FromBytes(
                bytes,
                location,
                wasCacheHit: false,
                wasContentUnchanged: wasContentUnchanged,
                contentHash);
        }

        private static async Task<EpgGuideHttpCacheMetadata?> TryReadCacheMetadataAsync(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<EpgGuideHttpCacheMetadata>(json);
            }
            catch
            {
                return null;
            }
        }

        private static async Task WriteCacheEntryAsync(
            EpgGuideHttpCachePaths cachePaths,
            EpgGuideHttpCacheMetadata metadata,
            byte[] bytes)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePaths.BodyPath)!);
                await File.WriteAllBytesAsync(cachePaths.BodyPath, bytes);
                await File.WriteAllTextAsync(cachePaths.MetadataPath, JsonSerializer.Serialize(metadata));
            }
            catch (Exception ex)
            {
                RuntimeEventLogger.Log("EPG-CACHE", ex, $"cache_write url={locationForLog(metadata.Url)}");
            }

            static string locationForLog(string value)
            {
                return string.IsNullOrWhiteSpace(value) ? "(empty)" : EpgDiagnosticFormatter.RedactUrl(value);
            }
        }

        private static IReadOnlyList<string> ParseGuideUrlList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            return value
                .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string DecodeMaybeGzip(byte[] bytes, string location)
        {
            if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
            {
                using var input = new MemoryStream(bytes);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                gzip.CopyTo(output);
                return DecodeUtf8WithBomFallback(output.ToArray());
            }

            return DecodeUtf8WithBomFallback(bytes);
        }

        private static string ComputeSha256(byte[] bytes)
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }

        private static string FormatDuration(long durationMs)
        {
            return durationMs >= 1000
                ? $"{durationMs / 1000d:0.0}s"
                : $"{durationMs:N0}ms";
        }

        private static string DecodeUtf8WithBomFallback(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            return Encoding.UTF8.GetString(bytes);
        }

        private static string TrimDiagnostic(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
        }

        private static string FormatDiagnosticValue(string value)
        {
            return EpgDiagnosticFormatter.Format(value);
        }

        internal sealed record EpgXmltvFetchResult(
            string Content,
            bool WasCacheHit,
            bool WasContentUnchanged,
            string ContentHash);

        private sealed record EpgTextReadResult(
            string Content,
            bool WasCacheHit,
            bool WasContentUnchanged,
            string ContentHash)
        {
            public static EpgTextReadResult FromBytes(
                byte[] bytes,
                string location,
                bool wasCacheHit,
                bool wasContentUnchanged,
                string? knownContentHash = null)
            {
                var contentHash = string.IsNullOrWhiteSpace(knownContentHash)
                    ? ComputeSha256(bytes)
                    : knownContentHash;
                return new EpgTextReadResult(
                    DecodeMaybeGzip(bytes, location),
                    wasCacheHit,
                    wasContentUnchanged,
                    contentHash);
            }
        }

        private sealed class EpgGuideHttpCacheMetadata
        {
            public string Url { get; set; } = string.Empty;
            public string ETag { get; set; } = string.Empty;
            public string LastModified { get; set; } = string.Empty;
            public string ContentHash { get; set; } = string.Empty;
            public DateTime SavedAtUtc { get; set; }
        }

        private sealed record EpgGuideHttpCachePaths(string BodyPath, string MetadataPath)
        {
            public static EpgGuideHttpCachePaths Create(string url)
            {
                var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.Trim().ToLowerInvariant()))).ToLowerInvariant();
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Kroira",
                    "epg-cache");
                return new EpgGuideHttpCachePaths(
                    Path.Combine(root, $"{key}.bin"),
                    Path.Combine(root, $"{key}.json"));
            }
        }
    }
}
