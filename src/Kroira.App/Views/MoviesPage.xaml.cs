using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;

namespace Kroira.App.Views
{
    public sealed partial class MoviesPage : Page
    {
        private bool _isRestoringCategorySelection;
        private bool _isCategorySelectionRestoreQueued;

        public MoviesViewModel ViewModel { get; }

        public Visibility DownloadActionsVisibility => StoreReleaseFeatures.ShowDownloadActions ? Visibility.Visible : Visibility.Collapsed;

        public MoviesPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<MoviesViewModel>();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.Categories.CollectionChanged += Categories_CollectionChanged;
            Loaded += MoviesPage_Loaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var navigationStopwatch = Stopwatch.StartNew();
            if (DispatcherQueue != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                    BrowseRuntimeLogger.Log(
                        "MOVIES UI",
                        $"page visible ms={navigationStopwatch.ElapsedMilliseconds} cached={ViewModel.HasLoadedOnce} slots={ViewModel.DisplayMovieSlots.Count}"));
            }

            var shouldLoad = !ViewModel.HasLoadedOnce ||
                             (ViewModel.DisplayMovieSlots.Count == 0 && ViewModel.SurfaceState.State != SurfaceViewState.Ready);
            if (shouldLoad)
            {
                BrowseRuntimeLogger.Log("MOVIES UI", "navigation triggered catalog load");
                _ = ViewModel.LoadMoviesCommand.ExecuteAsync(null);
                return;
            }

