using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App.Views
{
    public sealed partial class FavoritesPage : Page
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

        private void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is BrowserChannelViewModel channel)
            {
                if (!string.IsNullOrWhiteSpace(channel.StreamUrl))
                {
                    this.Frame.Navigate(typeof(EmbeddedPlaybackPage), new Kroira.App.Models.PlaybackLaunchContext
                    {
                        ContentId = channel.Id,
                        ContentType = Kroira.App.Models.PlaybackContentType.Channel,
                        StreamUrl = channel.StreamUrl,
                        StartPositionMs = 0
                    });
                }
            }
            ((ListView)sender).SelectedItem = null;
        }

        private void MovieList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is FavoriteMovieViewModel movie)
            {
                if (!string.IsNullOrWhiteSpace(movie.StreamUrl))
                {
                    this.Frame.Navigate(typeof(EmbeddedPlaybackPage), new Kroira.App.Models.PlaybackLaunchContext
                    {
                        ContentId = movie.Id,
                        ContentType = Kroira.App.Models.PlaybackContentType.Movie,
                        StreamUrl = movie.StreamUrl,
                        StartPositionMs = 0
                    });
                }
            }
            ((ListView)sender).SelectedItem = null;
        }

        private void SeriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is FavoriteSeriesViewModel)
            {
                this.Frame.Navigate(typeof(SeriesPage));
            }
            ((ListView)sender).SelectedItem = null;
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
    }
}
