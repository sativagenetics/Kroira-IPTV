#nullable enable

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
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.System;

namespace Kroira.App.Views
{
    public sealed partial class MoviesPage : Page, IRemoteNavigationPage, ILocalizationRefreshable
    {
        private const string MediaActionOverlayTag = "MediaActionOverlay";
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
            ViewModel.RefreshLocalizedLabelsIfNeeded();
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

        private void RetrySurface_Click(object sender, RoutedEventArgs e)
        {
            _ = ViewModel.LoadMoviesCommand.ExecuteAsync(null);
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

            var variant = await ChooseMovieVariantAsync(movie, "General_Play");
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
            var variant = await ChooseMovieVariantAsync(movie, "General_Play");
                if (variant != null && !string.IsNullOrWhiteSpace(variant.Movie.StreamUrl))
                {
                    this.Frame.Navigate(typeof(EmbeddedPlaybackPage), CreateMovieLaunchContext(variant));
                }
            }
        }

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Down || e.Key == VirtualKey.Enter)
            {
                RemoteNavigationHelper.TryFocusListItem(MovieGrid);
                e.Handled = true;
            }
        }

        private async void MovieGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                RemoteNavigationHelper.TryFocusElement(SearchBox);
                e.Handled = true;
                return;
            }

            if ((e.Key == VirtualKey.Enter || e.Key == VirtualKey.Space) &&
                !RemoteNavigationHelper.IsWithinInteractiveControl(e.OriginalSource) &&
                RemoteNavigationHelper.FindDataContextInAncestors<MovieBrowseSlotViewModel>(e.OriginalSource) is { HasMovie: true, Movie: { } movie })
            {
            var variant = await ChooseMovieVariantAsync(movie, "General_Play");
                if (variant != null && !string.IsNullOrWhiteSpace(variant.Movie.StreamUrl))
                {
                    Frame.Navigate(typeof(EmbeddedPlaybackPage), CreateMovieLaunchContext(variant));
                }

                e.Handled = true;
            }
        }

        private void RevealCardRoot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement root)
            {
                return;
            }

            SetActionOverlay(root, reveal: false);
            root.PointerEntered -= RevealCard_PointerEntered;
            root.PointerEntered += RevealCard_PointerEntered;
            root.PointerExited -= RevealCard_PointerExited;
            root.PointerExited += RevealCard_PointerExited;
            root.GotFocus -= RevealCard_GotFocus;
            root.GotFocus += RevealCard_GotFocus;
            root.LostFocus -= RevealCard_LostFocus;
            root.LostFocus += RevealCard_LostFocus;

            var item = FindAncestor<GridViewItem>(root);
            if (item == null)
            {
                return;
            }

            item.PointerEntered -= RevealCard_PointerEntered;
            item.PointerEntered += RevealCard_PointerEntered;
            item.PointerExited -= RevealCard_PointerExited;
            item.PointerExited += RevealCard_PointerExited;
            item.GotFocus -= RevealCard_GotFocus;
            item.GotFocus += RevealCard_GotFocus;
            item.LostFocus -= RevealCard_LostFocus;
            item.LostFocus += RevealCard_LostFocus;
        }

        private void RevealCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            SetActionOverlay(sender as DependencyObject, reveal: true);
        }

        private void RevealCard_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            var source = sender as DependencyObject;
            if (HasKeyboardFocusWithin(source))
            {
                return;
            }

            SetActionOverlay(source, reveal: false);
        }

        private void RevealCard_GotFocus(object sender, RoutedEventArgs e)
        {
            SetActionOverlay(sender as DependencyObject, reveal: true);
        }

        private void RevealCard_LostFocus(object sender, RoutedEventArgs e)
        {
            var source = sender as DependencyObject;
            if (DispatcherQueue != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!HasKeyboardFocusWithin(source))
                    {
                        SetActionOverlay(source, reveal: false);
                    }
                });
                return;
            }

            if (!HasKeyboardFocusWithin(source))
            {
                SetActionOverlay(source, reveal: false);
            }
        }

        private static void SetActionOverlay(DependencyObject? source, bool reveal)
        {
            if (source == null)
            {
                return;
            }

            var overlay = FindTaggedDescendant(source, MediaActionOverlayTag);
            if (overlay == null)
            {
                return;
            }

            overlay.Visibility = reveal ? Visibility.Visible : Visibility.Collapsed;
            overlay.Opacity = reveal ? 1 : 0;
            overlay.IsHitTestVisible = reveal;
        }

        private static bool HasKeyboardFocusWithin(DependencyObject? source)
        {
            if (source == null)
            {
                return false;
            }

            var focused = FocusManager.GetFocusedElement();
            return focused is DependencyObject focusedObject && IsDescendantOrSelf(source, focusedObject);
        }

        private static bool IsDescendantOrSelf(DependencyObject root, DependencyObject candidate)
        {
            var current = candidate;
            while (current != null)
            {
                if (ReferenceEquals(current, root))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static FrameworkElement? FindTaggedDescendant(DependencyObject root, string tag)
        {
            if (root is FrameworkElement element && Equals(element.Tag, tag))
            {
                return element;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var match = FindTaggedDescendant(child, tag);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static T? FindAncestor<T>(DependencyObject? source)
            where T : DependencyObject
        {
            var current = source == null ? null : VisualTreeHelper.GetParent(source);
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private async void FeaturedInspect_Click(object sender, RoutedEventArgs e)
        {
            var movie = ViewModel.FeaturedMovie;
            if (movie == null)
            {
                return;
            }

            var variant = await ChooseMovieVariantAsync(movie, "General_Inspect");
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

            var variant = await ChooseMovieVariantAsync(movie, "General_Inspect");
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
            var variant = await ChooseMovieVariantAsync(movie, "General_Download");
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
                    Title = LocalizedStrings.Get("Movies_DownloadFailedTitle"),
                    CloseButtonText = LocalizedStrings.Get("General_Close"),
                    XamlRoot = this.XamlRoot,
                    Content = ex.Message
                });
            }
        }

        private async Task<CatalogMovieVariant?> ChooseMovieVariantAsync(MovieBrowseItemViewModel movie, string actionResourceKey)
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
                PrimaryButtonText = LocalizedStrings.Get(actionResourceKey),
                CloseButtonText = LocalizedStrings.Get("General_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = actionResourceKey == "General_Play"
                                ? LocalizedStrings.Get("Movies_ChooseSource_Playback")
                                : actionResourceKey == "General_Download"
                                    ? LocalizedStrings.Get("Movies_ChooseSource_Download")
                                    : LocalizedStrings.Get("Movies_ChooseSource_Inspect"),
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

        public bool TryFocusPrimaryTarget()
        {
            return RemoteNavigationHelper.TryFocusElement(FeaturedPlayButton) ||
                   RemoteNavigationHelper.TryFocusElement(SearchBox) ||
                   RemoteNavigationHelper.TryFocusListItem(MovieGrid);
        }

        public bool TryHandleBackRequest()
        {
            var focusedElement = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (!RemoteNavigationHelper.IsDescendantOf(focusedElement, SearchBox))
            {
                return RemoteNavigationHelper.TryFocusElement(SearchBox);
            }

            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = string.Empty;
                return true;
            }

            return false;
        }

        public void RefreshLocalizedContent()
        {
            ViewModel.RefreshLocalizedLabelsIfNeeded();
            XamlRuntimeLocalizer.Apply(this);
            QueueRestoreCategorySelection("language-changed");
        }
    }
}
