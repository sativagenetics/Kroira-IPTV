using Kroira.App.Models;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App.Views
{
    public sealed partial class ChannelsPage : Page
    {
        public ChannelsPageViewModel ViewModel { get; }

        public ChannelsPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<ChannelsPageViewModel>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = ViewModel.LoadChannelsCommand.ExecuteAsync(null);
        }

        private void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is BrowserChannelViewModel channel)
            {
                if (!string.IsNullOrWhiteSpace(channel.StreamUrl))
                {
                    this.Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                    {
                        ContentId = channel.Id,
                        ContentType = PlaybackContentType.Channel,
                        StreamUrl = channel.StreamUrl,
                        StartPositionMs = 0
                    });
                }
            }
            ((ListView)sender).SelectedItem = null;
        }

        private void FavoriteToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                _ = ViewModel.ToggleFavoriteCommand.ExecuteAsync(id);
            }
        }
    }
}
