#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Models;

namespace Kroira.App.Services
{
    public interface IStalkerPortalClient
    {
        Task<StalkerPortalCatalog> LoadCatalogAsync(SourceCredential credential, CancellationToken cancellationToken = default);
        Task<StalkerStreamResolution> ResolveStreamAsync(
            SourceCredential credential,
            StalkerStreamLocator locator,
            SourceNetworkPurpose purpose,
            CancellationToken cancellationToken = default);
    }

    public sealed class StalkerPortalClient : IStalkerPortalClient
    {
        internal const string DefaultPortalName = "Stalker Portal";
        private readonly ISourceRoutingService _sourceRoutingService;

        public StalkerPortalClient(ISourceRoutingService sourceRoutingService)
        {
            _sourceRoutingService = sourceRoutingService;
        }

        public async Task<StalkerPortalCatalog> LoadCatalogAsync(SourceCredential credential, CancellationToken cancellationToken = default)
        {
            var session = await CreateSessionAsync(credential, SourceNetworkPurpose.Import, cancellationToken);
            var warnings = new List<string>();

            var liveCategories = await LoadCategoriesAsync(session, "itv", cancellationToken);
            var liveChannels = await LoadLiveChannelsAsync(session, cancellationToken);

            IReadOnlyList<StalkerCategory> movieCategories = Array.Empty<StalkerCategory>();
            IReadOnlyList<StalkerMovieItem> movies = Array.Empty<StalkerMovieItem>();
            try
            {
                movieCategories = await LoadCategoriesAsync(session, "vod", cancellationToken);
                movies = await LoadMoviesAsync(session, movieCategories, cancellationToken);
            }
            catch (Exception ex)
            {
                warnings.Add($"Movies could not be loaded: {ex.Message}");
            }

            IReadOnlyList<StalkerCategory> seriesCategories = Array.Empty<StalkerCategory>();
            IReadOnlyList<StalkerSeriesItem> series = Array.Empty<StalkerSeriesItem>();
            try
            {
                seriesCategories = await LoadCategoriesAsync(session, "series", cancellationToken);
                series = await LoadSeriesAsync(session, seriesCategories, cancellationToken);
            }
            catch (Exception ex)
            {
                warnings.Add($"Series could not be loaded: {ex.Message}");
            }

            return new StalkerPortalCatalog
            {
                PortalName = session.PortalName,
                PortalVersion = session.PortalVersion,
                ProfileName = session.ProfileName,
                ProfileId = session.ProfileId,
                DiscoveredApiUrl = session.ApiUrl,
                MacAddress = session.MacAddress,
                DeviceId = session.DeviceId,
                SerialNumber = session.SerialNumber,
                Locale = session.Locale,
                Timezone = session.Timezone,
                LiveCategories = liveCategories,
                LiveChannels = liveChannels,
                MovieCategories = movieCategories,
                Movies = movies,
                SeriesCategories = seriesCategories,
                Series = series,
                Warnings = warnings,
                SupportsLive = liveChannels.Count > 0 || liveCategories.Count > 0,
                SupportsMovies = movies.Count > 0 || movieCategories.Count > 0,
                SupportsSeries = series.Count > 0 || seriesCategories.Count > 0,
                LastHandshakeAtUtc = session.HandshakeAtUtc,
                LastProfileSyncAtUtc = session.ProfileFetchedAtUtc
            };
        }

        public async Task<StalkerStreamResolution> ResolveStreamAsync(
            SourceCredential credential,
            StalkerStreamLocator locator,
            SourceNetworkPurpose purpose,
            CancellationToken cancellationToken = default)
        {
            var session = await CreateSessionAsync(credential, purpose, cancellationToken);
            var response = await SendPortalRequestAsync(
                session,
                ResolveActionType(locator.ResourceType),
                "create_link",
                BuildCreateLinkParameters(locator),
                cancellationToken);

            var payload = ParseEnvelope(response);
            var resolvedUrl = NormalizeResolvedLink(
                GetFirstString(payload, "cmd", "url", "link"),
                session.ApiUrl);
            if (string.IsNullOrWhiteSpace(resolvedUrl))
            {
                throw new InvalidOperationException("The portal did not return a playable stream URL.");
            }

            return new StalkerStreamResolution(
                resolvedUrl,
                session.ApiUrl,
                session.Routing);
        }

