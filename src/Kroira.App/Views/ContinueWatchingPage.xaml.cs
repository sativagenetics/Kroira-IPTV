using Kroira.App.Models;
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
    public sealed partial class ContinueWatchingPage : Page, IRemoteNavigationPage
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

        private void OpenSources_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SourceListPage));
        }

        private void ItemList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ProgressItemViewModel item)
            {
                OpenProgressItem(item);
            }
        }

        private void ItemList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                RemoteNavigationHelper.TryFocusElement(HideWatchedToggle);
                e.Handled = true;
                return;
            }

            if ((e.Key == VirtualKey.Enter || e.Key == VirtualKey.Space) &&
                !RemoteNavigationHelper.IsWithinInteractiveControl(e.OriginalSource) &&
                RemoteNavigationHelper.FindDataContextInAncestors<ProgressItemViewModel>(e.OriginalSource) is { } item)
            {
                OpenProgressItem(item);
                e.Handled = true;
            }
        }

        private void RemoveProgress_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                _ = ViewModel.RemoveProgressCommand.ExecuteAsync(id);
            }
        }

        private void MarkWatched_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ProgressItemViewModel item })
            {
                _ = ViewModel.MarkWatchedCommand.ExecuteAsync(item);
            }
        }

        private void MarkUnwatched_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ProgressItemViewModel item })
            {
                _ = ViewModel.MarkUnwatchedCommand.ExecuteAsync(item);
            }
        }

        private void OpenProgressItem(ProgressItemViewModel item)
        {
            if (string.IsNullOrWhiteSpace(item.StreamUrl))
            {
                return;
            }

            Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
            {
                ContentId = item.ContentId,
                ContentType = item.ContentType,
                StreamUrl = item.StreamUrl,
                StartPositionMs = item.SavedPositionMs
            });
        }

        public bool TryFocusPrimaryTarget()
        {
            return RemoteNavigationHelper.TryFocusListItem(LiveList) ||
                   RemoteNavigationHelper.TryFocusListItem(MovieList) ||
                   RemoteNavigationHelper.TryFocusListItem(SeriesList) ||
                   RemoteNavigationHelper.TryFocusElement(HideWatchedToggle);
        }

        public bool TryHandleBackRequest()
        {
            var focusedElement = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (!RemoteNavigationHelper.IsDescendantOf(focusedElement, HideWatchedToggle))
            {
                return RemoteNavigationHelper.TryFocusElement(HideWatchedToggle);
            }

            return false;
        }
    }
}
