using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App.Views
{
    public sealed class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value == null ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public sealed class NullToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value == null ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public sealed class SeasonPrefixConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => $"Season {value}";
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public sealed class EpisodePrefixConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => $"Episode {value}";
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public sealed partial class SeriesPage : Page
    {
        private bool _isRestoringCategorySelection;
        private bool _isCategorySelectionRestoreQueued;

        public SeriesViewModel ViewModel { get; }

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
            _ = ViewModel.LoadSeriesCommand.ExecuteAsync(null);
        }

        private void OpenSources_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SourceListPage));
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

        private void EpisodeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is SeriesEpisodeItemViewModel item)
            {
                PlayEpisode(item);
            }
            ((ListView)sender).SelectedItem = null;
        }

        private void SeriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is SeriesBrowseSlotViewModel slot)
            {
                ViewModel.SelectSeriesFromSlot(slot);
            }

            ((GridView)sender).SelectedItem = null;
        }

        private void PlaySelectedEpisode_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedEpisode != null)
            {
                PlayEpisode(ViewModel.SelectedEpisode);
            }
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

        private void PlayEpisode(SeriesEpisodeItemViewModel item)
        {
            if (!string.IsNullOrWhiteSpace(item.StreamUrl))
            {
                this.Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                {
                    ContentId = item.Id,
                    ContentType = PlaybackContentType.Episode,
                    StreamUrl = item.StreamUrl,
                    StartPositionMs = item.ResumePositionMs
                });
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
    }
}
