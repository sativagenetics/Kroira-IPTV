using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private static readonly bool BypassInitialHomeLoad = false;
        private static readonly string StartupLogPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroira", "startup-log.txt");

        private static readonly string StartupErrorPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroira", "startup-error.txt");

        public HomeViewModel ViewModel { get; }

        public HomePage()
        {
            LogStartupCheckpoint("HOME 01: constructor entered");

            try
            {
                LogStartupCheckpoint("HOME 02: before InitializeComponent");
                this.InitializeComponent();
                LogStartupCheckpoint("HOME 03: after InitializeComponent");

                LogStartupCheckpoint("HOME 04: before resolving HomeViewModel");
                ViewModel = ((App)Application.Current).Services.GetRequiredService<HomeViewModel>();
                LogStartupCheckpoint("HOME 05: after resolving HomeViewModel");

                Loaded += HomePage_Loaded;
            }
            catch (Exception ex)
            {
                LogStartupCheckpoint("HOME FATAL: constructor exception");
                LogStartupException("HOMEPAGE CONSTRUCTOR EXCEPTION", ex);
                throw;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LogStartupCheckpoint($"HOME 06: OnNavigatedTo entered, mode={e.NavigationMode}, parameterType={e.Parameter?.GetType().Name ?? "null"}, bypass={BypassInitialHomeLoad}");

            if (BypassInitialHomeLoad)
            {
                LogStartupCheckpoint("HOME 07: skipping ViewModel.LoadCommand for startup isolation");
                return;
            }

            LogStartupCheckpoint("HOME 07: OnNavigatedTo before queue LoadCommand");
            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue == null)
            {
                throw new InvalidOperationException("UI dispatcher queue is unavailable for Home load.");
            }

            var enqueued = dispatcherQueue.TryEnqueue(() =>
            {
                LogStartupCheckpoint("HOME 08: starting queued LoadCommand");
                _ = ViewModel.LoadCommand.ExecuteAsync(null);
                LogStartupCheckpoint("HOME 09: queued LoadCommand returned control");
            });

            LogStartupCheckpoint($"HOME 10: OnNavigatedTo after queue LoadCommand, enqueued={enqueued}");
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            LogStartupCheckpoint("HOME 11: Loaded fired");
        }

        private void AddSource_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SourceOnboardingPage));
        }

        private void ContinueWatching_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ContinueWatchingPage));
        }

        private void FeaturedPrimary_Click(object sender, RoutedEventArgs e)
        {
            var item = ViewModel.FeaturedItem;
            if (item == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.StreamUrl))
            {
                Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                {
                    ContentId = item.ContentId,
                    ContentType = item.ContentType,
                    StreamUrl = item.StreamUrl,
                    StartPositionMs = 0
                });
                return;
            }

            NavigateToTarget(item.Target);
        }

        private void QuickAction_Click(object sender, RoutedEventArgs e)
        {
            if (GetTemplateItem<HomeActionItem>(sender) is { } item)
            {
                NavigateToTarget(item.Target);
            }
        }

        private void ContinueItem_Click(object sender, RoutedEventArgs e)
        {
            if (GetTemplateItem<HomeContinueItem>(sender) is { } item)
            {
                OpenContinueItem(item);
            }
        }

        private void MediaItem_Click(object sender, RoutedEventArgs e)
        {
            if (GetTemplateItem<HomeMediaItem>(sender) is not { } item)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.StreamUrl))
            {
                Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                {
                    ContentId = item.ContentId,
                    ContentType = item.ContentType,
                    StreamUrl = item.StreamUrl,
                    StartPositionMs = 0
                });
                return;
            }

            NavigateToTarget(item.Target);
        }

        private void OpenContinueItem(HomeContinueItem item)
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

        private void LiveNow_Click(object sender, RoutedEventArgs e)
        {
            if (GetTemplateItem<HomeLiveItem>(sender) is { } item &&
                !string.IsNullOrWhiteSpace(item.StreamUrl))
            {
                Frame.Navigate(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                {
                    ContentId = item.ContentId,
                    ContentType = PlaybackContentType.Channel,
                    StreamUrl = item.StreamUrl,
                    StartPositionMs = 0
                });
            }
        }

        private static T GetTemplateItem<T>(object sender) where T : class
        {
            if (sender is FrameworkElement element)
            {
                if (element.DataContext is T direct)
                {
                    return direct;
                }

                if (element is ContentControl { Content: FrameworkElement contentElement } &&
                    contentElement.DataContext is T content)
                {
                    return content;
                }
            }

            return null;
        }

        private void LiveTv_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(ChannelsPage));
        }

        private void Movies_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MoviesPage));
        }

        private void BrowseCatalog_Click(object sender, RoutedEventArgs e)
        {
            var preferredTarget = ViewModel.PopularItems.FirstOrDefault()?.Target;
            if (string.Equals(preferredTarget, "Series", StringComparison.OrdinalIgnoreCase))
            {
                Frame.Navigate(typeof(SeriesPage));
                return;
            }

            Frame.Navigate(typeof(MoviesPage));
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
                case "MediaLibrary":
                    Frame.Navigate(typeof(MediaLibraryPage));
                    break;
                case "Sources":
                    Frame.Navigate(typeof(SourceListPage));
                    break;
                case "Settings":
                    Frame.Navigate(typeof(SettingsPage));
                    break;
            }
        }

        private static void LogStartupCheckpoint(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                Debug.WriteLine(line);
                File.AppendAllText(StartupLogPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static void LogStartupException(string title, Exception ex)
        {
            try
            {
                var text =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {title}{Environment.NewLine}" +
                    ex + Environment.NewLine +
                    new string('-', 80) + Environment.NewLine;
                Debug.WriteLine(text);
                File.AppendAllText(StartupErrorPath, text);
                File.AppendAllText(StartupLogPath, text);
            }
            catch
            {
            }
        }
    }
}
