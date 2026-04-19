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
        public string MetadataLine =>
            Group.Variants.Count > 1 && !string.IsNullOrWhiteSpace(Group.SourceSummary)
                ? string.IsNullOrWhiteSpace(Movie.MetadataLine) ? Group.SourceSummary : $"{Movie.MetadataLine} / {Group.SourceSummary}"
                : Movie.MetadataLine;
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
        private readonly List<CatalogMovieGroup> _allMovieGroups = new();
        private readonly Dictionary<int, SourceType> _sourceTypeById = new();
        private readonly List<(string CategoryName, int Count)> _allCategories = new();
        private static readonly int _sessionRotationIndex = Math.Abs(Environment.TickCount % 5);
        private BrowsePreferences _preferences = new();
        private HashSet<int> _favoriteMovieIds = new();
        private int _activeProfileId;
        private string _languageCode = AppLanguageService.DefaultLanguageCode;
        private bool _isInitializing;

        public ObservableCollection<MovieBrowseItemViewModel> FilteredMovies { get; } = new();
        public ObservableCollection<MovieBrowseSlotViewModel> DisplayMovieSlots { get; } = new();
        public ObservableCollection<BrowserCategoryViewModel> Categories { get; } = new();
        public ObservableCollection<BrowseSortOptionViewModel> SortOptions { get; } = new();
        public ObservableCollection<BrowseSourceFilterOptionViewModel> SourceOptions { get; } = new();
        public ObservableCollection<BrowseSourceVisibilityViewModel> SourceVisibilityOptions { get; } = new();
        public ObservableCollection<BrowseCategoryManagerOptionViewModel> ManageCategoryOptions { get; } = new();

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
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FeaturedMovieCanPlay))]
        private MovieBrowseItemViewModel _featuredMovie = CreatePlaceholderFeaturedMovie();

        public bool FeaturedMovieCanPlay => !string.IsNullOrWhiteSpace(FeaturedMovie?.StreamUrl);
        public bool HasManageCategorySelection => SelectedManageCategory != null;

        public MoviesViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            SortOptions.Add(new BrowseSortOptionViewModel("recommended", "Recommended"));
            SortOptions.Add(new BrowseSortOptionViewModel("title_asc", "Title A-Z"));
            SortOptions.Add(new BrowseSortOptionViewModel("rating_desc", "Highest rated"));
            SortOptions.Add(new BrowseSortOptionViewModel("popularity_desc", "Most popular"));
            SortOptions.Add(new BrowseSortOptionViewModel("year_desc", "Newest release"));
            SortOptions.Add(new BrowseSortOptionViewModel("favorites_first", "Favorites first"));
        }

        partial void OnSearchQueryChanged(string value) => ApplyFilter();
        partial void OnSelectedCategoryChanged(BrowserCategoryViewModel? value) { if (!_isInitializing) ApplyFilter(); }
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
                ManageCategoryAliasDraft = string.Equals(value.RawName, value.EffectiveName, StringComparison.OrdinalIgnoreCase) ? string.Empty : value.EffectiveName;
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
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deduplicationService = scope.ServiceProvider.GetRequiredService<ICatalogDeduplicationService>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var browsePreferencesService = scope.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
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

            _allMovieGroups.Clear();
            _allMovieGroups.AddRange(CatalogOrderingService.OrderCatalog(
                movieGroups,
                _languageCode,
                group => group.PreferredMovie.CategoryName,
                group => group.PreferredMovie.Title));

            _sourceTypeById.Clear();
            foreach (var group in _allMovieGroups)
            {
                foreach (var variant in group.Variants)
                {
                    _sourceTypeById[variant.SourceProfile.Id] = variant.SourceProfile.Type;
                }
            }

            _allCategories.Clear();
            _allCategories.AddRange(_allMovieGroups
                .GroupBy(group => GetRawCategory(group.PreferredMovie.CategoryName))
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
                BuildVisibleCategories();
                SelectedCategory = Categories.FirstOrDefault();
            }
            finally
            {
                _isInitializing = false;
            }

            ApplyFilter();
            StartMetadataEnrichment();
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
            if (string.IsNullOrWhiteSpace(alias) || string.Equals(alias, SelectedManageCategory.RawName, StringComparison.OrdinalIgnoreCase))
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

        private void ApplyFilter()
        {
            var filteredGroups = BuildFilteredMovieGroups().ToList();
            var currentCategoryKey = SelectedCategory?.FilterKey ?? string.Empty;

            BuildVisibleCategories();
            if (!string.IsNullOrWhiteSpace(currentCategoryKey))
            {
                SelectedCategory = Categories.FirstOrDefault(category => string.Equals(category.FilterKey, currentCategoryKey, StringComparison.OrdinalIgnoreCase))
                    ?? Categories.FirstOrDefault();
            }

            FilteredMovies.Clear();
            foreach (var group in filteredGroups)
            {
                FilteredMovies.Add(new MovieBrowseItemViewModel(
                    group,
                    group.Variants.Any(variant => _favoriteMovieIds.Contains(variant.Movie.Id)),
                    GetEffectiveCategoryName(group.PreferredMovie.CategoryName)));
            }

            RefreshFeaturedMovie(FilteredMovies);
            IsEmpty = FilteredMovies.Count == 0;
            HasAdvancedFilters = FavoritesOnly ||
                                 HideSecondaryContent ||
                                 SourceVisibilityOptions.Any(option => !option.IsVisible) ||
                                 _preferences.HiddenCategoryKeys.Count > 0 ||
                                 _preferences.CategoryRemaps.Count > 0 ||
                                 (SelectedSourceOption?.Id ?? 0) != 0 ||
                                 !string.Equals(SelectedSortOption?.Key ?? "recommended", "recommended", StringComparison.OrdinalIgnoreCase);
            RefreshDisplayMovieSlots();
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
            ApplyFilter();
        }

        private IEnumerable<CatalogMovieGroup> BuildFilteredMovieGroups()
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
            var results = new List<CatalogMovieGroup>();

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
                if (IsCategoryHidden(preferredMovie.CategoryName))
                {
                    continue;
                }

                var displayCategory = GetEffectiveCategoryName(preferredMovie.CategoryName);
                if (!string.IsNullOrWhiteSpace(selectedCategoryKey) &&
                    !string.Equals(NormalizeCategoryKey(displayCategory), selectedCategoryKey, StringComparison.OrdinalIgnoreCase))
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
                    !MatchesMovieSearch(filteredGroup, displayCategory))
                {
                    continue;
                }

                results.Add(filteredGroup);
            }

            return SortMovieGroups(results);
        }

        private void RefreshDisplayMovieSlots()
        {
            DisplayMovieSlots.Clear();
            foreach (var movie in FilteredMovies)
            {
                DisplayMovieSlots.Add(new MovieBrowseSlotViewModel(movie));
            }

            if (FilteredMovies.Count == 0)
            {
                return;
            }

            var remainder = FilteredMovies.Count % BrowseGridColumns;
            var placeholderCount = remainder == 0 ? 0 : BrowseGridColumns - remainder;
            for (var index = 0; index < placeholderCount; index++)
            {
                DisplayMovieSlots.Add(new MovieBrowseSlotViewModel(null));
            }
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
            SourceOptions.Clear();
            SourceVisibilityOptions.Clear();
            SourceOptions.Add(new BrowseSourceFilterOptionViewModel(0, "All providers", _allMovieGroups.Count));

            foreach (var group in _allMovieGroups
                         .SelectMany(item => item.Variants)
                         .GroupBy(variant => variant.SourceProfile.Id)
                         .Select(group => new { Id = group.Key, Name = group.First().SourceProfile.Name, Count = group.Count() })
                         .OrderBy(group => group.Name))
            {
                var isVisible = !_preferences.HiddenSourceIds.Contains(group.Id);
                SourceOptions.Add(new BrowseSourceFilterOptionViewModel(group.Id, group.Name, group.Count));
                SourceVisibilityOptions.Add(new BrowseSourceVisibilityViewModel(group.Id, group.Name, $"{group.Count:N0} variants", isVisible, OnSourceVisibilityChanged));
            }

            SelectedSourceOption = SourceOptions.FirstOrDefault(option => option.Id == existingSelection) ?? SourceOptions.FirstOrDefault();
        }

        private void BuildVisibleCategories()
        {
            var currentKey = SelectedCategory?.FilterKey ?? string.Empty;
            Categories.Clear();
            Categories.Add(new BrowserCategoryViewModel { Id = 0, FilterKey = string.Empty, Name = "All Categories", OrderIndex = -1 });

            var visibleSourceIds = SourceVisibilityOptions.Where(option => option.IsVisible).Select(option => option.Id).ToHashSet();
            if (visibleSourceIds.Count == 0)
            {
                visibleSourceIds = _allMovieGroups.SelectMany(group => group.Variants).Select(variant => variant.SourceProfile.Id).ToHashSet();
            }

            var categories = _allMovieGroups
                .Where(group => group.Variants.Any(variant => visibleSourceIds.Contains(variant.SourceProfile.Id)))
                .Where(group => group.Variants.Any(variant => !HideSecondaryContent || string.Equals(variant.Movie.ContentKind, "Primary", StringComparison.OrdinalIgnoreCase)))
                .Select(group => group.PreferredMovie.CategoryName)
                .Where(category => !IsCategoryHidden(category))
                .Select(GetEffectiveCategoryName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var category in categories.Select((name, index) => new BrowserCategoryViewModel
                     {
                         Id = index + 1,
                         FilterKey = NormalizeCategoryKey(name),
                         Name = name,
                         OrderIndex = index + 1
                     }))
            {
                Categories.Add(category);
            }

            if (!string.IsNullOrWhiteSpace(currentKey))
            {
                SelectedCategory = Categories.FirstOrDefault(category => string.Equals(category.FilterKey, currentKey, StringComparison.OrdinalIgnoreCase))
                    ?? Categories.FirstOrDefault();
            }
        }

        private void BuildCategoryManagerOptions()
        {
            var currentKey = SelectedManageCategory?.Key ?? string.Empty;
            ManageCategoryOptions.Clear();

            foreach (var category in _allCategories.OrderBy(item => item.CategoryName, StringComparer.CurrentCultureIgnoreCase))
            {
                var key = NormalizeCategoryKey(category.CategoryName);
                ManageCategoryOptions.Add(new BrowseCategoryManagerOptionViewModel(
                    key,
                    category.CategoryName,
                    GetEffectiveCategoryName(category.CategoryName),
                    category.Count,
                    _preferences.HiddenCategoryKeys.Contains(key, StringComparer.OrdinalIgnoreCase)));
            }

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
                BuildVisibleCategories();
            }

            ApplyFilter();
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

        private string NormalizeCategoryKey(string categoryName)
        {
            return _serviceProvider.GetRequiredService<IBrowsePreferencesService>().NormalizeCategoryKey(categoryName);
        }

        private string GetEffectiveCategoryName(string categoryName)
        {
            return _serviceProvider.GetRequiredService<IBrowsePreferencesService>().GetEffectiveCategoryName(_preferences, GetRawCategory(categoryName));
        }

        private bool IsCategoryHidden(string categoryName)
        {
            return _serviceProvider.GetRequiredService<IBrowsePreferencesService>().IsCategoryHidden(_preferences, GetRawCategory(categoryName));
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
