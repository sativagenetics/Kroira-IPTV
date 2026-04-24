#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Kroira.App.ViewModels
{
    public sealed partial class SearchViewModel : ObservableObject, IDisposable
    {
        private const int SearchDebounceMilliseconds = 260;
        private const int ResultsPerGroup = 12;

        private readonly IServiceProvider _serviceProvider;
        private CancellationTokenSource? _searchCts;
        private int _searchVersion;
        private bool _isDisposed;

        public SearchViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ObservableCollection<SearchResultGroupViewModel> ResultGroups { get; } = new();

        [ObservableProperty]
        private string _query = string.Empty;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private bool _hasSearched;

        [ObservableProperty]
        private string _statusTitle = "Search your library";

        [ObservableProperty]
        private string _statusMessage = "Find live channels, movies, series, and episodes from synced sources.";

        public int ActiveProfileId { get; private set; } = 1;
        public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ResultsVisibility => ResultGroups.Any(group => group.Results.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility StatusVisibility => !IsLoading && ResultsVisibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        public string ResultCountText
        {
            get
            {
                var count = ResultGroups.Sum(group => group.Results.Count);
                return count == 1 ? "1 result" : $"{count:N0} results";
            }
        }

        partial void OnQueryChanged(string value)
        {
            QueueSearch(value);
        }

        partial void OnIsLoadingChanged(bool value)
        {
            OnPropertyChanged(nameof(LoadingVisibility));
            OnPropertyChanged(nameof(StatusVisibility));
        }

        [RelayCommand]
        public Task SearchNowAsync()
        {
            CancelPendingSearch();
            return ExecuteSearchAsync(Query, ++_searchVersion, CancellationToken.None);
        }

        public void Reset()
        {
            CancelPendingSearch();
            Query = string.Empty;
            ClearResults();
            HasSearched = false;
            SetStatus("Search your library", "Find live channels, movies, series, and episodes from synced sources.");
        }

        public void Dispose()
        {
            _isDisposed = true;
            CancelPendingSearch();
        }

        private void QueueSearch(string value)
        {
            CancelPendingSearch();

            var version = ++_searchVersion;
            var normalized = MediaSearchService.NormalizeQuery(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                IsLoading = false;
                HasSearched = false;
                ClearResults();
                SetStatus("Search your library", "Find live channels, movies, series, and episodes from synced sources.");
                return;
            }

            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;
            IsLoading = true;
            _ = RunDebouncedSearchAsync(value, version, token);
        }

        private async Task RunDebouncedSearchAsync(string value, int version, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(SearchDebounceMilliseconds, cancellationToken);
                await ExecuteSearchAsync(value, version, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task ExecuteSearchAsync(string value, int version, CancellationToken cancellationToken)
        {
            try
            {
                IsLoading = true;
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
                var searchService = scope.ServiceProvider.GetRequiredService<IMediaSearchService>();
                var access = await profileService.GetAccessSnapshotAsync(db);
                cancellationToken.ThrowIfCancellationRequested();

                var response = await searchService.SearchAsync(
                    db,
                    value,
                    access,
                    ResultsPerGroup,
                    cancellationToken);

                if (_isDisposed || version != _searchVersion)
                {
                    return;
                }

                ActiveProfileId = access.ProfileId;
                HasSearched = !response.IsEmptyQuery;
                ApplyResults(response);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (_isDisposed || version != _searchVersion)
                {
                    return;
                }

                ClearResults();
                HasSearched = true;
                SetStatus("Search failed", ex.Message);
            }
            finally
            {
                if (!_isDisposed && version == _searchVersion)
                {
                    IsLoading = false;
                }
            }
        }

        private void ApplyResults(MediaSearchResponse response)
        {
            ResultGroups.Clear();
            foreach (var group in response.Groups.Where(group => group.Results.Count > 0))
            {
                ResultGroups.Add(new SearchResultGroupViewModel(group));
            }

            if (response.TotalCount == 0)
            {
                SetStatus("No results", "Try another title, channel name, series name, category, or source.");
            }
            else
            {
                SetStatus(string.Empty, string.Empty);
            }

            NotifyResultStateChanged();
        }

        private void ClearResults()
        {
            ResultGroups.Clear();
            NotifyResultStateChanged();
        }

        private void SetStatus(string title, string message)
        {
            StatusTitle = title;
            StatusMessage = message;
            OnPropertyChanged(nameof(StatusVisibility));
        }

        private void NotifyResultStateChanged()
        {
            OnPropertyChanged(nameof(ResultsVisibility));
            OnPropertyChanged(nameof(StatusVisibility));
            OnPropertyChanged(nameof(ResultCountText));
        }

        private void CancelPendingSearch()
        {
            var cts = _searchCts;
            _searchCts = null;
            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }
    }

    public sealed class SearchResultGroupViewModel
    {
        public SearchResultGroupViewModel(MediaSearchResultGroup group)
        {
            Type = group.Type;
            Heading = group.Heading;
            CountText = group.Results.Count == 1 ? "1 result" : $"{group.Results.Count:N0} results";
            foreach (var result in group.Results)
            {
                Results.Add(new SearchResultItemViewModel(result));
            }
        }

        public MediaSearchResultType Type { get; }
        public string Heading { get; }
        public string CountText { get; }
        public ObservableCollection<SearchResultItemViewModel> Results { get; } = new();
    }

    public sealed class SearchResultItemViewModel
    {
        public SearchResultItemViewModel(MediaSearchResult result)
        {
            Result = result;
        }

        public MediaSearchResult Result { get; }
        public MediaSearchResultType Type => Result.Type;
        public int ContentId => Result.ContentId;
        public PlaybackContentType? PlaybackContentType => Result.PlaybackContentType;
        public int SourceProfileId => Result.SourceProfileId;
        public string Title => Result.Title;
        public string Subtitle => Result.Subtitle;
        public string Overview => Result.Overview;
        public string SourceBadge => Result.SourceBadge;
        public string CategoryBadge => Result.CategoryBadge;
        public string ArtworkUrl => Result.ArtworkUrl;
        public string StreamUrl => Result.StreamUrl;
        public string LogicalContentKey => Result.LogicalContentKey;
        public long ResumePositionMs => Result.ResumePositionMs;
        public string Glyph => Type switch
        {
            MediaSearchResultType.Live => "\uE714",
            MediaSearchResultType.Movie => "\uE8B2",
            MediaSearchResultType.Series => "\uE8A9",
            MediaSearchResultType.Episode => "\uE768",
            _ => "\uE721"
        };

        public string TypeLabel => Type switch
        {
            MediaSearchResultType.Live => "Live",
            MediaSearchResultType.Movie => "Movie",
            MediaSearchResultType.Series => "Series",
            MediaSearchResultType.Episode => "Episode",
            _ => "Result"
        };

        public string ActionLabel => Type switch
        {
            MediaSearchResultType.Live => "Play",
            MediaSearchResultType.Movie => "Details",
            MediaSearchResultType.Series => "Details",
            MediaSearchResultType.Episode => ResumePositionMs > 0 ? "Resume" : "Play",
            _ => "Open"
        };

        public string BadgeLine
        {
            get
            {
                var category = string.IsNullOrWhiteSpace(CategoryBadge) ? "Uncategorized" : CategoryBadge;
                var source = string.IsNullOrWhiteSpace(SourceBadge) ? "Unknown source" : SourceBadge;
                return $"{category} / {source}";
            }
        }

        public Visibility ArtworkVisibility => string.IsNullOrWhiteSpace(ArtworkUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;

        public Visibility OverviewVisibility => string.IsNullOrWhiteSpace(Overview)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
