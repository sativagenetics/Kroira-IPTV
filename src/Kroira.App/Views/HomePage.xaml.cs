using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App.Views
{
    public sealed partial class HomePage : Page, IRemoteNavigationPage
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
                throw new InvalidOperationException(LocalizedStrings.Get("Home.Error.DispatcherUnavailable"));
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
            NavigateFromHome(typeof(SourceOnboardingPage), $"AddSource_Click sender={DescribeNavigationSender(sender)}");
        }

        private void ContinueWatching_Click(object sender, RoutedEventArgs e)
        {
            NavigateFromHome(typeof(ContinueWatchingPage), $"ContinueWatching_Click sender={DescribeNavigationSender(sender)}");
        }

        private void OpenSources_Click(object sender, RoutedEventArgs e)
        {
            NavigateToTarget("Sources", $"OpenSources_Click sender={DescribeNavigationSender(sender)}");
        }

        private void RetrySurface_Click(object sender, RoutedEventArgs e)
        {
            _ = ViewModel.LoadCommand.ExecuteAsync(null);
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
                NavigateFromHome(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                {
                    ContentId = item.ContentId,
                    ContentType = item.ContentType,
                    StreamUrl = item.StreamUrl,
                    StartPositionMs = 0
                }, $"FeaturedPrimary_Click playback id={item.ContentId} title={item.Title}");
                return;
            }

            NavigateToTarget(item.Target, $"FeaturedPrimary_Click target={item.Target}");
        }

        private void QuickAction_Click(object sender, RoutedEventArgs e)
        {
            if (GetTemplateItem<HomeActionItem>(sender) is { } item)
            {
                NavigateToTarget(item.Target, $"QuickAction_Click title={item.Title} sender={DescribeNavigationSender(sender)}");
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
                NavigateFromHome(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                {
                    ContentId = item.ContentId,
                    ContentType = item.ContentType,
                    StreamUrl = item.StreamUrl,
                    StartPositionMs = 0
                }, $"MediaItem_Click playback id={item.ContentId} title={item.Title}");
                return;
            }

            NavigateToTarget(item.Target, $"MediaItem_Click target={item.Target} title={item.Title}");
        }

        private void OpenContinueItem(HomeContinueItem item)
        {
            if (string.IsNullOrWhiteSpace(item.StreamUrl))
            {
                return;
            }

            NavigateFromHome(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
            {
                ContentId = item.ContentId,
                ContentType = item.ContentType,
                StreamUrl = item.StreamUrl,
                StartPositionMs = item.SavedPositionMs
            }, $"ContinueItem_Click playback id={item.ContentId} title={item.Title}");
        }

        private void LiveNow_Click(object sender, RoutedEventArgs e)
        {
            if (GetTemplateItem<HomeLiveItem>(sender) is { } item &&
                !string.IsNullOrWhiteSpace(item.StreamUrl))
            {
                NavigateFromHome(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
                {
                    ContentId = item.ContentId,
                    ContentType = PlaybackContentType.Channel,
                    StreamUrl = item.StreamUrl,
                    StartPositionMs = 0
                }, $"LiveNow_Click channel id={item.ContentId} title={item.Title} sender={DescribeNavigationSender(sender)}");
            }
        }

        private async void LiveSportsNow_Click(object sender, RoutedEventArgs e)
        {
            if (GetTemplateItem<HomeSportsLiveItem>(sender) is not { } item ||
                string.IsNullOrWhiteSpace(item.StreamUrl))
            {
                return;
            }

            try
            {
                await ViewModel.RecordLiveChannelLaunchAsync(item.ContentId);
            }
            catch (Exception ex)
            {
                LogStartupException("HOME LIVE SPORTS LAUNCH RECORD ERROR", ex);
            }

            NavigateFromHome(typeof(EmbeddedPlaybackPage), new PlaybackLaunchContext
            {
                ContentId = item.ContentId,
                ContentType = PlaybackContentType.Channel,
                LogicalContentKey = item.LogicalContentKey,
                PreferredSourceProfileId = item.PreferredSourceProfileId,
                CatalogStreamUrl = item.StreamUrl,
                StreamUrl = item.StreamUrl,
                LiveStreamUrl = item.StreamUrl,
                StartPositionMs = 0
            }, $"LiveSportsNow_Click channel id={item.ContentId} title={item.Title} sender={DescribeNavigationSender(sender)}");
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
            NavigateFromHome(typeof(ChannelsPage), $"LiveTv_Click sender={DescribeNavigationSender(sender)}");
        }

        private void Sports_Click(object sender, RoutedEventArgs e)
        {
            NavigateFromHome(
                typeof(ChannelsPage),
                ChannelsNavigationContext.Sports(),
                $"Sports_Click sender={DescribeNavigationSender(sender)} mode=Sports");
        }

        private void Favorites_Click(object sender, RoutedEventArgs e)
        {
            NavigateFromHome(typeof(FavoritesPage), $"Favorites_Click sender={DescribeNavigationSender(sender)}");
        }

        private void Movies_Click(object sender, RoutedEventArgs e)
        {
            NavigateFromHome(typeof(MoviesPage), $"Movies_Click sender={DescribeNavigationSender(sender)}");
        }

        private void BrowseCatalog_Click(object sender, RoutedEventArgs e)
        {
            var preferredTarget = ViewModel.PopularItems.FirstOrDefault()?.Target;
            if (string.Equals(preferredTarget, "Series", StringComparison.OrdinalIgnoreCase))
            {
                NavigateFromHome(typeof(SeriesPage), $"BrowseCatalog_Click preferredTarget={preferredTarget} sender={DescribeNavigationSender(sender)}");
                return;
            }

            NavigateFromHome(typeof(MoviesPage), $"BrowseCatalog_Click preferredTarget={preferredTarget ?? "Movies"} sender={DescribeNavigationSender(sender)}");
        }

        private void NavigateToTarget(string target, string source = "NavigateToTarget")
        {
            switch (target)
            {
                case "Channels":
                    NavigateFromHome(typeof(ChannelsPage), $"{source} target=Channels");
                    break;
                case "Movies":
                    NavigateFromHome(typeof(MoviesPage), $"{source} target=Movies");
                    break;
                case "Series":
                    NavigateFromHome(typeof(SeriesPage), $"{source} target=Series");
                    break;
                case "Favorites":
                    NavigateFromHome(typeof(FavoritesPage), $"{source} target=Favorites");
                    break;
                case "MediaLibrary":
                    NavigateFromHome(StoreReleaseFeatures.ShowMediaLibrary ? typeof(MediaLibraryPage) : typeof(ContinueWatchingPage),
                        $"{source} target=MediaLibrary showMediaLibrary={StoreReleaseFeatures.ShowMediaLibrary}");
                    break;
                case "Sources":
                    NavigateFromHome(typeof(SourceListPage), $"{source} target=Sources");
                    break;
                case "Settings":
                    NavigateFromHome(typeof(SettingsPage), $"{source} target=Settings");
                    break;
            }
        }

        private bool NavigateFromHome(Type pageType, string source)
        {
            LogHomeNavigation($"{source} -> {pageType.Name}");
            return Frame.Navigate(pageType);
        }

        private bool NavigateFromHome(Type pageType, object parameter, string source)
        {
            LogHomeNavigation($"{source} -> {pageType.Name} parameter={parameter.GetType().Name}");
            return Frame.Navigate(pageType, parameter);
        }

        private static void LogHomeNavigation(string message)
        {
            LogStartupCheckpoint($"HOME NAV {message}");
        }

        private static string DescribeNavigationSender(object sender)
        {
            if (sender is not FrameworkElement element)
            {
                return sender?.GetType().Name ?? "null";
            }

            var name = string.IsNullOrWhiteSpace(element.Name) ? string.Empty : $"#{element.Name}";
            var automationName = AutomationProperties.GetName(element);
            var automation = string.IsNullOrWhiteSpace(automationName) ? string.Empty : $" automation='{automationName}'";
            var dataContext = element.DataContext == null ? string.Empty : $" dataContext={element.DataContext.GetType().Name}";
            return $"{element.GetType().Name}{name}{automation}{dataContext}";
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

        public bool TryFocusPrimaryTarget()
        {
            if (RemoteNavigationHelper.TryFocusElement(FeaturedPrimaryButton))
            {
                return true;
            }

            return ContinueWatchingRail.TryFocusPrimaryItem() ||
                   RecommendedRail.TryFocusPrimaryItem() ||
                   RecentlyAddedRail.TryFocusPrimaryItem() ||
                   TopRatedRail.TryFocusPrimaryItem() ||
                   LiveNowRail.TryFocusPrimaryItem();
        }

        public bool TryHandleBackRequest()
        {
            var focusedElement = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            if (!RemoteNavigationHelper.IsDescendantOf(focusedElement, FeaturedPrimaryButton))
            {
                return TryFocusPrimaryTarget();
            }

            return false;
        }
    }
}