        private async Task<StalkerPortalSession> CreateSessionAsync(
            SourceCredential credential,
            SourceNetworkPurpose purpose,
            CancellationToken cancellationToken)
        {
            if (credential == null || string.IsNullOrWhiteSpace(credential.Url))
            {
                throw new InvalidOperationException("Stalker portal URL is missing.");
            }

            if (string.IsNullOrWhiteSpace(credential.StalkerMacAddress))
            {
                throw new InvalidOperationException("Stalker MAC address is required.");
            }

            var profile = NormalizeCredential(credential);
            var routing = _sourceRoutingService.Resolve(profile, purpose);
            using var client = _sourceRoutingService.CreateHttpClient(profile, purpose, TimeSpan.FromSeconds(30));

            Exception? lastFailure = null;
            foreach (var apiUrl in DiscoverApiUrls(profile))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var handshake = await SendPortalRequestAsync(
                        client,
                        apiUrl,
                        profile,
                        null,
                        "stb",
                        "handshake",
                        new Dictionary<string, string>(),
                        cancellationToken);
                    var handshakePayload = ParseEnvelope(handshake);
                    var token = GetFirstString(handshakePayload, "token", "access_token");
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        throw new InvalidOperationException("Handshake did not return a session token.");
                    }

                    JsonElement? profilePayload = null;
                    DateTime? profileFetchedAtUtc = null;
                    try
                    {
                        var profileResponse = await SendPortalRequestAsync(
                            client,
                            apiUrl,
                            profile,
                            token,
                            "stb",
                            "get_profile",
                            BuildProfileParameters(profile),
                            cancellationToken);
                        profilePayload = ParseEnvelope(profileResponse);
                        profileFetchedAtUtc = DateTime.UtcNow;
                    }
                    catch
                    {
                    }

