using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Kroira.App.Views
{
    public sealed partial class FavoritesPage : Page, IRemoteNavigationPage
    {
        public FavoritesViewModel ViewModel { get; }

        public FavoritesPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<FavoritesViewModel>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.LoadFavoritesCommand.Execute(null);
        }

        private void OpenSources_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SourceListPage));
        }

        private void ChannelList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is BrowserChannelViewModel channel)
            {
                PlayChannel(channel);
            }
        }

        private void ChannelList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (HandleLaneBackKey(e))
            {
                return;
            }

            if ((e.Key == VirtualKey.Enter || e.Key == VirtualKey.Space) &&
                !RemoteNavigationHelper.IsWithinInteractiveControl(e.OriginalSource) &&
                RemoteNavigationHelper.FindDataContextInAncestors<BrowserChannelViewModel>(e.OriginalSource) is { } channel)
            {
                PlayChannel(channel);
                e.Handled = true;
            }
        }

        private void MovieList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FavoriteMovieViewModel movie)
            {
                PlayMovie(movie);
            }
        }

        private void MovieList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (HandleLaneBackKey(e))
            {
                return;
            }

            if ((e.Key == VirtualKey.Enter || e.Key == VirtualKey.Space) &&
                !RemoteNavigationHelper.IsWithinInteractiveControl(e.OriginalSource) &&
                RemoteNavigationHelper.FindDataContextInAncestors<FavoriteMovieViewModel>(e.OriginalSource) is { } movie)
            {
                PlayMovie(movie);
                e.Handled = true;
            }
        }

        private async void SeriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is FavoriteSeriesViewModel series)
            {
                await ViewModel.SelectSeriesAsync(series.Id);
            }
            ((ListView)sender).SelectedItem = null;
        }

        private async void SeriesList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                RemoteNavigationHelper.TryFocusListItem(ChannelList);
                e.Handled = true;
                return;
            }

            if ((e.Key == VirtualKey.Enter || e.Key == VirtualKey.Space) &&
                !RemoteNavigationHelper.IsWithinInteractiveControl(e.OriginalSource) &&
                RemoteNavigationHelper.FindDataContextInAncestors<FavoriteSeriesViewModel>(e.OriginalSource) is { } series)
            {
                await ViewModel.SelectSeriesAsync(series.Id);
                e.Handled = true;
            }
        }

        private void EpisodeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is Kroira.App.Models.Episode episode)
            {
                ViewModel.SelectEpisode(episode);
            }
            ((ListView)sender).SelectedItem = null;
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
                RemoteNavigationHelper.FindDataContextInAncestors<Kroira.App.Models.Episode>(e.OriginalSource) is { } episode)
            {
                ViewModel.SelectEpisode(episode);
                PlaySelectedEpisode_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void PlaySelectedEpisode_Click(object sender, RoutedEventArgs e)
        {
            var episode = ViewModel.SelectedEpisode;
            if (episode != null && !string.IsNullOrWhiteSpace(episode.StreamUrl))
            {
                this.Frame.Navigate(typeof(EmbeddedPlaybackPage), new Kroira.App.Models.PlaybackLaunchContext
                {
                    ContentId = episode.Id,
                    ContentType = Kroira.App.Models.PlaybackContentType.Episode,
                    StreamUrl = episode.StreamUrl,
                    StartPositionMs = 0
                });
            }
        }

        private void OpenSeriesPage_Click(object sender, RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(SeriesPage));
        }

        private void CloseSeriesDetail_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ClearSelectedSeries();
        }

        private void RemoveChannelFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                ViewModel.ToggleFavoriteCommand.Execute(id);
            }
        }

        private void RemoveMovieFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                ViewModel.RemoveMovieFavoriteCommand.Execute(id);
            }
        }

        private void RemoveSeriesFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                ViewModel.RemoveSeriesFavoriteCommand.Execute(id);
            }
        }

        private void PlayChannel(BrowserChannelViewModel channel)
        {
            if (string.IsNullOrWhiteSpace(channel.StreamUrl))
            {
                return;
            }

            Frame.Navigate(typeof(EmbeddedPlaybackPage), new Kroira.App.Models.PlaybackLaunchContext
            {
                ContentId = channel.Id,
                ContentType = Kroira.App.Models.PlaybackContentType.Channel,
                StreamUrl = channel.StreamUrl,
                StartPositionMs = 0
            });
        }

        private void PlayMovie(FavoriteMovieViewModel movie)
        {
            if (string.IsNullOrWhiteSpace(movie.StreamUrl))
            {
                return;
            }

            Frame.Navigate(typeof(EmbeddedPlaybackPage), new Kroira.App.Models.PlaybackLaunchContext
            {
                ContentId = movie.Id,
                ContentType = Kroira.App.Models.PlaybackContentType.Movie,
                StreamUrl = movie.StreamUrl,
                StartPositionMs = 0
            });
        }

        private bool HandleLaneBackKey(KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Escape)
            {
                return false;
            }

            if (ViewModel.SelectedSeries != null)
            {
                ViewModel.ClearSelectedSeries();
                RemoteNavigationHelper.TryFocusListItem(SeriesList);
            }
            else
            {
                RemoteNavigationHelper.TryFocusListItem(ChannelList);
            }

            e.Handled = true;
            return true;
        }

        public bool TryFocusPrimaryTarget()
        {
            return RemoteNavigationHelper.TryFocusListItem(ChannelList) ||
                   RemoteNavigationHelper.TryFocusListItem(MovieList) ||
                   RemoteNavigationHelper.TryFocusListItem(SeriesList) ||
                   RemoteNavigationHelper.TryFocusElement(CloseSeriesDetailButton) ||
                   RemoteNavigationHelper.TryFocusElement(PlaySelectedEpisodeButton);
        }

        public bool TryHandleBackRequest()
        {
            if (ViewModel.SelectedSeries != null)
            {
                ViewModel.ClearSelectedSeries();
                return RemoteNavigationHelper.TryFocusListItem(SeriesList);
            }

            var focusedElement = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (!RemoteNavigationHelper.IsDescendantOf(focusedElement, ChannelList))
            {
                return RemoteNavigationHelper.TryFocusListItem(ChannelList);
            }

            return false;
        }
    }
}
