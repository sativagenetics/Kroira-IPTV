using System;
using System.Threading.Tasks;
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

        private void RetrySurface_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.LoadFavoritesCommand.Execute(null);
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
                    ProfileId = ViewModel.ActiveProfileId,
                    ContentId = episode.Id,
                    ContentType = Kroira.App.Models.PlaybackContentType.Episode,
                    PreferredSourceProfileId = ViewModel.SelectedSeries?.PreferredSourceProfileId ?? 0,
                    CatalogStreamUrl = episode.StreamUrl,
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

        private async void ClearChannelFavorites_Click(object sender, RoutedEventArgs e)
        {
            await ConfirmAndRunAsync(
                LocalizedStrings.Get("Favorites.ClearChannelsTitle"),
                LocalizedStrings.Get("Favorites.ClearChannelsMessage"),
                () => ViewModel.ClearChannelFavoritesCommand.ExecuteAsync(null));
        }

        private async void ClearMovieFavorites_Click(object sender, RoutedEventArgs e)
        {
            await ConfirmAndRunAsync(
                LocalizedStrings.Get("Favorites.ClearMoviesTitle"),
                LocalizedStrings.Get("Favorites.ClearMoviesMessage"),
                () => ViewModel.ClearMovieFavoritesCommand.ExecuteAsync(null));
        }

        private async void ClearSeriesFavorites_Click(object sender, RoutedEventArgs e)
        {
            await ConfirmAndRunAsync(
                LocalizedStrings.Get("Favorites.ClearSeriesTitle"),
                LocalizedStrings.Get("Favorites.ClearSeriesMessage"),
                () => ViewModel.ClearSeriesFavoritesCommand.ExecuteAsync(null));
        }

        private async Task ConfirmAndRunAsync(string title, string message, Func<Task> action)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = LocalizedStrings.Get("General.ClearAll"),
                CloseButtonText = LocalizedStrings.Get("General.Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await action();
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
                ProfileId = ViewModel.ActiveProfileId,
                ContentId = channel.Id,
                ContentType = Kroira.App.Models.PlaybackContentType.Channel,
                LogicalContentKey = channel.LogicalContentKey,
                PreferredSourceProfileId = channel.PreferredSourceProfileId,
                CatalogStreamUrl = channel.StreamUrl,
                StreamUrl = channel.StreamUrl,
                LiveStreamUrl = channel.StreamUrl,
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
                ProfileId = ViewModel.ActiveProfileId,
                ContentId = movie.Id,
                ContentType = Kroira.App.Models.PlaybackContentType.Movie,
                LogicalContentKey = movie.LogicalContentKey,
                PreferredSourceProfileId = movie.PreferredSourceProfileId,
                CatalogStreamUrl = movie.StreamUrl,
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
