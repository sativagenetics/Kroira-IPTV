using Kroira.App.ViewModels;
using Kroira.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App.Views
{
    public sealed partial class MoviesPage : Page
    {
        public MoviesViewModel ViewModel { get; }

        public MoviesPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<MoviesViewModel>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = ViewModel.LoadMoviesCommand.ExecuteAsync(null);
        }

        private void GridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is Movie movie)
            {
                if (!string.IsNullOrWhiteSpace(movie.StreamUrl))
                {
                    this.Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                    {
                        ContentId = movie.Id,
                        ContentType = PlaybackContentType.Movie,
                        StreamUrl = movie.StreamUrl,
                        StartPositionMs = 0
                    });
                }
            }
            ((GridView)sender).SelectedItem = null;
        }
    }
}
