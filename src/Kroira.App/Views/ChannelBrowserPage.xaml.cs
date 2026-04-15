using Kroira.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App.Views
{
    public sealed partial class ChannelBrowserPage : Page
    {
        public ChannelBrowserViewModel ViewModel { get; }

        public ChannelBrowserPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<ChannelBrowserViewModel>();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is int sourceId)
            {
                await ViewModel.LoadSourceAsync(sourceId);
            }
        }

        private void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is BrowserChannelViewModel channel)
            {
                if (!string.IsNullOrWhiteSpace(channel.StreamUrl))
                {
                    this.Frame.Navigate(typeof(DevPlaybackPage), channel.StreamUrl);
                }
            }
            
            ((GridView)sender).SelectedItem = null;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (this.Frame.CanGoBack)
            {
                this.Frame.GoBack();
            }
        }
    }
}
