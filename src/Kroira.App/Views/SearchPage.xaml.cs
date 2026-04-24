#nullable enable
using System;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.System;

namespace Kroira.App.Views
{
    public sealed partial class SearchPage : Page, IRemoteNavigationPage
    {
        public SearchViewModel ViewModel { get; }

        public SearchPage()
        {
            InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<SearchViewModel>();
            Loaded += SearchPage_Loaded;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.Dispose();
        }

        private void SearchPage_Loaded(object sender, RoutedEventArgs e)
        {
            SearchBox.Focus(FocusState.Keyboard);
        }

        private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                _ = ViewModel.SearchNowCommand.ExecuteAsync(null);
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Down)
            {
                FocusResults();
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Escape && !string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = string.Empty;
                e.Handled = true;
            }
        }

        private async void ResultAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: SearchResultItemViewModel item })
            {
                return;
            }

            switch (item.Type)
            {
                case MediaSearchResultType.Live:
                case MediaSearchResultType.Episode:
                    await PlayResultAsync(item);
                    break;
                case MediaSearchResultType.Movie:
                    await ShowMovieDetailAsync(item);
                    break;
                case MediaSearchResultType.Series:
                    await ShowSeriesDetailAsync(item);
                    break;
            }
        }

        private async Task ShowMovieDetailAsync(SearchResultItemViewModel item)
        {
            var dialog = new ContentDialog
            {
                Title = item.Title,
                PrimaryButtonText = item.ResumePositionMs > 0 ? "Resume" : "Play",
                SecondaryButtonText = "Open Movies",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(item.StreamUrl),
                XamlRoot = XamlRoot,
                Content = BuildDetailContent(item)
            };

            var result = await ShowContentDialogAsync(dialog);
            if (result == ContentDialogResult.Primary)
            {
                await PlayResultAsync(item);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                Frame.Navigate(typeof(MoviesPage));
            }
        }

        private async Task ShowSeriesDetailAsync(SearchResultItemViewModel item)
        {
            var dialog = new ContentDialog
            {
                Title = item.Title,
                PrimaryButtonText = "Open Series",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                Content = BuildDetailContent(item)
            };

            if (await ShowContentDialogAsync(dialog) == ContentDialogResult.Primary)
            {
                Frame.Navigate(typeof(SeriesPage));
            }
        }

        private static FrameworkElement BuildDetailContent(SearchResultItemViewModel item)
        {
            var panel = new StackPanel
            {
                Spacing = 10,
                MaxWidth = 520
            };

            panel.Children.Add(new TextBlock
            {
                Text = item.Subtitle,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KroiraTextSecondaryBrush"]
            });

            panel.Children.Add(new TextBlock
            {
                Text = item.BadgeLine,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["KroiraAccentBrush"],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            if (!string.IsNullOrWhiteSpace(item.Overview))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = item.Overview,
                    TextWrapping = TextWrapping.Wrap,
                    MaxLines = 8
                });
            }

            return panel;
        }

        private async Task PlayResultAsync(SearchResultItemViewModel item)
        {
            if (string.IsNullOrWhiteSpace(item.StreamUrl))
            {
                return;
            }

            if (item.Type == MediaSearchResultType.Live)
            {
                await RecordLiveLaunchAsync(item.ContentId);
            }

            var contentType = item.PlaybackContentType ?? ResolvePlaybackContentType(item.Type);
            Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
            {
                ProfileId = ViewModel.ActiveProfileId,
                ContentId = item.ContentId,
                ContentType = contentType,
                LogicalContentKey = item.LogicalContentKey,
                PreferredSourceProfileId = item.SourceProfileId,
                CatalogStreamUrl = item.StreamUrl,
                StreamUrl = item.StreamUrl,
                LiveStreamUrl = item.Type == MediaSearchResultType.Live ? item.StreamUrl : string.Empty,
                StartPositionMs = item.ResumePositionMs
            });
        }

        private async Task RecordLiveLaunchAsync(int channelId)
        {
            if (channelId <= 0)
            {
                return;
            }

            try
            {
                using var scope = ((App)Application.Current).Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var logicalState = scope.ServiceProvider.GetRequiredService<ILogicalCatalogStateService>();
                await logicalState.RecordLiveChannelLaunchAsync(db, ViewModel.ActiveProfileId, channelId);
            }
            catch
            {
            }
        }

        private static PlaybackContentType ResolvePlaybackContentType(MediaSearchResultType type)
        {
            return type switch
            {
                MediaSearchResultType.Live => PlaybackContentType.Channel,
                MediaSearchResultType.Movie => PlaybackContentType.Movie,
                MediaSearchResultType.Episode => PlaybackContentType.Episode,
                _ => PlaybackContentType.Movie
            };
        }

        private void FocusResults()
        {
            if (GroupList.Items.Count == 0)
            {
                return;
            }

            GroupList.UpdateLayout();
            if (GroupList.ContainerFromIndex(0) is Control firstGroup)
            {
                firstGroup.Focus(FocusState.Keyboard);
                return;
            }

            GroupList.Focus(FocusState.Keyboard);
        }

        private static Task<ContentDialogResult> ShowContentDialogAsync(ContentDialog dialog)
        {
            var completion = new TaskCompletionSource<ContentDialogResult>();
            var operation = dialog.ShowAsync();
            operation.Completed = (info, status) =>
            {
                switch (status)
                {
                    case AsyncStatus.Completed:
                        completion.TrySetResult(info.GetResults());
                        break;
                    case AsyncStatus.Canceled:
                        completion.TrySetCanceled();
                        break;
                    case AsyncStatus.Error:
                        completion.TrySetException(info.ErrorCode);
                        break;
                }
            };

            return completion.Task;
        }

        public bool TryFocusPrimaryTarget()
        {
            return RemoteNavigationHelper.TryFocusElement(SearchBox) ||
                   RemoteNavigationHelper.TryFocusListItem(GroupList);
        }

        public bool TryHandleBackRequest()
        {
            var focusedElement = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (!RemoteNavigationHelper.IsDescendantOf(focusedElement, SearchBox))
            {
                return RemoteNavigationHelper.TryFocusElement(SearchBox);
            }

            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = string.Empty;
                return true;
            }

            return false;
        }
    }
}
