using Kroira.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
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
                    this.Frame.Navigate(typeof(DevPlaybackPage), new Kroira.App.Models.PlaybackLaunchContext
                    {
                        ContentId = channel.Id,
                        ContentType = Kroira.App.Models.PlaybackContentType.Channel,
                        StreamUrl = channel.StreamUrl,
                        StartPositionMs = 0
                    });
                }
            }
            ((GridView)sender).SelectedItem = null;
        }

        private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                ViewModel.ToggleFavoriteCommand.Execute(id);
            }
        }
    }
}
