using System;
using System.Threading.Tasks;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.System;

namespace Kroira.App.Views
{
    public sealed partial class ChannelsPage : Page
    {
        private static string LogPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kroira",
            "startup-log.txt");

        private static void Log(string message)
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogPath)!);
                System.IO.File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] CHANNELS {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        private sealed class RecordingDurationOption
        {
            public RecordingDurationOption(string label, TimeSpan duration)
            {
                Label = label;
                Duration = duration;
            }

            public string Label { get; }
            public TimeSpan Duration { get; }
        }

        public ChannelsPageViewModel ViewModel { get; }

        public ChannelsPage()
        {
            Log("01: constructor entered");
            this.InitializeComponent();
            Log("02: after InitializeComponent");
            ViewModel = ((App)Application.Current).Services.GetRequiredService<ChannelsPageViewModel>();
            Log("03: after resolving ChannelsPageViewModel");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            Log($"04: OnNavigatedTo entered, parameterType={e.Parameter?.GetType().FullName ?? "null"}");
            base.OnNavigatedTo(e);
            _ = ViewModel.LoadChannelsCommand.ExecuteAsync(null);
            Log("05: queued LoadChannelsCommand");
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
                _ = LaunchChannelAsync(channel);
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

        private async void RecordChannel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: BrowserChannelViewModel channel })
            {
                await ScheduleRecordingAsync(channel);
            }
        }

        private async Task LaunchChannelAsync(BrowserChannelViewModel channel)
        {
            if (string.IsNullOrWhiteSpace(channel.StreamUrl))
            {
                return;
            }

            await ViewModel.RecordChannelLaunchAsync(channel.Id);

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
                _ = LaunchChannelAsync(channel);
                e.Handled = true;
            }
        }

        private void RecentChannelButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: BrowserChannelViewModel channel })
            {
                _ = LaunchChannelAsync(channel);
            }
        }

        private async void ClearRecentHistory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                button.IsEnabled = false;
                try
                {
                    await ViewModel.ClearRecentHistoryCommand.ExecuteAsync(null);
                }
                finally
                {
                    button.IsEnabled = true;
                }
            }
        }

        private async void RemoveRecentChannel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int channelId)
            {
                button.IsEnabled = false;
                try
                {
                    await ViewModel.RemoveRecentHistoryItemCommand.ExecuteAsync(channelId);
                }
                finally
                {
                    button.IsEnabled = true;
                }
            }
        }

        private async Task ScheduleRecordingAsync(BrowserChannelViewModel channel)
        {
            if (string.IsNullOrWhiteSpace(channel.StreamUrl))
            {
                return;
            }

            var now = DateTime.Now.AddMinutes(2);
            var startDatePicker = new DatePicker
            {
                Date = new DateTimeOffset(now)
            };
            var startTimePicker = new TimePicker
            {
                Time = new TimeSpan(now.Hour, now.Minute, 0)
            };
            var durationOptions = new[]
            {
                new RecordingDurationOption("30 minutes", TimeSpan.FromMinutes(30)),
                new RecordingDurationOption("60 minutes", TimeSpan.FromMinutes(60)),
                new RecordingDurationOption("120 minutes", TimeSpan.FromMinutes(120))
            };
            var durationComboBox = new ComboBox
            {
                ItemsSource = durationOptions,
                DisplayMemberPath = nameof(RecordingDurationOption.Label),
                SelectedIndex = 1
            };

            var dialog = new ContentDialog
            {
                Title = $"Schedule recording",
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = channel.Name,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                        },
                        new TextBlock
                        {
                            Text = "Choose a local start time and duration.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        startDatePicker,
                        startTimePicker,
                        durationComboBox
                    }
                }
            };

            if (await ShowContentDialogAsync(dialog) != ContentDialogResult.Primary)
            {
                return;
            }

            var selectedDate = startDatePicker.Date.Date;
            var selectedLocal = DateTime.SpecifyKind(selectedDate.Date + startTimePicker.Time, DateTimeKind.Local);
            var duration = (durationComboBox.SelectedItem as RecordingDurationOption)?.Duration ?? TimeSpan.FromMinutes(60);

            try
            {
                var mediaJobService = ((App)Application.Current).Services.GetRequiredService<IMediaJobService>();
                await mediaJobService.ScheduleRecordingAsync(channel.Id, channel.Name, channel.StreamUrl, selectedLocal, duration);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Recording failed", ex.Message);
                return;
            }

            var successDialog = new ContentDialog
            {
                Title = "Recording scheduled",
                PrimaryButtonText = "Open library",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                Content = $"{channel.Name} is scheduled for {selectedLocal:g}."
            };

            if (await ShowContentDialogAsync(successDialog) == ContentDialogResult.Primary)
            {
                Frame.Navigate(typeof(MediaLibraryPage));
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

        private async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                CloseButtonText = "Close",
                XamlRoot = XamlRoot,
                Content = message
            };

            await ShowContentDialogAsync(dialog);
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
    }
}
