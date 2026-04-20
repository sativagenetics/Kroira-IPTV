using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services.Parsing
{
    public sealed class EpgDiscoveryResult
    {
        public EpgDiscoveryResult(string xmlContent, string description)
        {
            XmlContent = xmlContent;
            Description = description;
        }

        public string XmlContent { get; }
        public string Description { get; }
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

            if (!string.IsNullOrWhiteSpace(cred.EpgUrl))
            {
                var explicitXml = await ReadTextAsync(cred.EpgUrl);
                return new EpgDiscoveryResult(explicitXml, "M3U configured XMLTV URL");
            }

            var playlistContent = await ReadTextAsync(cred.Url);
            var headerMetadata = M3uMetadataParser.ParseHeaderMetadata(playlistContent, cred.Url);
            LogHeaderDiscovery(sourceProfileId, headerMetadata);

            if (headerMetadata.XmltvUrls.Count == 0)
            {
                throw new Exception(
                    $"No XMLTV EPG URL was configured or found in the M3U playlist metadata. header_preview={headerMetadata.RawHeaderPreview}; header_attributes={DescribeHeaderAttributes(headerMetadata)}");
            }

            Exception lastFailure = null;
            foreach (var candidateUrl in headerMetadata.XmltvUrls)
            {
                try
                {
                    var xmlContent = await ReadTextAsync(candidateUrl);
                    if (!LooksLikeXmltv(xmlContent))
                    {
                        throw new Exception("Discovered XMLTV URL did not return XMLTV content.");
                    }

                    ImportRuntimeLogger.Log(
                        "M3U EPG",
                        $"source_profile_id={sourceProfileId}; xmltv_candidate={FormatDiagnosticValue(candidateUrl)}; xmltv_candidate_status=success");

                    return new EpgDiscoveryResult(xmlContent, $"M3U embedded XMLTV metadata ({candidateUrl})");
                }
                catch (Exception ex)
                {
                    lastFailure = ex;
                    ImportRuntimeLogger.Log(
                        "M3U EPG",
                        $"source_profile_id={sourceProfileId}; xmltv_candidate={FormatDiagnosticValue(candidateUrl)}; xmltv_candidate_status=failed; failure_reason={FormatDiagnosticValue(ex.Message)}");
                }
            }

            throw new Exception(
                $"Discovered {headerMetadata.XmltvUrls.Count} XMLTV URL(s) in the M3U header, but none returned usable XMLTV. last_error={lastFailure?.Message ?? "unknown"}");
        }

        private static void LogHeaderDiscovery(int sourceProfileId, M3uHeaderMetadata headerMetadata)
        {
            ImportRuntimeLogger.Log(
                "M3U EPG",
                $"source_profile_id={sourceProfileId}; xmltv_url_found={(headerMetadata.XmltvUrls.Count > 0 ? "true" : "false")}; xmltv_url_value={FormatDiagnosticValue(string.Join(" | ", headerMetadata.XmltvUrls))}; header_preview={FormatDiagnosticValue(headerMetadata.RawHeaderPreview)}; header_attributes={FormatDiagnosticValue(DescribeHeaderAttributes(headerMetadata))}");
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

        private static bool LooksLikeXmltv(string content)
        {
            return !string.IsNullOrWhiteSpace(content) &&
                   content.IndexOf("<tv", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static async Task<string> ReadTextAsync(string location)
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

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            try
            {
                var xml = await client.GetStringAsync(providerGuideUrl);
                if (!LooksLikeXmltv(xml))
                {
                    throw new Exception("Xtream provider guide did not return XMLTV content.");
                }

                return new EpgDiscoveryResult(xml, "Xtream provider XMLTV guide");
            }
            catch when (!string.IsNullOrWhiteSpace(cred.EpgUrl))
            {
                var fallbackXml = await client.GetStringAsync(cred.EpgUrl);
                return new EpgDiscoveryResult(fallbackXml, "Xtream configured XMLTV fallback");
            }
        }

        private static bool LooksLikeXmltv(string content)
        {
            return !string.IsNullOrWhiteSpace(content) &&
                   content.IndexOf("<tv", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
