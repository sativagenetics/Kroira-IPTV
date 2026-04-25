#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
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
    public sealed partial class ChannelsPage : Page, IRemoteNavigationPage
    {
        private const string MediaActionOverlayTag = "MediaActionOverlay";

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

        public Visibility RecordingActionsVisibility => StoreReleaseFeatures.ShowRecordingActions ? Visibility.Visible : Visibility.Collapsed;

        public ChannelsPage()
        {
            Log("01: constructor entered");
            this.InitializeComponent();
            Log("02: after InitializeComponent");
            ViewModel = ((App)Application.Current).Services.GetRequiredService<ChannelsPageViewModel>();
            Log("03: after resolving ChannelsPageViewModel");
            Loaded += ChannelsPage_Loaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            var navigationContext = e.Parameter as ChannelsNavigationContext;
            Log($"04: OnNavigatedTo entered, parameterType={e.Parameter?.GetType().FullName ?? "null"}, mode={navigationContext?.Mode.ToString() ?? "Default"}");
            base.OnNavigatedTo(e);
            if (ViewModel.HasLoadedOnce)
            {
                ViewModel.RefreshNavigationContext(navigationContext);
                Log("05: refreshed navigation context without catalog reload");
                return;
            }

            ViewModel.SetNavigationContext(navigationContext);
            _ = ViewModel.LoadChannelsCommand.ExecuteAsync(null);
            Log("05: queued LoadChannelsCommand");
        }

        private void OpenSources_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SourceListPage));
        }

        private void OpenGuide_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(EpgCenterPage));
        }

        private void RetrySurface_Click(object sender, RoutedEventArgs e)
        {
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

        private void RevealCardRoot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement root)
            {
                return;
            }

            SetActionOverlay(root, reveal: false);
            root.PointerEntered -= RevealCard_PointerEntered;
            root.PointerEntered += RevealCard_PointerEntered;
            root.PointerExited -= RevealCard_PointerExited;
            root.PointerExited += RevealCard_PointerExited;
            root.GotFocus -= RevealCard_GotFocus;
            root.GotFocus += RevealCard_GotFocus;
            root.LostFocus -= RevealCard_LostFocus;
            root.LostFocus += RevealCard_LostFocus;

            var item = FindAncestor<GridViewItem>(root);
            if (item == null)
            {
                return;
            }

            item.PointerEntered -= RevealCard_PointerEntered;
            item.PointerEntered += RevealCard_PointerEntered;
            item.PointerExited -= RevealCard_PointerExited;
            item.PointerExited += RevealCard_PointerExited;
            item.GotFocus -= RevealCard_GotFocus;
            item.GotFocus += RevealCard_GotFocus;
            item.LostFocus -= RevealCard_LostFocus;
            item.LostFocus += RevealCard_LostFocus;
        }

        private void RevealCard_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            SetActionOverlay(sender as DependencyObject, reveal: true);
        }

        private void RevealCard_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            var source = sender as DependencyObject;
            if (HasKeyboardFocusWithin(source))
            {
                return;
            }

            SetActionOverlay(source, reveal: false);
        }

        private void RevealCard_GotFocus(object sender, RoutedEventArgs e)
        {
            SetActionOverlay(sender as DependencyObject, reveal: true);
        }

        private void RevealCard_LostFocus(object sender, RoutedEventArgs e)
        {
            var source = sender as DependencyObject;
            if (DispatcherQueue != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!HasKeyboardFocusWithin(source))
                    {
                        SetActionOverlay(source, reveal: false);
                    }
                });
                return;
            }

            if (!HasKeyboardFocusWithin(source))
            {
                SetActionOverlay(source, reveal: false);
            }
        }

        private static void SetActionOverlay(DependencyObject? source, bool reveal)
        {
            if (source == null)
            {
                return;
            }

            var overlay = FindTaggedDescendant(source, MediaActionOverlayTag);
            if (overlay == null)
            {
                return;
            }

            overlay.Visibility = reveal ? Visibility.Visible : Visibility.Collapsed;
            overlay.Opacity = reveal ? 1 : 0;
            overlay.IsHitTestVisible = reveal;
        }

        private static bool HasKeyboardFocusWithin(DependencyObject? source)
        {
            if (source == null)
            {
                return false;
            }

            var focused = FocusManager.GetFocusedElement();
            return focused is DependencyObject focusedObject && IsDescendantOrSelf(source, focusedObject);
        }

        private static bool IsDescendantOrSelf(DependencyObject root, DependencyObject candidate)
        {
            var current = candidate;
            while (current != null)
            {
                if (ReferenceEquals(current, root))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private static FrameworkElement? FindTaggedDescendant(DependencyObject root, string tag)
        {
            if (root is FrameworkElement element && Equals(element.Tag, tag))
            {
                return element;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var match = FindTaggedDescendant(child, tag);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static T? FindAncestor<T>(DependencyObject? source)
            where T : DependencyObject
        {
            var current = source == null ? null : VisualTreeHelper.GetParent(source);
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
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

        private async void StartCatchup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.DataContext is not BrowserChannelViewModel channel)
            {
                return;
            }

            button.IsEnabled = false;
            try
            {
                await LaunchCatchupAsync(channel);
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private async void InspectChannel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: BrowserChannelViewModel channel })
            {
                return;
            }

            await ItemInspectorDialog.ShowAsync(XamlRoot, CreateChannelLaunchContext(channel));
        }

        private async Task LaunchChannelAsync(BrowserChannelViewModel channel)
        {
            if (string.IsNullOrWhiteSpace(channel.StreamUrl))
            {
                return;
            }

            Log($"06: LaunchChannelAsync channelId={channel.Id} name='{channel.Name}'");
            await ViewModel.RecordChannelLaunchAsync(channel.Id);
            Frame.Navigate(typeof(EmbeddedPlaybackPage), CreateChannelLaunchContext(channel));
        }

        private async Task LaunchCatchupAsync(BrowserChannelViewModel channel)
        {
            if (channel.CatchupRequestKind == CatchupRequestKind.None ||
                !channel.CatchupProgramStartTimeUtc.HasValue ||
                !channel.CatchupProgramEndTimeUtc.HasValue)
            {
                if (!string.IsNullOrWhiteSpace(channel.CatchupStatusText))
                {
                    await ShowMessageAsync(LocalizedStrings.Get("Channels.CatchupUnavailableTitle"), channel.CatchupStatusText);
                }

                return;
            }

            Log($"06a: LaunchCatchupAsync channelId={channel.Id} name='{channel.Name}' kind={channel.CatchupRequestKind}");
            await ViewModel.RecordChannelLaunchAsync(channel.Id);

            var context = CreateChannelLaunchContext(channel);
            context.PlaybackMode = CatchupPlaybackMode.Catchup;
            context.CatchupRequestKind = channel.CatchupRequestKind;
            context.CatchupStatusText = channel.CatchupStatusText;
            context.CatchupProgramTitle = channel.CurrentProgramTitle;
            context.CatchupProgramStartTimeUtc = channel.CatchupProgramStartTimeUtc;
            context.CatchupProgramEndTimeUtc = channel.CatchupProgramEndTimeUtc;
            context.CatchupRequestedAtUtc = DateTime.UtcNow;
            Frame.Navigate(typeof(EmbeddedPlaybackPage), context);
        }

        private static PlaybackLaunchContext CreateChannelLaunchContext(BrowserChannelViewModel channel)
        {
            return new PlaybackLaunchContext
            {
                ContentId = channel.Id,
                ContentType = PlaybackContentType.Channel,
                LogicalContentKey = channel.LogicalContentKey,
                PreferredSourceProfileId = channel.PreferredSourceProfileId,
                CatalogStreamUrl = channel.StreamUrl,
                StreamUrl = channel.StreamUrl,
                LiveStreamUrl = channel.StreamUrl,
                StartPositionMs = 0
            };
        }

        private void ChannelsPage_Loaded(object sender, RoutedEventArgs e)
        {
            Log($"05a: page Loaded fired; current grid items={ViewModel.FilteredChannels.Count}");
        }

        private void ChannelList_Loaded(object sender, RoutedEventArgs e)
        {
            Log($"05b: ChannelList Loaded fired; current grid items={ViewModel.FilteredChannels.Count}");
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
                new RecordingDurationOption(LocalizedStrings.Format("Player.Menu.SleepMinutes", 30), TimeSpan.FromMinutes(30)),
                new RecordingDurationOption(LocalizedStrings.Format("Player.Menu.SleepMinutes", 60), TimeSpan.FromMinutes(60)),
                new RecordingDurationOption(LocalizedStrings.Format("Player.Menu.SleepMinutes", 120), TimeSpan.FromMinutes(120))
            };
            var durationComboBox = new ComboBox
            {
                ItemsSource = durationOptions,
                DisplayMemberPath = nameof(RecordingDurationOption.Label),
                SelectedIndex = 1
            };

            var dialog = new ContentDialog
            {
                Title = LocalizedStrings.Get("Channels.Recording.ScheduleTitle"),
                PrimaryButtonText = LocalizedStrings.Get("General.Save"),
                CloseButtonText = LocalizedStrings.Get("General.Cancel"),
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
                            Text = LocalizedStrings.Get("Channels.Recording.ScheduleMessage"),
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
                await ShowMessageAsync(LocalizedStrings.Get("Channels.Recording.FailedTitle"), ex.Message);
                return;
            }

            var successDialog = new ContentDialog
            {
                Title = LocalizedStrings.Get("Channels.Recording.ScheduledTitle"),
                PrimaryButtonText = LocalizedStrings.Get("General.OpenLibrary"),
                CloseButtonText = LocalizedStrings.Get("General.Close"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                Content = LocalizedStrings.Format("Channels.Recording.ScheduledMessage", channel.Name, selectedLocal)
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

        private static bool TryGetChannelFromSource(object source, [NotNullWhen(true)] out BrowserChannelViewModel? channel)
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
