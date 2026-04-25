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
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Kroira.App.Views
{
    public sealed class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value == null ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
    }

    public sealed class NullToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value == null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
    }

    public sealed class SeasonPrefixConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => $"Season {value}";
        public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
    }

    public sealed class EpisodePrefixConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => $"Episode {value}";
        public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
    }

    public sealed partial class SeriesPage : Page, IRemoteNavigationPage
    {
        private const string MediaActionOverlayTag = "MediaActionOverlay";
        private bool _isRestoringCategorySelection;
        private bool _isCategorySelectionRestoreQueued;

        public SeriesViewModel ViewModel { get; }

        public Visibility DownloadActionsVisibility => StoreReleaseFeatures.ShowDownloadActions ? Visibility.Visible : Visibility.Collapsed;

        public SeriesPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<SeriesViewModel>();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.Categories.CollectionChanged += Categories_CollectionChanged;
            Loaded += SeriesPage_Loaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var navigationStopwatch = Stopwatch.StartNew();
            if (DispatcherQueue != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                    BrowseRuntimeLogger.Log(
                        "SERIES UI",
                        $"page visible ms={navigationStopwatch.ElapsedMilliseconds} cached={ViewModel.HasLoadedOnce} slots={ViewModel.DisplaySeriesSlots.Count}"));
            }

            var shouldLoad = !ViewModel.HasLoadedOnce ||
                             (ViewModel.DisplaySeriesSlots.Count == 0 && ViewModel.SurfaceState.State != SurfaceViewState.Ready);
            if (shouldLoad)
            {
                BrowseRuntimeLogger.Log("SERIES UI", "navigation triggered catalog load");
                _ = ViewModel.LoadSeriesCommand.ExecuteAsync(null);
                return;
            }

            BrowseRuntimeLogger.Log(
                "SERIES UI",
                $"navigation reused cached surface slots={ViewModel.DisplaySeriesSlots.Count} selected={ViewModel.SelectedSeries?.Title ?? "<none>"}");
        }

        private void OpenSources_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SourceListPage));
        }

        private void RetrySurface_Click(object sender, RoutedEventArgs e)
        {
            _ = ViewModel.LoadSeriesCommand.ExecuteAsync(null);
        }

        private void SeriesPage_Loaded(object sender, RoutedEventArgs e)
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

            BrowseRuntimeLogger.Log("SERIES UI", $"selection restore queued reason={reason} enqueued={enqueued}");
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
                BrowseRuntimeLogger.Log("SERIES UI", $"selection restored reason={reason} key={resolved.FilterKey}");
            }
            finally
            {
                _isRestoringCategorySelection = false;
            }
        }

        private void EpisodeList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SeriesEpisodeItemViewModel item)
            {
                PlayEpisode(item);
            }
        }

        private void SeriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is SeriesBrowseSlotViewModel slot)
            {
                ViewModel.SelectSeriesFromSlot(slot);
            }

            ((GridView)sender).SelectedItem = null;
        }

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Down || e.Key == VirtualKey.Enter)
            {
                RemoteNavigationHelper.TryFocusListItem(SeriesGrid);
                e.Handled = true;
            }
        }

        private void SeriesGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                RemoteNavigationHelper.TryFocusElement(SearchBox);
                e.Handled = true;
                return;
            }

            if ((e.Key == VirtualKey.Enter || e.Key == VirtualKey.Space) &&
                !RemoteNavigationHelper.IsWithinInteractiveControl(e.OriginalSource) &&
                RemoteNavigationHelper.FindDataContextInAncestors<SeriesBrowseSlotViewModel>(e.OriginalSource) is { HasSeries: true } slot)
            {
                ViewModel.SelectSeriesFromSlot(slot);
                if (!RemoteNavigationHelper.TryFocusElement(PlaySelectedEpisodeButton))
                {
                    RemoteNavigationHelper.TryFocusListItem(EpisodeList);
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

            var gridItem = FindAncestor<GridViewItem>(root);
            if (gridItem != null)
            {
                gridItem.PointerEntered -= RevealCard_PointerEntered;
                gridItem.PointerEntered += RevealCard_PointerEntered;
                gridItem.PointerExited -= RevealCard_PointerExited;
                gridItem.PointerExited += RevealCard_PointerExited;
                gridItem.GotFocus -= RevealCard_GotFocus;
                gridItem.GotFocus += RevealCard_GotFocus;
                gridItem.LostFocus -= RevealCard_LostFocus;
                gridItem.LostFocus += RevealCard_LostFocus;
                return;
            }

            var listItem = FindAncestor<ListViewItem>(root);
            if (listItem == null)
            {
                return;
            }

            listItem.PointerEntered -= RevealCard_PointerEntered;
            listItem.PointerEntered += RevealCard_PointerEntered;
            listItem.PointerExited -= RevealCard_PointerExited;
            listItem.PointerExited += RevealCard_PointerExited;
            listItem.GotFocus -= RevealCard_GotFocus;
            listItem.GotFocus += RevealCard_GotFocus;
            listItem.LostFocus -= RevealCard_LostFocus;
            listItem.LostFocus += RevealCard_LostFocus;
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

        private void PlaySelectedEpisode_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedEpisode != null)
            {
                PlayEpisode(ViewModel.SelectedEpisode);
            }
        }

        private async void InspectSelectedEpisode_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedEpisode == null)
            {
                return;
            }

            await ItemInspectorDialog.ShowAsync(XamlRoot, CreateEpisodeLaunchContext(ViewModel.SelectedEpisode));
        }

        private async void DownloadSelectedEpisode_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedEpisode != null)
            {
                await QueueEpisodeDownloadAsync(ViewModel.SelectedEpisode);
            }
        }

        private async void FavoriteToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: int id })
            {
                await ViewModel.ToggleFavoriteCommand.ExecuteAsync(id);
            }
        }

        private async void MarkEpisodeWatched_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: SeriesEpisodeItemViewModel item })
            {
                await ViewModel.MarkEpisodeWatchedCommand.ExecuteAsync(item);
            }
        }

        private async void MarkEpisodeUnwatched_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: SeriesEpisodeItemViewModel item })
            {
                await ViewModel.MarkEpisodeUnwatchedCommand.ExecuteAsync(item);
            }
        }

        private async void DownloadEpisode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: SeriesEpisodeItemViewModel item })
            {
                await QueueEpisodeDownloadAsync(item);
            }
        }

        private async void InspectEpisode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: SeriesEpisodeItemViewModel item })
            {
                return;
            }

            await ItemInspectorDialog.ShowAsync(XamlRoot, CreateEpisodeLaunchContext(item));
        }

        private void EpisodeList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                RemoteNavigationHelper.TryFocusElement(PlaySelectedEpisodeButton);
                e.Handled = true;
                return;
            }

            if ((e.Key == VirtualKey.Enter || e.Key == VirtualKey.Space) &&
                !RemoteNavigationHelper.IsWithinInteractiveControl(e.OriginalSource) &&
                RemoteNavigationHelper.FindDataContextInAncestors<SeriesEpisodeItemViewModel>(e.OriginalSource) is { } item)
            {
                PlayEpisode(item);
                e.Handled = true;
            }
        }

        private void PlayEpisode(SeriesEpisodeItemViewModel item)
        {
            if (!string.IsNullOrWhiteSpace(item.StreamUrl))
            {
                this.Frame.Navigate(typeof(EmbeddedPlaybackPage), CreateEpisodeLaunchContext(item));
            }
        }

        private async Task QueueEpisodeDownloadAsync(SeriesEpisodeItemViewModel item)
        {
            if (string.IsNullOrWhiteSpace(item.StreamUrl))
            {
                return;
            }

            try
            {
                var mediaJobService = ((App)Application.Current).Services.GetRequiredService<IMediaJobService>();
                await mediaJobService.QueueDownloadAsync(
                    PlaybackContentType.Episode,
                    item.Id,
                    item.Title,
                    $"Season {item.SeasonNumber} Episode {item.EpisodeNumber}",
                    item.StreamUrl);
            }
            catch (Exception ex)
            {
                var dialog = new ContentDialog
                {
                    Title = "Download failed",
                    CloseButtonText = "Close",
                    XamlRoot = this.XamlRoot,
                    Content = ex.Message
                };
                await dialog.ShowAsync();
            }
        }

        private static PlaybackLaunchContext CreateEpisodeLaunchContext(SeriesEpisodeItemViewModel item)
        {
            return new PlaybackLaunchContext
            {
                ContentId = item.Id,
                ContentType = PlaybackContentType.Episode,
                CatalogStreamUrl = item.StreamUrl,
                StreamUrl = item.StreamUrl,
                StartPositionMs = item.ResumePositionMs
            };
        }

        public bool TryFocusPrimaryTarget()
        {
            return RemoteNavigationHelper.TryFocusElement(SearchBox) ||
                   RemoteNavigationHelper.TryFocusListItem(SeriesGrid) ||
                   RemoteNavigationHelper.TryFocusElement(PlaySelectedEpisodeButton) ||
                   RemoteNavigationHelper.TryFocusListItem(EpisodeList);
        }

        public bool TryHandleBackRequest()
        {
            var focusedElement = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (RemoteNavigationHelper.IsDescendantOf(focusedElement, EpisodeList) ||
                RemoteNavigationHelper.IsDescendantOf(focusedElement, PlaySelectedEpisodeButton))
            {
                return RemoteNavigationHelper.TryFocusListItem(SeriesGrid);
            }

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
    }
}
