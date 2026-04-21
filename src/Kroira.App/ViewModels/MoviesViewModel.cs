#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

        public CatalogMovieGroup Group { get; }
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
        public string DisplayCategoryName { get; }
        public double Popularity => Movie.Popularity;
        public double VoteAverage => Movie.VoteAverage;
        public string BackdropUrl => Movie.BackdropUrl;
        public string TmdbBackdropPath => Movie.TmdbBackdropPath;
        public string PosterUrl => Movie.PosterUrl;
        public string TmdbPosterPath => Movie.TmdbPosterPath;
        public bool HasAlternateSources => Group.Variants.Count > 1;
        public string SourceSummary => Group.SourceSummary;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FavoriteGlyph))]
        [NotifyPropertyChangedFor(nameof(FavoriteLabel))]
        private bool _isFavorite;

        public string FavoriteGlyph => IsFavorite ? "\uE735" : "\uE734";
        public string FavoriteLabel => IsFavorite ? "Saved" : "Save";
    }

    public partial class MovieBrowseSlotViewModel : ObservableObject
    {
        public MovieBrowseSlotViewModel(MovieBrowseItemViewModel? movie)
        {
            Movie = movie;
        }

        public MovieBrowseItemViewModel? Movie { get; }
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
    }

    public partial class MoviesViewModel : ObservableObject
    {
        private const string Domain = ProfileDomains.Movies;
        private const string FixedFeaturedMovieTitle = "Kurtlar Vadisi Gladio";
        private const int BrowseGridColumns = 5;
        private readonly IServiceProvider _serviceProvider;
        private readonly IBrowsePreferencesService _browsePreferencesService;
        private readonly ICatalogTaxonomyService _taxonomyService;
        private readonly List<CatalogMovieGroup> _allMovieGroups = new();
        private readonly Dictionary<int, CatalogCategoryProjection> _movieCategoryById = new();
        private readonly Dictionary<int, SourceType> _sourceTypeById = new();
        private readonly List<(string CategoryName, int Count)> _allCategories = new();
        private static readonly int _sessionRotationIndex = Math.Abs(Environment.TickCount % 5);
        private List<MovieBrowseItemViewModel> _filteredMovies = new();
        private BrowsePreferences _preferences = new();
        private HashSet<int> _favoriteMovieIds = new();
        private int _activeProfileId;
        private string _languageCode = AppLanguageService.DefaultLanguageCode;
        private bool _isInitializing;
        private bool _isApplyingFilter;
        private bool _pendingApplyFilter;
        private string _pendingApplyFilterReason = string.Empty;

        public IReadOnlyList<MovieBrowseItemViewModel> FilteredMovies => _filteredMovies;
        public int FilteredMovieCount => _filteredMovies.Count;
        public BulkObservableCollection<MovieBrowseSlotViewModel> DisplayMovieSlots { get; } = new();
        public BulkObservableCollection<BrowserCategoryViewModel> Categories { get; } = new();
        public ObservableCollection<BrowseSortOptionViewModel> SortOptions { get; } = new();
        public BulkObservableCollection<BrowseSourceFilterOptionViewModel> SourceOptions { get; } = new();
        public BulkObservableCollection<BrowseSourceVisibilityViewModel> SourceVisibilityOptions { get; } = new();
        public BulkObservableCollection<BrowseCategoryManagerOptionViewModel> ManageCategoryOptions { get; } = new();

        [ObservableProperty] private string _searchQuery = string.Empty;
        [ObservableProperty] private BrowserCategoryViewModel? _selectedCategory;
        [ObservableProperty] private BrowseSortOptionViewModel? _selectedSortOption;
        [ObservableProperty] private BrowseSourceFilterOptionViewModel? _selectedSourceOption;
        [ObservableProperty] private BrowseCategoryManagerOptionViewModel? _selectedManageCategory;
        [ObservableProperty] private string _manageCategoryAliasDraft = string.Empty;
        [ObservableProperty] private bool _isManageCategoryHidden;
        [ObservableProperty] private bool _favoritesOnly;
        [ObservableProperty] private bool _hideSecondaryContent;
        [ObservableProperty] private bool _hasAdvancedFilters;
        [ObservableProperty] private bool _isEmpty;
        [ObservableProperty] private SurfaceStatePresentation _surfaceState = SurfaceStateCopies.Movies.Create(SurfaceViewState.Loading);
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FeaturedMovieCanPlay))]
        private MovieBrowseItemViewModel _featuredMovie = CreatePlaceholderFeaturedMovie();

        public bool FeaturedMovieCanPlay => !string.IsNullOrWhiteSpace(FeaturedMovie?.StreamUrl);
        public bool HasManageCategorySelection => SelectedManageCategory != null;

        private sealed record FilteredMovieResult(CatalogMovieGroup Group, string DisplayCategoryName);

        public MoviesViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _browsePreferencesService = serviceProvider.GetRequiredService<IBrowsePreferencesService>();
            _taxonomyService = serviceProvider.GetRequiredService<ICatalogTaxonomyService>();
            SortOptions.Add(new BrowseSortOptionViewModel("recommended", "Recommended"));
            SortOptions.Add(new BrowseSortOptionViewModel("title_asc", "Title A-Z"));
            SortOptions.Add(new BrowseSortOptionViewModel("rating_desc", "Highest rated"));
            SortOptions.Add(new BrowseSortOptionViewModel("popularity_desc", "Most popular"));
            SortOptions.Add(new BrowseSortOptionViewModel("year_desc", "Newest release"));
            SortOptions.Add(new BrowseSortOptionViewModel("favorites_first", "Favorites first"));
        }

        partial void OnSearchQueryChanged(string value)
        {
            LogBrowse($"search changed query='{value}'");
            ApplyFilter("search-query-changed");
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
            SurfaceState = SurfaceStateCopies.Movies.Create(SurfaceViewState.Loading);
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var deduplicationService = scope.ServiceProvider.GetRequiredService<ICatalogDeduplicationService>();
                var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
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
                _languageCode = await AppLanguageService.GetLanguageAsync(db, access.ProfileId);
                _preferences = await browsePreferencesService.GetAsync(db, Domain, _activeProfileId);

                var movieGroups = (await deduplicationService.LoadMovieGroupsAsync(db))
                    .Select(group => FilterGroup(group, access))
                    .OfType<CatalogMovieGroup>()
                    .ToList();
                _favoriteMovieIds = (await db.Favorites
                    .Where(favorite => favorite.ProfileId == access.ProfileId && favorite.ContentType == FavoriteType.Movie)
                    .Select(favorite => favorite.ContentId)
                    .ToListAsync())
                    .ToHashSet();

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
                BuildCategoryManagerOptions();

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
                StartMetadataEnrichment();
            }
            catch (Exception ex)
            {
                BrowseRuntimeLogger.Log("MOVIES", $"load failed {ex}");
                SurfaceState = _serviceProvider.GetRequiredService<ISurfaceStateService>().CreateFailureState(SurfaceStateCopies.Movies, ex);
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
            string currentCategoryKey = SelectedCategory?.FilterKey ?? string.Empty;
            LogBrowse(
                $"apply start reason={reason} selectedKey={currentCategoryKey} search='{SearchQuery}' source={SelectedSourceOption?.Id ?? 0}");

            var filteredResults = BuildFilteredMovieResults().ToList();

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

            _filteredMovies = filteredResults
                .Select(result => new MovieBrowseItemViewModel(
                    result.Group,
                    result.Group.Variants.Any(variant => _favoriteMovieIds.Contains(variant.Movie.Id)),
                    result.DisplayCategoryName))
                .ToList();
            OnPropertyChanged(nameof(FilteredMovies));
            OnPropertyChanged(nameof(FilteredMovieCount));

            RefreshFeaturedMovie(_filteredMovies);
            IsEmpty = _filteredMovies.Count == 0;
            HasAdvancedFilters = FavoritesOnly ||
                                 HideSecondaryContent ||
                                 SourceVisibilityOptions.Any(option => !option.IsVisible) ||
                                 _preferences.HiddenCategoryKeys.Count > 0 ||
                                 _preferences.CategoryRemaps.Count > 0 ||
                                 (SelectedSourceOption?.Id ?? 0) != 0 ||
                                 !string.Equals(SelectedSortOption?.Key ?? "recommended", "recommended", StringComparison.OrdinalIgnoreCase);
            RefreshDisplayMovieSlots();
            LogBrowse(
                $"apply end reason={reason} groups={filteredResults.Count} results={_filteredMovies.Count} slots={DisplayMovieSlots.Count} selectedKey={SelectedCategory?.FilterKey ?? "<all>"}");
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
            var variantIds = group.Variants.Select(variant => variant.Movie.Id).ToList();
            var existingFavorites = await db.Favorites
                .Where(favorite => favorite.ProfileId == activeProfileId && favorite.ContentType == FavoriteType.Movie && variantIds.Contains(favorite.ContentId))
                .ToListAsync();

            if (existingFavorites.Count == 0)
            {
                db.Favorites.Add(new Favorite { ProfileId = activeProfileId, ContentType = FavoriteType.Movie, ContentId = movieId });
                _favoriteMovieIds.Add(movieId);
            }
            else
            {
                db.Favorites.RemoveRange(existingFavorites);
                foreach (var favorite in existingFavorites)
                {
                    _favoriteMovieIds.Remove(favorite.ContentId);
                }
            }

            await db.SaveChangesAsync();
            ApplyFilter("toggle-favorite");
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

            foreach (var group in _allMovieGroups)
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

                if (!string.IsNullOrWhiteSpace(selectedCategoryKey) &&
                    !string.Equals(categoryProjection.DisplayCategoryKey, selectedCategoryKey, StringComparison.OrdinalIgnoreCase))
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

                if (FavoritesOnly && !filteredGroup.Variants.Any(variant => _favoriteMovieIds.Contains(variant.Movie.Id)))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(SearchQuery) &&
                    !MatchesMovieSearch(filteredGroup, categoryProjection.DisplayCategoryName))
                {
                    continue;
                }

                results.Add(new FilteredMovieResult(filteredGroup, categoryProjection.DisplayCategoryName));
            }

            return SortMovieGroups(results.Select(result => result.Group))
                .Select(group => new FilteredMovieResult(
                    group,
                    GetCategoryProjection(group.PreferredMovie).DisplayCategoryName));
        }

        private void RefreshDisplayMovieSlots()
        {
            if (_filteredMovies.Count == 0)
            {
                DisplayMovieSlots.ReplaceAll(Array.Empty<MovieBrowseSlotViewModel>());
                return;
            }

            var slots = _filteredMovies
                .Select(movie => new MovieBrowseSlotViewModel(movie))
                .ToList();

            var remainder = _filteredMovies.Count % BrowseGridColumns;
            var placeholderCount = remainder == 0 ? 0 : BrowseGridColumns - remainder;
            for (var index = 0; index < placeholderCount; index++)
            {
                slots.Add(new MovieBrowseSlotViewModel(null));
            }

            DisplayMovieSlots.ReplaceAll(slots);
        }

        private void RefreshFeaturedMovie(IEnumerable<MovieBrowseItemViewModel> filteredMovies)
        {
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
                        Title = "Movies",
                        Overview = "Sync an Xtream VOD source to build a poster-first library with TMDb artwork, ratings, genres, and backdrops.",
                        CategoryName = "VOD library"
                    },
                    Variants = Array.Empty<CatalogMovieVariant>()
                },
                false,
                "VOD library");
        }

        private void BuildSourceOptions()
        {
            var existingSelection = _preferences.SelectedSourceId;
            var sourceOptions = new List<BrowseSourceFilterOptionViewModel>
            {
                new BrowseSourceFilterOptionViewModel(0, "All providers", _allMovieGroups.Count)
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
                sourceVisibilityOptions.Add(new BrowseSourceVisibilityViewModel(group.Id, group.Name, $"{group.Count:N0} variants", isVisible, OnSourceVisibilityChanged));
            }

            SourceOptions.ReplaceAll(sourceOptions);
            SourceVisibilityOptions.ReplaceAll(sourceVisibilityOptions);
            SelectedSourceOption = SourceOptions.FirstOrDefault(option => option.Id == existingSelection) ?? SourceOptions.FirstOrDefault();
        }

        private BrowserCategoryViewModel? BuildVisibleCategories(string currentKey)
        {
            LogBrowse($"categories rebuild start preserveKey={currentKey ?? string.Empty}");

            var visibleSourceIds = SourceVisibilityOptions.Where(option => option.IsVisible).Select(option => option.Id).ToHashSet();
            if (visibleSourceIds.Count == 0)
            {
                visibleSourceIds = _allMovieGroups.SelectMany(group => group.Variants).Select(variant => variant.SourceProfile.Id).ToHashSet();
            }

            var categoryItems = new List<BrowserCategoryViewModel>
            {
                new BrowserCategoryViewModel { Id = 0, FilterKey = string.Empty, Name = "All Categories", OrderIndex = -1 }
            };

            var categories = _allMovieGroups
                .Where(group => group.Variants.Any(variant => visibleSourceIds.Contains(variant.SourceProfile.Id)))
                .Where(group => group.Variants.Any(variant => !HideSecondaryContent || string.Equals(variant.Movie.ContentKind, "Primary", StringComparison.OrdinalIgnoreCase)))
                .Select(group => group.PreferredMovie)
                .Select(GetCategoryProjection)
                .Where(category => !IsCategoryHidden(category.RawCategoryName))
                .Select(category => category.DisplayCategoryName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            categoryItems.AddRange(categories.Select((name, index) => new BrowserCategoryViewModel
                     {
                         Id = index + 1,
                         FilterKey = NormalizeCategoryKey(name),
                         Name = name,
                         OrderIndex = index + 1
                     }));

            Categories.ReplaceAll(categoryItems);

            var resolvedCategory = !string.IsNullOrWhiteSpace(currentKey)
                ? Categories.FirstOrDefault(category => string.Equals(category.FilterKey, currentKey, StringComparison.OrdinalIgnoreCase))
                    ?? Categories.FirstOrDefault()
                : Categories.FirstOrDefault();

            LogBrowse($"categories rebuild end count={Categories.Count} resolvedKey={resolvedCategory?.FilterKey ?? "<all>"}");
            return resolvedCategory;
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
                "favorites_first" => groups.OrderByDescending(group => group.Variants.Any(variant => _favoriteMovieIds.Contains(variant.Movie.Id))).ThenBy(group => group.PreferredMovie.Title, StringComparer.CurrentCultureIgnoreCase),
                _ => groups
            };
        }

        private bool MatchesMovieSearch(CatalogMovieGroup group, string displayCategory)
        {
            return group.PreferredMovie.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                   displayCategory.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                   group.SourceSummary.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
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
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var metadataService = scope.ServiceProvider.GetRequiredService<ITmdbMetadataService>();
                    var movies = await db.Movies.Take(36).ToListAsync();
                    await metadataService.EnrichMoviesAsync(db, movies, 36);
                }
                catch
                {
                }
            });
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
            return $"{distinct[0]} +{distinct.Count - 1} more";
        }
    }
}
