using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

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
            string xmlContent,
            string description,
            string detectedXmltvUrl,
            string activeXmltvUrl,
            EpgActiveMode activeMode)
        {
            XmlContent = xmlContent;
            Description = description;
            DetectedXmltvUrl = detectedXmltvUrl ?? string.Empty;
            ActiveXmltvUrl = activeXmltvUrl ?? string.Empty;
            ActiveMode = activeMode;
        }

        public string XmlContent { get; }
        public string Description { get; }
        public string DetectedXmltvUrl { get; }
        public string ActiveXmltvUrl { get; }
        public EpgActiveMode ActiveMode { get; }
    }

    public interface IEpgSourceDiscoveryService
    {
        SourceType SourceType { get; }
        Task<EpgDiscoveryResult> DiscoverAsync(AppDbContext db, int sourceProfileId);
    }

    public sealed class M3uEpgDiscoveryService : IEpgSourceDiscoveryService
    {
        public SourceType SourceType => SourceType.M3U;

        public async Task<EpgDiscoveryResult> DiscoverAsync(AppDbContext db, int sourceProfileId)
        {
            var cred = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == sourceProfileId);
            if (cred == null || string.IsNullOrWhiteSpace(cred.Url))
            {
                throw new Exception("M3U source URL or path is empty.");
            }

            var playlistContent = await EpgDiscoveryHelpers.ReadTextAsync(cred.Url);
            var headerMetadata = M3uMetadataParser.ParseHeaderMetadata(playlistContent, cred.Url);
            LogHeaderDiscovery(sourceProfileId, headerMetadata);

            var discoveryCandidates = new List<(string Url, string Description, string Method)>();
            foreach (var candidateUrl in headerMetadata.XmltvUrls)
            {
                discoveryCandidates.Add((candidateUrl, $"M3U embedded XMLTV metadata ({candidateUrl})", "header"));
            }

            if (discoveryCandidates.Count == 0)
            {
                var derivedUrl = M3uMetadataParser.TryBuildXtreamXmltvUrl(cred.Url);
                if (!string.IsNullOrWhiteSpace(derivedUrl))
                {
                    discoveryCandidates.Add((derivedUrl, $"Derived Xtream XMLTV from M3U playlist URL ({derivedUrl})", "xtream_playlist_url"));
                    ImportRuntimeLogger.Log(
                        "EPG DISCOVERY",
                        $"source_profile_id={sourceProfileId}; source_type=M3U; xmltv_url_found=false; fallback_method=xtream_playlist_url; xmltv_candidate={FormatDiagnosticValue(derivedUrl)}");
                }
            }

            cred.DetectedEpgUrl = discoveryCandidates.FirstOrDefault().Url ?? string.Empty;

            var activeMode = EpgDiscoveryHelpers.ResolveActiveMode(cred);
            if (activeMode == EpgActiveMode.Manual)
            {
                var manualUrl = cred.ManualEpgUrl;
                if (string.IsNullOrWhiteSpace(manualUrl))
                {
                    throw new EpgUnavailableException("Manual XMLTV URL is not configured.");
                }

                var xmlContent = await EpgDiscoveryHelpers.ReadXmltvAsync(manualUrl, activeMode);
                return new EpgDiscoveryResult(
                    xmlContent,
                    "Manual XMLTV override",
                    cred.DetectedEpgUrl,
                    manualUrl,
                    activeMode);
            }

            if (discoveryCandidates.Count == 0)
            {
                throw new EpgUnavailableException("Playlist does not advertise an XMLTV guide URL");
            }

            Exception? lastFailure = null;
            foreach (var candidate in discoveryCandidates)
            {
                try
                {
                    var xmlContent = await EpgDiscoveryHelpers.ReadXmltvAsync(candidate.Url, EpgActiveMode.Detected);
                    ImportRuntimeLogger.Log(
                        "EPG DISCOVERY",
                        $"source_profile_id={sourceProfileId}; source_type=M3U; mode=detected; discovery_method={candidate.Method}; xmltv_candidate={FormatDiagnosticValue(candidate.Url)}; fetch_status=success");

                    return new EpgDiscoveryResult(
                        xmlContent,
                        candidate.Description,
                        cred.DetectedEpgUrl,
                        candidate.Url,
                        EpgActiveMode.Detected);
                }
                catch (Exception ex)
                {
                    lastFailure = ex;
                    ImportRuntimeLogger.Log(
                        "EPG DISCOVERY",
                        $"source_profile_id={sourceProfileId}; source_type=M3U; mode=detected; discovery_method={candidate.Method}; xmltv_candidate={FormatDiagnosticValue(candidate.Url)}; fetch_status=failed; failure_reason={FormatDiagnosticValue(ex.Message)}");
                }
            }

            throw new EpgFetchException(
                $"Discovered {discoveryCandidates.Count} XMLTV URL candidate(s) for the M3U source, but none returned usable XMLTV. last_error={lastFailure?.Message ?? "unknown"}",
                cred.DetectedEpgUrl,
                EpgActiveMode.Detected);
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
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return $"\"{value.Replace("\"", "'")}\"";
        }
    }

    public sealed class XtreamEpgDiscoveryService : IEpgSourceDiscoveryService
    {
        public SourceType SourceType => SourceType.Xtream;

        public async Task<EpgDiscoveryResult> DiscoverAsync(AppDbContext db, int sourceProfileId)
        {
            var cred = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == sourceProfileId);
            if (cred == null || string.IsNullOrWhiteSpace(cred.Url) || string.IsNullOrWhiteSpace(cred.Username))
            {
                throw new Exception("Xtream credentials are incomplete.");
            }

            var baseUrl = cred.Url.TrimEnd('/');
            var authQuery = $"?username={Uri.EscapeDataString(cred.Username)}&password={Uri.EscapeDataString(cred.Password)}";
            var providerGuideUrl = $"{baseUrl}/xmltv.php{authQuery}";
            cred.DetectedEpgUrl = providerGuideUrl;

            var activeMode = EpgDiscoveryHelpers.ResolveActiveMode(cred);
            if (activeMode == EpgActiveMode.Manual)
            {
                var manualUrl = cred.ManualEpgUrl;
                if (string.IsNullOrWhiteSpace(manualUrl))
                {
                    throw new EpgUnavailableException("Manual XMLTV URL is not configured.");
                }

                var xml = await EpgDiscoveryHelpers.ReadXmltvAsync(manualUrl, activeMode);
                return new EpgDiscoveryResult(
                    xml,
                    "Manual XMLTV override",
                    providerGuideUrl,
                    manualUrl,
                    activeMode);
            }

            try
            {
                var xml = await EpgDiscoveryHelpers.ReadXmltvAsync(providerGuideUrl, EpgActiveMode.Detected);
                ImportRuntimeLogger.Log(
                    "EPG DISCOVERY",
                    $"source_profile_id={sourceProfileId}; source_type=Xtream; mode=detected; xmltv_candidate={FormatDiagnosticValue(providerGuideUrl)}; fetch_status=success");

                return new EpgDiscoveryResult(
                    xml,
                    "Xtream provider XMLTV guide",
                    providerGuideUrl,
                    providerGuideUrl,
                    EpgActiveMode.Detected);
            }
            catch (Exception ex)
            {
                ImportRuntimeLogger.Log(
                    "EPG DISCOVERY",
                    $"source_profile_id={sourceProfileId}; source_type=Xtream; mode=detected; xmltv_candidate={FormatDiagnosticValue(providerGuideUrl)}; fetch_status=failed; failure_reason={FormatDiagnosticValue(ex.Message)}");

                throw new EpgFetchException(
                    $"Xtream provider XMLTV fetch failed: {ex.Message}",
                    providerGuideUrl,
                    EpgActiveMode.Detected);
            }
        }

        private static string FormatDiagnosticValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\"\"";
            }

            return $"\"{value.Replace("\"", "'")}\"";
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

        internal static async Task<string> ReadXmltvAsync(string location, EpgActiveMode activeMode)
        {
            try
            {
                var content = await ReadTextAsync(location);
                if (!LooksLikeXmltv(content))
                {
                    throw new EpgFetchException("XMLTV URL did not return XMLTV content.", location, activeMode);
                }

                return content;
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

        internal static async Task<string> ReadTextAsync(string location)
        {
            if (location.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                return await client.GetStringAsync(location);
            }

            if (!File.Exists(location))
            {
                throw new FileNotFoundException("EPG or playlist file was not found.", location);
            }

            return await File.ReadAllTextAsync(location);
        }

        internal static bool LooksLikeXmltv(string content)
        {
            return !string.IsNullOrWhiteSpace(content) &&
                   content.IndexOf("<tv", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
