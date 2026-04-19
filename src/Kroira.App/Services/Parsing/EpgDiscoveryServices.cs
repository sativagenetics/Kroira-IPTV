using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
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
        private static readonly Regex AttributeRegex = new(
            @"(?<key>[A-Za-z0-9_-]+)\s*=\s*(?:""(?<quoted>[^""]*)""|(?<bare>\S+))",
            RegexOptions.Compiled);

        private static readonly string[] EpgAttributeNames =
        {
            "x-tvg-url",
            "url-tvg",
            "tvg-url",
            "epg-url",
            "xmltv"
        };

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
            var embeddedEpgUrl = DiscoverEmbeddedEpgUrl(playlistContent, cred.Url);
            if (string.IsNullOrWhiteSpace(embeddedEpgUrl))
            {
                throw new Exception("No XMLTV EPG URL was configured or found in the M3U playlist metadata.");
            }

            var embeddedXml = await ReadTextAsync(embeddedEpgUrl);
            return new EpgDiscoveryResult(embeddedXml, "M3U embedded XMLTV metadata");
        }

        private static string DiscoverEmbeddedEpgUrl(string playlistContent, string playlistLocation)
        {
            var header = playlistContent
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(header))
            {
                return string.Empty;
            }

            var attributes = ParseAttributes(header);
            foreach (var attrName in EpgAttributeNames)
            {
                if (attributes.TryGetValue(attrName, out var epgUrl) && !string.IsNullOrWhiteSpace(epgUrl))
                {
                    return ResolveUrl(TakeFirstEpgUrl(epgUrl), playlistLocation);
                }
            }

            return string.Empty;
        }

        private static Dictionary<string, string> ParseAttributes(string line)
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in AttributeRegex.Matches(line))
            {
                var key = match.Groups["key"].Value.Trim();
                var value = match.Groups["quoted"].Success
                    ? match.Groups["quoted"].Value
                    : match.Groups["bare"].Value;

                if (!string.IsNullOrWhiteSpace(key))
                {
                    attributes[key] = value.Trim();
                }
            }

            return attributes;
        }

        private static string ResolveUrl(string epgUrl, string playlistLocation)
        {
            if (Uri.TryCreate(epgUrl, UriKind.Absolute, out _))
            {
                return epgUrl;
            }

            if (Uri.TryCreate(playlistLocation, UriKind.Absolute, out var playlistUri))
            {
                return new Uri(playlistUri, epgUrl).ToString();
            }

            var playlistDirectory = Path.GetDirectoryName(playlistLocation);
            if (!string.IsNullOrWhiteSpace(playlistDirectory))
            {
                return Path.GetFullPath(Path.Combine(playlistDirectory, epgUrl));
            }

            return epgUrl;
        }

        private static string TakeFirstEpgUrl(string epgUrl)
        {
            return epgUrl
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(url => url.Trim())
                .FirstOrDefault() ?? string.Empty;
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

            string baseUrl = cred.Url.TrimEnd('/');
            string authQuery = $"?username={Uri.EscapeDataString(cred.Username)}&password={Uri.EscapeDataString(cred.Password)}";
            string providerGuideUrl = $"{baseUrl}/xmltv.php{authQuery}";

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
