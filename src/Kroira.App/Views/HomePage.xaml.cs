using Kroira.App.Models;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App.Views
{
    public sealed partial class HomePage : Page
    {
        public HomeViewModel ViewModel { get; }

        public HomePage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<HomeViewModel>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = ViewModel.LoadCommand.ExecuteAsync(null);
        }

        private void AddSource_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SourceOnboardingPage));
        }

        private void ContinueWatching_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ContinueWatchingPage));
        }

        private void QuickAction_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not HomeActionItem item)
            {
                return;
            }

            NavigateToTarget(item.Target);
        }

        private void ContinueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is HomeContinueItem item)
            {
                Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                {
                    ContentId = item.ContentId,
                    ContentType = item.ContentType,
                    StreamUrl = item.StreamUrl,
                    StartPositionMs = item.SavedPositionMs
                });
            }

            if (sender is ListView listView)
            {
                listView.SelectedItem = null;
            }
        }

        private void NavigateToTarget(string target)
        {
            switch (target)
            {
                case "Channels":
                    Frame.Navigate(typeof(ChannelsPage));
                    break;
                case "Movies":
                    Frame.Navigate(typeof(MoviesPage));
                    break;
                case "Series":
                    Frame.Navigate(typeof(SeriesPage));
                    break;
                case "Favorites":
                    Frame.Navigate(typeof(FavoritesPage));
                    break;
                case "Sources":
                    Frame.Navigate(typeof(SourceListPage));
                    break;
                case "Settings":
                    Frame.Navigate(typeof(SettingsPage));
                    break;
            }
        }
    }
}
