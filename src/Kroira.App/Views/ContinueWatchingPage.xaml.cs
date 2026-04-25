using System;
using System.Threading.Tasks;
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

        private void RetrySurface_Click(object sender, RoutedEventArgs e)
        {
            _ = ViewModel.LoadProgressCommand.ExecuteAsync(null);
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

        private async void ClearLiveProgress_Click(object sender, RoutedEventArgs e)
        {
            await ConfirmAndRunAsync(
                LocalizedStrings.Get("ContinueWatching_ClearLiveTitle"),
                LocalizedStrings.Get("ContinueWatching_ClearLiveMessage"),
                () => ViewModel.ClearLiveProgressCommand.ExecuteAsync(null));
        }

        private async void ClearMovieProgress_Click(object sender, RoutedEventArgs e)
        {
            await ConfirmAndRunAsync(
                LocalizedStrings.Get("ContinueWatching_ClearMoviesTitle"),
                LocalizedStrings.Get("ContinueWatching_ClearMoviesMessage"),
                () => ViewModel.ClearMovieProgressCommand.ExecuteAsync(null));
        }

        private async void ClearSeriesProgress_Click(object sender, RoutedEventArgs e)
        {
            await ConfirmAndRunAsync(
                LocalizedStrings.Get("ContinueWatching_ClearSeriesTitle"),
                LocalizedStrings.Get("ContinueWatching_ClearSeriesMessage"),
                () => ViewModel.ClearSeriesProgressCommand.ExecuteAsync(null));
        }

        private async Task ConfirmAndRunAsync(string title, string message, Func<Task> action)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = LocalizedStrings.Get("General_ClearAll"),
                CloseButtonText = LocalizedStrings.Get("General_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await action();
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
                ProfileId = ViewModel.ActiveProfileId,
                ContentId = item.ContentId,
                ContentType = item.ContentType,
                LogicalContentKey = item.LogicalContentKey,
                PreferredSourceProfileId = item.PreferredSourceProfileId,
                CatalogStreamUrl = item.StreamUrl,
                StreamUrl = item.StreamUrl,
                LiveStreamUrl = item.ContentType == PlaybackContentType.Channel ? item.StreamUrl : string.Empty,
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
