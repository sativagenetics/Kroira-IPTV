using System.Threading.Tasks;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace Kroira.App.Views
{
    public sealed partial class ChannelBrowserPage : Page, IRemoteNavigationPage
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

        private async void ChannelList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is BrowserChannelViewModel channel)
            {
                await OpenChannelAsync(channel);
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (this.Frame.CanGoBack)
            {
                this.Frame.GoBack();
            }
        }

        private void FavoriteToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                ViewModel.ToggleFavoriteCommand.Execute(id);
            }
        }

        private async void InspectChannel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: BrowserChannelViewModel channel })
            {
                return;
            }

            await ItemInspectorDialog.ShowAsync(
                XamlRoot,
                new Kroira.App.Models.PlaybackLaunchContext
                {
                    ContentId = channel.Id,
                    ContentType = Kroira.App.Models.PlaybackContentType.Channel,
                    LogicalContentKey = channel.LogicalContentKey,
                    PreferredSourceProfileId = channel.PreferredSourceProfileId,
                    CatalogStreamUrl = channel.StreamUrl,
                    StreamUrl = channel.StreamUrl,
                    LiveStreamUrl = channel.StreamUrl
                });
        }

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Down || e.Key == VirtualKey.Enter)
            {
                RemoteNavigationHelper.TryFocusListItem(ChannelList);
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Escape && !string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = string.Empty;
                e.Handled = true;
            }
        }

        private void ChannelList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                RemoteNavigationHelper.TryFocusElement(SearchBox);
                e.Handled = true;
                return;
            }

            if ((e.Key == VirtualKey.Enter || e.Key == VirtualKey.Space) &&
                !RemoteNavigationHelper.IsWithinInteractiveControl(e.OriginalSource) &&
                TryGetChannelFromSource(e.OriginalSource, out var channel))
            {
                _ = OpenChannelAsync(channel);
                e.Handled = true;
            }
        }

        private Task OpenChannelAsync(BrowserChannelViewModel channel)
        {
            if (string.IsNullOrWhiteSpace(channel.StreamUrl))
            {
                return Task.CompletedTask;
            }

            Frame.Navigate(typeof(EmbeddedPlaybackPage), CreateChannelLaunchContext(channel));
            return Task.CompletedTask;
        }

        private static Kroira.App.Models.PlaybackLaunchContext CreateChannelLaunchContext(BrowserChannelViewModel channel)
        {
            return new Kroira.App.Models.PlaybackLaunchContext
            {
                ContentId = channel.Id,
                ContentType = Kroira.App.Models.PlaybackContentType.Channel,
                LogicalContentKey = channel.LogicalContentKey,
                PreferredSourceProfileId = channel.PreferredSourceProfileId,
                CatalogStreamUrl = channel.StreamUrl,
                StreamUrl = channel.StreamUrl,
                LiveStreamUrl = channel.StreamUrl,
                StartPositionMs = 0
            };
        }

        private static bool TryGetChannelFromSource(object source, out BrowserChannelViewModel channel)
        {
            var element = source as DependencyObject;
            while (element != null)
            {
                if (element is FrameworkElement { DataContext: BrowserChannelViewModel item })
                {
                    channel = item;
                    return true;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            channel = null!;
            return false;
        }

        public bool TryFocusPrimaryTarget()
        {
            return RemoteNavigationHelper.TryFocusElement(SearchBox) ||
                   RemoteNavigationHelper.TryFocusListItem(ChannelList);
        }

        public bool TryHandleBackRequest()
        {
            if (!string.IsNullOrWhiteSpace(SearchBox.Text) &&
                RemoteNavigationHelper.IsDescendantOf(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject, SearchBox))
            {
                SearchBox.Text = string.Empty;
                return true;
            }

            var focusedElement = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (!RemoteNavigationHelper.IsDescendantOf(focusedElement, SearchBox))
            {
                return RemoteNavigationHelper.TryFocusElement(SearchBox);
            }

            return false;
        }
    }
}