            BrowseRuntimeLogger.Log(
                "MOVIES UI",
                $"navigation reused cached surface slots={ViewModel.DisplayMovieSlots.Count} featured={ViewModel.FeaturedMovie?.Title ?? "<none>"}");
        }

        private void OpenSources_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SourceListPage));
        }

        private void MoviesPage_Loaded(object sender, RoutedEventArgs e)
        {
            QueueRestoreCategorySelection("page-loaded");
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (string.Equals(e.PropertyName, nameof(ViewModel.SelectedCategory), StringComparison.Ordinal))
            {
                QueueRestoreCategorySelection("selected-category-changed");
            }
        }

        private void Categories_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            QueueRestoreCategorySelection($"categories-{e.Action.ToString().ToLowerInvariant()}");
        }

        private void QueueRestoreCategorySelection(string reason)
        {
            if (_isCategorySelectionRestoreQueued)
            {
                return;
            }

            var dispatcherQueue = DispatcherQueue;
            if (dispatcherQueue == null)
            {
                RestoreCategorySelection(reason);
                return;
            }

            _isCategorySelectionRestoreQueued = true;
            var enqueued = dispatcherQueue.TryEnqueue(() =>
            {
                _isCategorySelectionRestoreQueued = false;
                RestoreCategorySelection(reason);
            });
            if (!enqueued)
            {
                _isCategorySelectionRestoreQueued = false;
            }

            BrowseRuntimeLogger.Log("MOVIES UI", $"selection restore queued reason={reason} enqueued={enqueued}");
        }

        private void RestoreCategorySelection(string reason)
        {
            if (_isRestoringCategorySelection || CategoryList == null)
            {
                return;
            }

            var selected = ViewModel.SelectedCategory;
            if (selected == null)
            {
                return;
            }

            var resolved = ViewModel.Categories.FirstOrDefault(category =>
                string.Equals(category.FilterKey, selected.FilterKey, StringComparison.OrdinalIgnoreCase));
            if (resolved == null)
            {
                return;
            }

            if (ReferenceEquals(CategoryList.SelectedItem, resolved))
            {
                return;
            }

            try
            {
                _isRestoringCategorySelection = true;
                CategoryList.SelectedItem = resolved;
                BrowseRuntimeLogger.Log("MOVIES UI", $"selection restored reason={reason} key={resolved.FilterKey}");
            }
            finally
            {
                _isRestoringCategorySelection = false;
            }
        }

        private async void FeaturedPlay_Click(object sender, RoutedEventArgs e)
        {
            var movie = ViewModel.FeaturedMovie;
            if (movie == null)
            {
                return;
            }

            var variant = await ChooseMovieVariantAsync(movie, "Play");
            if (variant == null || string.IsNullOrWhiteSpace(variant.Movie.StreamUrl))
            {
                return;
            }

            this.Frame.Navigate(typeof(EmbeddedPlaybackPage), CreateMovieLaunchContext(variant));
        }

        private async void FeaturedDownload_Click(object sender, RoutedEventArgs e)
        {
            var movie = ViewModel.FeaturedMovie;
            if (movie != null)
            {
                await QueueMovieDownloadAsync(movie);
            }
        }

        private async void FavoriteToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: int id })
            {
                await ViewModel.ToggleFavoriteCommand.ExecuteAsync(id);
            }
        }

        private async void GridView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is MovieBrowseSlotViewModel { HasMovie: true, Movie: { } movie })
            {
                var variant = await ChooseMovieVariantAsync(movie, "Play");
                if (variant != null && !string.IsNullOrWhiteSpace(variant.Movie.StreamUrl))
                {
                    this.Frame.Navigate(typeof(EmbeddedPlaybackPage), CreateMovieLaunchContext(variant));
                }
            }
        }

        private async void FeaturedInspect_Click(object sender, RoutedEventArgs e)
        {
            var movie = ViewModel.FeaturedMovie;
            if (movie == null)
            {
                return;
            }

            var variant = await ChooseMovieVariantAsync(movie, "Inspect");
            if (variant == null)
            {
                return;
            }

            await ItemInspectorDialog.ShowAsync(XamlRoot, CreateMovieLaunchContext(variant));
        }

        private async void MovieInspect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: MovieBrowseSlotViewModel { HasMovie: true, Movie: { } movie } })
            {
                return;
            }

            var variant = await ChooseMovieVariantAsync(movie, "Inspect");
            if (variant == null)
            {
                return;
            }

            await ItemInspectorDialog.ShowAsync(XamlRoot, CreateMovieLaunchContext(variant));
        }

        private async void MovieDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: MovieBrowseSlotViewModel { HasMovie: true, Movie: { } movie } })
            {
                await QueueMovieDownloadAsync(movie);
            }
        }

        private async Task QueueMovieDownloadAsync(MovieBrowseItemViewModel movie)
        {
            var variant = await ChooseMovieVariantAsync(movie, "Download");
            if (variant == null || string.IsNullOrWhiteSpace(variant.Movie.StreamUrl))
            {
                return;
            }

            try
            {
                var mediaJobService = ((App)Application.Current).Services.GetRequiredService<IMediaJobService>();
                await mediaJobService.QueueDownloadAsync(
                    PlaybackContentType.Movie,
                    variant.Movie.Id,
                    variant.Movie.Title,
                    variant.DisplayName,
                    variant.Movie.StreamUrl);
            }
            catch (Exception ex)
            {
                await ShowContentDialogAsync(new ContentDialog
                {
                    Title = "Download failed",
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot,
                    Content = ex.Message
                });
            }
        }

        private async Task<CatalogMovieVariant?> ChooseMovieVariantAsync(MovieBrowseItemViewModel movie, string actionLabel)
        {
            if (!movie.HasAlternateSources)
            {
                return movie.Variants.Count > 0 ? movie.Variants[0] : null;
            }

            var comboBox = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            foreach (var variant in movie.Variants)
            {
                comboBox.Items.Add(new ComboBoxItem
                {
                    Content = variant.DisplayName,
                    Tag = variant
                });
            }

            comboBox.SelectedIndex = 0;

            var dialog = new ContentDialog
            {
                Title = movie.Title,
                PrimaryButtonText = actionLabel,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = actionLabel == "Play"
                                ? "Choose a source for playback."
                                : actionLabel == "Download"
                                    ? "Choose a source to download."
                                    : "Choose a source to inspect.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        comboBox
                    }
                }
            };

            var result = await ShowContentDialogAsync(dialog);
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            return (comboBox.SelectedItem as ComboBoxItem)?.Tag as CatalogMovieVariant;
        }

        private static PlaybackLaunchContext CreateMovieLaunchContext(CatalogMovieVariant variant, long startPositionMs = 0)
        {
            return new PlaybackLaunchContext
            {
                ContentId = variant.Movie.Id,
                ContentType = PlaybackContentType.Movie,
                PreferredSourceProfileId = variant.SourceProfile.Id,
                CatalogStreamUrl = variant.Movie.StreamUrl,
                StreamUrl = variant.Movie.StreamUrl,
                StartPositionMs = startPositionMs
            };
        }

        private static Task<ContentDialogResult> ShowContentDialogAsync(ContentDialog dialog)
        {
            var completion = new TaskCompletionSource<ContentDialogResult>();
            var operation = dialog.ShowAsync();
            operation.Completed = (info, status) =>
            {
                switch (status)
                {
                    case AsyncStatus.Completed:
                        completion.TrySetResult(info.GetResults());
                        break;
                    case AsyncStatus.Canceled:
                        completion.TrySetCanceled();
                        break;
                    case AsyncStatus.Error:
                        completion.TrySetException(info.ErrorCode);
                        break;
                }
            };

            return completion.Task;
        }
    }
}
