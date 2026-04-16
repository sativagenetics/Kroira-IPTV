using Kroira.App.Models;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App.Views
{
    public sealed partial class ContinueWatchingPage : Page
    {
        public ContinueWatchingViewModel ViewModel { get; }

        public ContinueWatchingPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<ContinueWatchingViewModel>();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadProgressCommand.ExecuteAsync(null);
        }

        private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is ProgressItemViewModel item)
            {
                if (!string.IsNullOrWhiteSpace(item.StreamUrl))
                {
                    this.Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                    {
                        ContentId = item.ContentId,
                        ContentType = item.ContentType,
                        StreamUrl = item.StreamUrl,
                        StartPositionMs = item.SavedPositionMs
                    });
                }
            }
            ((GridView)sender).SelectedItem = null;
        }

        private void RemoveProgress_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                _ = ViewModel.RemoveProgressCommand.ExecuteAsync(id);
            }
        }
    }
}
