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

        public CatalogSeriesGroup Group { get; }
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
        public string MetadataLine =>
            Group.Variants.Count > 1 && !string.IsNullOrWhiteSpace(Group.SourceSummary)
                ? string.IsNullOrWhiteSpace(ActiveSeries.MetadataLine)
                    ? Group.SourceSummary
                    : $"{ActiveSeries.MetadataLine} / {Group.SourceSummary}"
                : ActiveSeries.MetadataLine;
        public string Overview => string.IsNullOrWhiteSpace(ActiveSeries.Overview) ? PreferredSeries.Overview : ActiveSeries.Overview;
        public string CategoryName => PreferredSeries.CategoryName;
        public string DisplayCategoryName { get; }
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
            Series = series;
        }

        public SeriesBrowseItemViewModel? Series { get; }
        public bool HasSeries => Series != null;
        public int Id => Series?.Id ?? 0;
        public string Title => Series?.Title ?? string.Empty;
        public string DisplayPosterUrl => Series?.DisplayPosterUrl ?? string.Empty;
        public string CategoryName => Series?.DisplayCategoryName ?? string.Empty;
        public string FavoriteGlyph => Series?.FavoriteGlyph ?? "\uE734";
        public string FavoriteLabel => Series?.FavoriteLabel ?? string.Empty;
        public Visibility SeriesVisibility => HasSeries ? Visibility.Visible : Visibility.Collapsed;
        public Visibility PlaceholderVisibility => HasSeries ? Visibility.Collapsed : Visibility.Visible;

        public void RefreshFavoriteState()
        {
            OnPropertyChanged(nameof(FavoriteGlyph));
            OnPropertyChanged(nameof(FavoriteLabel));
        }
    }

    public partial class SeriesViewModel : ObservableObject
    {
        private const string Domain = ProfileDomains.Series;
        private const int BrowseGridColumns = 3;
        private readonly IServiceProvider _serviceProvider;
        private readonly List<CatalogSeriesGroup> _allSeriesGroups = new();
        private List<SeriesBrowseItemViewModel> _allSeries = new List<SeriesBrowseItemViewModel>();
        private readonly List<(string CategoryName, int Count)> _allCategories = new();
        private readonly Dictionary<int, WatchProgressSnapshot> _selectedEpisodeSnapshots = new();
        private bool _isLoadingWatchPreferences;
        private bool _isInitializingBrowsePreferences;
        private int _activeProfileId;
        private int? _pendingEpisodeSelectionId;
        private BrowsePreferences _browsePreferences = new();
        private HashSet<int> _favoriteSeriesIds = new();
        private string _browseLanguageCode = AppLanguageService.DefaultLanguageCode;
        private bool _isApplyingFilter;
        private bool _pendingApplyFilter;
        private string _pendingApplyFilterReason = string.Empty;

        public ObservableCollection<SeriesBrowseItemViewModel> FilteredSeries { get; } = new ObservableCollection<SeriesBrowseItemViewModel>();
        public ObservableCollection<SeriesBrowseSlotViewModel> DisplaySeriesSlots { get; } = new ObservableCollection<SeriesBrowseSlotViewModel>();
        public ObservableCollection<BrowserCategoryViewModel> Categories { get; } = new ObservableCollection<BrowserCategoryViewModel>();
        public ObservableCollection<SeriesSeasonOptionViewModel> SeasonOptions { get; } = new ObservableCollection<SeriesSeasonOptionViewModel>();
        public ObservableCollection<SeriesEpisodeItemViewModel> EpisodeItems { get; } = new ObservableCollection<SeriesEpisodeItemViewModel>();
        public ObservableCollection<BrowseSortOptionViewModel> SortOptions { get; } = new ObservableCollection<BrowseSortOptionViewModel>();
        public ObservableCollection<BrowseSourceFilterOptionViewModel> SourceOptions { get; } = new ObservableCollection<BrowseSourceFilterOptionViewModel>();
        public ObservableCollection<BrowseSourceVisibilityViewModel> SourceVisibilityOptions { get; } = new ObservableCollection<BrowseSourceVisibilityViewModel>();
        public ObservableCollection<BrowseCategoryManagerOptionViewModel> ManageCategoryOptions { get; } = new ObservableCollection<BrowseCategoryManagerOptionViewModel>();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private BrowserCategoryViewModel? _selectedCategory;

        [ObservableProperty]
        private BrowseSortOptionViewModel? _selectedSortOption;

        [ObservableProperty]
        private BrowseSourceFilterOptionViewModel? _selectedSourceOption;

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

        [ObservableProperty]
        private string _selectedSeriesStatus = string.Empty;

        [ObservableProperty]
        private Visibility _selectedSeriesStatusVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private bool _isEmpty;

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
                ManageCategoryAliasDraft = string.Equals(value.RawName, value.EffectiveName, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : value.EffectiveName;
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

        partial void OnSelectedSeriesChanged(SeriesBrowseItemViewModel? value)
        {
            SelectedSeason = null;
            SelectedEpisode = null;
            SeasonOptions.Clear();
            EpisodeItems.Clear();
            SelectedSeriesStatus = string.Empty;
            SelectedSeriesStatusVisibility = Visibility.Collapsed;

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
            SortOptions.Add(new BrowseSortOptionViewModel("recommended", "Recommended"));
            SortOptions.Add(new BrowseSortOptionViewModel("title_asc", "Title A-Z"));
            SortOptions.Add(new BrowseSortOptionViewModel("rating_desc", "Highest rated"));
            SortOptions.Add(new BrowseSortOptionViewModel("popularity_desc", "Most popular"));
            SortOptions.Add(new BrowseSortOptionViewModel("year_desc", "Newest first"));
            SortOptions.Add(new BrowseSortOptionViewModel("favorites_first", "Favorites first"));
        }

        [RelayCommand]
        public async Task LoadSeriesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deduplicationService = scope.ServiceProvider.GetRequiredService<ICatalogDeduplicationService>();
            var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
            var watchStateService = scope.ServiceProvider.GetRequiredService<ILibraryWatchStateService>();
            var browsePreferencesService = scope.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
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
            var seriesGroups = (await deduplicationService.LoadSeriesGroupsAsync(db))
                .Select(group => FilterGroup(group, access))
                .OfType<CatalogSeriesGroup>()
                .ToList();
            _favoriteSeriesIds = (await db.Favorites
                .Where(f => f.ProfileId == access.ProfileId && f.ContentType == FavoriteType.Series)
                .Select(f => f.ContentId)
                .ToListAsync())
                .ToHashSet();

            _allSeriesGroups.Clear();
            _allSeriesGroups.AddRange(CatalogOrderingService
                .OrderCatalog(seriesGroups, languageCode, g => g.PreferredSeries.CategoryName, g => g.PreferredSeries.Title));

            foreach (var item in _allSeriesGroups)
            {
                foreach (var variant in item.Variants.Select(groupVariant => groupVariant.Series))
                {
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
                .GroupBy(group => GetRawCategory(group.PreferredSeries.CategoryName))
                .Select(group => (group.Key, group.Count()))
                .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase));

            BuildSourceOptions();
            BuildCategoryManagerOptions();

            _isInitializingBrowsePreferences = true;
            try
            {
                FavoritesOnly = _browsePreferences.FavoritesOnly;
                HideSecondaryContent = _browsePreferences.HideSecondaryContent;
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
            StartMetadataEnrichment();
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
            var currentCategoryKey = SelectedCategory?.FilterKey ?? string.Empty;
            LogBrowse(
                $"apply start reason={reason} selectedKey={currentCategoryKey} search='{SearchQuery}' source={SelectedSourceOption?.Id ?? 0}");

            var previousSelectedGroupKey = SelectedSeries?.GroupKey ?? string.Empty;
            foreach (var item in _allSeries)
            {
                item.ActiveSeriesChanged -= SelectedSeries_ActiveSeriesChanged;
            }

            var filteredGroups = BuildFilteredSeriesGroups().ToList();

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

            _allSeries = filteredGroups
                .Select(group => new SeriesBrowseItemViewModel(
                    group,
                    group.Variants.Any(variant => _favoriteSeriesIds.Contains(variant.Series.Id)),
                    GetEffectiveCategoryName(group.PreferredSeries.CategoryName)))
                .ToList();

            FilteredSeries.Clear();
            foreach (var item in _allSeries)
            {
                FilteredSeries.Add(item);
            }

            SelectedSeries = _allSeries.FirstOrDefault(item => string.Equals(item.GroupKey, previousSelectedGroupKey, StringComparison.OrdinalIgnoreCase));
            HasAdvancedFilters = FavoritesOnly ||
                                 HideSecondaryContent ||
                                 SourceVisibilityOptions.Any(option => !option.IsVisible) ||
                                 _browsePreferences.HiddenCategoryKeys.Count > 0 ||
                                 _browsePreferences.CategoryRemaps.Count > 0 ||
                                 (SelectedSourceOption?.Id ?? 0) != 0 ||
                                 !string.Equals(SelectedSortOption?.Key ?? "recommended", "recommended", StringComparison.OrdinalIgnoreCase);
            IsEmpty = FilteredSeries.Count == 0;
            RefreshDisplaySeriesSlots();
            LogBrowse(
                $"apply end reason={reason} groups={filteredGroups.Count} results={FilteredSeries.Count} slots={DisplaySeriesSlots.Count} selectedKey={SelectedCategory?.FilterKey ?? "<all>"}");
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
            var variantIds = targetGroup.Variants.Select(variant => variant.Series.Id).ToList();
            var existingFavorites = await db.Favorites
                .Where(f => f.ProfileId == activeProfileId && f.ContentType == FavoriteType.Series && variantIds.Contains(f.ContentId))
                .ToListAsync();

            if (existingFavorites.Count == 0)
            {
                db.Favorites.Add(new Favorite { ProfileId = activeProfileId, ContentType = FavoriteType.Series, ContentId = seriesId });
                _favoriteSeriesIds.Add(seriesId);
            }
            else
            {
                db.Favorites.RemoveRange(existingFavorites);
                foreach (var favorite in existingFavorites)
                {
                    _favoriteSeriesIds.Remove(favorite.ContentId);
                }
            }

            await db.SaveChangesAsync();
            ApplyFilter("toggle-favorite");
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
            if (string.IsNullOrWhiteSpace(alias) || string.Equals(alias, SelectedManageCategory.RawName, StringComparison.OrdinalIgnoreCase))
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

        private void RefreshDisplaySeriesSlots()
        {
            DisplaySeriesSlots.Clear();

            foreach (var show in FilteredSeries)
            {
                DisplaySeriesSlots.Add(new SeriesBrowseSlotViewModel(show));
            }

            if (FilteredSeries.Count == 0)
            {
                return;
            }

            var remainder = FilteredSeries.Count % BrowseGridColumns;
            var placeholderCount = remainder == 0 ? 0 : BrowseGridColumns - remainder;
            for (var i = 0; i < placeholderCount; i++)
            {
                DisplaySeriesSlots.Add(new SeriesBrowseSlotViewModel(null));
            }
        }

        private IEnumerable<CatalogSeriesGroup> BuildFilteredSeriesGroups()
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
            var results = new List<CatalogSeriesGroup>();

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
                if (IsCategoryHidden(preferredSeries.CategoryName))
                {
                    continue;
                }

                var displayCategory = GetEffectiveCategoryName(preferredSeries.CategoryName);
                if (!string.IsNullOrWhiteSpace(selectedCategoryKey) &&
                    !string.Equals(NormalizeCategoryKey(displayCategory), selectedCategoryKey, StringComparison.OrdinalIgnoreCase))
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

                if (FavoritesOnly && !filteredGroup.Variants.Any(variant => _favoriteSeriesIds.Contains(variant.Series.Id)))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(SearchQuery) &&
                    !MatchesSeriesSearch(filteredGroup, displayCategory))
                {
                    continue;
                }

                results.Add(filteredGroup);
            }

            return SortSeriesGroups(results);
        }

        private void BuildSourceOptions()
        {
            var existingSelection = _browsePreferences.SelectedSourceId;
            SourceOptions.Clear();
            SourceVisibilityOptions.Clear();
            SourceOptions.Add(new BrowseSourceFilterOptionViewModel(0, "All providers", _allSeriesGroups.Count));

            foreach (var group in _allSeriesGroups
                         .SelectMany(item => item.Variants)
                         .GroupBy(variant => variant.SourceProfile.Id)
                         .Select(group => new { Id = group.Key, Name = group.First().SourceProfile.Name, Count = group.Count() })
                         .OrderBy(group => group.Name))
            {
                var isVisible = !_browsePreferences.HiddenSourceIds.Contains(group.Id);
                SourceOptions.Add(new BrowseSourceFilterOptionViewModel(group.Id, group.Name, group.Count));
                SourceVisibilityOptions.Add(new BrowseSourceVisibilityViewModel(group.Id, group.Name, $"{group.Count:N0} variants", isVisible, OnSourceVisibilityChanged));
            }

            SelectedSourceOption = SourceOptions.FirstOrDefault(option => option.Id == existingSelection) ?? SourceOptions.FirstOrDefault();
        }

        private BrowserCategoryViewModel? BuildVisibleCategories(string languageCode, string currentKey)
        {
            LogBrowse($"categories rebuild start preserveKey={currentKey}");
            Categories.Clear();
            Categories.Add(new BrowserCategoryViewModel { Id = 0, FilterKey = string.Empty, Name = "All Categories", OrderIndex = -1 });

            var visibleSourceIds = SourceVisibilityOptions.Where(option => option.IsVisible).Select(option => option.Id).ToHashSet();
            if (visibleSourceIds.Count == 0)
            {
                visibleSourceIds = _allSeriesGroups.SelectMany(group => group.Variants).Select(variant => variant.SourceProfile.Id).ToHashSet();
            }

            var categoryNames = _allSeriesGroups
                .Where(group => group.Variants.Any(variant => visibleSourceIds.Contains(variant.SourceProfile.Id)))
                .Where(group => group.Variants.Any(variant => !HideSecondaryContent || string.Equals(variant.Series.ContentKind, "Primary", StringComparison.OrdinalIgnoreCase)))
                .Select(group => group.PreferredSeries.CategoryName)
                .Where(category => !IsCategoryHidden(category))
                .Select(GetEffectiveCategoryName)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var orderedCategories = CatalogOrderingService.OrderCategories(categoryNames, languageCode);
            var categoryIndex = 1;
            foreach (var categoryName in orderedCategories)
            {
                Categories.Add(new BrowserCategoryViewModel
                {
                    Id = categoryIndex,
                    FilterKey = NormalizeCategoryKey(categoryName),
                    Name = categoryName,
                    OrderIndex = categoryIndex
                });
                categoryIndex++;
            }

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
            ManageCategoryOptions.Clear();

            foreach (var category in _allCategories.OrderBy(item => item.CategoryName, StringComparer.CurrentCultureIgnoreCase))
            {
                var key = NormalizeCategoryKey(category.CategoryName);
                ManageCategoryOptions.Add(new BrowseCategoryManagerOptionViewModel(
                    key,
                    category.CategoryName,
                    GetEffectiveCategoryName(category.CategoryName),
                    category.Count,
                    _browsePreferences.HiddenCategoryKeys.Contains(key, StringComparer.OrdinalIgnoreCase)));
            }

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
                "favorites_first" => groups.OrderByDescending(group => group.Variants.Any(variant => _favoriteSeriesIds.Contains(variant.Series.Id))).ThenBy(group => group.PreferredSeries.Title, StringComparer.CurrentCultureIgnoreCase),
                _ => groups
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
            return _serviceProvider.GetRequiredService<IBrowsePreferencesService>().NormalizeCategoryKey(categoryName);
        }

        private string GetEffectiveCategoryName(string categoryName)
        {
            return _serviceProvider.GetRequiredService<IBrowsePreferencesService>().GetEffectiveCategoryName(_browsePreferences, GetRawCategory(categoryName));
        }

        private bool IsCategoryHidden(string categoryName)
        {
            return _serviceProvider.GetRequiredService<IBrowsePreferencesService>().IsCategoryHidden(_browsePreferences, GetRawCategory(categoryName));
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
                    var series = await db.Series.Take(28).ToListAsync();
                    await metadataService.EnrichSeriesAsync(db, series, 28);
                }
                catch
                {
                }
            });
        }

        private async Task EnsureSeriesDetailsAsync(SeriesBrowseItemViewModel item)
        {
            var series = item.ActiveSeries;

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
                SelectedSeason = null;
                SelectedEpisode = null;
                SeasonOptions.Clear();
                EpisodeItems.Clear();
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

            var queueSelection = watchStateService.BuildSeriesQueueSelection(series, _selectedEpisodeSnapshots, includeWatched: !HideWatchedEpisodes);
            _pendingEpisodeSelectionId = queueSelection?.Episode.Id;
            SelectedSeason = queueSelection != null
                ? SeasonOptions.FirstOrDefault(option => option.Id == queueSelection.Season.Id)
                : SeasonOptions.FirstOrDefault();
        }

        private void BuildSeasonOptions(Series series)
        {
            SeasonOptions.Clear();

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

                SeasonOptions.Add(new SeriesSeasonOptionViewModel(season, watchedEpisodeCount, playableEpisodes.Count));
            }

            OnPropertyChanged(nameof(SeasonOptions));
        }

        private void BuildEpisodeItems(SeriesSeasonOptionViewModel? seasonOption, int? preferredEpisodeId)
        {
            EpisodeItems.Clear();
            SelectedEpisode = null;

            var season = seasonOption?.Season;
            if (season == null)
            {
                OnPropertyChanged(nameof(EpisodesListVisibility));
                OnPropertyChanged(nameof(EpisodesEmptyVisibility));
                return;
            }

            foreach (var episode in (season.Episodes ?? Array.Empty<Episode>())
                         .Where(episode => !string.IsNullOrWhiteSpace(episode.StreamUrl))
                         .OrderBy(episode => episode.EpisodeNumber))
            {
                _selectedEpisodeSnapshots.TryGetValue(episode.Id, out var snapshot);
                if (HideWatchedEpisodes && snapshot?.IsWatched == true)
                {
                    continue;
                }

                EpisodeItems.Add(new SeriesEpisodeItemViewModel(episode, season.SeasonNumber, snapshot));
            }

            SelectedEpisode = preferredEpisodeId.HasValue
                ? EpisodeItems.FirstOrDefault(item => item.Id == preferredEpisodeId.Value)
                : EpisodeItems.FirstOrDefault(item => item.ResumePositionMs > 0) ?? EpisodeItems.FirstOrDefault();

            OnPropertyChanged(nameof(EpisodesListVisibility));
            OnPropertyChanged(nameof(EpisodesEmptyVisibility));
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
