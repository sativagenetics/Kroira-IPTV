#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
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
    public sealed class SeriesSourceOptionViewModel
    {
        public SeriesSourceOptionViewModel(CatalogSeriesVariant variant)
        {
            Variant = variant;
        }

        public CatalogSeriesVariant Variant { get; }
        public int Id => Variant.Series.Id;
        public string Label => Variant.DisplayName;
    }

    public sealed class SeriesSeasonOptionViewModel
    {
        public SeriesSeasonOptionViewModel(Season season, int watchedEpisodeCount, int totalEpisodeCount)
        {
            Season = season;
            WatchedEpisodeCount = watchedEpisodeCount;
            TotalEpisodeCount = totalEpisodeCount;
        }

        public Season Season { get; }
        public int Id => Season.Id;
        public int SeasonNumber => Season.SeasonNumber;
        public int WatchedEpisodeCount { get; }
        public int TotalEpisodeCount { get; }
        public string DisplayName => TotalEpisodeCount > 0
            ? $"Season {SeasonNumber} ({WatchedEpisodeCount}/{TotalEpisodeCount})"
            : $"Season {SeasonNumber}";
    }

    public sealed class SeriesEpisodeItemViewModel
    {
        public SeriesEpisodeItemViewModel(Episode episode, int seasonNumber, WatchProgressSnapshot? snapshot)
        {
            Episode = episode;
            SeasonNumber = seasonNumber;
            Snapshot = snapshot;
        }

        public Episode Episode { get; }
        public WatchProgressSnapshot? Snapshot { get; }
        public int Id => Episode.Id;
        public int SeasonNumber { get; }
        public int EpisodeNumber => Episode.EpisodeNumber;
        public string Title => Episode.Title;
        public string StreamUrl => Episode.StreamUrl;
        public bool IsWatched => Snapshot?.IsWatched == true;
        public long ResumePositionMs => Snapshot?.ResumePositionMs ?? 0;
        public string StatusText => IsWatched
            ? "Watched"
            : Snapshot?.HasResumePoint == true
                ? $"Resume {TimeSpan.FromMilliseconds(ResumePositionMs):hh\\:mm\\:ss}"
                : "Unwatched";
        public Visibility MarkWatchedVisibility => IsWatched ? Visibility.Collapsed : Visibility.Visible;
        public Visibility MarkUnwatchedVisibility => IsWatched ? Visibility.Visible : Visibility.Collapsed;
    }

    public partial class SeriesBrowseItemViewModel : ObservableObject
    {
        public SeriesBrowseItemViewModel(CatalogSeriesGroup group, bool isFavorite, string displayCategoryName)
        {
            Group = group;
            DisplayCategoryName = displayCategoryName;
            IsFavorite = isFavorite;
            foreach (var variant in group.Variants)
            {
                SourceOptions.Add(new SeriesSourceOptionViewModel(variant));
            }

            SelectedSourceOption = SourceOptions.FirstOrDefault();
        }

        public event EventHandler? ActiveSeriesChanged;

        public CatalogSeriesGroup Group { get; private set; }
        public Series PreferredSeries => Group.PreferredSeries;
        public string GroupKey => Group.GroupKey;
        public Series ActiveSeries => SelectedSourceOption?.Variant.Series ?? PreferredSeries;
        public ObservableCollection<SeriesSourceOptionViewModel> SourceOptions { get; } = new();
        public IReadOnlyList<int> VariantIds => Group.Variants.Select(variant => variant.Series.Id).ToList();
        public int Id => PreferredSeries.Id;
        public string Title => PreferredSeries.Title;
        public string DisplayPosterUrl => ActiveSeries.DisplayPosterUrl;
        public string DisplayHeroArtworkUrl => ActiveSeries.DisplayHeroArtworkUrl;
        public string RatingText => ActiveSeries.RatingText;
        public string MetadataLine
        {
            get
            {
                var parts = new[]
                {
                    ActiveSeries.DisplayYear,
                    string.IsNullOrWhiteSpace(ActiveSeries.Genres) ? DisplayCategoryName : ActiveSeries.Genres,
                    string.IsNullOrWhiteSpace(ActiveSeries.OriginalLanguage) ? string.Empty : ActiveSeries.OriginalLanguage.ToUpperInvariant()
                };

                var metadata = string.Join(" / ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
                return Group.Variants.Count > 1 && !string.IsNullOrWhiteSpace(Group.SourceSummary)
                    ? string.IsNullOrWhiteSpace(metadata)
                        ? Group.SourceSummary
                        : $"{metadata} / {Group.SourceSummary}"
                    : metadata;
            }
        }
        public string Overview => string.IsNullOrWhiteSpace(ActiveSeries.Overview) ? PreferredSeries.Overview : ActiveSeries.Overview;
        public string CategoryName => DisplayCategoryName;
        public string DisplayCategoryName { get; private set; }
        public ICollection<Season>? Seasons => ActiveSeries.Seasons;
        public bool HasAlternateSources => SourceOptions.Count > 1;
        public Visibility SourceSelectionVisibility => HasAlternateSources ? Visibility.Visible : Visibility.Collapsed;
        public string SourceSummary => Group.SourceSummary;

        [ObservableProperty]
        private SeriesSourceOptionViewModel? _selectedSourceOption;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FavoriteGlyph))]
        [NotifyPropertyChangedFor(nameof(FavoriteLabel))]
        private bool _isFavorite;

        public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";
        public string FavoriteLabel => IsFavorite ? "Saved" : "Save";

        public void UpdateFrom(CatalogSeriesGroup group, bool isFavorite, string displayCategoryName)
        {
            var selectedVariantId = SelectedSourceOption?.Id;
            Group = group;
            DisplayCategoryName = displayCategoryName;
            IsFavorite = isFavorite;

            SourceOptions.Clear();
            foreach (var variant in group.Variants)
            {
                SourceOptions.Add(new SeriesSourceOptionViewModel(variant));
            }

            SelectedSourceOption = SourceOptions.FirstOrDefault(option => option.Id == selectedVariantId) ?? SourceOptions.FirstOrDefault();
            RefreshSeriesState();
        }

        public void RefreshSeriesState()
        {
            OnPropertyChanged(nameof(Group));
            OnPropertyChanged(nameof(PreferredSeries));
            OnPropertyChanged(nameof(GroupKey));
            OnPropertyChanged(nameof(ActiveSeries));
            OnPropertyChanged(nameof(SourceOptions));
            OnPropertyChanged(nameof(VariantIds));
            OnPropertyChanged(nameof(Id));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayPosterUrl));
            OnPropertyChanged(nameof(DisplayHeroArtworkUrl));
            OnPropertyChanged(nameof(RatingText));
            OnPropertyChanged(nameof(MetadataLine));
            OnPropertyChanged(nameof(Overview));
            OnPropertyChanged(nameof(CategoryName));
            OnPropertyChanged(nameof(DisplayCategoryName));
            OnPropertyChanged(nameof(Seasons));
            OnPropertyChanged(nameof(HasAlternateSources));
            OnPropertyChanged(nameof(SourceSelectionVisibility));
            OnPropertyChanged(nameof(SourceSummary));
        }

        partial void OnSelectedSourceOptionChanged(SeriesSourceOptionViewModel? value)
        {
            OnPropertyChanged(nameof(DisplayPosterUrl));
            OnPropertyChanged(nameof(DisplayHeroArtworkUrl));
            OnPropertyChanged(nameof(RatingText));
            OnPropertyChanged(nameof(MetadataLine));
            OnPropertyChanged(nameof(Overview));
            OnPropertyChanged(nameof(Seasons));
            ActiveSeriesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public partial class SeriesBrowseSlotViewModel : ObservableObject
    {
        public SeriesBrowseSlotViewModel(SeriesBrowseItemViewModel? series)
        {
            _series = series;
        }

        private SeriesBrowseItemViewModel? _series;

        public SeriesBrowseItemViewModel? Series => _series;
        public bool HasSeries => Series != null;
        public int Id => Series?.Id ?? 0;
        public string Title => Series?.Title ?? string.Empty;
        public string DisplayPosterUrl => Series?.DisplayPosterUrl ?? string.Empty;
        public string CategoryName => Series?.DisplayCategoryName ?? string.Empty;
        public string FavoriteGlyph => Series?.FavoriteGlyph ?? "\uE734";
        public string FavoriteLabel => Series?.FavoriteLabel ?? string.Empty;
        public Visibility SeriesVisibility => HasSeries ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PlaceholderVisibility => HasSeries ? Visibility.Collapsed : Visibility.Visible;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectionBackgroundOpacity))]
        [NotifyPropertyChangedFor(nameof(SelectionChromeOpacity))]
        [NotifyPropertyChangedFor(nameof(SelectionAccentOpacity))]
        private bool _isSelected;

        public double SelectionBackgroundOpacity => IsSelected ? 1 : 0;
        public double SelectionChromeOpacity => IsSelected ? 0.92 : 0;
        public double SelectionAccentOpacity => IsSelected ? 1 : 0.22;

        public void RefreshFavoriteState()
        {
            OnPropertyChanged(nameof(FavoriteGlyph));
            OnPropertyChanged(nameof(FavoriteLabel));
        }

        public bool Matches(SeriesBrowseSlotViewModel other)
        {
            return Matches(other.Series);
        }

        public bool Matches(SeriesBrowseItemViewModel? series)
        {
            if (!HasSeries && series == null)
            {
                return true;
            }

            return HasSeries &&
                   series != null &&
                   string.Equals(Series?.GroupKey, series.GroupKey, StringComparison.OrdinalIgnoreCase);
        }

        public void UpdateFrom(SeriesBrowseItemViewModel? series)
        {
            _series = series;
            RefreshSeriesState();
        }

        public void RefreshSeriesState()
        {
            OnPropertyChanged(nameof(Series));
            OnPropertyChanged(nameof(HasSeries));
            OnPropertyChanged(nameof(Id));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayPosterUrl));
            OnPropertyChanged(nameof(CategoryName));
            OnPropertyChanged(nameof(FavoriteGlyph));
            OnPropertyChanged(nameof(FavoriteLabel));
            OnPropertyChanged(nameof(SeriesVisibility));
            OnPropertyChanged(nameof(PlaceholderVisibility));
        }
    }

    public partial class SeriesViewModel : ObservableObject
    {
        private const string Domain = ProfileDomains.Series;
        private const int BrowseGridColumns = 3;
        private const int InitialDisplaySeriesSlotBatchSize = BrowseGridColumns * 2;
        private const int DeferredSlotAppendBatchSize = BrowseGridColumns * 10;
        private const int SearchApplyDebounceMilliseconds = 180;
        private readonly IServiceProvider _serviceProvider;
        private readonly IBrowsePreferencesService _browsePreferencesService;
        private readonly ICatalogDiscoveryService _catalogDiscoveryService;
        private readonly ICatalogTaxonomyService _taxonomyService;
        private readonly ISmartCategoryService _smartCategoryService;
        private readonly ILogicalCatalogStateService _logicalCatalogStateService;
        private readonly List<CatalogSeriesGroup> _allSeriesGroups = new();
        private readonly Dictionary<int, CatalogCategoryProjection> _seriesCategoryById = new();
        private readonly Dictionary<int, SeriesSmartWatchState> _seriesSmartStateById = new();
        private readonly Dictionary<int, SourceType> _sourceTypeById = new();
        private readonly Dictionary<int, CatalogDiscoveryHealthBucket> _sourceHealthById = new();
        private readonly Dictionary<int, DateTime?> _sourceLastSyncById = new();
        private List<SeriesBrowseItemViewModel> _allSeries = new List<SeriesBrowseItemViewModel>();
        private List<SeriesBrowseItemViewModel> _filteredSeries = new();
        private readonly List<(string CategoryName, int Count)> _allCategories = new();
        private readonly Dictionary<int, WatchProgressSnapshot> _selectedEpisodeSnapshots = new();
        private string _visibleCategorySignature = string.Empty;
        private bool _isLoadingWatchPreferences;
        private bool _isInitializingBrowsePreferences;
        private int _activeProfileId;
        private int? _pendingEpisodeSelectionId;
        private int? _preferredSeasonNumber;
        private int? _preferredEpisodeId;
        private int? _preferredSeriesVariantId;
        private BrowsePreferences _browsePreferences = new();
        private HashSet<string> _favoriteSeriesKeys = new(StringComparer.OrdinalIgnoreCase);
        private string _browseLanguageCode = AppLanguageService.DefaultLanguageCode;
        private bool _isApplyingFilter;
        private bool _pendingApplyFilter;
        private bool _hasLoadedOnce;
        private bool _preferStagedFirstPaint;
        private string _pendingApplyFilterReason = string.Empty;
        private bool _preserveSeriesDetailContextOnSelectionChange;
        private int _seriesSlotRefreshVersion;
        private int _searchApplyRequestVersion;
        private CancellationTokenSource? _metadataEnrichmentCts;
        private Task? _metadataEnrichmentTask;
        private Stopwatch? _activeLoadStopwatch;
        private Task _displaySeriesSlotRefreshTask = Task.CompletedTask;

        public IReadOnlyList<SeriesBrowseItemViewModel> FilteredSeries => _filteredSeries;
        public int FilteredSeriesCount => _filteredSeries.Count;
        public bool HasLoadedOnce => _hasLoadedOnce;
        public Visibility ContentShellVisibility => IsBlockingSurfaceState ? Visibility.Collapsed : Visibility.Visible;
        public Visibility BlockingSurfaceVisibility => IsBlockingSurfaceState ? Visibility.Visible : Visibility.Collapsed;
        public BulkObservableCollection<SeriesBrowseSlotViewModel> DisplaySeriesSlots { get; } = new BulkObservableCollection<SeriesBrowseSlotViewModel>();
        public BulkObservableCollection<BrowserCategoryViewModel> Categories { get; } = new BulkObservableCollection<BrowserCategoryViewModel>();
        public BulkObservableCollection<SeriesSeasonOptionViewModel> SeasonOptions { get; } = new BulkObservableCollection<SeriesSeasonOptionViewModel>();
        public BulkObservableCollection<SeriesEpisodeItemViewModel> EpisodeItems { get; } = new BulkObservableCollection<SeriesEpisodeItemViewModel>();
        public ObservableCollection<BrowseSortOptionViewModel> SortOptions { get; } = new ObservableCollection<BrowseSortOptionViewModel>();
        public BulkObservableCollection<BrowseSourceFilterOptionViewModel> SourceOptions { get; } = new BulkObservableCollection<BrowseSourceFilterOptionViewModel>();
        public BulkObservableCollection<BrowseSourceVisibilityViewModel> SourceVisibilityOptions { get; } = new BulkObservableCollection<BrowseSourceVisibilityViewModel>();
        public BulkObservableCollection<BrowseCategoryManagerOptionViewModel> ManageCategoryOptions { get; } = new BulkObservableCollection<BrowseCategoryManagerOptionViewModel>();
        public BulkObservableCollection<BrowseFacetOptionViewModel> DiscoverySignalOptions { get; } = new BulkObservableCollection<BrowseFacetOptionViewModel>();
        public BulkObservableCollection<BrowseFacetOptionViewModel> DiscoverySourceTypeOptions { get; } = new BulkObservableCollection<BrowseFacetOptionViewModel>();
        public BulkObservableCollection<BrowseFacetOptionViewModel> DiscoveryLanguageOptions { get; } = new BulkObservableCollection<BrowseFacetOptionViewModel>();
        public BulkObservableCollection<BrowseFacetOptionViewModel> DiscoveryTagOptions { get; } = new BulkObservableCollection<BrowseFacetOptionViewModel>();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private BrowserCategoryViewModel? _selectedCategory;

        [ObservableProperty]
        private BrowseSortOptionViewModel? _selectedSortOption;

        [ObservableProperty]
        private BrowseSourceFilterOptionViewModel? _selectedSourceOption;

        [ObservableProperty]
        private BrowseFacetOptionViewModel? _selectedDiscoverySignalOption;

        [ObservableProperty]
        private BrowseFacetOptionViewModel? _selectedDiscoverySourceTypeOption;

        [ObservableProperty]
        private BrowseFacetOptionViewModel? _selectedDiscoveryLanguageOption;

        [ObservableProperty]
        private BrowseFacetOptionViewModel? _selectedDiscoveryTagOption;

        [ObservableProperty]
        private BrowseCategoryManagerOptionViewModel? _selectedManageCategory;

        [ObservableProperty]
        private string _manageCategoryAliasDraft = string.Empty;

        [ObservableProperty]
        private bool _isManageCategoryHidden;

        [ObservableProperty]
        private bool _favoritesOnly;

        [ObservableProperty]
        private bool _hideSecondaryContent;

        [ObservableProperty]
        private bool _hasAdvancedFilters;

        [ObservableProperty]
        private bool _isAdvancedControlsExpanded;

        [ObservableProperty]
        private SeriesBrowseItemViewModel? _selectedSeries;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EpisodesListVisibility))]
        [NotifyPropertyChangedFor(nameof(EpisodesEmptyVisibility))]
        private SeriesSeasonOptionViewModel? _selectedSeason;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedEpisodePlayVisibility))]
        private SeriesEpisodeItemViewModel? _selectedEpisode;

        [ObservableProperty]
        private bool _hideWatchedEpisodes;

        public Visibility EpisodesListVisibility =>
            EpisodeItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility EpisodesEmptyVisibility =>
            SelectedSeason != null && EpisodeItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility SelectedEpisodePlayVisibility => SelectedEpisode == null ? Visibility.Collapsed : Visibility.Visible;
        public bool HasManageCategorySelection => SelectedManageCategory != null;
        public Visibility DiscoverySignalVisibility => DiscoverySignalOptions.Count > 2 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DiscoverySourceTypeVisibility => DiscoverySourceTypeOptions.Count > 2 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DiscoveryLanguageVisibility => DiscoveryLanguageOptions.Count > 2 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DiscoveryTagVisibility => DiscoveryTagOptions.Count > 2 ? Visibility.Visible : Visibility.Collapsed;

        private sealed record FilteredSeriesResult(CatalogSeriesGroup Group, string DisplayCategoryName);

        private sealed class SeriesSmartWatchState
        {
            public int SeasonCount { get; init; }
            public int EpisodeCount { get; init; }
            public int WatchedEpisodeCount { get; init; }
            public bool HasAnyProgress { get; init; }
            public bool HasResumePoint { get; init; }
            public bool IsRecentlyWatched { get; init; }
            public bool IsCompleted => EpisodeCount > 0 && WatchedEpisodeCount >= EpisodeCount;
            public bool HasCompleteSeasons => SeasonCount > 0 && EpisodeCount > 0;
        }

        [ObservableProperty]
        private string _selectedSeriesStatus = string.Empty;

        [ObservableProperty]
        private Visibility _selectedSeriesStatusVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private bool _isEmpty;

        [ObservableProperty]
        private string _discoverySummaryText = "Browse by source, language, genre, and episode availability.";

        [ObservableProperty]
        private string _emptyStateTitle = "No series to show";

        [ObservableProperty]
        private string _emptyStateMessage = "Sync a VOD source, or clear your search and browse filters.";

        [ObservableProperty]
        private SurfaceStatePresentation _surfaceState = SurfaceStateCopies.Series.Create(SurfaceViewState.Loading);
        private bool IsBlockingSurfaceState =>
            SurfaceState.State is SurfaceViewState.NoSources or SurfaceViewState.Offline or SurfaceViewState.ImportFailed;

        partial void OnSurfaceStateChanged(SurfaceStatePresentation value)
        {
            OnPropertyChanged(nameof(ContentShellVisibility));
            OnPropertyChanged(nameof(BlockingSurfaceVisibility));
        }

        partial void OnSearchQueryChanged(string value)
        {
            LogBrowse($"search changed query='{value}'");
            QueueSearchApplyFilter(value);
        }

        partial void OnSelectedCategoryChanged(BrowserCategoryViewModel? value)
        {
            if (value == null)
            {
                LogBrowse("category changed key=<null> ignored transient-null selection");
                return;
            }

            UpdateCategorySelectionState(value.FilterKey, "selected-category-changed");
            LogBrowse(
                $"category changed key={value.FilterKey} name={value.Name} initializing={_isInitializingBrowsePreferences} applying={_isApplyingFilter}");
            if (!_isInitializingBrowsePreferences)
            {
                ApplyFilter("selected-category-changed");
            }
        }

        partial void OnSelectedSortOptionChanged(BrowseSortOptionViewModel? value)
        {
            if (_isInitializingBrowsePreferences)
            {
                return;
            }

            _browsePreferences.SortKey = value?.Key ?? "recommended";
            _ = SaveBrowsePreferencesAsync(rebuildCollections: false);
        }

        partial void OnSelectedSourceOptionChanged(BrowseSourceFilterOptionViewModel? value)
        {
            if (_isInitializingBrowsePreferences)
            {
                return;
            }

            _browsePreferences.SelectedSourceId = value?.Id ?? 0;
            _ = SaveBrowsePreferencesAsync(rebuildCollections: false);
        }

        partial void OnSelectedDiscoverySignalOptionChanged(BrowseFacetOptionViewModel? value)
        {
            if (_isInitializingBrowsePreferences)
            {
                return;
            }

            _browsePreferences.DiscoverySignalKey = value?.Key ?? string.Empty;
            _ = SaveBrowsePreferencesAsync(rebuildCollections: false);
        }

        partial void OnSelectedDiscoverySourceTypeOptionChanged(BrowseFacetOptionViewModel? value)
        {
            if (_isInitializingBrowsePreferences)
            {
                return;
            }

            _browsePreferences.DiscoverySourceTypeKey = value?.Key ?? string.Empty;
            _ = SaveBrowsePreferencesAsync(rebuildCollections: false);
        }

        partial void OnSelectedDiscoveryLanguageOptionChanged(BrowseFacetOptionViewModel? value)
        {
            if (_isInitializingBrowsePreferences)
            {
                return;
            }

            _browsePreferences.DiscoveryLanguageKey = value?.Key ?? string.Empty;
            _ = SaveBrowsePreferencesAsync(rebuildCollections: false);
        }

        partial void OnSelectedDiscoveryTagOptionChanged(BrowseFacetOptionViewModel? value)
        {
            if (_isInitializingBrowsePreferences)
            {
                return;
            }

            _browsePreferences.DiscoveryTagKey = value?.Key ?? string.Empty;
            _ = SaveBrowsePreferencesAsync(rebuildCollections: false);
        }

        partial void OnSelectedManageCategoryChanged(BrowseCategoryManagerOptionViewModel? value)
        {
            if (value == null)
            {
                ManageCategoryAliasDraft = string.Empty;
                IsManageCategoryHidden = false;
                OnPropertyChanged(nameof(HasManageCategorySelection));
                return;
            }

            _isInitializingBrowsePreferences = true;
            try
            {
                ManageCategoryAliasDraft = value.HasManualAlias
                    ? value.EffectiveName
                    : string.Empty;
                IsManageCategoryHidden = value.IsHidden;
            }
            finally
            {
                _isInitializingBrowsePreferences = false;
            }

            OnPropertyChanged(nameof(HasManageCategorySelection));
        }

        partial void OnFavoritesOnlyChanged(bool value)
        {
            if (_isInitializingBrowsePreferences)
            {
                return;
            }

            _browsePreferences.FavoritesOnly = value;
            _ = SaveBrowsePreferencesAsync(rebuildCollections: false);
        }

        partial void OnHideSecondaryContentChanged(bool value)
        {
            if (_isInitializingBrowsePreferences)
            {
                return;
            }

            _browsePreferences.HideSecondaryContent = value;
            _ = SaveBrowsePreferencesAsync(rebuildCollections: true);
        }

        partial void OnIsAdvancedControlsExpandedChanged(bool value)
        {
            if (_isInitializingBrowsePreferences)
            {
                return;
            }

            _browsePreferences.IsAdvancedControlsExpanded = value;
            _ = SaveBrowsePreferencesAsync(rebuildCollections: false);
        }

        partial void OnSelectedSeriesChanged(SeriesBrowseItemViewModel? value)
        {
            var preserveDetailContext = _preserveSeriesDetailContextOnSelectionChange;
            _preserveSeriesDetailContextOnSelectionChange = false;

            if (!preserveDetailContext)
            {
                SelectedSeason = null;
                SelectedEpisode = null;
                SeasonOptions.ReplaceAll(Array.Empty<SeriesSeasonOptionViewModel>());
                EpisodeItems.ReplaceAll(Array.Empty<SeriesEpisodeItemViewModel>());
                SelectedSeriesStatus = string.Empty;
                SelectedSeriesStatusVisibility = Visibility.Collapsed;
            }

            foreach (var item in _allSeries)
            {
                item.ActiveSeriesChanged -= SelectedSeries_ActiveSeriesChanged;
            }

            if (value == null)
            {
                return;
            }

            value.ActiveSeriesChanged -= SelectedSeries_ActiveSeriesChanged;
            value.ActiveSeriesChanged += SelectedSeries_ActiveSeriesChanged;
            UpdateSelectedSeriesSlotState();
            _ = EnsureSeriesDetailsAsync(value);
        }

        partial void OnSelectedSeasonChanged(SeriesSeasonOptionViewModel? value)
        {
            BuildEpisodeItems(value, _pendingEpisodeSelectionId);
            _pendingEpisodeSelectionId = null;
        }

        partial void OnHideWatchedEpisodesChanged(bool value)
        {
            if (_isLoadingWatchPreferences)
            {
                return;
            }

            _ = SaveHideWatchedEpisodesAsync(value);
        }

        public SeriesViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _browsePreferencesService = serviceProvider.GetRequiredService<IBrowsePreferencesService>();
            _catalogDiscoveryService = serviceProvider.GetRequiredService<ICatalogDiscoveryService>();
            _taxonomyService = serviceProvider.GetRequiredService<ICatalogTaxonomyService>();
            _smartCategoryService = serviceProvider.GetRequiredService<ISmartCategoryService>();
            _logicalCatalogStateService = serviceProvider.GetRequiredService<ILogicalCatalogStateService>();
            SortOptions.Add(new BrowseSortOptionViewModel("recommended", "Recommended"));
            SortOptions.Add(new BrowseSortOptionViewModel("title_asc", "Title A-Z"));
            SortOptions.Add(new BrowseSortOptionViewModel("rating_desc", "Highest rated"));
            SortOptions.Add(new BrowseSortOptionViewModel("popularity_desc", "Most popular"));
            SortOptions.Add(new BrowseSortOptionViewModel("year_desc", "Newest first"));
            SortOptions.Add(new BrowseSortOptionViewModel("favorites_first", "Favorites first"));
            DiscoverySignalOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DiscoverySignalVisibility));
            DiscoverySourceTypeOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DiscoverySourceTypeVisibility));
            DiscoveryLanguageOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DiscoveryLanguageVisibility));
            DiscoveryTagOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DiscoveryTagVisibility));
        }

        [RelayCommand]
        public async Task LoadSeriesAsync()
        {
            var loadStopwatch = Stopwatch.StartNew();
            _activeLoadStopwatch = loadStopwatch;
            _preferStagedFirstPaint = !_hasLoadedOnce || DisplaySeriesSlots.Count == 0;
            if (!_hasLoadedOnce || DisplaySeriesSlots.Count == 0)
            {
                SurfaceState = SurfaceStateCopies.Series.Create(SurfaceViewState.Loading);
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var deduplicationService = scope.ServiceProvider.GetRequiredService<ICatalogDeduplicationService>();
                var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
                var watchStateService = scope.ServiceProvider.GetRequiredService<ILibraryWatchStateService>();
                var browsePreferencesService = scope.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
                var surfaceStateService = scope.ServiceProvider.GetRequiredService<ISurfaceStateService>();
                var sourceAvailability = await surfaceStateService.GetSourceAvailabilityAsync(db);
                if (sourceAvailability.SourceCount == 0)
                {
                    SurfaceState = surfaceStateService.ResolveSourceBackedState(sourceAvailability, 0, SurfaceStateCopies.Series);
                    return;
                }

                var access = await profileService.GetAccessSnapshotAsync(db);
                _activeProfileId = access.ProfileId;
                _isLoadingWatchPreferences = true;
                try
                {
                    HideWatchedEpisodes = await watchStateService.GetHideWatchedEpisodesAsync(db, _activeProfileId);
                }
                finally
                {
                    _isLoadingWatchPreferences = false;
                }
                _browsePreferences = await browsePreferencesService.GetAsync(db, Domain, _activeProfileId);
                var languageCode = await AppLanguageService.GetLanguageAsync(db, access.ProfileId);
                _browseLanguageCode = languageCode;
                await _logicalCatalogStateService.ReconcileFavoritesAsync(db, access.ProfileId);
                var sourceProfiles = await db.SourceProfiles
                    .AsNoTracking()
                    .Select(source => new
                    {
                        source.Id,
                        source.Type,
                        source.LastSync
                    })
                    .ToListAsync();
                var healthStates = await db.SourceHealthReports
                    .AsNoTracking()
                    .OrderByDescending(report => report.EvaluatedAtUtc)
                    .Select(report => new
                    {
                        report.SourceProfileId,
                        report.HealthState
                    })
                    .ToListAsync();
                var seriesGroups = (await deduplicationService.LoadSeriesGroupsAsync(db))
                    .Select(group => FilterGroup(group, access))
                    .OfType<CatalogSeriesGroup>()
                    .ToList();
                _favoriteSeriesKeys = await _logicalCatalogStateService.GetFavoriteLogicalKeysAsync(db, access.ProfileId, FavoriteType.Series);
                await LoadSeriesSmartWatchStateAsync(db, access.ProfileId, seriesGroups);

                var seriesOrderEntries = seriesGroups
                    .Select(group => new
                    {
                        Group = group,
                        CategoryProjection = BuildSeriesCategoryProjection(group.PreferredSeries)
                    })
                    .ToList();

                _allSeriesGroups.Clear();
                _allSeriesGroups.AddRange(CatalogOrderingService
                    .OrderCatalog(seriesOrderEntries, languageCode, entry => entry.CategoryProjection.DisplayCategoryName, entry => entry.Group.PreferredSeries.Title)
                    .Select(entry => entry.Group));

                _seriesCategoryById.Clear();
                _sourceTypeById.Clear();
                _sourceHealthById.Clear();
                _sourceLastSyncById.Clear();
                foreach (var source in sourceProfiles)
                {
                    _sourceTypeById[source.Id] = source.Type;
                    _sourceLastSyncById[source.Id] = source.LastSync;
                }

                foreach (var health in healthStates.GroupBy(item => item.SourceProfileId))
                {
                    _sourceHealthById[health.Key] = _catalogDiscoveryService.ResolveHealthBucket(health.First().HealthState);
                }

                foreach (var item in _allSeriesGroups)
                {
                    foreach (var variant in item.Variants.Select(groupVariant => groupVariant.Series))
                    {
                        _seriesCategoryById[variant.Id] = BuildSeriesCategoryProjection(variant);
                        if (variant.Seasons == null)
                        {
                            continue;
                        }

                        variant.Seasons = variant.Seasons.OrderBy(sn => sn.SeasonNumber).ToList();
                        foreach (var season in variant.Seasons)
                        {
                            if (season.Episodes != null)
                            {
                                season.Episodes = season.Episodes.OrderBy(e => e.EpisodeNumber).ToList();
                            }
                        }
                    }
                }

                _allCategories.Clear();
                _allCategories.AddRange(_allSeriesGroups
                    .GroupBy(group => GetCategoryProjection(group.PreferredSeries).RawCategoryName)
                    .Select(group => (group.Key, group.Count()))
                    .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase));

                BuildSourceOptions();

                _isInitializingBrowsePreferences = true;
                try
                {
                    FavoritesOnly = _browsePreferences.FavoritesOnly;
                    HideSecondaryContent = _browsePreferences.HideSecondaryContent;
                    IsAdvancedControlsExpanded = _browsePreferences.IsAdvancedControlsExpanded;
                    SelectedSortOption = SortOptions.FirstOrDefault(option => string.Equals(option.Key, _browsePreferences.SortKey, StringComparison.OrdinalIgnoreCase))
                        ?? SortOptions.First();
                    SelectedSourceOption = SourceOptions.FirstOrDefault(option => option.Id == _browsePreferences.SelectedSourceId)
                        ?? SourceOptions.FirstOrDefault();
                    var selectedCategory = BuildVisibleCategories(languageCode, SelectedCategory?.FilterKey ?? string.Empty);
                    ReassignSelectedCategory(selectedCategory, "load-initial");
                }
                finally
                {
                    _isInitializingBrowsePreferences = false;
                }

                ApplyFilter("load-series");
                SurfaceState = surfaceStateService.ResolveSourceBackedState(sourceAvailability, _allSeriesGroups.Count, SurfaceStateCopies.Series);
                _hasLoadedOnce = true;
                OnPropertyChanged(nameof(HasLoadedOnce));
                await Task.Yield();
                BuildCategoryManagerOptions();
                LogBrowse(
                    $"load ready fullMs={loadStopwatch.ElapsedMilliseconds} groups={_allSeriesGroups.Count} results={_filteredSeries.Count} slots={DisplaySeriesSlots.Count}");
                StartMetadataEnrichment();
            }
            catch (Exception ex)
            {
                BrowseRuntimeLogger.Log("SERIES", $"load failed {ex}");
                SurfaceState = _serviceProvider.GetRequiredService<ISurfaceStateService>().CreateFailureState(SurfaceStateCopies.Series, ex);
            }
            finally
            {
                _activeLoadStopwatch = null;
            }
        }

        private async Task LoadSeriesSmartWatchStateAsync(
            AppDbContext db,
            int profileId,
            IReadOnlyList<CatalogSeriesGroup> seriesGroups)
        {
            _seriesSmartStateById.Clear();
            var seriesIds = seriesGroups
                .SelectMany(group => group.Variants)
                .Select(variant => variant.Series.Id)
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (seriesIds.Count == 0)
            {
                return;
            }

            var seasonCounts = new Dictionary<int, int>();
            var episodeCounts = new Dictionary<int, int>();
            var progressRows = new List<(int SeriesId, PlaybackProgress Progress)>();

            foreach (var chunk in seriesIds.Chunk(800))
            {
                var chunkIds = chunk.ToList();
                foreach (var item in await db.Seasons
                             .AsNoTracking()
                             .Where(season => chunkIds.Contains(season.SeriesId))
                             .GroupBy(season => season.SeriesId)
                             .Select(group => new { SeriesId = group.Key, Count = group.Count() })
                             .ToListAsync())
                {
                    seasonCounts[item.SeriesId] = item.Count;
                }

                foreach (var item in await (
                             from season in db.Seasons.AsNoTracking()
                             join episode in db.Episodes.AsNoTracking() on season.Id equals episode.SeasonId
                             where chunkIds.Contains(season.SeriesId)
                             select new { season.SeriesId, episode.Id })
                         .GroupBy(item => item.SeriesId)
                         .Select(group => new { SeriesId = group.Key, Count = group.Count() })
                         .ToListAsync())
                {
                    episodeCounts[item.SeriesId] = item.Count;
                }

                var chunkProgressRows = await (
                        from progress in db.PlaybackProgresses.AsNoTracking()
                        join episode in db.Episodes.AsNoTracking() on progress.ContentId equals episode.Id
                        join season in db.Seasons.AsNoTracking() on episode.SeasonId equals season.Id
                        where progress.ProfileId == profileId &&
                              progress.ContentType == PlaybackContentType.Episode &&
                              chunkIds.Contains(season.SeriesId)
                        select new { season.SeriesId, Progress = progress })
                    .ToListAsync();
                progressRows.AddRange(chunkProgressRows.Select(item => (item.SeriesId, item.Progress)));
            }

            var progressBySeries = progressRows
                .GroupBy(item => item.SeriesId)
                .ToDictionary(group => group.Key, group => group.Select(item => item.Progress).ToList());

            foreach (var seriesId in seriesIds)
            {
                progressBySeries.TryGetValue(seriesId, out var rows);
                rows ??= new List<PlaybackProgress>();
                var watchedCount = rows.Count(WatchStateRules.IsWatched);
                var hasResumePoint = rows.Any(progress =>
                    WatchStateRules.NormalizeResumePosition(
                        progress.PositionMs,
                        progress.DurationMs,
                        WatchStateRules.IsWatched(progress)) >= WatchStateRules.MinimumResumePositionMs);

                _seriesSmartStateById[seriesId] = new SeriesSmartWatchState
                {
                    SeasonCount = seasonCounts.TryGetValue(seriesId, out var seasonCount) ? seasonCount : 0,
                    EpisodeCount = episodeCounts.TryGetValue(seriesId, out var episodeCount) ? episodeCount : 0,
                    WatchedEpisodeCount = watchedCount,
                    HasAnyProgress = rows.Count > 0,
                    HasResumePoint = hasResumePoint,
                    IsRecentlyWatched = rows.Count > 0
                };
            }
        }

        private void ApplyFilter(string reason = "direct")
        {
            if (!string.Equals(reason, "search-query-changed", StringComparison.Ordinal))
            {
                System.Threading.Interlocked.Increment(ref _searchApplyRequestVersion);
            }

            if (_isApplyingFilter)
            {
                _pendingApplyFilter = true;
                _pendingApplyFilterReason = reason;
                LogBrowse($"apply queued reason={reason}");
                return;
            }

            _isApplyingFilter = true;
            try
            {
                var nextReason = reason;
                do
                {
                    _pendingApplyFilter = false;
                    ApplyFilterCore(nextReason);
                    nextReason = _pendingApplyFilterReason;
                }
                while (_pendingApplyFilter);
            }
            finally
            {
                _pendingApplyFilterReason = string.Empty;
                _isApplyingFilter = false;
            }
        }

        private void ApplyFilterCore(string reason)
        {
            var currentCategoryKey = SelectedCategory?.FilterKey ?? string.Empty;
            LogBrowse(
                $"apply start reason={reason} selectedKey={currentCategoryKey} search='{SearchQuery}' source={SelectedSourceOption?.Id ?? 0}");

            var previousSelectedSeries = SelectedSeries;
            var previousSelectedGroupKey = SelectedSeries?.GroupKey ?? string.Empty;
            var previousSelectedSeasonNumber = SelectedSeason?.SeasonNumber;
            var previousSelectedEpisodeId = SelectedEpisode?.Id;
            var previousSelectedVariantId = SelectedSeries?.ActiveSeries.Id;
            foreach (var item in _allSeries)
            {
                item.ActiveSeriesChanged -= SelectedSeries_ActiveSeriesChanged;
            }

            var baseResults = BuildFilteredSeriesResults().ToList();
            var discoveryProjection = BuildDiscoveryProjection(baseResults, DateTime.UtcNow);
            RefreshDiscoveryOptions(discoveryProjection);
            var filteredResults = baseResults
                .Where(result => discoveryProjection.MatchingKeys.Contains(result.Group.GroupKey))
                .ToList();

            _isInitializingBrowsePreferences = true;
            try
            {
                var selectedCategory = BuildVisibleCategories(_browseLanguageCode, currentCategoryKey);
                ReassignSelectedCategory(selectedCategory, $"apply:{reason}");
            }
            finally
            {
                _isInitializingBrowsePreferences = false;
            }

            PatchFilteredSeriesItems(filteredResults);

            _filteredSeries = _allSeries;
            OnPropertyChanged(nameof(FilteredSeries));
            OnPropertyChanged(nameof(FilteredSeriesCount));

            var nextSelectedSeries = _allSeries.FirstOrDefault(item => string.Equals(item.GroupKey, previousSelectedGroupKey, StringComparison.OrdinalIgnoreCase));
            if (previousSelectedSeries != null && nextSelectedSeries != null)
            {
                PreserveSelectedSeriesContext(previousSelectedSeries, nextSelectedSeries, previousSelectedVariantId, previousSelectedSeasonNumber, previousSelectedEpisodeId);
            }
            else
            {
                ClearPreferredSeriesDetailContext();
            }

            if (ReferenceEquals(SelectedSeries, nextSelectedSeries) && nextSelectedSeries != null)
            {
                _preserveSeriesDetailContextOnSelectionChange = true;
                nextSelectedSeries.ActiveSeriesChanged -= SelectedSeries_ActiveSeriesChanged;
                nextSelectedSeries.ActiveSeriesChanged += SelectedSeries_ActiveSeriesChanged;
                UpdateSelectedSeriesSlotState();
                _ = EnsureSeriesDetailsAsync(nextSelectedSeries);
            }
            else
            {
                SelectedSeries = nextSelectedSeries;
            }
            DiscoverySummaryText = discoveryProjection.SummaryText;
            HasAdvancedFilters = FavoritesOnly ||
                                 HideSecondaryContent ||
                                 SourceVisibilityOptions.Any(option => !option.IsVisible) ||
                                 _browsePreferences.HiddenCategoryKeys.Count > 0 ||
                                 _browsePreferences.CategoryRemaps.Count > 0 ||
                                 HasDiscoveryFilters(discoveryProjection.EffectiveSelection) ||
                                 (SelectedSourceOption?.Id ?? 0) != 0 ||
                                 !string.Equals(SelectedSortOption?.Key ?? "recommended", "recommended", StringComparison.OrdinalIgnoreCase);
            IsEmpty = _filteredSeries.Count == 0;
            UpdateEmptyState(baseResults.Count, discoveryProjection.HasActiveFacetFilters);
            var shouldStageFirstPaint = _preferStagedFirstPaint;
            _preferStagedFirstPaint = false;
            _displaySeriesSlotRefreshTask = RefreshDisplaySeriesSlotsAsync(shouldStageFirstPaint, reason);
            LogBrowse(
                $"apply end reason={reason} groups={filteredResults.Count} results={_filteredSeries.Count} slots={DisplaySeriesSlots.Count} selectedKey={SelectedCategory?.FilterKey ?? "<all>"}");
        }

        [RelayCommand]
        public async Task ToggleFavoriteAsync(int seriesId)
        {
            var targetGroup = _allSeriesGroups.FirstOrDefault(group => group.Variants.Any(variant => variant.Series.Id == seriesId));
            if (targetGroup == null)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var activeProfileId = await profileService.GetActiveProfileIdAsync(db);
            var targetSeries = targetGroup.Variants.First(variant => variant.Series.Id == seriesId).Series;
            var logicalKey = _logicalCatalogStateService.BuildSeriesLogicalKey(targetSeries);
            var isFavorite = await _logicalCatalogStateService.ToggleFavoriteAsync(db, activeProfileId, FavoriteType.Series, seriesId);
            if (isFavorite)
            {
                _favoriteSeriesKeys.Add(logicalKey);
            }
            else
            {
                _favoriteSeriesKeys.Remove(logicalKey);
            }

            if (RequiresFullBrowseRefreshForFavoriteToggle())
            {
                ApplyFilter("toggle-favorite");
                return;
            }

            RefreshSeriesFavoriteState(targetGroup.GroupKey);
        }

        [RelayCommand]
        public async Task SaveCategoryPreferenceAsync()
        {
            if (SelectedManageCategory == null)
            {
                return;
            }

            if (IsManageCategoryHidden)
            {
                if (!_browsePreferences.HiddenCategoryKeys.Contains(SelectedManageCategory.Key, StringComparer.OrdinalIgnoreCase))
                {
                    _browsePreferences.HiddenCategoryKeys.Add(SelectedManageCategory.Key);
                }
            }
            else
            {
                _browsePreferences.HiddenCategoryKeys.RemoveAll(value => string.Equals(value, SelectedManageCategory.Key, StringComparison.OrdinalIgnoreCase));
            }

            var alias = ManageCategoryAliasDraft.Trim();
            if (string.IsNullOrWhiteSpace(alias) ||
                string.Equals(alias, SelectedManageCategory.RawName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(alias, SelectedManageCategory.AutoDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                _browsePreferences.CategoryRemaps.Remove(SelectedManageCategory.Key);
            }
            else
            {
                _browsePreferences.CategoryRemaps[SelectedManageCategory.Key] = alias;
            }

            await SaveBrowsePreferencesAsync(true);
        }

        [RelayCommand]
        public async Task ClearCategoryPreferenceAsync()
        {
            if (SelectedManageCategory == null)
            {
                return;
            }

            _browsePreferences.HiddenCategoryKeys.RemoveAll(value => string.Equals(value, SelectedManageCategory.Key, StringComparison.OrdinalIgnoreCase));
            _browsePreferences.CategoryRemaps.Remove(SelectedManageCategory.Key);
            _isInitializingBrowsePreferences = true;
            try
            {
                ManageCategoryAliasDraft = string.Empty;
                IsManageCategoryHidden = false;
            }
            finally
            {
                _isInitializingBrowsePreferences = false;
            }

            await SaveBrowsePreferencesAsync(true);
        }

        private static string GetDisplayCategory(string categoryName)
        {
            return string.IsNullOrWhiteSpace(categoryName) ? "Uncategorized" : categoryName.Trim();
        }

        public void SelectSeriesFromSlot(SeriesBrowseSlotViewModel slot)
        {
            if (slot.HasSeries && slot.Series != null)
            {
                SelectedSeries = slot.Series;
            }
        }

        private async Task RefreshDisplaySeriesSlotsAsync(bool prioritizeFirstPaint, string reason)
        {
            if (_filteredSeries.Count == 0)
            {
                var emptyPatch = DisplaySeriesSlots.PatchToMatch(
                    Array.Empty<SeriesBrowseSlotViewModel>(),
                    static (existing, incoming) => existing.Matches(incoming),
                    static (existing, incoming) => existing.UpdateFrom(incoming.Series));
                LogBrowse($"slot patch reason={reason} reused={emptyPatch.ReusedCount} inserted={emptyPatch.InsertedCount} removed={emptyPatch.RemovedCount} moved={emptyPatch.MovedCount}");
                UpdateSelectedSeriesSlotState();
                return;
            }

            try
            {
                var refreshVersion = ++_seriesSlotRefreshVersion;
                var slots = BuildSeriesSlots(_filteredSeries);
                if (!prioritizeFirstPaint || slots.Count <= InitialDisplaySeriesSlotBatchSize)
                {
                    var patch = DisplaySeriesSlots.PatchToMatch(
                        slots,
                        static (existing, incoming) => existing.Matches(incoming),
                        static (existing, incoming) => existing.UpdateFrom(incoming.Series));
                    LogBrowse($"slot patch reason={reason} reused={patch.ReusedCount} inserted={patch.InsertedCount} removed={patch.RemovedCount} moved={patch.MovedCount}");
                    UpdateSelectedSeriesSlotState();
                    if (prioritizeFirstPaint)
                    {
                        LogBrowse(
                            $"first content ready ms={_activeLoadStopwatch?.ElapsedMilliseconds ?? -1} reason={reason} visibleSlots={slots.Count}");
                    }

                    return;
                }

                var initialSlots = slots.Take(InitialDisplaySeriesSlotBatchSize).ToList();
                var deferredSlots = slots.Skip(InitialDisplaySeriesSlotBatchSize).ToList();
                var initialPatch = DisplaySeriesSlots.PatchToMatch(
                    initialSlots,
                    static (existing, incoming) => existing.Matches(incoming),
                    static (existing, incoming) => existing.UpdateFrom(incoming.Series));
                LogBrowse($"slot patch reason={reason}:initial reused={initialPatch.ReusedCount} inserted={initialPatch.InsertedCount} removed={initialPatch.RemovedCount} moved={initialPatch.MovedCount}");
                UpdateSelectedSeriesSlotState();
                LogBrowse(
                    $"first content ready ms={_activeLoadStopwatch?.ElapsedMilliseconds ?? -1} reason={reason} visibleSlots={initialSlots.Count}");

                await Task.Yield();
                if (refreshVersion != _seriesSlotRefreshVersion)
                {
                    return;
                }

                for (var index = 0; index < deferredSlots.Count; index += DeferredSlotAppendBatchSize)
                {
                    if (refreshVersion != _seriesSlotRefreshVersion)
                    {
                        return;
                    }

                    DisplaySeriesSlots.AppendRange(deferredSlots.GetRange(
                        index,
                        Math.Min(DeferredSlotAppendBatchSize, deferredSlots.Count - index)));
                    UpdateSelectedSeriesSlotState();
                    await Task.Yield();
                }
            }
            catch (Exception ex)
            {
                LogBrowse($"slot refresh failed reason={reason} error={ex.Message}");
            }
        }

        private List<SeriesBrowseSlotViewModel> BuildSeriesSlots(IReadOnlyList<SeriesBrowseItemViewModel> filteredSeries)
        {
            var slots = filteredSeries
                .Select(show => new SeriesBrowseSlotViewModel(show))
                .ToList();

            var remainder = filteredSeries.Count % BrowseGridColumns;
            var placeholderCount = remainder == 0 ? 0 : BrowseGridColumns - remainder;
            for (var i = 0; i < placeholderCount; i++)
            {
                slots.Add(new SeriesBrowseSlotViewModel(null));
            }

            return slots;
        }

        private void PatchFilteredSeriesItems(IReadOnlyList<FilteredSeriesResult> filteredResults)
        {
            var existingByGroupKey = _allSeries
                .Where(item => !string.IsNullOrWhiteSpace(item.GroupKey))
                .GroupBy(item => item.GroupKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var nextItems = new List<SeriesBrowseItemViewModel>(filteredResults.Count);
            var reusedCount = 0;

            foreach (var result in filteredResults)
            {
                if (existingByGroupKey.TryGetValue(result.Group.GroupKey, out var existing))
                {
                    existing.UpdateFrom(result.Group, IsSeriesGroupFavorite(result.Group), result.DisplayCategoryName);
                    nextItems.Add(existing);
                    reusedCount++;
                    continue;
                }

                nextItems.Add(new SeriesBrowseItemViewModel(
                    result.Group,
                    IsSeriesGroupFavorite(result.Group),
                    result.DisplayCategoryName));
            }

            _allSeries = nextItems;
            LogBrowse($"item patch reused={reusedCount} inserted={Math.Max(nextItems.Count - reusedCount, 0)} total={nextItems.Count}");
        }

        private IEnumerable<FilteredSeriesResult> BuildFilteredSeriesResults()
        {
            var visibleSourceIds = SourceVisibilityOptions
                .Where(option => option.IsVisible)
                .Select(option => option.Id)
                .ToHashSet();
            if (visibleSourceIds.Count == 0)
            {
                visibleSourceIds = _allSeriesGroups.SelectMany(group => group.Variants).Select(variant => variant.SourceProfile.Id).ToHashSet();
            }

            var selectedCategoryKey = SelectedCategory?.FilterKey ?? string.Empty;
            var selectedSourceId = SelectedSourceOption?.Id ?? 0;
            var results = new List<FilteredSeriesResult>();

            foreach (var group in _allSeriesGroups)
            {
                var variants = group.Variants
                    .Where(variant => visibleSourceIds.Contains(variant.SourceProfile.Id))
                    .Where(variant => selectedSourceId == 0 || variant.SourceProfile.Id == selectedSourceId)
                    .Where(variant => !HideSecondaryContent || string.Equals(variant.Series.ContentKind, "Primary", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (variants.Count == 0)
                {
                    continue;
                }

                var preferredSeries = variants.FirstOrDefault(variant => variant.Series.Id == group.PreferredSeries.Id)?.Series ?? variants[0].Series;
                var categoryProjection = GetCategoryProjection(preferredSeries);
                if (IsCategoryHidden(categoryProjection.RawCategoryName))
                {
                    continue;
                }

                var filteredGroup = new CatalogSeriesGroup
                {
                    GroupKey = group.GroupKey,
                    PreferredSeries = preferredSeries,
                    Variants = variants,
                    SourceSummary = BuildSourceSummary(variants.Select(variant => variant.SourceProfile.Name))
                };

                if (!string.IsNullOrWhiteSpace(selectedCategoryKey) &&
                    !MatchesSeriesCategorySelection(filteredGroup, categoryProjection, selectedCategoryKey))
                {
                    continue;
                }

                if (FavoritesOnly && !IsSeriesGroupFavorite(filteredGroup))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(SearchQuery) &&
                    !MatchesSeriesSearch(filteredGroup, categoryProjection.DisplayCategoryName))
                {
                    continue;
                }

                results.Add(new FilteredSeriesResult(filteredGroup, categoryProjection.DisplayCategoryName));
            }

            return SortSeriesGroups(results.Select(result => result.Group))
                .Select(group => new FilteredSeriesResult(
                    group,
                    GetCategoryProjection(group.PreferredSeries).DisplayCategoryName));
        }

        private CatalogDiscoveryProjection BuildDiscoveryProjection(
            IReadOnlyList<FilteredSeriesResult> results,
            DateTime nowUtc)
        {
            var records = results
                .Select(result =>
                {
                    var sourceProfileIds = result.Group.Variants
                        .Select(variant => variant.SourceProfile.Id)
                        .Where(id => id > 0)
                        .Distinct()
                        .ToList();

                    return new CatalogDiscoveryRecord
                    {
                        Key = result.Group.GroupKey,
                        Domain = CatalogDiscoveryDomain.Series,
                        SourceProfileIds = sourceProfileIds,
                        SourceTypes = sourceProfileIds
                            .Select(id => _sourceTypeById.TryGetValue(id, out var sourceType) ? sourceType : SourceType.M3U)
                            .Distinct()
                            .ToList(),
                        LanguageKey = _catalogDiscoveryService.ResolveLanguageKey(result.Group.PreferredSeries.OriginalLanguage),
                        LanguageLabel = _catalogDiscoveryService.ResolveLanguageLabel(result.Group.PreferredSeries.OriginalLanguage),
                        Tags = _catalogDiscoveryService.ExtractTags(result.Group.PreferredSeries.Genres),
                        IsFavorite = IsSeriesGroupFavorite(result.Group),
                        HasArtwork = HasSeriesArtwork(result.Group.PreferredSeries),
                        HasPlayableChildren = HasPlayableEpisodes(result.Group.PreferredSeries) ||
                                              result.Group.Variants.Any(variant => HasPlayableEpisodes(variant.Series)),
                        HealthBucket = ResolveBestHealthBucket(sourceProfileIds),
                        LastSyncUtc = ResolveLatestSync(sourceProfileIds)
                    };
                })
                .ToList();

            return _catalogDiscoveryService.BuildProjection(
                CatalogDiscoveryDomain.Series,
                records,
                new CatalogDiscoverySelection
                {
                    SignalKey = SelectedDiscoverySignalOption?.Key ?? _browsePreferences.DiscoverySignalKey,
                    SourceTypeKey = SelectedDiscoverySourceTypeOption?.Key ?? _browsePreferences.DiscoverySourceTypeKey,
                    LanguageKey = SelectedDiscoveryLanguageOption?.Key ?? _browsePreferences.DiscoveryLanguageKey,
                    TagKey = SelectedDiscoveryTagOption?.Key ?? _browsePreferences.DiscoveryTagKey
                },
                nowUtc);
        }

        private void RefreshDiscoveryOptions(CatalogDiscoveryProjection projection)
        {
            _browsePreferences.DiscoverySignalKey = projection.EffectiveSelection.SignalKey;
            _browsePreferences.DiscoverySourceTypeKey = projection.EffectiveSelection.SourceTypeKey;
            _browsePreferences.DiscoveryLanguageKey = projection.EffectiveSelection.LanguageKey;
            _browsePreferences.DiscoveryTagKey = projection.EffectiveSelection.TagKey;

            var wasInitializing = _isInitializingBrowsePreferences;
            _isInitializingBrowsePreferences = true;
            try
            {
                DiscoverySignalOptions.ReplaceAll(projection.SignalOptions.Select(ToBrowseFacetOption));
                DiscoverySourceTypeOptions.ReplaceAll(projection.SourceTypeOptions.Select(ToBrowseFacetOption));
                DiscoveryLanguageOptions.ReplaceAll(projection.LanguageOptions.Select(ToBrowseFacetOption));
                DiscoveryTagOptions.ReplaceAll(projection.TagOptions.Select(ToBrowseFacetOption));

                SelectedDiscoverySignalOption = DiscoverySignalOptions.FirstOrDefault(option => string.Equals(option.Key, projection.EffectiveSelection.SignalKey, StringComparison.OrdinalIgnoreCase))
                    ?? DiscoverySignalOptions.FirstOrDefault();
                SelectedDiscoverySourceTypeOption = DiscoverySourceTypeOptions.FirstOrDefault(option => string.Equals(option.Key, projection.EffectiveSelection.SourceTypeKey, StringComparison.OrdinalIgnoreCase))
                    ?? DiscoverySourceTypeOptions.FirstOrDefault();
                SelectedDiscoveryLanguageOption = DiscoveryLanguageOptions.FirstOrDefault(option => string.Equals(option.Key, projection.EffectiveSelection.LanguageKey, StringComparison.OrdinalIgnoreCase))
                    ?? DiscoveryLanguageOptions.FirstOrDefault();
                SelectedDiscoveryTagOption = DiscoveryTagOptions.FirstOrDefault(option => string.Equals(option.Key, projection.EffectiveSelection.TagKey, StringComparison.OrdinalIgnoreCase))
                    ?? DiscoveryTagOptions.FirstOrDefault();
            }
            finally
            {
                _isInitializingBrowsePreferences = wasInitializing;
            }
        }

        private void QueueSearchApplyFilter(string query)
        {
            var requestVersion = System.Threading.Interlocked.Increment(ref _searchApplyRequestVersion);
            LogBrowse($"search apply queued request={requestVersion} query='{query}'");
            _ = ApplySearchFilterAsync(requestVersion);
        }

        private async Task ApplySearchFilterAsync(int requestVersion)
        {
            await Task.Delay(SearchApplyDebounceMilliseconds);
            if (requestVersion != _searchApplyRequestVersion)
            {
                LogBrowse($"search apply skipped stale request={requestVersion}");
                return;
            }

            ApplyFilter("search-query-changed");
        }

        private static BrowseFacetOptionViewModel ToBrowseFacetOption(CatalogDiscoveryFacetOption option)
        {
            return new BrowseFacetOptionViewModel(option.Key, option.Label, option.ItemCount);
        }

        private void UpdateEmptyState(int baseResultCount, bool hasDiscoveryFacetFilters)
        {
            if (!IsEmpty)
            {
                EmptyStateTitle = string.Empty;
                EmptyStateMessage = string.Empty;
                return;
            }

            var hasNarrowingFilters = hasDiscoveryFacetFilters ||
                                      FavoritesOnly ||
                                      (SelectedSourceOption?.Id ?? 0) != 0 ||
                                      !string.IsNullOrWhiteSpace(SearchQuery) ||
                                      !string.IsNullOrWhiteSpace(SelectedCategory?.FilterKey);
            if (hasNarrowingFilters)
            {
                EmptyStateTitle = "Nothing matches this explore mix";
                EmptyStateMessage = "Relax one or two facets, switch providers, or widen your search to reopen the series library.";
                return;
            }

            EmptyStateTitle = baseResultCount == 0 ? "No series to show" : "Series shelf is empty";
            EmptyStateMessage = "Sync a VOD source, or review your category rules if this provider should already contain series.";
        }

        private static bool HasSeriesArtwork(Series series)
        {
            return !string.IsNullOrWhiteSpace(series.DisplayPosterUrl) ||
                   !string.IsNullOrWhiteSpace(series.DisplayHeroArtworkUrl);
        }

        private CatalogDiscoveryHealthBucket ResolveBestHealthBucket(IReadOnlyList<int> sourceProfileIds)
        {
            var buckets = sourceProfileIds
                .Select(id => _sourceHealthById.TryGetValue(id, out var bucket) ? bucket : CatalogDiscoveryHealthBucket.Unknown)
                .ToList();
            if (buckets.Contains(CatalogDiscoveryHealthBucket.Healthy))
            {
                return CatalogDiscoveryHealthBucket.Healthy;
            }

            if (buckets.Contains(CatalogDiscoveryHealthBucket.Attention))
            {
                return CatalogDiscoveryHealthBucket.Attention;
            }

            if (buckets.Contains(CatalogDiscoveryHealthBucket.Degraded))
            {
                return CatalogDiscoveryHealthBucket.Degraded;
            }

            return CatalogDiscoveryHealthBucket.Unknown;
        }

        private DateTime? ResolveLatestSync(IReadOnlyList<int> sourceProfileIds)
        {
            return sourceProfileIds
                .Select(id => _sourceLastSyncById.TryGetValue(id, out var lastSyncUtc) ? lastSyncUtc : null)
                .Where(value => value.HasValue)
                .Max();
        }

        private static bool HasDiscoveryFilters(CatalogDiscoverySelection selection)
        {
            return !string.IsNullOrWhiteSpace(selection.SignalKey) &&
                   !string.Equals(selection.SignalKey, "all", StringComparison.OrdinalIgnoreCase) ||
                   !string.IsNullOrWhiteSpace(selection.SourceTypeKey) &&
                   !string.Equals(selection.SourceTypeKey, "all", StringComparison.OrdinalIgnoreCase) ||
                   !string.IsNullOrWhiteSpace(selection.LanguageKey) &&
                   !string.Equals(selection.LanguageKey, "all", StringComparison.OrdinalIgnoreCase) ||
                   !string.IsNullOrWhiteSpace(selection.TagKey) &&
                   !string.Equals(selection.TagKey, "all", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSelectedSeriesSlotState()
        {
            var selectedGroupKey = SelectedSeries?.GroupKey ?? string.Empty;
            foreach (var slot in DisplaySeriesSlots)
            {
                slot.IsSelected = slot.HasSeries &&
                                  string.Equals(slot.Series?.GroupKey, selectedGroupKey, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void CapturePreferredSeriesDetailContext()
        {
            _preferredSeasonNumber = SelectedSeason?.SeasonNumber;
            _preferredEpisodeId = SelectedEpisode?.Id;
            _preferredSeriesVariantId = SelectedSeries?.ActiveSeries.Id;
        }

        private void ClearPreferredSeriesDetailContext()
        {
            _preferredSeasonNumber = null;
            _preferredEpisodeId = null;
            _preferredSeriesVariantId = null;
        }

        private void PreserveSelectedSeriesContext(
            SeriesBrowseItemViewModel previousSelectedSeries,
            SeriesBrowseItemViewModel nextSelectedSeries,
            int? previousSelectedVariantId,
            int? previousSelectedSeasonNumber,
            int? previousSelectedEpisodeId)
        {
            _preserveSeriesDetailContextOnSelectionChange = true;
            _preferredSeasonNumber = previousSelectedSeasonNumber;
            _preferredEpisodeId = previousSelectedEpisodeId;
            _preferredSeriesVariantId = previousSelectedVariantId;

            if (previousSelectedVariantId.HasValue)
            {
                var preferredSourceOption = nextSelectedSeries.SourceOptions
                    .FirstOrDefault(option => option.Id == previousSelectedVariantId.Value);
                if (preferredSourceOption != null)
                {
                    nextSelectedSeries.SelectedSourceOption = preferredSourceOption;
                }
            }

            CopySeriesDetails(previousSelectedSeries.ActiveSeries, nextSelectedSeries.ActiveSeries);
        }

        private static void CopySeriesDetails(Series source, Series target)
        {
            if (source.Seasons is not { Count: > 0 } || target.Seasons is { Count: > 0 })
            {
                return;
            }

            target.Seasons = CopySeasons(source.Seasons);
        }

        private bool RequiresFullBrowseRefreshForFavoriteToggle()
        {
            return FavoritesOnly ||
                   string.Equals(SelectedSortOption?.Key ?? "recommended", "favorites_first", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSeriesGroupFavorite(CatalogSeriesGroup group)
        {
            return group.Variants.Any(variant =>
                _favoriteSeriesKeys.Contains(_logicalCatalogStateService.BuildSeriesLogicalKey(variant.Series)));
        }

        private void RefreshSeriesFavoriteState(string groupKey)
        {
            if (string.IsNullOrWhiteSpace(groupKey))
            {
                return;
            }

            var isFavorite = _allSeriesGroups
                .FirstOrDefault(group => string.Equals(group.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase)) is { } group &&
                IsSeriesGroupFavorite(group);

            foreach (var item in _allSeries.Where(item => string.Equals(item.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase)))
            {
                item.IsFavorite = isFavorite;
            }

            foreach (var slot in DisplaySeriesSlots.Where(slot => slot.HasSeries &&
                                                                  string.Equals(slot.Series?.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase)))
            {
                slot.RefreshFavoriteState();
            }
        }

        private void BuildSourceOptions()
        {
            var existingSelection = _browsePreferences.SelectedSourceId;
            var sourceOptions = new List<BrowseSourceFilterOptionViewModel>
            {
                new BrowseSourceFilterOptionViewModel(0, "All providers", _allSeriesGroups.Count)
            };
            var sourceVisibilityOptions = new List<BrowseSourceVisibilityViewModel>();

            foreach (var group in _allSeriesGroups
                         .SelectMany(item => item.Variants)
                         .GroupBy(variant => variant.SourceProfile.Id)
                         .Select(group => new { Id = group.Key, Name = group.First().SourceProfile.Name, Count = group.Count() })
                         .OrderBy(group => group.Name))
            {
                var isVisible = !_browsePreferences.HiddenSourceIds.Contains(group.Id);
                sourceOptions.Add(new BrowseSourceFilterOptionViewModel(group.Id, group.Name, group.Count));
                sourceVisibilityOptions.Add(new BrowseSourceVisibilityViewModel(group.Id, group.Name, $"{group.Count:N0} variants", isVisible, OnSourceVisibilityChanged));
            }

            var wasInitializingBrowsePreferences = _isInitializingBrowsePreferences;
            _isInitializingBrowsePreferences = true;
            try
            {
                var sourcePatch = SourceOptions.PatchToMatch(
                    sourceOptions,
                    static (existing, incoming) => existing.Id == incoming.Id,
                    static (existing, incoming) => existing.UpdateFrom(incoming));
                var visibilityPatch = SourceVisibilityOptions.PatchToMatch(
                    sourceVisibilityOptions,
                    static (existing, incoming) => existing.Id == incoming.Id,
                    static (existing, incoming) => existing.UpdateFrom(incoming));
                var selectedSourceOption = SourceOptions.FirstOrDefault(option => option.Id == existingSelection) ?? SourceOptions.FirstOrDefault();
                SelectedSourceOption = selectedSourceOption;
                LogBrowse(
                    $"source options patched reused={sourcePatch.ReusedCount} inserted={sourcePatch.InsertedCount} removed={sourcePatch.RemovedCount}; visibility_reused={visibilityPatch.ReusedCount} visibility_inserted={visibilityPatch.InsertedCount} visibility_removed={visibilityPatch.RemovedCount}");
            }
            finally
            {
                _isInitializingBrowsePreferences = wasInitializingBrowsePreferences;
            }
        }

        private BrowserCategoryViewModel? BuildVisibleCategories(string languageCode, string currentKey)
        {
            LogBrowse($"categories rebuild start preserveKey={currentKey}");

            var visibleSourceIds = SourceVisibilityOptions.Where(option => option.IsVisible).Select(option => option.Id).ToHashSet();
            if (visibleSourceIds.Count == 0)
            {
                visibleSourceIds = _allSeriesGroups.SelectMany(group => group.Variants).Select(variant => variant.SourceProfile.Id).ToHashSet();
            }

            var visibleGroups = _allSeriesGroups
                .Select(group =>
                {
                    var variants = group.Variants
                        .Where(variant => visibleSourceIds.Contains(variant.SourceProfile.Id))
                        .Where(variant => !HideSecondaryContent || string.Equals(variant.Series.ContentKind, "Primary", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (variants.Count == 0)
                    {
                        return null;
                    }

                    var preferredSeries = variants.FirstOrDefault(variant => variant.Series.Id == group.PreferredSeries.Id)?.Series ?? variants[0].Series;
                    var categoryProjection = GetCategoryProjection(preferredSeries);
                    if (IsCategoryHidden(categoryProjection.RawCategoryName))
                    {
                        return null;
                    }

                    return new CatalogSeriesGroup
                    {
                        GroupKey = group.GroupKey,
                        PreferredSeries = preferredSeries,
                        Variants = variants,
                        SourceSummary = BuildSourceSummary(variants.Select(variant => variant.SourceProfile.Name))
                    };
                })
                .OfType<CatalogSeriesGroup>()
                .ToList();

            var categoryItems = new List<BrowserCategoryViewModel>();
            var categoryIndex = 0;
            var smartContexts = visibleGroups
                .Select(BuildSeriesSmartCategoryContext)
                .ToList();

            foreach (var definition in _smartCategoryService.GetDefinitions(SmartCategoryMediaType.Series))
            {
                var count = definition.IsAllCategory
                    ? visibleGroups.Count
                    : smartContexts.Count(definition.Predicate);
                if (count <= 0 && !definition.AlwaysShow)
                {
                    continue;
                }

                categoryItems.Add(new BrowserCategoryViewModel
                {
                    Id = categoryIndex++,
                    FilterKey = definition.IsAllCategory ? string.Empty : definition.Id,
                    SmartCategoryId = definition.Id,
                    Name = definition.DisplayName,
                    Description = definition.MatchRule,
                    SectionName = definition.SectionName,
                    ItemCount = count,
                    OrderIndex = definition.SortPriority,
                    IsSmartCategory = true,
                    IconGlyph = definition.IconGlyph
                });
            }

            var providerCategories = visibleGroups
                .SelectMany(group => group.Variants
                    .Select(variant => new { group.GroupKey, RawCategory = GetRawCategory(variant.Series) })
                    .GroupBy(item => item.RawCategory, StringComparer.OrdinalIgnoreCase)
                    .Select(grouping => grouping.First()))
                .GroupBy(item => item.RawCategory, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { Name = group.Key, Count = group.Select(item => item.GroupKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() })
                .OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select((category, index) => new BrowserCategoryViewModel
                {
                    Id = categoryIndex++,
                    FilterKey = _smartCategoryService.BuildOriginalProviderGroupKey(category.Name),
                    Name = category.Name,
                    Description = ResolveDisplayCategoryName(category.Name),
                    SectionName = "Original Provider Groups",
                    ItemCount = category.Count,
                    OrderIndex = 100_000 + index,
                    IsOriginalProviderGroup = true,
                    IconGlyph = "\uE7C3"
                });

            categoryItems.AddRange(providerCategories);
            ApplyCategorySectionHeaders(categoryItems);

            var signature = BuildCategorySignature(categoryItems);
            if (!string.Equals(_visibleCategorySignature, signature, StringComparison.Ordinal))
            {
                Categories.ReplaceAll(categoryItems);
                _visibleCategorySignature = signature;
            }

            var resolvedCategory = !string.IsNullOrWhiteSpace(currentKey)
                ? Categories.FirstOrDefault(category => string.Equals(category.FilterKey, currentKey, StringComparison.OrdinalIgnoreCase))
                    ?? Categories.FirstOrDefault()
                : Categories.FirstOrDefault();

            LogBrowse($"categories rebuild end count={Categories.Count} resolvedKey={resolvedCategory?.FilterKey ?? "<all>"}");
            return resolvedCategory;
        }

        private static void ApplyCategorySectionHeaders(IReadOnlyList<BrowserCategoryViewModel> categories)
        {
            var previousSection = string.Empty;
            foreach (var category in categories)
            {
                category.SectionHeaderVisibility = string.IsNullOrWhiteSpace(category.SectionName) ||
                                                   string.Equals(category.SectionName, previousSection, StringComparison.Ordinal)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                previousSection = category.SectionName;
            }
        }

        private void ReassignSelectedCategory(BrowserCategoryViewModel? category, string reason)
        {
            var currentKey = SelectedCategory?.FilterKey ?? string.Empty;
            var targetKey = category?.FilterKey ?? string.Empty;
            var selectionAlreadyBound = ReferenceEquals(SelectedCategory, category);
            var selectionHasCurrentKey = SelectedCategory != null &&
                                         Categories.Contains(SelectedCategory) &&
                                         string.Equals(currentKey, targetKey, StringComparison.OrdinalIgnoreCase);

            if (selectionAlreadyBound || selectionHasCurrentKey)
            {
                UpdateCategorySelectionState(targetKey, $"rebind-skip:{reason}");
                LogBrowse($"category rebind skipped reason={reason} key={targetKey}");
                return;
            }

            LogBrowse($"category reassigned reason={reason} from={currentKey} to={targetKey}");
            SelectedCategory = category;
            UpdateCategorySelectionState(targetKey, $"rebind:{reason}");
        }

        private void UpdateCategorySelectionState(string selectedKey, string reason)
        {
            foreach (var category in Categories)
            {
                category.IsSelected = string.Equals(category.FilterKey, selectedKey, StringComparison.OrdinalIgnoreCase);
            }

            LogBrowse($"category selection state updated reason={reason} key={selectedKey}");
        }

        private void BuildCategoryManagerOptions()
        {
            var currentKey = SelectedManageCategory?.Key ?? string.Empty;
            var options = new List<BrowseCategoryManagerOptionViewModel>();

            foreach (var category in _allCategories.OrderBy(item => item.CategoryName, StringComparer.CurrentCultureIgnoreCase))
            {
                var key = NormalizeCategoryKey(category.CategoryName);
                var autoDisplayName = ResolveDisplayCategoryName(category.CategoryName);
                var hasManualAlias = _browsePreferences.CategoryRemaps.ContainsKey(key);
                options.Add(new BrowseCategoryManagerOptionViewModel(
                    key,
                    category.CategoryName,
                    GetEffectiveCategoryName(category.CategoryName, autoDisplayName),
                    autoDisplayName,
                    category.Count,
                    _browsePreferences.HiddenCategoryKeys.Contains(key, StringComparer.OrdinalIgnoreCase),
                    hasManualAlias));
            }

            ManageCategoryOptions.ReplaceAll(options);
            SelectedManageCategory = ManageCategoryOptions.FirstOrDefault(option => string.Equals(option.Key, currentKey, StringComparison.OrdinalIgnoreCase));
        }

        private async Task SaveBrowsePreferencesAsync(bool rebuildCollections)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var browsePreferencesService = scope.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
            await browsePreferencesService.SaveAsync(db, Domain, _activeProfileId, _browsePreferences);

            if (rebuildCollections)
            {
                BuildSourceOptions();
                BuildCategoryManagerOptions();
                LogBrowse("save preferences requested collection rebuild");
            }

            ApplyFilter(rebuildCollections ? "save-preferences-rebuild" : "save-preferences");
        }

        private IEnumerable<CatalogSeriesGroup> SortSeriesGroups(IEnumerable<CatalogSeriesGroup> groups)
        {
            return (SelectedSortOption?.Key ?? "recommended") switch
            {
                "title_asc" => groups.OrderBy(group => group.PreferredSeries.Title, StringComparer.CurrentCultureIgnoreCase),
                "rating_desc" => groups.OrderByDescending(group => group.PreferredSeries.VoteAverage).ThenByDescending(group => group.PreferredSeries.Popularity).ThenBy(group => group.PreferredSeries.Title, StringComparer.CurrentCultureIgnoreCase),
                "popularity_desc" => groups.OrderByDescending(group => group.PreferredSeries.Popularity).ThenByDescending(group => group.PreferredSeries.VoteAverage).ThenBy(group => group.PreferredSeries.Title, StringComparer.CurrentCultureIgnoreCase),
                "year_desc" => groups.OrderByDescending(group => group.PreferredSeries.FirstAirDate ?? DateTime.MinValue).ThenBy(group => group.PreferredSeries.Title, StringComparer.CurrentCultureIgnoreCase),
                "favorites_first" => groups.OrderByDescending(IsSeriesGroupFavorite).ThenBy(group => group.PreferredSeries.Title, StringComparer.CurrentCultureIgnoreCase),
                _ => groups
            };
        }

        private bool MatchesSeriesCategorySelection(
            CatalogSeriesGroup group,
            CatalogCategoryProjection categoryProjection,
            string selectedCategoryKey)
        {
            if (_smartCategoryService.TryParseOriginalProviderGroupKey(selectedCategoryKey, out var providerGroupKey))
            {
                return group.Variants.Any(variant => string.Equals(
                    _smartCategoryService.NormalizeKey(GetRawCategory(variant.Series)),
                    providerGroupKey,
                    StringComparison.OrdinalIgnoreCase));
            }

            if (_smartCategoryService.IsSmartCategoryKey(selectedCategoryKey))
            {
                return _smartCategoryService.Matches(selectedCategoryKey, BuildSeriesSmartCategoryContext(group));
            }

            return string.Equals(categoryProjection.DisplayCategoryKey, selectedCategoryKey, StringComparison.OrdinalIgnoreCase);
        }

        private SmartCategoryItemContext BuildSeriesSmartCategoryContext(CatalogSeriesGroup group)
        {
            var series = group.PreferredSeries;
            var states = group.Variants
                .Select(variant => _seriesSmartStateById.TryGetValue(variant.Series.Id, out var state) ? state : null)
                .OfType<SeriesSmartWatchState>()
                .ToList();
            var sourceProfileIds = group.Variants
                .Select(variant => variant.SourceProfile.Id)
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            var seasonCount = states.Count > 0
                ? states.Max(state => state.SeasonCount)
                : group.Variants.Max(variant => variant.Series.Seasons?.Count ?? 0);
            var episodeCount = states.Count > 0
                ? states.Max(state => state.EpisodeCount)
                : group.Variants.Max(variant => variant.EpisodeCount);
            var isCompleted = states.Any(state => state.IsCompleted);

            return new SmartCategoryItemContext
            {
                MediaType = SmartCategoryMediaType.Series,
                Title = series.Title,
                RawTitle = series.RawSourceTitle,
                ProviderGroupName = string.Join(' ', group.Variants.Select(variant => GetRawCategory(variant.Series)).Distinct(StringComparer.OrdinalIgnoreCase)),
                DisplayCategoryName = GetCategoryProjection(series).DisplayCategoryName,
                Genres = series.Genres,
                OriginalLanguage = series.OriginalLanguage,
                SourceName = group.SourceSummary,
                SourceSummary = group.SourceSummary,
                ReleaseDate = series.FirstAirDate,
                SourceLastSyncUtc = ResolveLatestSync(sourceProfileIds),
                VoteAverage = series.VoteAverage,
                Popularity = series.Popularity,
                VariantCount = group.Variants.Count,
                SeasonCount = seasonCount,
                EpisodeCount = episodeCount,
                IsFavorite = IsSeriesGroupFavorite(group),
                IsRecentlyWatched = states.Any(state => state.IsRecentlyWatched),
                IsContinueWatching = states.Any(state => state.HasResumePoint),
                IsWatched = isCompleted,
                IsInProgress = states.Any(state => state.HasResumePoint || state.HasAnyProgress && !state.IsCompleted),
                IsCompleted = isCompleted,
                HasMetadata = !string.IsNullOrWhiteSpace(series.TmdbId) ||
                              !string.IsNullOrWhiteSpace(series.ImdbId) ||
                              !string.IsNullOrWhiteSpace(series.Genres) ||
                              !string.IsNullOrWhiteSpace(series.Overview) ||
                              series.MetadataUpdatedAt.HasValue,
                HasArtwork = HasSeriesArtwork(series),
                IsPrimary = string.Equals(series.ContentKind, "Primary", StringComparison.OrdinalIgnoreCase),
                HasCompleteSeasons = states.Any(state => state.HasCompleteSeasons)
            };
        }

        private bool MatchesSeriesSearch(CatalogSeriesGroup group, string displayCategory)
        {
            return group.PreferredSeries.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                   displayCategory.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                   group.SourceSummary.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
        }

        private void OnSourceVisibilityChanged(BrowseSourceVisibilityViewModel option, bool isVisible)
        {
            if (_isInitializingBrowsePreferences)
            {
                return;
            }

            if (isVisible)
            {
                _browsePreferences.HiddenSourceIds.RemoveAll(id => id == option.Id);
            }
            else if (!_browsePreferences.HiddenSourceIds.Contains(option.Id))
            {
                _browsePreferences.HiddenSourceIds.Add(option.Id);
            }

            _ = SaveBrowsePreferencesAsync(true);
        }

        private void LogBrowse(string message)
        {
            BrowseRuntimeLogger.Log("SERIES", message);
        }

        private string NormalizeCategoryKey(string categoryName)
        {
            return _browsePreferencesService.NormalizeCategoryKey(categoryName);
        }

        private string GetEffectiveCategoryName(Series series)
        {
            var projection = GetCategoryProjection(series);
            return GetEffectiveCategoryName(projection.RawCategoryName, projection.DisplayCategoryName);
        }

        private string GetEffectiveCategoryName(string rawCategoryName, string defaultDisplayCategory)
        {
            return _browsePreferencesService.GetEffectiveCategoryName(_browsePreferences, rawCategoryName, defaultDisplayCategory);
        }

        private bool IsCategoryHidden(string rawCategoryName)
        {
            return _browsePreferencesService.IsCategoryHidden(_browsePreferences, rawCategoryName);
        }

        private CatalogCategoryProjection GetCategoryProjection(Series series)
        {
            if (_seriesCategoryById.TryGetValue(series.Id, out var projection))
            {
                return projection;
            }

            projection = BuildSeriesCategoryProjection(series);
            _seriesCategoryById[series.Id] = projection;
            return projection;
        }

        private CatalogCategoryProjection BuildSeriesCategoryProjection(Series series)
        {
            var rawCategory = GetRawCategory(series);
            var displayCategory = ResolveDisplayCategoryName(series);
            return new CatalogCategoryProjection(
                rawCategory,
                displayCategory,
                NormalizeCategoryKey(displayCategory));
        }

        private string ResolveDisplayCategoryName(Series series)
        {
            return _taxonomyService.ResolveSeriesCategory(
                series.CategoryName,
                series.RawSourceCategoryName,
                series.Title,
                series.OriginalLanguage).DisplayCategoryName;
        }

        private string ResolveDisplayCategoryName(string rawCategoryName)
        {
            return _taxonomyService.ResolveSeriesCategory(
                rawCategoryName,
                rawCategoryName,
                string.Empty,
                string.Empty).DisplayCategoryName;
        }

        private static string GetRawCategory(Series series)
        {
            return string.IsNullOrWhiteSpace(series.RawSourceCategoryName)
                ? GetRawCategory(series.CategoryName)
                : GetRawCategory(series.RawSourceCategoryName);
        }

        private static string GetRawCategory(string categoryName)
        {
            return string.IsNullOrWhiteSpace(categoryName) ? "Uncategorized" : categoryName.Trim();
        }

        private void StartMetadataEnrichment()
        {
            if (_metadataEnrichmentTask is { IsCompleted: false })
            {
                return;
            }

            _metadataEnrichmentCts?.Dispose();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _metadataEnrichmentCts = cts;
            var token = cts.Token;

            _metadataEnrichmentTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(750), token);
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var metadataService = scope.ServiceProvider.GetRequiredService<ITmdbMetadataService>();
                    var series = await db.Series.AsNoTracking().Take(28).ToListAsync(token);
                    await metadataService.EnrichSeriesAsync(db, series, 28, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
                catch
                {
                }
                finally
                {
                    if (ReferenceEquals(_metadataEnrichmentCts, cts))
                    {
                        _metadataEnrichmentCts = null;
                    }

                    cts.Dispose();
                }
            }, token);
        }

        private async Task EnsureSeriesDetailsAsync(SeriesBrowseItemViewModel item)
        {
            var series = item.ActiveSeries;
            await HydrateStoredSeriesDetailsAsync(series);

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

            if (!ReferenceEquals(SelectedSeries, item))
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
            await RefreshSelectedSeriesWatchStateAsync(series);

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

        private async Task HydrateStoredSeriesDetailsAsync(Series series)
        {
            if (series.Seasons is { Count: > 0 })
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storedSeries = await db.Series
                .AsNoTracking()
                .Include(s => s.Seasons!)
                .ThenInclude(season => season.Episodes)
                .FirstOrDefaultAsync(s => s.Id == series.Id);

            if (storedSeries?.Seasons is { Count: > 0 })
            {
                series.Seasons = CopySeasons(storedSeries.Seasons);
            }
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

        private void SelectedSeries_ActiveSeriesChanged(object? sender, EventArgs e)
        {
            if (sender is SeriesBrowseItemViewModel item && ReferenceEquals(SelectedSeries, item))
            {
                CapturePreferredSeriesDetailContext();
                _preserveSeriesDetailContextOnSelectionChange = true;
                _ = EnsureSeriesDetailsAsync(item);
            }
        }

        [RelayCommand]
        public async Task MarkEpisodeWatchedAsync(SeriesEpisodeItemViewModel? item)
        {
            if (item == null)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var watchStateService = scope.ServiceProvider.GetRequiredService<ILibraryWatchStateService>();
            await watchStateService.MarkWatchedAsync(db, _activeProfileId, PlaybackContentType.Episode, item.Id);
            if (SelectedSeries != null)
            {
                await RefreshSelectedSeriesWatchStateAsync(SelectedSeries.ActiveSeries);
            }
        }

        [RelayCommand]
        public async Task MarkEpisodeUnwatchedAsync(SeriesEpisodeItemViewModel? item)
        {
            if (item == null)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var watchStateService = scope.ServiceProvider.GetRequiredService<ILibraryWatchStateService>();
            await watchStateService.MarkUnwatchedAsync(db, _activeProfileId, PlaybackContentType.Episode, item.Id);
            if (SelectedSeries != null)
            {
                await RefreshSelectedSeriesWatchStateAsync(SelectedSeries.ActiveSeries);
            }
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

        private async Task RefreshSelectedSeriesWatchStateAsync(Series series)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var watchStateService = scope.ServiceProvider.GetRequiredService<ILibraryWatchStateService>();

            if (_activeProfileId <= 0)
            {
                var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
                _activeProfileId = await profileService.GetActiveProfileIdAsync(db);
            }

            _selectedEpisodeSnapshots.Clear();
            var episodeIds = (series.Seasons ?? Array.Empty<Season>())
                .SelectMany(season => season.Episodes ?? Array.Empty<Episode>())
                .Select(episode => episode.Id)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            var snapshots = await watchStateService.LoadSnapshotsAsync(db, _activeProfileId, PlaybackContentType.Episode, episodeIds);
            foreach (var snapshot in snapshots)
            {
                _selectedEpisodeSnapshots[snapshot.Key] = snapshot.Value;
            }

            BuildSeasonOptions(series);

            SeriesSeasonOptionViewModel? preferredSeason = null;
            if (_preferredSeasonNumber.HasValue)
            {
                preferredSeason = SeasonOptions.FirstOrDefault(option => option.SeasonNumber == _preferredSeasonNumber.Value);
            }

            var queueSelection = watchStateService.BuildSeriesQueueSelection(series, _selectedEpisodeSnapshots, includeWatched: !HideWatchedEpisodes);
            _pendingEpisodeSelectionId = preferredSeason != null
                ? _preferredEpisodeId
                : queueSelection?.Episode.Id;
            SelectedSeason = preferredSeason ??
                             (queueSelection != null
                                 ? SeasonOptions.FirstOrDefault(option => option.Id == queueSelection.Season.Id)
                                 : SeasonOptions.FirstOrDefault());

            ClearPreferredSeriesDetailContext();
        }

        private void BuildSeasonOptions(Series series)
        {
            var options = new List<SeriesSeasonOptionViewModel>();

            foreach (var season in (series.Seasons ?? Array.Empty<Season>()).OrderBy(item => item.SeasonNumber))
            {
                var playableEpisodes = (season.Episodes ?? Array.Empty<Episode>())
                    .Where(episode => !string.IsNullOrWhiteSpace(episode.StreamUrl))
                    .OrderBy(episode => episode.EpisodeNumber)
                    .ToList();

                if (playableEpisodes.Count == 0)
                {
                    continue;
                }

                var watchedEpisodeCount = playableEpisodes.Count(episode =>
                    _selectedEpisodeSnapshots.TryGetValue(episode.Id, out var snapshot) && snapshot.IsWatched);

                options.Add(new SeriesSeasonOptionViewModel(season, watchedEpisodeCount, playableEpisodes.Count));
            }

            ReplaceSeasonOptionsIfChanged(options);
            OnPropertyChanged(nameof(SeasonOptions));
        }

        private void BuildEpisodeItems(SeriesSeasonOptionViewModel? seasonOption, int? preferredEpisodeId)
        {
            SelectedEpisode = null;

            var season = seasonOption?.Season;
            if (season == null)
            {
                ReplaceEpisodeItemsIfChanged(Array.Empty<SeriesEpisodeItemViewModel>());
                OnPropertyChanged(nameof(EpisodesListVisibility));
                OnPropertyChanged(nameof(EpisodesEmptyVisibility));
                return;
            }

            var episodeItems = new List<SeriesEpisodeItemViewModel>();
            foreach (var episode in (season.Episodes ?? Array.Empty<Episode>())
                         .Where(episode => !string.IsNullOrWhiteSpace(episode.StreamUrl))
                         .OrderBy(episode => episode.EpisodeNumber))
            {
                _selectedEpisodeSnapshots.TryGetValue(episode.Id, out var snapshot);
                if (HideWatchedEpisodes && snapshot?.IsWatched == true)
                {
                    continue;
                }

                episodeItems.Add(new SeriesEpisodeItemViewModel(episode, season.SeasonNumber, snapshot));
            }

            ReplaceEpisodeItemsIfChanged(episodeItems);
            SelectedEpisode = preferredEpisodeId.HasValue
                ? EpisodeItems.FirstOrDefault(item => item.Id == preferredEpisodeId.Value)
                : EpisodeItems.FirstOrDefault(item => item.ResumePositionMs > 0) ?? EpisodeItems.FirstOrDefault();

            OnPropertyChanged(nameof(EpisodesListVisibility));
            OnPropertyChanged(nameof(EpisodesEmptyVisibility));
        }

        private void ReplaceSeasonOptionsIfChanged(IReadOnlyList<SeriesSeasonOptionViewModel> options)
        {
            if (SeasonOptions.Count == options.Count &&
                !SeasonOptions.Where((existing, index) =>
                        existing.Id != options[index].Id ||
                        existing.WatchedEpisodeCount != options[index].WatchedEpisodeCount ||
                        existing.TotalEpisodeCount != options[index].TotalEpisodeCount)
                    .Any())
            {
                return;
            }

            SeasonOptions.ReplaceAll(options);
        }

        private void ReplaceEpisodeItemsIfChanged(IReadOnlyList<SeriesEpisodeItemViewModel> items)
        {
            if (EpisodeItems.Count == items.Count &&
                !EpisodeItems.Where((existing, index) =>
                        existing.Id != items[index].Id ||
                        existing.ResumePositionMs != items[index].ResumePositionMs ||
                        existing.IsWatched != items[index].IsWatched)
                    .Any())
            {
                return;
            }

            EpisodeItems.ReplaceAll(items);
        }

        private static string BuildCategorySignature(IEnumerable<BrowserCategoryViewModel> categories)
        {
            return string.Join("|", categories.Select(category => $"{category.FilterKey}:{category.SectionName}:{category.Name}:{category.ItemCount}:{category.IconGlyph}:{category.OrderIndex}"));
        }

        private async Task SaveHideWatchedEpisodesAsync(bool value)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var watchStateService = scope.ServiceProvider.GetRequiredService<ILibraryWatchStateService>();
            await watchStateService.SetHideWatchedEpisodesAsync(db, _activeProfileId, value);
            if (SelectedSeries != null)
            {
                await RefreshSelectedSeriesWatchStateAsync(SelectedSeries.ActiveSeries);
            }
        }

        private static CatalogSeriesGroup? FilterGroup(CatalogSeriesGroup group, ProfileAccessSnapshot access)
        {
            var variants = group.Variants
                .Where(variant => access.IsSeriesAllowed(variant.Series))
                .ToList();

            if (variants.Count == 0)
            {
                return null;
            }

            return new CatalogSeriesGroup
            {
                GroupKey = group.GroupKey,
                PreferredSeries = variants[0].Series,
                Variants = variants,
                SourceSummary = BuildSourceSummary(variants.Select(variant => variant.SourceProfile.Name))
            };
        }

        private static string BuildSourceSummary(IEnumerable<string> sourceNames)
        {
            var distinct = sourceNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (distinct.Count == 0)
            {
                return string.Empty;
            }

            if (distinct.Count == 1)
            {
                return distinct[0];
            }

            if (distinct.Count == 2)
            {
                return $"{distinct[0]} + {distinct[1]}";
            }

            return $"{distinct[0]} +{distinct.Count - 1} more";
        }
    }
}
