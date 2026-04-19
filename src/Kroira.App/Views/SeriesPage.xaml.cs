using System;
using Kroira.App.Models;
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
        public SeriesViewModel ViewModel { get; }

        public SeriesPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<SeriesViewModel>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = ViewModel.LoadSeriesCommand.ExecuteAsync(null);
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
    }
}
