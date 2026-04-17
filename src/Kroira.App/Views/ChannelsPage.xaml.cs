using Kroira.App.Models;
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

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Down || e.Key == VirtualKey.Enter)
            {
                FocusFirstChannel();
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Escape && !string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = string.Empty;
                e.Handled = true;
            }
        }

        private void ChannelList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                SearchBox.Focus(FocusState.Keyboard);
                e.Handled = true;
                return;
            }

            ActivateChannelFromKey(e);
        }

        private void ChannelList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is BrowserChannelViewModel channel)
            {
                LaunchChannel(channel);
            }
        }

        private async void FavoriteToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                btn.IsEnabled = false;
                try
                {
                    await ViewModel.ToggleFavoriteCommand.ExecuteAsync(id);
                }
                finally
                {
                    btn.IsEnabled = true;
                    btn.Focus(FocusState.Keyboard);
                }
            }
        }

        private void LaunchChannel(BrowserChannelViewModel channel)
        {
            if (string.IsNullOrWhiteSpace(channel.StreamUrl))
            {
                return;
            }

            Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
            {
                ContentId = channel.Id,
                ContentType = PlaybackContentType.Channel,
                StreamUrl = channel.StreamUrl,
                StartPositionMs = 0
            });
        }

        private void ActivateChannelFromKey(KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter && e.Key != VirtualKey.Space)
            {
                return;
            }

            if (IsWithinButton(e.OriginalSource))
            {
                return;
            }

            if (TryGetChannelFromSource(e.OriginalSource, out var channel))
            {
                LaunchChannel(channel);
                e.Handled = true;
            }
        }

        private void FocusFirstChannel()
        {
            if (ViewModel.FilteredChannels.Count == 0)
            {
                return;
            }

            ChannelList.UpdateLayout();
            if (ChannelList.ContainerFromIndex(0) is Control firstItem)
            {
                firstItem.Focus(FocusState.Keyboard);
            }
            else
            {
                ChannelList.Focus(FocusState.Keyboard);
            }
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

            channel = null;
            return false;
        }

        private static bool IsWithinButton(object source)
        {
            var element = source as DependencyObject;
            while (element != null)
            {
                if (element is Button)
                {
                    return true;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            return false;
        }
    }
}