                    return new StalkerPortalSession(
                        profile,
                        apiUrl,
                        token,
                        purpose,
                        routing,
                        profile.StalkerMacAddress,
                        profile.StalkerDeviceId,
                        profile.StalkerSerialNumber,
                        profile.StalkerLocale,
                        profile.StalkerTimezone,
                        ResolvePortalName(profilePayload),
                        ResolvePortalVersion(profilePayload),
                        ResolveProfileName(profilePayload),
                        ResolveProfileId(profilePayload),
                        DateTime.UtcNow,
                        profileFetchedAtUtc);
                }
                catch (Exception ex)
                {
                    lastFailure = ex;
                }
            }

            throw new InvalidOperationException(lastFailure?.Message ?? "The portal handshake could not be completed.");
        }

        private async Task<IReadOnlyList<StalkerCategory>> LoadCategoriesAsync(
            StalkerPortalSession session,
            string type,
            CancellationToken cancellationToken)
        {
            var response = await SendPortalRequestAsync(session, type, "get_genres", new Dictionary<string, string>(), cancellationToken);
            var payload = ParseEnvelope(response);
            return ExtractItems(payload)
                .Select(item => new StalkerCategory(
                    GetFirstString(item, "id", "genre_id", "category_id"),
                    GetFirstString(item, "title", "name", "genre_name", "category_name")))
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First() with
                {
                    Name = string.IsNullOrWhiteSpace(group.First().Name) ? "Uncategorized" : group.First().Name.Trim()
                })
                .ToList();
        }

        private async Task<IReadOnlyList<StalkerLiveChannel>> LoadLiveChannelsAsync(
            StalkerPortalSession session,
            CancellationToken cancellationToken)
        {
            var response = await SendPortalRequestAsync(session, "itv", "get_all_channels", new Dictionary<string, string>(), cancellationToken);
            var payload = ParseEnvelope(response);
            return ExtractItems(payload)
                .Select(item => new StalkerLiveChannel(
                    GetFirstString(item, "id", "ch_id", "stream_id"),
                    GetFirstString(item, "name", "title"),
                    GetFirstString(item, "tv_genre_id", "genre_id", "category_id"),
                    GetFirstString(item, "logo", "screenshot_uri", "stream_icon"),
                    GetFirstString(item, "xmltv_id", "epg_id", "tvg_id"),
                    GetFirstString(item, "cmd", "stream_url")))
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) &&
                               !string.IsNullOrWhiteSpace(item.Name) &&
                               !string.IsNullOrWhiteSpace(item.Command))
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private async Task<IReadOnlyList<StalkerMovieItem>> LoadMoviesAsync(
            StalkerPortalSession session,
            IReadOnlyList<StalkerCategory> categories,
            CancellationToken cancellationToken)
        {
            var results = new List<StalkerMovieItem>();
            foreach (var item in await LoadOrderedItemsAsync(session, "vod", categories, cancellationToken))
            {
                var id = GetFirstString(item, "id", "movie_id", "stream_id");
                var title = GetFirstString(item, "name", "title");
                var command = GetFirstString(item, "cmd", "stream_url");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(command))
                {
                    continue;
                }

                results.Add(new StalkerMovieItem(
                    id,
                    title,
                    GetFirstString(item, "genre_id", "category_id"),
                    GetFirstString(item, "screenshot_uri", "logo", "stream_icon"),
                    command,
                    GetFirstString(item, "description", "plot"),
                    GetFirstString(item, "year", "release_date")));
            }

            return results
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private async Task<IReadOnlyList<StalkerSeriesItem>> LoadSeriesAsync(
            StalkerPortalSession session,
            IReadOnlyList<StalkerCategory> categories,
            CancellationToken cancellationToken)
        {
            var orderedItems = await LoadOrderedItemsAsync(session, "series", categories, cancellationToken);
            var output = new List<StalkerSeriesItem>();
            foreach (var item in orderedItems)
            {
                var id = GetFirstString(item, "id", "movie_id", "series_id");
                var title = GetFirstString(item, "name", "title");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var details = await TryLoadSeriesDetailsAsync(session, id, cancellationToken);
                output.Add(new StalkerSeriesItem(
                    id,
                    title,
                    GetFirstString(item, "genre_id", "category_id"),
                    GetFirstString(item, "screenshot_uri", "logo", "stream_icon"),
                    details));
            }

            return output;
        }

        private async Task<IReadOnlyList<StalkerSeasonItem>> TryLoadSeriesDetailsAsync(
            StalkerPortalSession session,
            string seriesId,
            CancellationToken cancellationToken)
        {
            foreach (var action in new[] { "get_short_info", "get_info" })
            {
                try
                {
                    var response = await SendPortalRequestAsync(
                        session,
                        "series",
                        action,
                        new Dictionary<string, string>
                        {
                            ["movie_id"] = seriesId,
                            ["id"] = seriesId
                        },
                        cancellationToken);
                    var payload = ParseEnvelope(response);
                    var seasons = ParseSeriesSeasons(payload);
                    if (seasons.Count > 0)
                    {
                        return seasons;
                    }
                }
                catch
                {
                }
            }

            return Array.Empty<StalkerSeasonItem>();
        }

        private async Task<IReadOnlyList<JsonElement>> LoadOrderedItemsAsync(
            StalkerPortalSession session,
            string type,
            IReadOnlyList<StalkerCategory> categories,
            CancellationToken cancellationToken)
        {
            var categoryIds = categories.Count == 0
                ? new[] { string.Empty }
                : categories.Select(category => category.Id).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase);
            var results = new List<JsonElement>();
            foreach (var categoryId in categoryIds)
            {
                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var page = 1; page <= 12; page++)
                {
                    var response = await SendPortalRequestAsync(
                        session,
                        type,
                        "get_ordered_list",
                        new Dictionary<string, string>
                        {
                            ["genre"] = categoryId,
                            ["category"] = categoryId,
                            ["p"] = page.ToString(CultureInfo.InvariantCulture),
                            ["sortby"] = "number",
                            ["fav"] = "0",
                            ["hd"] = "0"
                        },
                        cancellationToken);
                    var payload = ParseEnvelope(response);
                    var pageItems = ExtractItems(payload).ToList();
                    if (pageItems.Count == 0)
                    {
                        break;
                    }

                    var anyNew = false;
                    foreach (var item in pageItems)
                    {
                        var id = GetFirstString(item, "id", "movie_id", "series_id", "stream_id");
                        if (!string.IsNullOrWhiteSpace(id) && !seenIds.Add(id))
                        {
                            continue;
                        }

                        results.Add(CloneElement(item));
                        anyNew = true;
                    }

                    if (!anyNew || !HasMorePages(payload, pageItems.Count, page))
                    {
                        break;
                    }
                }
            }

            return results;
        }

        private static List<StalkerSeasonItem> ParseSeriesSeasons(JsonElement payload)
        {
            var results = new List<StalkerSeasonItem>();
            foreach (var episodesContainer in EnumerateEpisodeContainers(payload))
            {
                var seasonNumber = ResolveSeasonNumber(episodesContainer.Key);
                var episodes = new List<StalkerEpisodeItem>();
                foreach (var episodeElement in ExtractItems(episodesContainer.Value))
                {
                    var id = GetFirstString(episodeElement, "id", "movie_id", "stream_id");
                    var title = GetFirstString(episodeElement, "name", "title");
                    var command = GetFirstString(episodeElement, "cmd", "stream_url");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(command))
                    {
                        continue;
                    }

                    var episodeNumber = ParseInt(GetFirstString(episodeElement, "series_number", "episode_num", "number"));
                    episodes.Add(new StalkerEpisodeItem(
                        id,
                        string.IsNullOrWhiteSpace(title) ? $"Episode {Math.Max(episodeNumber, 1)}" : title,
                        Math.Max(episodeNumber, 1),
                        command));
                }

                if (episodes.Count > 0)
                {
                    results.Add(new StalkerSeasonItem(Math.Max(seasonNumber, 1), episodes));
                }
            }

            return results
                .OrderBy(item => item.SeasonNumber)
                .ToList();
        }

        private async Task<string> SendPortalRequestAsync(
            StalkerPortalSession session,
            string type,
            string action,
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken cancellationToken)
        {
            using var client = _sourceRoutingService.CreateHttpClient(session.Credential, session.RequestPurpose, TimeSpan.FromSeconds(30));
            return await SendPortalRequestAsync(
                client,
                session.ApiUrl,
                session.Credential,
                session.Token,
                type,
                action,
                parameters,
                cancellationToken);
        }

        private static async Task<string> SendPortalRequestAsync(
            HttpClient client,
            string apiUrl,
            SourceCredential credential,
            string? token,
            string type,
            string action,
            IReadOnlyDictionary<string, string> parameters,
            CancellationToken cancellationToken)
        {
            var requestUri = BuildRequestUri(apiUrl, type, action, parameters);
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            ApplyPortalHeaders(request, credential, token);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        private static Uri BuildRequestUri(
            string apiUrl,
            string type,
            string action,
            IReadOnlyDictionary<string, string> parameters)
        {
            var queryParts = new List<string>
            {
                $"type={Uri.EscapeDataString(type)}",
                $"action={Uri.EscapeDataString(action)}",
                "JsHttpRequest=1-xml"
            };

            foreach (var parameter in parameters)
            {
                if (string.IsNullOrWhiteSpace(parameter.Value))
                {
                    continue;
                }

                queryParts.Add($"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}");
            }

            var separator = apiUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return new Uri($"{apiUrl}{separator}{string.Join("&", queryParts)}");
        }

        private static void ApplyPortalHeaders(HttpRequestMessage request, SourceCredential credential, string? token)
        {
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("X-User-Agent", "Model: MAG250; Link: Ethernet");
            request.Headers.TryAddWithoutValidation("Cookie", BuildCookieHeader(credential));
            request.Headers.TryAddWithoutValidation("Referer", ResolvePortalReferer(credential.Url));
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        private static string BuildCookieHeader(SourceCredential credential)
        {
            var parts = new[]
            {
                $"mac={credential.StalkerMacAddress}",
                $"stb_lang={(string.IsNullOrWhiteSpace(credential.StalkerLocale) ? "en" : credential.StalkerLocale)}",
                $"timezone={(string.IsNullOrWhiteSpace(credential.StalkerTimezone) ? "UTC" : credential.StalkerTimezone)}"
            };

            return string.Join("; ", parts);
        }

        private static string ResolvePortalReferer(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            return $"{uri.Scheme}://{uri.Authority}/";
        }

        private static SourceCredential NormalizeCredential(SourceCredential credential)
        {
            var normalized = new SourceCredential
            {
                Url = credential.Url?.Trim() ?? string.Empty,
                ProxyScope = credential.ProxyScope,
                ProxyUrl = credential.ProxyUrl?.Trim() ?? string.Empty,
                StalkerMacAddress = NormalizeMacAddress(credential.StalkerMacAddress),
                StalkerLocale = string.IsNullOrWhiteSpace(credential.StalkerLocale) ? "en" : credential.StalkerLocale.Trim(),
                StalkerTimezone = string.IsNullOrWhiteSpace(credential.StalkerTimezone) ? "UTC" : credential.StalkerTimezone.Trim()
            };

            normalized.StalkerDeviceId = string.IsNullOrWhiteSpace(credential.StalkerDeviceId)
                ? ComputeStableDeviceId(normalized.StalkerMacAddress)
                : credential.StalkerDeviceId.Trim();
            normalized.StalkerSerialNumber = string.IsNullOrWhiteSpace(credential.StalkerSerialNumber)
                ? normalized.StalkerMacAddress.Replace(":", string.Empty, StringComparison.Ordinal)
                : credential.StalkerSerialNumber.Trim();
            normalized.StalkerApiUrl = credential.StalkerApiUrl?.Trim() ?? string.Empty;
            return normalized;
        }

        private static IEnumerable<string> DiscoverApiUrls(SourceCredential credential)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(credential.StalkerApiUrl))
            {
                candidates.Add(credential.StalkerApiUrl.Trim());
            }

            var primary = credential.Url.Trim();
            if (Uri.TryCreate(primary, UriKind.Absolute, out var uri))
            {
                var normalized = $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}";
                candidates.Add(normalized);
                if (!normalized.EndsWith(".php", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add($"{normalized}/portal.php");
                    candidates.Add($"{normalized}/server/load.php");
                }
            }

            return candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static string ResolveActionType(string resourceType) => resourceType switch
        {
            "live" => "itv",
            "movie" => "vod",
            "episode" => "series",
            _ => "itv"
        };

        private static Dictionary<string, string> BuildCreateLinkParameters(StalkerStreamLocator locator)
        {
            return new Dictionary<string, string>
            {
                ["cmd"] = locator.Command,
                ["series"] = locator.ResourceType == "episode" ? "1" : "0",
                ["forced_storage"] = "undefined",
                ["disable_ad"] = "0",
                ["download"] = "0"
            };
        }

        private static Dictionary<string, string> BuildProfileParameters(SourceCredential credential)
        {
            return new Dictionary<string, string>
            {
                ["hd"] = "1",
                ["stb_type"] = "MAG250",
                ["ver"] = "ImageDescription: 0.2.18-r23-250; ImageDate: Thu Sep 13 11:31:16 EEST 2018; PORTAL version: 5.6.2; API Version: JS API version: 343; STB API version: 146;",
                ["num_banks"] = "1",
                ["sn"] = credential.StalkerSerialNumber,
                ["device_id"] = credential.StalkerDeviceId,
                ["device_id2"] = credential.StalkerDeviceId,
                ["auth_second_step"] = "1",
                ["not_valid_token"] = "0",
                ["hw_version"] = "1.7-BD-00"
            };
        }

        private static JsonElement ParseEnvelope(string content)
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("js", out var js))
            {
                return CloneElement(js);
            }

            return CloneElement(root);
        }

        private static IReadOnlyList<JsonElement> ExtractItems(JsonElement payload)
        {
            if (payload.ValueKind == JsonValueKind.Array)
            {
                return payload.EnumerateArray().Select(CloneElement).ToList();
            }

            if (payload.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertyName in new[] { "data", "results", "items", "channels" })
                {
                    if (payload.TryGetProperty(propertyName, out var property))
                    {
                        return ExtractItems(property);
                    }
                }
            }

            return Array.Empty<JsonElement>();
        }

        private static IEnumerable<KeyValuePair<string, JsonElement>> EnumerateEpisodeContainers(JsonElement payload)
        {
            if (payload.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertyName in new[] { "episodes", "series", "seasons" })
                {
                    if (payload.TryGetProperty(propertyName, out var property))
                    {
                        if (property.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var child in property.EnumerateObject())
                            {
                                yield return new KeyValuePair<string, JsonElement>(child.Name, CloneElement(child.Value));
                            }
                        }
                        else if (property.ValueKind == JsonValueKind.Array)
                        {
                            var index = 1;
                            foreach (var child in property.EnumerateArray())
                            {
                                yield return new KeyValuePair<string, JsonElement>(index.ToString(CultureInfo.InvariantCulture), CloneElement(child));
                                index++;
                            }
                        }
                    }
                }
            }
        }

        private static bool HasMorePages(JsonElement payload, int pageSize, int currentPage)
        {
            if (payload.ValueKind != JsonValueKind.Object)
            {
                return pageSize >= 14;
            }

            var totalItems = ParseInt(GetFirstString(payload, "total_items", "totalItems"));
            var maxPageItems = ParseInt(GetFirstString(payload, "max_page_items", "maxPageItems", "page_size"));
            if (totalItems <= 0 || maxPageItems <= 0)
            {
                return pageSize >= 14;
            }

            return currentPage * maxPageItems < totalItems;
        }

        private static string ResolvePortalName(JsonElement? payload)
        {
            return payload.HasValue
                ? GetFirstString(payload.Value, "portal_name", "portal", "name", "fname")
                : string.Empty;
        }

        private static string ResolvePortalVersion(JsonElement? payload)
        {
            return payload.HasValue
                ? GetFirstString(payload.Value, "portal_version", "version", "image_version")
                : string.Empty;
        }

        private static string ResolveProfileName(JsonElement? payload)
        {
            return payload.HasValue
                ? GetFirstString(payload.Value, "fname", "ls", "name", "login")
                : string.Empty;
        }

        private static string ResolveProfileId(JsonElement? payload)
        {
            return payload.HasValue
                ? GetFirstString(payload.Value, "id", "uid", "profile_id")
                : string.Empty;
        }

        private static string NormalizeResolvedLink(string rawLink, string apiUrl)
        {
            if (string.IsNullOrWhiteSpace(rawLink))
            {
                return string.Empty;
            }

            var trimmed = rawLink.Trim();
            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var candidate = parts.LastOrDefault() ?? trimmed;

            if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
            {
                return absolute.ToString();
            }

            if (Uri.TryCreate(new Uri(apiUrl), candidate, out var relative))
            {
                return relative.ToString();
            }

            return string.Empty;
        }

        private static string GetFirstString(JsonElement element, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (!TryGetPropertyCaseInsensitive(element, propertyName, out var property))
                {
                    continue;
                }

                switch (property.ValueKind)
                {
                    case JsonValueKind.String:
                        var value = property.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value.Trim();
                        }

                        break;
                    case JsonValueKind.Number:
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        return property.GetRawText().Trim('"');
                }
            }

            return string.Empty;
        }

        private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
        {
            value = default;
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (element.TryGetProperty(propertyName, out value))
            {
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            return false;
        }

        private static JsonElement CloneElement(JsonElement element)
        {
            using var document = JsonDocument.Parse(element.GetRawText());
            return document.RootElement.Clone();
        }

        private static int ResolveSeasonNumber(string key)
        {
            var digits = new string((key ?? string.Empty).Where(char.IsDigit).ToArray());
            return ParseInt(digits);
        }

        private static int ParseInt(string? value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        private static string NormalizeMacAddress(string? macAddress)
        {
            var raw = new string((macAddress ?? string.Empty).Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
            if (raw.Length != 12)
            {
                return string.IsNullOrWhiteSpace(macAddress) ? string.Empty : macAddress.Trim().ToUpperInvariant();
            }

            return string.Join(":", Enumerable.Range(0, 6).Select(index => raw.Substring(index * 2, 2)));
        }

        private static string ComputeStableDeviceId(string seed)
        {
            if (string.IsNullOrWhiteSpace(seed))
            {
                return string.Empty;
            }

            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(seed.Trim().ToUpperInvariant()));
            return Convert.ToHexString(bytes[..16]).ToLowerInvariant();
        }

        private sealed record StalkerPortalSession(
            SourceCredential Credential,
            string ApiUrl,
            string Token,
            SourceNetworkPurpose RequestPurpose,
            SourceRoutingDecision Routing,
            string MacAddress,
            string DeviceId,
            string SerialNumber,
            string Locale,
            string Timezone,
            string PortalName,
            string PortalVersion,
            string ProfileName,
            string ProfileId,
            DateTime LastHandshakeAtUtc,
            DateTime? ProfileFetchedAtUtc)
        {
            public DateTime HandshakeAtUtc => LastHandshakeAtUtc;
        }
    }

    public sealed class StalkerPortalCatalog
    {
        public string PortalName { get; set; } = StalkerPortalClient.DefaultPortalName;
        public string PortalVersion { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public string DiscoveredApiUrl { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string Locale { get; set; } = string.Empty;
        public string Timezone { get; set; } = string.Empty;
        public IReadOnlyList<StalkerCategory> LiveCategories { get; set; } = Array.Empty<StalkerCategory>();
        public IReadOnlyList<StalkerLiveChannel> LiveChannels { get; set; } = Array.Empty<StalkerLiveChannel>();
        public IReadOnlyList<StalkerCategory> MovieCategories { get; set; } = Array.Empty<StalkerCategory>();
        public IReadOnlyList<StalkerMovieItem> Movies { get; set; } = Array.Empty<StalkerMovieItem>();
        public IReadOnlyList<StalkerCategory> SeriesCategories { get; set; } = Array.Empty<StalkerCategory>();
        public IReadOnlyList<StalkerSeriesItem> Series { get; set; } = Array.Empty<StalkerSeriesItem>();
        public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
        public bool SupportsLive { get; set; }
        public bool SupportsMovies { get; set; }
        public bool SupportsSeries { get; set; }
        public DateTime? LastHandshakeAtUtc { get; set; }
        public DateTime? LastProfileSyncAtUtc { get; set; }
    }

    public sealed record StalkerCategory(string Id, string Name);
    public sealed record StalkerLiveChannel(string Id, string Name, string CategoryId, string LogoUrl, string EpgChannelId, string Command);
    public sealed record StalkerMovieItem(string Id, string Name, string CategoryId, string LogoUrl, string Command, string Description, string Year);
    public sealed record StalkerSeriesItem(string Id, string Name, string CategoryId, string LogoUrl, IReadOnlyList<StalkerSeasonItem> Seasons);
    public sealed record StalkerSeasonItem(int SeasonNumber, IReadOnlyList<StalkerEpisodeItem> Episodes);
    public sealed record StalkerEpisodeItem(string Id, string Title, int EpisodeNumber, string Command);
    public sealed record StalkerStreamResolution(string StreamUrl, string ApiUrl, SourceRoutingDecision Routing);
}
