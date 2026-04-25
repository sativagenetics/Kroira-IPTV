#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
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
    public partial class MovieBrowseItemViewModel : ObservableObject
    {
        public MovieBrowseItemViewModel(CatalogMovieGroup group, bool isFavorite, string displayCategoryName)
        {
            Group = group;
            DisplayCategoryName = displayCategoryName;
            IsFavorite = isFavorite;
        }

        public CatalogMovieGroup Group { get; private set; }
        public Movie Movie => Group.PreferredMovie;
        public IReadOnlyList<CatalogMovieVariant> Variants => Group.Variants;
        public IReadOnlyList<int> VariantIds => Group.Variants.Select(variant => variant.Movie.Id).ToList();
        public string GroupKey => Group.GroupKey;
        public int Id => Movie.Id;
        public string Title => Movie.Title;
        public string StreamUrl => Movie.StreamUrl;
        public string DisplayPosterUrl => Movie.DisplayPosterUrl;
        public string DisplayHeroArtworkUrl => Movie.DisplayHeroArtworkUrl;
        public string RatingText => Movie.RatingText;
        public Visibility RatingVisibility => string.IsNullOrWhiteSpace(RatingText) ? Visibility.Collapsed : Visibility.Visible;
        public string MetadataLine
        {
            get
            {
                var parts = new[]
                {
                    Movie.DisplayYear,
                    string.IsNullOrWhiteSpace(Movie.Genres) ? DisplayCategoryName : Movie.Genres,
                    string.IsNullOrWhiteSpace(Movie.OriginalLanguage) ? string.Empty : Movie.OriginalLanguage.ToUpperInvariant()
                };

                var metadata = string.Join(" / ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
                return Group.Variants.Count > 1 && !string.IsNullOrWhiteSpace(Group.SourceSummary)
                    ? string.IsNullOrWhiteSpace(metadata) ? Group.SourceSummary : $"{metadata} / {Group.SourceSummary}"
                    : metadata;
            }
        }
        public string Overview => Movie.Overview;
        public string CategoryName => Movie.CategoryName;
        public double Popularity => Movie.Popularity;
        public double VoteAverage => Movie.VoteAverage;
        public string BackdropUrl => Movie.BackdropUrl;
        public string TmdbBackdropPath => Movie.TmdbBackdropPath;
        public string PosterUrl => Movie.PosterUrl;
        public string TmdbPosterPath => Movie.TmdbPosterPath;
        public bool HasAlternateSources => Group.Variants.Count > 1;
        public string SourceSummary => Group.SourceSummary;
        public string DisplayCategoryName { get; private set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FavoriteGlyph))]
        [NotifyPropertyChangedFor(nameof(FavoriteLabel))]
        private bool _isFavorite;

        public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";
        public string FavoriteLabel => IsFavorite ? LocalizedStrings.Get("General_Saved") : LocalizedStrings.Get("General_Save");

        public void UpdateFrom(CatalogMovieGroup group, bool isFavorite, string displayCategoryName)
        {
            Group = group;
            DisplayCategoryName = displayCategoryName;
            IsFavorite = isFavorite;
            RefreshContentState();
        }

        public void RefreshContentState()
        {
            OnPropertyChanged(nameof(Group));
            OnPropertyChanged(nameof(Movie));
            OnPropertyChanged(nameof(Variants));
            OnPropertyChanged(nameof(VariantIds));
            OnPropertyChanged(nameof(GroupKey));
            OnPropertyChanged(nameof(Id));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(StreamUrl));
            OnPropertyChanged(nameof(DisplayPosterUrl));
            OnPropertyChanged(nameof(DisplayHeroArtworkUrl));
            OnPropertyChanged(nameof(RatingText));
            OnPropertyChanged(nameof(RatingVisibility));
            OnPropertyChanged(nameof(MetadataLine));
            OnPropertyChanged(nameof(Overview));
            OnPropertyChanged(nameof(CategoryName));
            OnPropertyChanged(nameof(DisplayCategoryName));
            OnPropertyChanged(nameof(Popularity));
            OnPropertyChanged(nameof(VoteAverage));
            OnPropertyChanged(nameof(BackdropUrl));
            OnPropertyChanged(nameof(TmdbBackdropPath));
            OnPropertyChanged(nameof(PosterUrl));
            OnPropertyChanged(nameof(TmdbPosterPath));
            OnPropertyChanged(nameof(HasAlternateSources));
            OnPropertyChanged(nameof(SourceSummary));
            OnPropertyChanged(nameof(FavoriteLabel));
        }

        public void RefreshLocalizedText()
        {
            OnPropertyChanged(nameof(FavoriteLabel));
        }
    }

    public partial class MovieBrowseSlotViewModel : ObservableObject
    {
        public MovieBrowseSlotViewModel(MovieBrowseItemViewModel? movie)
        {
            _movie = movie;
        }

        private MovieBrowseItemViewModel? _movie;

        public MovieBrowseItemViewModel? Movie => _movie;
        public bool HasMovie => Movie != null;
        public int Id => Movie?.Id ?? 0;
        public string Title => Movie?.Title ?? string.Empty;
        public string DisplayPosterUrl => Movie?.DisplayPosterUrl ?? string.Empty;
        public string RatingText => Movie?.RatingText ?? string.Empty;
        public string MetadataLine => Movie?.MetadataLine ?? string.Empty;
        public string FavoriteGlyph => Movie?.FavoriteGlyph ?? "\uE734";
        public string FavoriteLabel => Movie?.FavoriteLabel ?? string.Empty;
        public Visibility MovieVisibility => HasMovie ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PlaceholderVisibility => HasMovie ? Visibility.Collapsed : Visibility.Visible;

        public void RefreshFavoriteState()
        {
            OnPropertyChanged(nameof(FavoriteGlyph));
            OnPropertyChanged(nameof(FavoriteLabel));
        }

        public bool Matches(MovieBrowseSlotViewModel other)
        {
            return Matches(other.Movie);
        }

        public bool Matches(MovieBrowseItemViewModel? movie)
        {
            if (!HasMovie && movie == null)
            {
                return true;
            }

            return HasMovie &&
                   movie != null &&
                   string.Equals(Movie?.GroupKey, movie.GroupKey, StringComparison.OrdinalIgnoreCase);
        }

        public void UpdateFrom(MovieBrowseItemViewModel? movie)
        {
            _movie = movie;
            RefreshMovieState();
        }

        public void RefreshMovieState()
        {
            OnPropertyChanged(nameof(Movie));
            OnPropertyChanged(nameof(HasMovie));
            OnPropertyChanged(nameof(Id));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayPosterUrl));
            OnPropertyChanged(nameof(RatingText));
            OnPropertyChanged(nameof(MetadataLine));
            OnPropertyChanged(nameof(FavoriteGlyph));
            OnPropertyChanged(nameof(FavoriteLabel));
            OnPropertyChanged(nameof(MovieVisibility));
            OnPropertyChanged(nameof(PlaceholderVisibility));
        }
    }

    public partial class MoviesViewModel : ObservableObject
    {
        private const string Domain = ProfileDomains.Movies;
        private const string FixedFeaturedMovieTitle = "Kurtlar Vadisi Gladio";
        private const int BrowseGridColumns = 6;
        private const int InitialDisplayMovieSlotBatchSize = BrowseGridColumns * 2;
        private const int DeferredSlotAppendBatchSize = BrowseGridColumns * 8;
        private const int SearchApplyDebounceMilliseconds = 180;
        private readonly IServiceProvider _serviceProvider;
        private readonly IBrowsePreferencesService _browsePreferencesService;
        private readonly ICatalogDiscoveryService _catalogDiscoveryService;
        private readonly ICatalogTaxonomyService _taxonomyService;
        private readonly ISmartCategoryService _smartCategoryService;
        private readonly ILogicalCatalogStateService _logicalCatalogStateService;
        private readonly List<CatalogMovieGroup> _allMovieGroups = new();
        private readonly Dictionary<int, CatalogCategoryProjection> _movieCategoryById = new();
        private readonly Dictionary<int, WatchProgressSnapshot> _movieWatchSnapshotsById = new();
        private readonly Dictionary<string, string> _movieSearchTextByGroupKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, SourceType> _sourceTypeById = new();
        private readonly Dictionary<int, CatalogDiscoveryHealthBucket> _sourceHealthById = new();
        private readonly Dictionary<int, DateTime?> _sourceLastSyncById = new();
        private readonly List<(string CategoryName, int Count)> _allCategories = new();
        private static readonly int _sessionRotationIndex = Math.Abs(Environment.TickCount % 5);
        private List<MovieBrowseItemViewModel> _filteredMovies = new();
        private BrowsePreferences _preferences = new();
        private HashSet<string> _favoriteMovieKeys = new(StringComparer.OrdinalIgnoreCase);
        private SmartCategoryIndex<CatalogMovieGroup> _movieCategoryIndex = SmartCategoryIndex<CatalogMovieGroup>.Empty;
        private int _activeProfileId;
        private string _languageCode = AppLanguageService.DefaultLanguageCode;
        private string _visibleCategorySignature = string.Empty;
        private int _localizedTextVersion = -1;
        private int _lastBaseResultCount;
        private bool _lastHasDiscoveryFacetFilters;
        private bool _isInitializing;
        private bool _isApplyingFilter;
        private bool _pendingApplyFilter;
        private bool _hasLoadedOnce;
        private bool _preferStagedFirstPaint;
        private string _pendingApplyFilterReason = string.Empty;
        private int _movieSlotRefreshVersion;
        private int _searchApplyRequestVersion;
        private CancellationTokenSource? _metadataEnrichmentCts;
        private Task? _metadataEnrichmentTask;
        private Stopwatch? _activeLoadStopwatch;
        private Task _displayMovieSlotRefreshTask = Task.CompletedTask;

        public IReadOnlyList<MovieBrowseItemViewModel> FilteredMovies => _filteredMovies;
        public int FilteredMovieCount => _filteredMovies.Count;
        public bool HasLoadedOnce => _hasLoadedOnce;
        public Visibility ContentShellVisibility => IsBlockingSurfaceState ? Visibility.Collapsed : Visibility.Visible;
        public Visibility BlockingSurfaceVisibility => IsBlockingSurfaceState ? Visibility.Visible : Visibility.Collapsed;
        public BulkObservableCollection<MovieBrowseSlotViewModel> DisplayMovieSlots { get; } = new();
        public BulkObservableCollection<BrowserCategoryViewModel> Categories { get; } = new();
        public ObservableCollection<BrowseSortOptionViewModel> SortOptions { get; } = new();
        public BulkObservableCollection<BrowseSourceFilterOptionViewModel> SourceOptions { get; } = new();
        public BulkObservableCollection<BrowseSourceVisibilityViewModel> SourceVisibilityOptions { get; } = new();
        public BulkObservableCollection<BrowseCategoryManagerOptionViewModel> ManageCategoryOptions { get; } = new();
        public BulkObservableCollection<BrowseFacetOptionViewModel> DiscoverySignalOptions { get; } = new();
        public BulkObservableCollection<BrowseFacetOptionViewModel> DiscoverySourceTypeOptions { get; } = new();
        public BulkObservableCollection<BrowseFacetOptionViewModel> DiscoveryLanguageOptions { get; } = new();
        public BulkObservableCollection<BrowseFacetOptionViewModel> DiscoveryTagOptions { get; } = new();

        [ObservableProperty] private string _searchQuery = string.Empty;
        [ObservableProperty] private BrowserCategoryViewModel? _selectedCategory;
        [ObservableProperty] private BrowseSortOptionViewModel? _selectedSortOption;
        [ObservableProperty] private BrowseSourceFilterOptionViewModel? _selectedSourceOption;
        [ObservableProperty] private BrowseFacetOptionViewModel? _selectedDiscoverySignalOption;
        [ObservableProperty] private BrowseFacetOptionViewModel? _selectedDiscoverySourceTypeOption;
        [ObservableProperty] private BrowseFacetOptionViewModel? _selectedDiscoveryLanguageOption;
        [ObservableProperty] private BrowseFacetOptionViewModel? _selectedDiscoveryTagOption;
        [ObservableProperty] private BrowseCategoryManagerOptionViewModel? _selectedManageCategory;
        [ObservableProperty] private string _manageCategoryAliasDraft = string.Empty;
        [ObservableProperty] private bool _isManageCategoryHidden;
        [ObservableProperty] private bool _favoritesOnly;
        [ObservableProperty] private bool _hideSecondaryContent;
        [ObservableProperty] private bool _hasAdvancedFilters;
        [ObservableProperty] private bool _isEmpty;
        [ObservableProperty] private string _discoverySummaryText = LocalizedStrings.Get("Movies_DiscoverySummary_Default");
        [ObservableProperty] private string _emptyStateTitle = LocalizedStrings.Get("Movies_Empty_NoMovies_Title");
        [ObservableProperty] private string _emptyStateMessage = LocalizedStrings.Get("Movies_Empty_NoMovies_Message");
        [ObservableProperty] private SurfaceStatePresentation _surfaceState = SurfaceStateCopies.Movies.Create(SurfaceViewState.Loading);
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FeaturedMovieCanPlay))]
        private MovieBrowseItemViewModel _featuredMovie = CreatePlaceholderFeaturedMovie();

        public bool FeaturedMovieCanPlay => !string.IsNullOrWhiteSpace(FeaturedMovie?.StreamUrl);
        public bool HasManageCategorySelection => SelectedManageCategory != null;
        public Visibility DiscoverySignalVisibility => DiscoverySignalOptions.Count > 2 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DiscoverySourceTypeVisibility => DiscoverySourceTypeOptions.Count > 2 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DiscoveryLanguageVisibility => DiscoveryLanguageOptions.Count > 2 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DiscoveryTagVisibility => DiscoveryTagOptions.Count > 2 ? Visibility.Visible : Visibility.Collapsed;
        private bool IsBlockingSurfaceState =>
            SurfaceState.State is SurfaceViewState.NoSources or SurfaceViewState.Offline or SurfaceViewState.ImportFailed;

        private sealed record FilteredMovieResult(CatalogMovieGroup Group, string DisplayCategoryName);

        partial void OnSurfaceStateChanged(SurfaceStatePresentation value)
        {
            OnPropertyChanged(nameof(ContentShellVisibility));
            OnPropertyChanged(nameof(BlockingSurfaceVisibility));
        }

        public MoviesViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _browsePreferencesService = serviceProvider.GetRequiredService<IBrowsePreferencesService>();
            _catalogDiscoveryService = serviceProvider.GetRequiredService<ICatalogDiscoveryService>();
            _taxonomyService = serviceProvider.GetRequiredService<ICatalogTaxonomyService>();
            _smartCategoryService = serviceProvider.GetRequiredService<ISmartCategoryService>();
            _logicalCatalogStateService = serviceProvider.GetRequiredService<ILogicalCatalogStateService>();
            RebuildSortOptions();
            DiscoverySignalOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DiscoverySignalVisibility));
            DiscoverySourceTypeOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DiscoverySourceTypeVisibility));
            DiscoveryLanguageOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DiscoveryLanguageVisibility));
            DiscoveryTagOptions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DiscoveryTagVisibility));
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
                $"category changed key={value.FilterKey} name={value.Name} initializing={_isInitializing} applying={_isApplyingFilter}");
            if (!_isInitializing)
            {
                ApplyFilter("selected-category-changed");
            }
        }
        partial void OnSelectedSortOptionChanged(BrowseSortOptionViewModel? value) { if (!_isInitializing) { _preferences.SortKey = value?.Key ?? "recommended"; _ = SavePreferencesAsync(false); } }
        partial void OnSelectedSourceOptionChanged(BrowseSourceFilterOptionViewModel? value) { if (!_isInitializing) { _preferences.SelectedSourceId = value?.Id ?? 0; _ = SavePreferencesAsync(false); } }
        partial void OnSelectedDiscoverySignalOptionChanged(BrowseFacetOptionViewModel? value) { if (!_isInitializing) { _preferences.DiscoverySignalKey = value?.Key ?? string.Empty; _ = SavePreferencesAsync(false); } }
        partial void OnSelectedDiscoverySourceTypeOptionChanged(BrowseFacetOptionViewModel? value) { if (!_isInitializing) { _preferences.DiscoverySourceTypeKey = value?.Key ?? string.Empty; _ = SavePreferencesAsync(false); } }
        partial void OnSelectedDiscoveryLanguageOptionChanged(BrowseFacetOptionViewModel? value) { if (!_isInitializing) { _preferences.DiscoveryLanguageKey = value?.Key ?? string.Empty; _ = SavePreferencesAsync(false); } }
        partial void OnSelectedDiscoveryTagOptionChanged(BrowseFacetOptionViewModel? value) { if (!_isInitializing) { _preferences.DiscoveryTagKey = value?.Key ?? string.Empty; _ = SavePreferencesAsync(false); } }
        partial void OnFavoritesOnlyChanged(bool value) { if (!_isInitializing) { _preferences.FavoritesOnly = value; _ = SavePreferencesAsync(false); } }
        partial void OnHideSecondaryContentChanged(bool value) { if (!_isInitializing) { _preferences.HideSecondaryContent = value; _ = SavePreferencesAsync(true); } }

        partial void OnSelectedManageCategoryChanged(BrowseCategoryManagerOptionViewModel? value)
        {
            if (value == null)
            {
                ManageCategoryAliasDraft = string.Empty;
                IsManageCategoryHidden = false;
                OnPropertyChanged(nameof(HasManageCategorySelection));
                return;
            }

            _isInitializing = true;
            try
            {
                ManageCategoryAliasDraft = value.HasManualAlias ? value.EffectiveName : string.Empty;
                IsManageCategoryHidden = value.IsHidden;
            }
            finally
            {
                _isInitializing = false;
            }

            OnPropertyChanged(nameof(HasManageCategorySelection));
        }

        [RelayCommand]
        public async Task LoadMoviesAsync()
        {
            RefreshLocalizedLabelsIfNeeded();
            var loadStopwatch = Stopwatch.StartNew();
            _activeLoadStopwatch = loadStopwatch;
            _preferStagedFirstPaint = !_hasLoadedOnce || DisplayMovieSlots.Count == 0;
            if (!_hasLoadedOnce || DisplayMovieSlots.Count == 0)
            {
                SurfaceState = SurfaceStateCopies.Movies.Create(SurfaceViewState.Loading);
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
                    SurfaceState = surfaceStateService.ResolveSourceBackedState(sourceAvailability, 0, SurfaceStateCopies.Movies);
                    return;
                }

                var access = await profileService.GetAccessSnapshotAsync(db);
                _activeProfileId = access.ProfileId;
                await _logicalCatalogStateService.ReconcileFavoritesAsync(db, access.ProfileId);
                _languageCode = await AppLanguageService.GetLanguageAsync(db, access.ProfileId);
                _preferences = await browsePreferencesService.GetAsync(db, Domain, _activeProfileId);
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

                var movieGroups = (await deduplicationService.LoadMovieGroupsAsync(db))
                    .Select(group => FilterGroup(group, access))
                    .OfType<CatalogMovieGroup>()
                    .ToList();
                _favoriteMovieKeys = await _logicalCatalogStateService.GetFavoriteLogicalKeysAsync(db, access.ProfileId, FavoriteType.Movie);
                _movieWatchSnapshotsById.Clear();
                foreach (var snapshot in await watchStateService.LoadSnapshotsAsync(
                             db,
                             access.ProfileId,
                             PlaybackContentType.Movie,
                             movieGroups.SelectMany(group => group.Variants).Select(variant => variant.Movie.Id)))
                {
                    _movieWatchSnapshotsById[snapshot.Key] = snapshot.Value;
                }

                var movieOrderEntries = movieGroups
                    .Select(group => new
                    {
                        Group = group,
                        CategoryProjection = BuildMovieCategoryProjection(group.PreferredMovie)
                    })
                    .ToList();

                _allMovieGroups.Clear();
                _allMovieGroups.AddRange(CatalogOrderingService.OrderCatalog(
                    movieOrderEntries,
                    _languageCode,
                    entry => entry.CategoryProjection.DisplayCategoryName,
                    entry => entry.Group.PreferredMovie.Title)
                    .Select(entry => entry.Group));

                _movieCategoryById.Clear();
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

                foreach (var group in _allMovieGroups)
                {
                    foreach (var variant in group.Variants)
                    {
                        _movieCategoryById[variant.Movie.Id] = BuildMovieCategoryProjection(variant.Movie);
                        _sourceTypeById[variant.SourceProfile.Id] = variant.SourceProfile.Type;
                    }
                }

                _allCategories.Clear();
                _allCategories.AddRange(_allMovieGroups
                    .GroupBy(group => GetCategoryProjection(group.PreferredMovie).RawCategoryName)
                    .Select(group => (group.Key, group.Count()))
                    .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase));

                BuildSourceOptions();

                _isInitializing = true;
                try
                {
                    FavoritesOnly = _preferences.FavoritesOnly;
                    HideSecondaryContent = _preferences.HideSecondaryContent;
                    SelectedSortOption = SortOptions.FirstOrDefault(option => string.Equals(option.Key, _preferences.SortKey, StringComparison.OrdinalIgnoreCase))
                        ?? SortOptions.First();
                    SelectedSourceOption = SourceOptions.FirstOrDefault(option => option.Id == _preferences.SelectedSourceId)
                        ?? SourceOptions.FirstOrDefault();
                    var selectedCategory = BuildVisibleCategories(SelectedCategory?.FilterKey ?? string.Empty);
                    ReassignSelectedCategory(selectedCategory, "load-initial");
                }
                finally
                {
                    _isInitializing = false;
                }

                ApplyFilter("load-movies");
                SurfaceState = surfaceStateService.ResolveSourceBackedState(sourceAvailability, _allMovieGroups.Count, SurfaceStateCopies.Movies);
                _hasLoadedOnce = true;
                OnPropertyChanged(nameof(HasLoadedOnce));
                await Task.Yield();
                BuildCategoryManagerOptions();
                LogBrowse(
                    $"load ready fullMs={loadStopwatch.ElapsedMilliseconds} groups={_allMovieGroups.Count} results={_filteredMovies.Count} slots={DisplayMovieSlots.Count}");
                StartMetadataEnrichment();
            }
            catch (Exception ex)
            {
                BrowseRuntimeLogger.Log("MOVIES", $"load failed {ex}");
                SurfaceState = _serviceProvider.GetRequiredService<ISurfaceStateService>().CreateFailureState(SurfaceStateCopies.Movies, ex);
            }
            finally
            {
                _activeLoadStopwatch = null;
            }
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
                if (!_preferences.HiddenCategoryKeys.Contains(SelectedManageCategory.Key, StringComparer.OrdinalIgnoreCase))
                {
                    _preferences.HiddenCategoryKeys.Add(SelectedManageCategory.Key);
                }
            }
            else
            {
                _preferences.HiddenCategoryKeys.RemoveAll(value => string.Equals(value, SelectedManageCategory.Key, StringComparison.OrdinalIgnoreCase));
            }

            var alias = ManageCategoryAliasDraft.Trim();
            if (string.IsNullOrWhiteSpace(alias) ||
                string.Equals(alias, SelectedManageCategory.RawName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(alias, SelectedManageCategory.AutoDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                _preferences.CategoryRemaps.Remove(SelectedManageCategory.Key);
            }
            else
            {
                _preferences.CategoryRemaps[SelectedManageCategory.Key] = alias;
            }

            await SavePreferencesAsync(true);
        }

        [RelayCommand]
        public async Task ClearCategoryPreferenceAsync()
        {
            if (SelectedManageCategory == null)
            {
                return;
            }

            _preferences.HiddenCategoryKeys.RemoveAll(value => string.Equals(value, SelectedManageCategory.Key, StringComparison.OrdinalIgnoreCase));
            _preferences.CategoryRemaps.Remove(SelectedManageCategory.Key);
            _isInitializing = true;
            try
            {
                ManageCategoryAliasDraft = string.Empty;
                IsManageCategoryHidden = false;
            }
            finally
            {
                _isInitializing = false;
            }

            await SavePreferencesAsync(true);
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

        private void ApplyFilterCore(string reason)
        {
            var totalStopwatch = Stopwatch.StartNew();
            string currentCategoryKey = SelectedCategory?.FilterKey ?? string.Empty;
            LogBrowse(
                $"PERF apply_start reason={reason} selectedKey={currentCategoryKey} search='{SearchQuery}' source={SelectedSourceOption?.Id ?? 0}");

            var filterStopwatch = Stopwatch.StartNew();
            var baseResults = BuildFilteredMovieResults().ToList();
            LogBrowse($"PERF filter_compose reason={reason} groups={baseResults.Count} ms={filterStopwatch.ElapsedMilliseconds}");
            var discoveryStopwatch = Stopwatch.StartNew();
            var discoveryProjection = BuildDiscoveryProjection(baseResults, DateTime.UtcNow);
            RefreshDiscoveryOptions(discoveryProjection);
            var filteredResults = baseResults
                .Where(result => discoveryProjection.MatchingKeys.Contains(result.Group.GroupKey))
                .ToList();
            LogBrowse($"PERF discovery reason={reason} groups={filteredResults.Count} ms={discoveryStopwatch.ElapsedMilliseconds}");

            if (ShouldRebuildCategoryRail(reason))
            {
                _isInitializing = true;
                try
                {
                    var selectedCategory = BuildVisibleCategories(currentCategoryKey);
                    ReassignSelectedCategory(selectedCategory, $"apply:{reason}");
                }
                finally
                {
                    _isInitializing = false;
                }
            }

            var uiStopwatch = Stopwatch.StartNew();
            PatchFilteredMovies(filteredResults);
            OnPropertyChanged(nameof(FilteredMovies));
            OnPropertyChanged(nameof(FilteredMovieCount));
            LogBrowse($"PERF ui_items reason={reason} results={_filteredMovies.Count} ms={uiStopwatch.ElapsedMilliseconds}");

            RefreshFeaturedMovie(_filteredMovies);
            DiscoverySummaryText = discoveryProjection.SummaryText;
            IsEmpty = _filteredMovies.Count == 0;
            HasAdvancedFilters = FavoritesOnly ||
                                 HideSecondaryContent ||
                                 SourceVisibilityOptions.Any(option => !option.IsVisible) ||
                                 _preferences.HiddenCategoryKeys.Count > 0 ||
                                 _preferences.CategoryRemaps.Count > 0 ||
                                 HasDiscoveryFilters(discoveryProjection.EffectiveSelection) ||
                                 (SelectedSourceOption?.Id ?? 0) != 0 ||
                                 !string.Equals(SelectedSortOption?.Key ?? "recommended", "recommended", StringComparison.OrdinalIgnoreCase);
            _lastBaseResultCount = baseResults.Count;
            _lastHasDiscoveryFacetFilters = discoveryProjection.HasActiveFacetFilters;
            UpdateEmptyState(_lastBaseResultCount, _lastHasDiscoveryFacetFilters);
            var shouldStageFirstPaint = _preferStagedFirstPaint;
            _preferStagedFirstPaint = false;
            _displayMovieSlotRefreshTask = RefreshDisplayMovieSlotsAsync(shouldStageFirstPaint, reason);
            LogBrowse(
                $"PERF apply_end reason={reason} groups={filteredResults.Count} results={_filteredMovies.Count} slots={DisplayMovieSlots.Count} selectedKey={SelectedCategory?.FilterKey ?? "<all>"} total_ms={totalStopwatch.ElapsedMilliseconds}");
        }

        [RelayCommand]
        public async Task ToggleFavoriteAsync(int movieId)
        {
            var group = _allMovieGroups.FirstOrDefault(item => item.Variants.Any(variant => variant.Movie.Id == movieId));
            if (group == null)
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var activeProfileId = await profileService.GetActiveProfileIdAsync(db);
            var targetMovie = group.Variants.First(variant => variant.Movie.Id == movieId).Movie;
            var logicalKey = _logicalCatalogStateService.BuildMovieLogicalKey(targetMovie);
            var isFavorite = await _logicalCatalogStateService.ToggleFavoriteAsync(db, activeProfileId, FavoriteType.Movie, movieId);
            if (isFavorite)
            {
                _favoriteMovieKeys.Add(logicalKey);
            }
            else
            {
                _favoriteMovieKeys.Remove(logicalKey);
            }

            if (RequiresFullBrowseRefreshForFavoriteToggle())
            {
                ApplyFilter("toggle-favorite");
                return;
            }

            RefreshMovieFavoriteState(group.GroupKey);
            var selectedCategory = BuildVisibleCategories(SelectedCategory?.FilterKey ?? string.Empty);
            ReassignSelectedCategory(selectedCategory, "toggle-favorite-lite");
        }

        private IEnumerable<FilteredMovieResult> BuildFilteredMovieResults()
        {
            var visibleSourceIds = SourceVisibilityOptions
                .Where(option => option.IsVisible)
                .Select(option => option.Id)
                .ToHashSet();
            if (visibleSourceIds.Count == 0)
            {
                visibleSourceIds = _allMovieGroups.SelectMany(group => group.Variants).Select(variant => variant.SourceProfile.Id).ToHashSet();
            }

            var selectedCategoryKey = SelectedCategory?.FilterKey ?? string.Empty;
            var selectedSourceId = SelectedSourceOption?.Id ?? 0;
            var results = new List<FilteredMovieResult>();
            var candidateGroups = _movieCategoryIndex.AllItems.Count > 0
                ? _movieCategoryIndex.GetItems(selectedCategoryKey)
                : _allMovieGroups;
            var normalizedSearchQuery = _smartCategoryService.NormalizeKey(SearchQuery);

            foreach (var group in candidateGroups)
            {
                var variants = group.Variants
                    .Where(variant => visibleSourceIds.Contains(variant.SourceProfile.Id))
                    .Where(variant => selectedSourceId == 0 || variant.SourceProfile.Id == selectedSourceId)
                    .Where(variant => !HideSecondaryContent || string.Equals(variant.Movie.ContentKind, "Primary", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (variants.Count == 0)
                {
                    continue;
                }

                var preferredMovie = variants.FirstOrDefault(variant => variant.Movie.Id == group.PreferredMovie.Id)?.Movie ?? variants[0].Movie;
                var categoryProjection = GetCategoryProjection(preferredMovie);
                if (IsCategoryHidden(categoryProjection.RawCategoryName))
                {
                    continue;
                }

                var filteredGroup = new CatalogMovieGroup
                {
                    GroupKey = group.GroupKey,
                    PreferredMovie = preferredMovie,
                    Variants = variants,
                    SourceSummary = BuildSourceSummary(variants.Select(variant => variant.SourceProfile.Name))
                };

                if (FavoritesOnly && !IsMovieGroupFavorite(filteredGroup))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(SearchQuery) &&
                    !MatchesMovieSearch(filteredGroup, categoryProjection.DisplayCategoryName, normalizedSearchQuery))
                {
                    continue;
                }

                results.Add(new FilteredMovieResult(filteredGroup, categoryProjection.DisplayCategoryName));
            }

            var sortStopwatch = Stopwatch.StartNew();
            var sortedResults = SortMovieGroups(results.Select(result => result.Group))
                .Select(group => new FilteredMovieResult(
                    group,
                    GetCategoryProjection(group.PreferredMovie).DisplayCategoryName))
                .ToList();
            LogBrowse($"PERF sort media=movies input={results.Count} output={sortedResults.Count} ms={sortStopwatch.ElapsedMilliseconds}");
            return sortedResults;
        }

        private CatalogDiscoveryProjection BuildDiscoveryProjection(
            IReadOnlyList<FilteredMovieResult> results,
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
                        Domain = CatalogDiscoveryDomain.Movies,
                        SourceProfileIds = sourceProfileIds,
                        SourceTypes = sourceProfileIds
                            .Select(id => _sourceTypeById.TryGetValue(id, out var sourceType) ? sourceType : SourceType.M3U)
                            .Distinct()
                            .ToList(),
                        LanguageKey = _catalogDiscoveryService.ResolveLanguageKey(result.Group.PreferredMovie.OriginalLanguage),
                        LanguageLabel = _catalogDiscoveryService.ResolveLanguageLabel(result.Group.PreferredMovie.OriginalLanguage),
                        Tags = _catalogDiscoveryService.ExtractTags(result.Group.PreferredMovie.Genres),
                        IsFavorite = IsMovieGroupFavorite(result.Group),
                        HasArtwork = HasMovieArtwork(result.Group.PreferredMovie),
                        HealthBucket = ResolveBestHealthBucket(sourceProfileIds),
                        LastSyncUtc = ResolveLatestSync(sourceProfileIds)
                    };
                })
                .ToList();

            return _catalogDiscoveryService.BuildProjection(
                CatalogDiscoveryDomain.Movies,
                records,
                new CatalogDiscoverySelection
                {
                    SignalKey = SelectedDiscoverySignalOption?.Key ?? _preferences.DiscoverySignalKey,
                    SourceTypeKey = SelectedDiscoverySourceTypeOption?.Key ?? _preferences.DiscoverySourceTypeKey,
                    LanguageKey = SelectedDiscoveryLanguageOption?.Key ?? _preferences.DiscoveryLanguageKey,
                    TagKey = SelectedDiscoveryTagOption?.Key ?? _preferences.DiscoveryTagKey
                },
                nowUtc);
        }

        private void RefreshDiscoveryOptions(CatalogDiscoveryProjection projection)
        {
            _preferences.DiscoverySignalKey = projection.EffectiveSelection.SignalKey;
            _preferences.DiscoverySourceTypeKey = projection.EffectiveSelection.SourceTypeKey;
            _preferences.DiscoveryLanguageKey = projection.EffectiveSelection.LanguageKey;
            _preferences.DiscoveryTagKey = projection.EffectiveSelection.TagKey;

            var wasInitializing = _isInitializing;
            _isInitializing = true;
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
                _isInitializing = wasInitializing;
            }
        }

        private static BrowseFacetOptionViewModel ToBrowseFacetOption(CatalogDiscoveryFacetOption option)
        {
            return new BrowseFacetOptionViewModel(option.Key, option.Label, option.ItemCount);
        }

        public void RefreshLocalizedLabelsIfNeeded()
        {
            if (_localizedTextVersion == LocalizedStrings.Version)
            {
                return;
            }

            _localizedTextVersion = LocalizedStrings.Version;
            DiscoverySummaryText = LocalizedStrings.Get("Movies_DiscoverySummary_Default");
            RebuildSortOptions();
            if (_hasLoadedOnce)
            {
                BuildSourceOptions();
                RefreshCategoryText();
                BuildCategoryManagerOptions();
                UpdateEmptyState(_lastBaseResultCount, _lastHasDiscoveryFacetFilters);
            }

            foreach (var movie in _filteredMovies)
            {
                movie.RefreshLocalizedText();
            }

            FeaturedMovie.RefreshLocalizedText();
            OnPropertyChanged(nameof(FilteredMovies));
        }

        private void RebuildSortOptions()
        {
            var selectedKey = SelectedSortOption?.Key ?? _preferences.SortKey;
            if (string.IsNullOrWhiteSpace(selectedKey))
            {
                selectedKey = "recommended";
            }

            var wasInitializing = _isInitializing;
            _isInitializing = true;
            try
            {
                SortOptions.Clear();
                foreach (var option in BrowseLocalization.CreateSortOptions(BrowseLocalization.MovieSortOptions))
                {
                    SortOptions.Add(option);
                }

                SelectedSortOption = SortOptions.FirstOrDefault(option => string.Equals(option.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                    ?? SortOptions.FirstOrDefault();
            }
            finally
            {
                _isInitializing = wasInitializing;
            }
        }

        private void RefreshCategoryText()
        {
            foreach (var category in Categories)
            {
                if (category.IsSmartCategory &&
                    _smartCategoryService.GetDefinition(category.SmartCategoryId) is { } definition)
                {
                    category.UpdateText(
                        BrowseLocalization.SmartCategoryName(definition),
                        BrowseLocalization.SmartCategoryDescription(definition),
                        BrowseLocalization.SmartCategorySection(definition));
                }
                else if (category.IsOriginalProviderGroup)
                {
                    category.UpdateText(
                        category.Name,
                        category.Description,
                        BrowseLocalization.OriginalProviderGroups);
                }
            }

            ApplyCategorySectionHeaders(Categories);
            _visibleCategorySignature = BuildCategorySignature(Categories);
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
                EmptyStateTitle = LocalizedStrings.Get("Browse_Empty_NoMatches_Title");
                EmptyStateMessage = LocalizedStrings.Get("Movies_Empty_NoMatches_Message");
                return;
            }

            EmptyStateTitle = baseResultCount == 0
                ? LocalizedStrings.Get("Movies_Empty_NoMovies_Title")
                : LocalizedStrings.Get("Movies_Empty_ShelfEmpty_Title");
            EmptyStateMessage = LocalizedStrings.Get("Movies_Empty_ShelfEmpty_Message");
        }

        private static bool HasMovieArtwork(Movie movie)
        {
            return !string.IsNullOrWhiteSpace(movie.DisplayPosterUrl) ||
                   !string.IsNullOrWhiteSpace(movie.DisplayHeroArtworkUrl);
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

        private async Task RefreshDisplayMovieSlotsAsync(bool prioritizeFirstPaint, string reason)
        {
            var totalStopwatch = Stopwatch.StartNew();
            if (_filteredMovies.Count == 0)
            {
                var emptyPatch = DisplayMovieSlots.PatchToMatch(
                    Array.Empty<MovieBrowseSlotViewModel>(),
                    static (existing, incoming) => existing.Matches(incoming),
                    static (existing, incoming) => existing.UpdateFrom(incoming.Movie));
                LogBrowse($"slot patch reason={reason} reused={emptyPatch.ReusedCount} inserted={emptyPatch.InsertedCount} removed={emptyPatch.RemovedCount} moved={emptyPatch.MovedCount}");
                return;
            }

            try
            {
                var refreshVersion = ++_movieSlotRefreshVersion;
                var buildStopwatch = Stopwatch.StartNew();
                var slots = BuildMovieSlots(_filteredMovies);
                LogBrowse($"PERF slots_build media=movies reason={reason} slots={slots.Count} ms={buildStopwatch.ElapsedMilliseconds}");
                if (!prioritizeFirstPaint || slots.Count <= InitialDisplayMovieSlotBatchSize)
                {
                    var uiStopwatch = Stopwatch.StartNew();
                    var patch = DisplayMovieSlots.PatchToMatch(
                        slots,
                        static (existing, incoming) => existing.Matches(incoming),
                        static (existing, incoming) => existing.UpdateFrom(incoming.Movie));
                    LogBrowse($"slot patch reason={reason} reused={patch.ReusedCount} inserted={patch.InsertedCount} removed={patch.RemovedCount} moved={patch.MovedCount}");
                    LogBrowse($"PERF ui_update media=movies reason={reason} slots={DisplayMovieSlots.Count} ms={uiStopwatch.ElapsedMilliseconds} total_ms={totalStopwatch.ElapsedMilliseconds}");
                    if (prioritizeFirstPaint)
                    {
                        LogBrowse(
                            $"first content ready ms={_activeLoadStopwatch?.ElapsedMilliseconds ?? -1} reason={reason} visibleSlots={slots.Count}");
                    }

                    return;
                }

                var initialSlots = slots.Take(InitialDisplayMovieSlotBatchSize).ToList();
                var deferredSlots = slots.Skip(InitialDisplayMovieSlotBatchSize).ToList();
                var initialUiStopwatch = Stopwatch.StartNew();
                var initialPatch = DisplayMovieSlots.PatchToMatch(
                    initialSlots,
                    static (existing, incoming) => existing.Matches(incoming),
                    static (existing, incoming) => existing.UpdateFrom(incoming.Movie));
                LogBrowse($"slot patch reason={reason}:initial reused={initialPatch.ReusedCount} inserted={initialPatch.InsertedCount} removed={initialPatch.RemovedCount} moved={initialPatch.MovedCount}");
                LogBrowse($"PERF ui_update media=movies reason={reason}:initial slots={DisplayMovieSlots.Count} ms={initialUiStopwatch.ElapsedMilliseconds} total_ms={totalStopwatch.ElapsedMilliseconds}");
                LogBrowse(
                    $"first content ready ms={_activeLoadStopwatch?.ElapsedMilliseconds ?? -1} reason={reason} visibleSlots={initialSlots.Count}");

                await Task.Yield();
                if (refreshVersion != _movieSlotRefreshVersion)
                {
                    return;
                }

                for (var index = 0; index < deferredSlots.Count; index += DeferredSlotAppendBatchSize)
                {
                    if (refreshVersion != _movieSlotRefreshVersion)
                    {
                        return;
                    }

                    DisplayMovieSlots.AppendRange(deferredSlots.GetRange(
                        index,
                        Math.Min(DeferredSlotAppendBatchSize, deferredSlots.Count - index)));
                    await Task.Yield();
                }

                LogBrowse($"PERF ui_update media=movies reason={reason}:deferred slots={DisplayMovieSlots.Count} total_ms={totalStopwatch.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                LogBrowse($"slot refresh failed reason={reason} error={ex.Message}");
            }
        }

        private List<MovieBrowseSlotViewModel> BuildMovieSlots(IReadOnlyList<MovieBrowseItemViewModel> filteredMovies)
        {
            var slots = filteredMovies
                .Select(movie => new MovieBrowseSlotViewModel(movie))
                .ToList();

            var remainder = filteredMovies.Count % BrowseGridColumns;
            var placeholderCount = remainder == 0 ? 0 : BrowseGridColumns - remainder;
            for (var index = 0; index < placeholderCount; index++)
            {
                slots.Add(new MovieBrowseSlotViewModel(null));
            }

            return slots;
        }

        private void PatchFilteredMovies(IReadOnlyList<FilteredMovieResult> filteredResults)
        {
            var existingByGroupKey = _filteredMovies
                .Where(item => !string.IsNullOrWhiteSpace(item.GroupKey))
                .GroupBy(item => item.GroupKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var nextItems = new List<MovieBrowseItemViewModel>(filteredResults.Count);
            var reusedCount = 0;

            foreach (var result in filteredResults)
            {
                if (existingByGroupKey.TryGetValue(result.Group.GroupKey, out var existing))
                {
                    existing.UpdateFrom(result.Group, IsMovieGroupFavorite(result.Group), result.DisplayCategoryName);
                    nextItems.Add(existing);
                    reusedCount++;
                    continue;
                }

                nextItems.Add(new MovieBrowseItemViewModel(
                    result.Group,
                    IsMovieGroupFavorite(result.Group),
                    result.DisplayCategoryName));
            }

            _filteredMovies = nextItems;
            LogBrowse($"item patch reused={reusedCount} inserted={Math.Max(nextItems.Count - reusedCount, 0)} total={nextItems.Count}");
        }

        private void RefreshFeaturedMovie(IEnumerable<MovieBrowseItemViewModel> filteredMovies)
        {
            var currentFeaturedGroupKey = FeaturedMovie?.GroupKey ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(currentFeaturedGroupKey))
            {
                var persistedFeatured = filteredMovies.FirstOrDefault(movie =>
                    string.Equals(movie.GroupKey, currentFeaturedGroupKey, StringComparison.OrdinalIgnoreCase));
                if (persistedFeatured != null && IsSafeForFeatured(persistedFeatured))
                {
                    FeaturedMovie = persistedFeatured;
                    return;
                }
            }

            var pinned = filteredMovies.FirstOrDefault(movie =>
                string.Equals(movie.Title.Trim(), FixedFeaturedMovieTitle, StringComparison.OrdinalIgnoreCase));
            if (pinned != null && IsSafeForFeatured(pinned))
            {
                FeaturedMovie = pinned;
                return;
            }

            FeaturedMovie = SelectFeaturedMovie(filteredMovies, _sourceTypeById);
        }

        private bool IsSafeForFeatured(MovieBrowseItemViewModel item)
        {
            var type = _sourceTypeById.TryGetValue(item.Movie.SourceProfileId, out var sourceType) ? sourceType : SourceType.M3U;
            return ContentClassifier.IsFeaturedSafeMovie(type, item.Title, item.CategoryName, item.StreamUrl);
        }

        private static MovieBrowseItemViewModel SelectFeaturedMovie(IEnumerable<MovieBrowseItemViewModel> movies, IReadOnlyDictionary<int, SourceType> sourceTypeById)
        {
            SourceType TypeFor(MovieBrowseItemViewModel item) =>
                sourceTypeById.TryGetValue(item.Movie.SourceProfileId, out var sourceType) ? sourceType : SourceType.M3U;

            var safeMovies = movies
                .Where(movie => ContentClassifier.IsFeaturedSafeMovie(TypeFor(movie), movie.Title, movie.CategoryName, movie.StreamUrl))
                .ToList();

            if (safeMovies.Count == 0)
            {
                return CreatePlaceholderFeaturedMovie();
            }

            var allRanked = safeMovies
                .OrderByDescending(GetArtworkScore)
                .ThenByDescending(movie => movie.Popularity)
                .ThenByDescending(movie => movie.VoteAverage)
                .ToList();

            var backdropPool = allRanked.Where(movie => GetArtworkScore(movie) >= 3).Take(5).ToList();
            return backdropPool.Count > 0
                ? backdropPool[_sessionRotationIndex % backdropPool.Count]
                : allRanked.FirstOrDefault() ?? CreatePlaceholderFeaturedMovie();
        }

        private static int GetArtworkScore(MovieBrowseItemViewModel movie)
        {
            if (!string.IsNullOrWhiteSpace(movie.BackdropUrl)) return 4;
            if (!string.IsNullOrWhiteSpace(movie.TmdbBackdropPath)) return 3;
            if (!string.IsNullOrWhiteSpace(movie.PosterUrl)) return 2;
            return string.IsNullOrWhiteSpace(movie.TmdbPosterPath) ? 0 : 1;
        }

        private static MovieBrowseItemViewModel CreatePlaceholderFeaturedMovie()
        {
            return new MovieBrowseItemViewModel(
                new CatalogMovieGroup
                {
                    GroupKey = "placeholder",
                    PreferredMovie = new Movie
                    {
                        Title = LocalizedStrings.Get("Movies_Placeholder_Title"),
                        Overview = LocalizedStrings.Get("Movies_Placeholder_Overview"),
                        CategoryName = BrowseLocalization.VodLibrary
                    },
                    Variants = Array.Empty<CatalogMovieVariant>()
                },
                false,
                BrowseLocalization.VodLibrary);
        }

        private void BuildSourceOptions()
        {
            var existingSelection = _preferences.SelectedSourceId;
            var sourceOptions = new List<BrowseSourceFilterOptionViewModel>
            {
                new BrowseSourceFilterOptionViewModel(0, BrowseLocalization.AllProviders, _allMovieGroups.Count)
            };
            var sourceVisibilityOptions = new List<BrowseSourceVisibilityViewModel>();

            foreach (var group in _allMovieGroups
                         .SelectMany(item => item.Variants)
                         .GroupBy(variant => variant.SourceProfile.Id)
                         .Select(group => new { Id = group.Key, Name = group.First().SourceProfile.Name, Count = group.Count() })
                         .OrderBy(group => group.Name))
            {
                var isVisible = !_preferences.HiddenSourceIds.Contains(group.Id);
                sourceOptions.Add(new BrowseSourceFilterOptionViewModel(group.Id, group.Name, group.Count));
                sourceVisibilityOptions.Add(new BrowseSourceVisibilityViewModel(group.Id, group.Name, BrowseLocalization.VariantCount(group.Count), isVisible, OnSourceVisibilityChanged));
            }

            var wasInitializing = _isInitializing;
            _isInitializing = true;
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
                _isInitializing = wasInitializing;
            }
        }

        private BrowserCategoryViewModel? BuildVisibleCategories(string currentKey)
        {
            var stopwatch = Stopwatch.StartNew();
            LogBrowse($"categories rebuild start preserveKey={currentKey ?? string.Empty}");

            var visibleSourceIds = SourceVisibilityOptions.Where(option => option.IsVisible).Select(option => option.Id).ToHashSet();
            if (visibleSourceIds.Count == 0)
            {
                visibleSourceIds = _allMovieGroups.SelectMany(group => group.Variants).Select(variant => variant.SourceProfile.Id).ToHashSet();
            }

            var visibleGroups = _allMovieGroups
                .Select(group =>
                {
                    var variants = group.Variants
                        .Where(variant => visibleSourceIds.Contains(variant.SourceProfile.Id))
                        .Where(variant => !HideSecondaryContent || string.Equals(variant.Movie.ContentKind, "Primary", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (variants.Count == 0)
                    {
                        return null;
                    }

                    var preferredMovie = variants.FirstOrDefault(variant => variant.Movie.Id == group.PreferredMovie.Id)?.Movie ?? variants[0].Movie;
                    var categoryProjection = GetCategoryProjection(preferredMovie);
                    if (IsCategoryHidden(categoryProjection.RawCategoryName))
                    {
                        return null;
                    }

                    return new CatalogMovieGroup
                    {
                        GroupKey = group.GroupKey,
                        PreferredMovie = preferredMovie,
                        Variants = variants,
                        SourceSummary = BuildSourceSummary(variants.Select(variant => variant.SourceProfile.Name))
                    };
                })
                .OfType<CatalogMovieGroup>()
                .ToList();

            var categoryItems = new List<BrowserCategoryViewModel>();
            var categoryId = 0;
            _movieSearchTextByGroupKey.Clear();
            foreach (var group in visibleGroups)
            {
                _movieSearchTextByGroupKey[group.GroupKey] = BuildMovieSearchText(group, GetCategoryProjection(group.PreferredMovie).DisplayCategoryName);
            }

            var indexStopwatch = Stopwatch.StartNew();
            _movieCategoryIndex = SmartCategoryIndexBuilder.Build(
                visibleGroups,
                _smartCategoryService.GetDefinitions(SmartCategoryMediaType.Movie),
                BuildMovieSmartCategoryContext,
                group => group.Variants.Select(variant => GetRawCategory(variant.Movie)).Distinct(StringComparer.OrdinalIgnoreCase),
                _smartCategoryService);
            LogBrowse($"PERF smart_index media=movies groups={visibleGroups.Count} contexts={_movieCategoryIndex.ContextBuildCount} keys={_movieCategoryIndex.ItemsByCategoryKey.Count} ms={indexStopwatch.ElapsedMilliseconds}");

            foreach (var definition in _smartCategoryService.GetDefinitions(SmartCategoryMediaType.Movie))
            {
                var count = definition.IsAllCategory
                    ? _movieCategoryIndex.GetCount(string.Empty)
                    : _movieCategoryIndex.GetCount(definition.Id);
                if (count <= 0 && !definition.AlwaysShow)
                {
                    continue;
                }

                categoryItems.Add(new BrowserCategoryViewModel
                {
                    Id = categoryId++,
                    FilterKey = definition.IsAllCategory ? string.Empty : definition.Id,
                    SmartCategoryId = definition.Id,
                    Name = BrowseLocalization.SmartCategoryName(definition),
                    Description = BrowseLocalization.SmartCategoryDescription(definition),
                    SectionName = BrowseLocalization.SmartCategorySection(definition),
                    ItemCount = count,
                    OrderIndex = definition.SortPriority,
                    IsSmartCategory = true,
                    IconGlyph = definition.IconGlyph
                });
            }

            var providerCategories = _movieCategoryIndex.OriginalProviderGroups
                .Select((category, index) => new BrowserCategoryViewModel
                {
                    Id = categoryId++,
                    FilterKey = category.Key,
                    Name = category.Name,
                    Description = ResolveDisplayCategoryName(category.Name),
                    SectionName = BrowseLocalization.OriginalProviderGroups,
                    ItemCount = category.Count,
                    OrderIndex = 100_000 + index,
                    IsOriginalProviderGroup = true,
                    IconGlyph = "\uE8FD"
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

            LogBrowse($"PERF categories_built media=movies categories={Categories.Count} groups={visibleGroups.Count} resolvedKey={resolvedCategory?.FilterKey ?? "<all>"} ms={stopwatch.ElapsedMilliseconds}");
            return resolvedCategory;
        }

        private static void ApplyCategorySectionHeaders(IReadOnlyList<BrowserCategoryViewModel> categories)
        {
            var previousSection = string.Empty;
            foreach (var category in categories)
            {
                var visibility = string.IsNullOrWhiteSpace(category.SectionName) ||
                                 string.Equals(category.SectionName, previousSection, StringComparison.Ordinal)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                category.UpdateSectionHeaderVisibility(visibility);
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
                var hasManualAlias = _preferences.CategoryRemaps.ContainsKey(key);
                options.Add(new BrowseCategoryManagerOptionViewModel(
                    key,
                    category.CategoryName,
                    GetEffectiveCategoryName(category.CategoryName, autoDisplayName),
                    autoDisplayName,
                    category.Count,
                    _preferences.HiddenCategoryKeys.Contains(key, StringComparer.OrdinalIgnoreCase),
                    hasManualAlias));
            }

            ManageCategoryOptions.ReplaceAll(options);
            SelectedManageCategory = ManageCategoryOptions.FirstOrDefault(option => string.Equals(option.Key, currentKey, StringComparison.OrdinalIgnoreCase));
        }

        private async Task SavePreferencesAsync(bool rebuildCollections)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var browsePreferencesService = scope.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
            await browsePreferencesService.SaveAsync(db, Domain, _activeProfileId, _preferences);

            if (rebuildCollections)
            {
                BuildSourceOptions();
                BuildCategoryManagerOptions();
                LogBrowse("save preferences requested collection rebuild");
            }

            ApplyFilter(rebuildCollections ? "save-preferences-rebuild" : "save-preferences");
        }

        private IEnumerable<CatalogMovieGroup> SortMovieGroups(IEnumerable<CatalogMovieGroup> groups)
        {
            return (SelectedSortOption?.Key ?? "recommended") switch
            {
                "title_asc" => groups.OrderBy(group => group.PreferredMovie.Title, StringComparer.CurrentCultureIgnoreCase),
                "rating_desc" => groups.OrderByDescending(group => group.PreferredMovie.VoteAverage).ThenByDescending(group => group.PreferredMovie.Popularity).ThenBy(group => group.PreferredMovie.Title, StringComparer.CurrentCultureIgnoreCase),
                "popularity_desc" => groups.OrderByDescending(group => group.PreferredMovie.Popularity).ThenByDescending(group => group.PreferredMovie.VoteAverage).ThenBy(group => group.PreferredMovie.Title, StringComparer.CurrentCultureIgnoreCase),
                "year_desc" => groups.OrderByDescending(group => group.PreferredMovie.ReleaseDate ?? DateTime.MinValue).ThenBy(group => group.PreferredMovie.Title, StringComparer.CurrentCultureIgnoreCase),
                "favorites_first" => groups.OrderByDescending(IsMovieGroupFavorite).ThenBy(group => group.PreferredMovie.Title, StringComparer.CurrentCultureIgnoreCase),
                _ => groups
            };
        }

        private bool MatchesMovieCategorySelection(
            CatalogMovieGroup group,
            CatalogCategoryProjection categoryProjection,
            string selectedCategoryKey)
        {
            if (_smartCategoryService.TryParseOriginalProviderGroupKey(selectedCategoryKey, out var providerGroupKey))
            {
                return group.Variants.Any(variant => string.Equals(
                    _smartCategoryService.NormalizeKey(GetRawCategory(variant.Movie)),
                    providerGroupKey,
                    StringComparison.OrdinalIgnoreCase));
            }

            if (_smartCategoryService.IsSmartCategoryKey(selectedCategoryKey))
            {
                return _smartCategoryService.Matches(selectedCategoryKey, BuildMovieSmartCategoryContext(group));
            }

            return string.Equals(categoryProjection.DisplayCategoryKey, selectedCategoryKey, StringComparison.OrdinalIgnoreCase);
        }

        private SmartCategoryItemContext BuildMovieSmartCategoryContext(CatalogMovieGroup group)
        {
            var movie = group.PreferredMovie;
            var snapshots = group.Variants
                .Select(variant => _movieWatchSnapshotsById.TryGetValue(variant.Movie.Id, out var snapshot) ? snapshot : null)
                .OfType<WatchProgressSnapshot>()
                .ToList();
            var sourceProfileIds = group.Variants
                .Select(variant => variant.SourceProfile.Id)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            return new SmartCategoryItemContext
            {
                MediaType = SmartCategoryMediaType.Movie,
                Title = movie.Title,
                RawTitle = movie.RawSourceTitle,
                ProviderGroupName = string.Join(' ', group.Variants.Select(variant => GetRawCategory(variant.Movie)).Distinct(StringComparer.OrdinalIgnoreCase)),
                DisplayCategoryName = GetCategoryProjection(movie).DisplayCategoryName,
                Genres = movie.Genres,
                OriginalLanguage = movie.OriginalLanguage,
                SourceName = group.SourceSummary,
                SourceSummary = group.SourceSummary,
                ReleaseDate = movie.ReleaseDate,
                SourceLastSyncUtc = ResolveLatestSync(sourceProfileIds),
                VoteAverage = movie.VoteAverage,
                Popularity = movie.Popularity,
                VariantCount = group.Variants.Count,
                IsFavorite = IsMovieGroupFavorite(group),
                IsRecentlyWatched = snapshots.Count > 0,
                IsContinueWatching = snapshots.Any(snapshot => snapshot.HasResumePoint),
                IsWatched = snapshots.Any(snapshot => snapshot.IsWatched),
                IsInProgress = snapshots.Any(snapshot => snapshot.HasResumePoint),
                IsCompleted = snapshots.Any(snapshot => snapshot.IsCompleted),
                HasMetadata = !string.IsNullOrWhiteSpace(movie.TmdbId) ||
                              !string.IsNullOrWhiteSpace(movie.ImdbId) ||
                              !string.IsNullOrWhiteSpace(movie.Genres) ||
                              !string.IsNullOrWhiteSpace(movie.Overview) ||
                              movie.MetadataUpdatedAt.HasValue,
                HasArtwork = HasMovieArtwork(movie),
                IsPrimary = string.Equals(movie.ContentKind, "Primary", StringComparison.OrdinalIgnoreCase)
            };
        }

        private bool MatchesMovieSearch(CatalogMovieGroup group, string displayCategory, string normalizedQuery)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery))
            {
                return true;
            }

            if (_movieSearchTextByGroupKey.TryGetValue(group.GroupKey, out var searchText))
            {
                return searchText.Contains(normalizedQuery, StringComparison.Ordinal);
            }

            return BuildMovieSearchText(group, displayCategory)
                .Contains(normalizedQuery, StringComparison.Ordinal);
        }

        private string BuildMovieSearchText(CatalogMovieGroup group, string displayCategory)
        {
            return _smartCategoryService.NormalizeKey(
                $"{group.PreferredMovie.Title} {group.PreferredMovie.RawSourceTitle} {displayCategory} {group.SourceSummary} {group.PreferredMovie.Genres} {group.PreferredMovie.OriginalLanguage}");
        }

        private bool ShouldRebuildCategoryRail(string reason)
        {
            return Categories.Count == 0 ||
                   _movieCategoryIndex.AllItems.Count == 0 ||
                   reason.Contains("rebuild", StringComparison.OrdinalIgnoreCase) ||
                   reason.Contains("toggle-favorite", StringComparison.OrdinalIgnoreCase);
        }

        private bool RequiresFullBrowseRefreshForFavoriteToggle()
        {
            return FavoritesOnly ||
                   string.Equals(SelectedCategory?.FilterKey, "movie.library.favorites", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(SelectedSortOption?.Key ?? "recommended", "favorites_first", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsMovieGroupFavorite(CatalogMovieGroup group)
        {
            return group.Variants.Any(variant =>
                _favoriteMovieKeys.Contains(_logicalCatalogStateService.BuildMovieLogicalKey(variant.Movie)));
        }

        private void RefreshMovieFavoriteState(string groupKey)
        {
            if (string.IsNullOrWhiteSpace(groupKey))
            {
                return;
            }

            var isFavorite = _allMovieGroups
                .FirstOrDefault(group => string.Equals(group.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase)) is { } group &&
                IsMovieGroupFavorite(group);

            foreach (var item in _filteredMovies.Where(item => string.Equals(item.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase)))
            {
                item.IsFavorite = isFavorite;
            }

            foreach (var slot in DisplayMovieSlots.Where(slot => slot.HasMovie &&
                                                                 string.Equals(slot.Movie?.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase)))
            {
                slot.RefreshFavoriteState();
            }

            if (FeaturedMovie != null &&
                string.Equals(FeaturedMovie.GroupKey, groupKey, StringComparison.OrdinalIgnoreCase))
            {
                FeaturedMovie.IsFavorite = isFavorite;
            }
        }

        private static string BuildCategorySignature(IEnumerable<BrowserCategoryViewModel> categories)
        {
            return string.Join("|", categories.Select(category => $"{category.FilterKey}:{category.SectionName}:{category.Name}:{category.ItemCount}:{category.IconGlyph}:{category.OrderIndex}"));
        }

        private void OnSourceVisibilityChanged(BrowseSourceVisibilityViewModel option, bool isVisible)
        {
            if (_isInitializing)
            {
                return;
            }

            if (isVisible)
            {
                _preferences.HiddenSourceIds.RemoveAll(id => id == option.Id);
            }
            else if (!_preferences.HiddenSourceIds.Contains(option.Id))
            {
                _preferences.HiddenSourceIds.Add(option.Id);
            }

            _ = SavePreferencesAsync(true);
        }

        private void LogBrowse(string message)
        {
            BrowseRuntimeLogger.Log("MOVIES", message);
        }

        private string NormalizeCategoryKey(string categoryName)
        {
            return _browsePreferencesService.NormalizeCategoryKey(categoryName);
        }

        private string GetEffectiveCategoryName(Movie movie)
        {
            var projection = GetCategoryProjection(movie);
            return GetEffectiveCategoryName(projection.RawCategoryName, projection.DisplayCategoryName);
        }

        private string GetEffectiveCategoryName(string rawCategoryName, string defaultDisplayCategory)
        {
            return _browsePreferencesService.GetEffectiveCategoryName(_preferences, rawCategoryName, defaultDisplayCategory);
        }

        private bool IsCategoryHidden(string rawCategoryName)
        {
            return _browsePreferencesService.IsCategoryHidden(_preferences, rawCategoryName);
        }

        private CatalogCategoryProjection GetCategoryProjection(Movie movie)
        {
            if (_movieCategoryById.TryGetValue(movie.Id, out var projection))
            {
                return projection;
            }

            projection = BuildMovieCategoryProjection(movie);
            _movieCategoryById[movie.Id] = projection;
            return projection;
        }

        private CatalogCategoryProjection BuildMovieCategoryProjection(Movie movie)
        {
            var rawCategory = GetRawCategory(movie);
            var displayCategory = ResolveDisplayCategoryName(movie);
            return new CatalogCategoryProjection(
                rawCategory,
                displayCategory,
                NormalizeCategoryKey(displayCategory));
        }

        private string ResolveDisplayCategoryName(Movie movie)
        {
            return _taxonomyService.ResolveMovieCategory(
                movie.CategoryName,
                movie.RawSourceCategoryName,
                movie.Title,
                movie.OriginalLanguage).DisplayCategoryName;
        }

        private string ResolveDisplayCategoryName(string rawCategoryName)
        {
            return _taxonomyService.ResolveMovieCategory(
                rawCategoryName,
                rawCategoryName,
                string.Empty,
                string.Empty).DisplayCategoryName;
        }

        private static string GetRawCategory(Movie movie)
        {
            return string.IsNullOrWhiteSpace(movie.RawSourceCategoryName)
                ? GetRawCategory(movie.CategoryName)
                : GetRawCategory(movie.RawSourceCategoryName);
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
                    var movies = await db.Movies.AsNoTracking().Take(36).ToListAsync(token);
                    await metadataService.EnrichMoviesAsync(db, movies, 36, token);
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

        private static CatalogMovieGroup? FilterGroup(CatalogMovieGroup group, ProfileAccessSnapshot access)
        {
            var variants = group.Variants.Where(variant => access.IsMovieAllowed(variant.Movie)).ToList();
            if (variants.Count == 0)
            {
                return null;
            }

            return new CatalogMovieGroup
            {
                GroupKey = group.GroupKey,
                PreferredMovie = variants[0].Movie,
                Variants = variants,
                SourceSummary = BuildSourceSummary(variants.Select(variant => variant.SourceProfile.Name))
            };
        }

        private static string BuildSourceSummary(IEnumerable<string> sourceNames)
        {
            var distinct = sourceNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
            if (distinct.Count == 0) return string.Empty;
            if (distinct.Count == 1) return distinct[0];
            if (distinct.Count == 2) return $"{distinct[0]} + {distinct[1]}";
            return $"{distinct[0]} +{BrowseLocalization.MoreCount(distinct.Count - 1)}";
        }
    }
}
