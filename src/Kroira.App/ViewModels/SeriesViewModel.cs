#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.Services.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Kroira.App.ViewModels
{
    public partial class SeriesViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private List<Series> _allSeries = new List<Series>();

        public ObservableCollection<Series> FilteredSeries { get; } = new ObservableCollection<Series>();
        public ObservableCollection<BrowserCategoryViewModel> Categories { get; } = new ObservableCollection<BrowserCategoryViewModel>();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private BrowserCategoryViewModel? _selectedCategory;

        [ObservableProperty]
        private Series? _selectedSeries;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EpisodesListVisibility))]
        [NotifyPropertyChangedFor(nameof(EpisodesEmptyVisibility))]
        private Season? _selectedSeason;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedEpisodePlayVisibility))]
        private Episode? _selectedEpisode;

        public Visibility EpisodesListVisibility =>
            SelectedSeason?.Episodes?.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility EpisodesEmptyVisibility =>
            SelectedSeason != null && !(SelectedSeason.Episodes?.Count > 0) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility SelectedEpisodePlayVisibility => SelectedEpisode == null ? Visibility.Collapsed : Visibility.Visible;

        [ObservableProperty]
        private string _selectedSeriesStatus = string.Empty;

        [ObservableProperty]
        private Visibility _selectedSeriesStatusVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private bool _isEmpty;
        partial void OnSearchQueryChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedCategoryChanged(BrowserCategoryViewModel? value)
        {
            SelectedSeries = null;
            ApplyFilter();
        }

        partial void OnSelectedSeriesChanged(Series? value)
        {
            SelectedSeason = null;
            SelectedEpisode = null;
            SelectedSeriesStatus = string.Empty;
            SelectedSeriesStatusVisibility = Visibility.Collapsed;

            if (value != null)
            {
                _ = EnsureSeriesDetailsAsync(value);
            }
        }

        partial void OnSelectedSeasonChanged(Season? value)
        {
            SelectSingleEpisodeForSeason(value);
        }

        public SeriesViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [RelayCommand]
        public async Task LoadSeriesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var languageCode = await AppLanguageService.GetLanguageAsync(db);
            var rawSeries = await db.Series
                .Include(s => s.Seasons!)
                .ThenInclude(sn => sn.Episodes)
                .ToListAsync();

            _allSeries = CatalogOrderingService
                .OrderCatalog(rawSeries, languageCode, s => s.CategoryName, s => s.Title)
                .ToList();

            foreach (var s in _allSeries)
            {
                if (s.Seasons != null)
                {
                    s.Seasons = s.Seasons.OrderBy(sn => sn.SeasonNumber).ToList();
                    foreach (var sn in s.Seasons)
                    {
                        if (sn.Episodes != null)
                        {
                            sn.Episodes = sn.Episodes.OrderBy(e => e.EpisodeNumber).ToList();
                        }
                    }
                }
            }

            Categories.Clear();
            Categories.Add(new BrowserCategoryViewModel { Id = 0, Name = "All Categories", OrderIndex = -1 });

            var categoryIndex = 1;
            var orderedCategories = CatalogOrderingService.OrderCategories(
                _allSeries
                    .Select(s => string.IsNullOrWhiteSpace(s.CategoryName) ? "Uncategorized" : s.CategoryName.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase),
                languageCode);

            foreach (var categoryName in orderedCategories)
            {
                Categories.Add(new BrowserCategoryViewModel
                {
                    Id = categoryIndex,
                    Name = categoryName,
                    OrderIndex = categoryIndex
                });
                categoryIndex++;
            }

            SelectedCategory = Categories.FirstOrDefault();
            ApplyFilter();
            StartMetadataEnrichment();
        }

        private void ApplyFilter()
        {
            FilteredSeries.Clear();
            var filtered = _allSeries.AsEnumerable();

            if (SelectedCategory != null && SelectedCategory.Id != 0)
            {
                filtered = filtered.Where(s =>
                    string.Equals(GetDisplayCategory(s.CategoryName), SelectedCategory.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                filtered = filtered.Where(s => s.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var item in filtered)
            {
                FilteredSeries.Add(item);
            }

            IsEmpty = FilteredSeries.Count == 0;
        }

        private static string GetDisplayCategory(string categoryName)
        {
            return string.IsNullOrWhiteSpace(categoryName) ? "Uncategorized" : categoryName.Trim();
        }

        private void StartMetadataEnrichment()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var metadataService = scope.ServiceProvider.GetRequiredService<ITmdbMetadataService>();
                    var series = await db.Series.Take(28).ToListAsync();
                    await metadataService.EnrichSeriesAsync(db, series, 28);
                }
                catch
                {
                }
            });
        }

        private async Task EnsureSeriesDetailsAsync(Series series)
        {
            if (!HasPlayableEpisodes(series))
            {
                SelectedSeriesStatus = "Loading episode details...";
                SelectedSeriesStatusVisibility = Visibility.Visible;

                try
                {
                    await TryLoadEpisodesFromProviderAsync(series);
                }
                catch
                {
                    // Keep the series visible, but do not let provider detail failures break selection.
                }
            }

            NormalizeSeasonOrder(series);

            if (SelectedSeries?.Id != series.Id)
            {
                return;
            }

            var playableSeasonEpisodes = (series.Seasons ?? Array.Empty<Season>())
                .Select(season => new
                {
                    Season = season,
                    Episodes = (season.Episodes ?? Array.Empty<Episode>())
                        .Where(episode => !string.IsNullOrWhiteSpace(episode.StreamUrl))
                        .OrderBy(episode => episode.EpisodeNumber)
                        .ToList()
                })
                .Where(item => item.Episodes.Count > 0)
                .OrderBy(item => item.Season.SeasonNumber)
                .ToList();

            var allPlayableEpisodes = playableSeasonEpisodes
                .SelectMany(item => item.Episodes.Select(episode => new { item.Season, Episode = episode }))
                .ToList();

            if (allPlayableEpisodes.Count == 1)
            {
                SelectedSeason = allPlayableEpisodes[0].Season;
                SelectedEpisode = allPlayableEpisodes[0].Episode;
            }
            else
            {
                SelectedSeason = playableSeasonEpisodes.Count == 1
                    ? playableSeasonEpisodes[0].Season
                    : playableSeasonEpisodes.FirstOrDefault()?.Season;
                SelectSingleEpisodeForSeason(SelectedSeason);
            }

            if (SelectedSeason == null)
            {
                SelectedSeriesStatus = "Episode details are not available for this series yet. Try syncing VOD again or check the provider data.";
                SelectedSeriesStatusVisibility = Visibility.Visible;
            }
            else
            {
                SelectedSeriesStatus = string.Empty;
                SelectedSeriesStatusVisibility = Visibility.Collapsed;
            }

            OnPropertyChanged(nameof(SelectedSeries));
            OnPropertyChanged(nameof(EpisodesListVisibility));
            OnPropertyChanged(nameof(EpisodesEmptyVisibility));
        }

        private void SelectSingleEpisodeForSeason(Season? season)
        {
            var playableEpisodes = season?.Episodes?
                .Where(episode => !string.IsNullOrWhiteSpace(episode.StreamUrl))
                .OrderBy(episode => episode.EpisodeNumber)
                .ToList();

            SelectedEpisode = playableEpisodes?.Count == 1 ? playableEpisodes[0] : null;
        }

        private async Task TryLoadEpisodesFromProviderAsync(Series series)
        {
            if (string.IsNullOrWhiteSpace(series.ExternalId))
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var storedSeries = await db.Series
                .Include(s => s.Seasons!)
                .ThenInclude(season => season.Episodes)
                .FirstOrDefaultAsync(s => s.Id == series.Id);

            if (storedSeries == null)
            {
                return;
            }

            if (HasPlayableEpisodes(storedSeries))
            {
                series.Seasons = CopySeasons(storedSeries.Seasons);
                return;
            }

            var source = await db.SourceProfiles.FirstOrDefaultAsync(profile => profile.Id == storedSeries.SourceProfileId);
            if (source == null || source.Type != SourceType.Xtream)
            {
                return;
            }

            var cred = await db.SourceCredentials.FirstOrDefaultAsync(c => c.SourceProfileId == storedSeries.SourceProfileId);
            if (cred == null || string.IsNullOrWhiteSpace(cred.Url) || string.IsNullOrWhiteSpace(cred.Username))
            {
                return;
            }

            var baseUrl = cred.Url.TrimEnd('/');
            var authQuery = $"?username={Uri.EscapeDataString(cred.Username)}&password={Uri.EscapeDataString(cred.Password)}";

            List<Season> fetchedSeasons;
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) })
            {
                var infoJson = await client.GetStringAsync($"{baseUrl}/player_api.php{authQuery}&action=get_series_info&series_id={Uri.EscapeDataString(series.ExternalId)}");
                if (string.IsNullOrWhiteSpace(infoJson))
                {
                    return;
                }

                using var doc = JsonDocument.Parse(infoJson);
                fetchedSeasons = ExtractSeasons(doc.RootElement, baseUrl, cred.Username, cred.Password);
            }

            if (fetchedSeasons.Count == 0)
            {
                return;
            }

            if (storedSeries.Seasons != null)
            {
                foreach (var existingSeason in storedSeries.Seasons.ToList())
                {
                    if (existingSeason.Episodes != null)
                    {
                        db.Episodes.RemoveRange(existingSeason.Episodes);
                    }
                    db.Seasons.Remove(existingSeason);
                }
            }

            foreach (var season in fetchedSeasons)
            {
                season.SeriesId = storedSeries.Id;
                db.Seasons.Add(season);
            }

            await db.SaveChangesAsync();

            series.Seasons = fetchedSeasons;
        }

        private static List<Season> ExtractSeasons(JsonElement root, string baseUrl, string username, string password)
        {
            var seasons = new List<Season>();

            if (!root.TryGetProperty("episodes", out var episodesNode))
            {
                return seasons;
            }

            if (episodesNode.ValueKind == JsonValueKind.Object)
            {
                var fallbackSeasonNumber = 1;
                foreach (var seasonProperty in episodesNode.EnumerateObject())
                {
                    var seasonNumber = int.TryParse(seasonProperty.Name, out var parsedSeason) && parsedSeason > 0
                        ? parsedSeason
                        : fallbackSeasonNumber;

                    if (seasonProperty.Value.ValueKind == JsonValueKind.Array)
                    {
                        var season = BuildSeason(seasonNumber, seasonProperty.Value, baseUrl, username, password);
                        if (season.Episodes != null && season.Episodes.Count > 0)
                        {
                            seasons.Add(season);
                        }
                    }

                    fallbackSeasonNumber++;
                }
            }
            else if (episodesNode.ValueKind == JsonValueKind.Array)
            {
                var season = BuildSeason(1, episodesNode, baseUrl, username, password);
                if (season.Episodes != null && season.Episodes.Count > 0)
                {
                    seasons.Add(season);
                }
            }

            return seasons;
        }

        private static Season BuildSeason(int seasonNumber, JsonElement episodesArray, string baseUrl, string username, string password)
        {
            var season = new Season
            {
                SeasonNumber = seasonNumber <= 0 ? 1 : seasonNumber,
                Episodes = new List<Episode>()
            };

            var fallbackEpisodeNumber = 1;
            foreach (var episodeNode in episodesArray.EnumerateArray())
            {
                var episodeId = GetString(episodeNode, "id")
                             ?? GetString(episodeNode, "stream_id")
                             ?? GetString(episodeNode, "episode_id");

                if (string.IsNullOrWhiteSpace(episodeId))
                {
                    fallbackEpisodeNumber++;
                    continue;
                }

                var extension = GetString(episodeNode, "container_extension") ?? "mp4";
                var episodeNumber = GetInt(episodeNode, "episode_num") ?? fallbackEpisodeNumber;
                var title = GetString(episodeNode, "title");

                season.Episodes.Add(new Episode
                {
                    ExternalId = episodeId,
                    EpisodeNumber = episodeNumber,
                    Title = string.IsNullOrWhiteSpace(title) ? $"Episode {episodeNumber}" : title,
                    StreamUrl = $"{baseUrl}/series/{username}/{password}/{episodeId}.{extension}"
                });

                fallbackEpisodeNumber++;
            }

            return season;
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                _ => null
            };
        }

        private static int? GetInt(JsonElement element, string propertyName)
        {
            var value = GetString(element, propertyName);
            return int.TryParse(value, out var parsed) ? parsed : null;
        }

        private static bool HasPlayableEpisodes(Series series)
        {
            return series.Seasons != null &&
                   series.Seasons.Any(season =>
                       season.Episodes != null &&
                       season.Episodes.Any(episode => !string.IsNullOrWhiteSpace(episode.StreamUrl)));
        }

        private static ICollection<Season>? CopySeasons(ICollection<Season>? seasons)
        {
            if (seasons == null)
            {
                return null;
            }

            return seasons
                .Select(season => new Season
                {
                    Id = season.Id,
                    SeriesId = season.SeriesId,
                    SeasonNumber = season.SeasonNumber,
                    PosterUrl = season.PosterUrl,
                    Episodes = season.Episodes?
                        .Select(episode => new Episode
                        {
                            Id = episode.Id,
                            SeasonId = episode.SeasonId,
                            ExternalId = episode.ExternalId,
                            Title = episode.Title,
                            StreamUrl = episode.StreamUrl,
                            EpisodeNumber = episode.EpisodeNumber
                        })
                        .OrderBy(episode => episode.EpisodeNumber)
                        .ToList()
                })
                .OrderBy(season => season.SeasonNumber)
                .ToList();
        }

        private static void NormalizeSeasonOrder(Series series)
        {
            if (series.Seasons == null)
            {
                return;
            }

            series.Seasons = series.Seasons.OrderBy(season => season.SeasonNumber).ToList();
            foreach (var season in series.Seasons)
            {
                if (season.Episodes != null)
                {
                    season.Episodes = season.Episodes
                        .Where(episode => !string.IsNullOrWhiteSpace(episode.StreamUrl))
                        .OrderBy(episode => episode.EpisodeNumber)
                        .ToList();
                }
            }
        }
    }
}
