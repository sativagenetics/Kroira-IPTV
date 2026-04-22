#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Kroira.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using Windows.UI.Core;

namespace Kroira.App
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }
        private readonly IWindowManagerService _windowManager;
        private readonly IRemoteNavigationService _remoteNavigationService;
        private static readonly string StartupLogPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroira", "startup-log.txt");

        private static readonly string StartupErrorPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kroira", "startup-error.txt");

        public Visibility MediaLibraryNavigationVisibility => StoreReleaseFeatures.ShowMediaLibrary ? Visibility.Visible : Visibility.Collapsed;

        public MainWindow()
        {
            LogStartupCheckpoint("MW 01: constructor entered");

            try
            {
                LogStartupCheckpoint("MW 02: before InitializeComponent");
                InitializeComponent();
                LogStartupCheckpoint("MW 03: after InitializeComponent");

                ContentFrame.NavigationFailed += ContentFrame_NavigationFailed;
                ContentFrame.Navigated += ContentFrame_Navigated;
                var app = RequireApp();

                LogStartupCheckpoint("MW 04: before resolving MainViewModel");
                ViewModel = app.Services.GetRequiredService<MainViewModel>();
                LogStartupCheckpoint("MW 05: after resolving MainViewModel");

                LogStartupCheckpoint("MW 06: before resolving IWindowManagerService");
                _windowManager = app.Services.GetRequiredService<IWindowManagerService>();
                LogStartupCheckpoint("MW 07: after resolving IWindowManagerService");
                _remoteNavigationService = app.Services.GetRequiredService<IRemoteNavigationService>();

                Title = "Kroira IPTV";
                LogStartupCheckpoint("MW 08: title set");

                _windowManager.FullscreenStateChanged += WindowManager_FullscreenStateChanged;
                LogStartupCheckpoint("MW 09: subscribed to FullscreenStateChanged");
                UpdatePaneHeader(true);

                if (Content is UIElement rootElement)
                {
                    rootElement.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RootElement_KeyDown), true);
                }
            }
            catch (Exception ex)
            {
                LogStartupCheckpoint("MW FATAL: exception in MainWindow constructor");
                LogStartupException("MAINWINDOW CONSTRUCTOR EXCEPTION", ex);
                throw;
            }
        }

        private void WindowManager_FullscreenStateChanged(object? sender, EventArgs e)
        {
            try
            {
                LogStartupCheckpoint($"MW FS: fullscreen changed, isFullscreen={_windowManager.IsFullscreen}");

                if (_windowManager.IsFullscreen)
                {
                    RootNavView.IsPaneVisible = false;
                    RootNavView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
                }
                else
                {
                    RootNavView.IsPaneVisible = true;
                    RootNavView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
                    RootNavView.IsPaneOpen = true;
                }
            }
            catch (Exception ex)
            {
                LogStartupCheckpoint("MW FS ERROR");
                LogStartupException("MAINWINDOW FULLSCREEN ERROR", ex);
                throw;
            }
        }

        private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            LogStartupCheckpoint($"MW NAV FAILED: {e.SourcePageType?.FullName}");
            if (e.Exception != null)
            {
                LogStartupException("MAINWINDOW NAVIGATION FAILED", e.Exception);
            }
            throw new InvalidOperationException(
                $"Navigation to {e.SourcePageType?.FullName} failed.",
                e.Exception);
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            SyncSelectedNavigationItem(e.SourcePageType);
            if (!_remoteNavigationService.IsRemoteModeEnabled ||
                ContentFrame.Content is not IRemoteNavigationPage remotePage)
            {
                return;
            }

            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue == null)
            {
                remotePage.TryFocusPrimaryTarget();
                return;
            }

            dispatcherQueue.TryEnqueue(() =>
            {
                if (remotePage.TryFocusPrimaryTarget())
                {
                    return;
                }

                dispatcherQueue.TryEnqueue(() => remotePage.TryFocusPrimaryTarget());
            });
        }

        private void RootElement_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Handled)
            {
                return;
            }

            var altDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down);
            switch (e.Key)
            {
                case VirtualKey.Left when altDown:
                case VirtualKey.GoBack:
                case VirtualKey.Back when altDown:
                    if (TryHandleGlobalBackRequest())
                    {
                        e.Handled = true;
                    }
                    break;
                case VirtualKey.Escape:
                    if (_remoteNavigationService.IsRemoteModeEnabled &&
                        ((ContentFrame.Content is IRemoteNavigationPage remotePage && remotePage.TryHandleBackRequest()) ||
                         TryFocusSelectedNavigationItem()))
                    {
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void RootNavView_PaneOpening(NavigationView sender, object args)
        {
            UpdatePaneHeader(true);
        }

        private void RootNavView_PaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args)
        {
            UpdatePaneHeader(false);
        }

        private void UpdatePaneHeader(bool isOpen)
        {
            PaneBrandText.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            PaneFooterRoot.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            PaneHeaderRoot.Margin = isOpen ? new Thickness(18, 28, 14, 30) : new Thickness(17, 28, 0, 30);
        }

        private void RootNavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            try
            {
                if (args.IsSettingsInvoked)
                {
                    NavigateTo(typeof(SettingsPage));
                    return;
                }

                var tag = args.InvokedItemContainer?.Tag?.ToString();
                LogStartupCheckpoint($"MW NAV: item invoked, tag={tag}");

                switch (tag)
                {
                    case "Sources":
                        NavigateTo(typeof(SourceListPage));
                        break;
                    case "Channels":
                        NavigateTo(typeof(ChannelsPage));
                        break;
                    case "Home":
                        NavigateTo(typeof(HomePage));
                        break;
                    case "Favorites":
                        NavigateTo(typeof(FavoritesPage));
                        break;
                    case "ContinueWatching":
                        NavigateTo(typeof(ContinueWatchingPage));
                        break;
                    case "MediaLibrary":
                        NavigateTo(StoreReleaseFeatures.ShowMediaLibrary ? typeof(MediaLibraryPage) : typeof(ContinueWatchingPage));
                        break;
                    case "Movies":
                        NavigateTo(typeof(MoviesPage));
                        break;
                    case "Series":
                        NavigateTo(typeof(SeriesPage));
                        break;
                    case "Settings":
                        NavigateTo(typeof(SettingsPage));
                        break;
                    case "Profile":
                        NavigateTo(typeof(ProfilePage));
                        break;
                }
            }
            catch (Exception ex)
            {
                LogStartupCheckpoint("MW NAV ERROR");
                LogStartupException("MAINWINDOW NAVIGATION COMMAND ERROR", ex);
                throw;
            }
        }

        private bool TryHandleGlobalBackRequest()
        {
            if (ContentFrame.Content is IRemoteNavigationPage remotePage &&
                remotePage.TryHandleBackRequest())
            {
                return true;
            }

            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
                return true;
            }

            return _remoteNavigationService.IsRemoteModeEnabled && TryFocusSelectedNavigationItem();
        }

        private bool TryFocusSelectedNavigationItem()
        {
            if (!RootNavView.IsPaneVisible)
            {
                return false;
            }

            var focusedElement = FocusManager.GetFocusedElement() as DependencyObject;
            if (RemoteNavigationHelper.IsDescendantOf(focusedElement, RootNavView))
            {
                if (ContentFrame.CanGoBack)
                {
                    ContentFrame.GoBack();
                    return true;
                }

                return false;
            }

            if (RootNavView.SelectedItem is NavigationViewItem selectedItem &&
                selectedItem.Visibility == Visibility.Visible &&
                selectedItem.Focus(FocusState.Keyboard))
            {
                return true;
            }

            foreach (var item in RootNavView.MenuItems.OfType<NavigationViewItem>())
            {
                if (item.Visibility == Visibility.Visible && item.Focus(FocusState.Keyboard))
                {
                    return true;
                }
            }

            return false;
        }

        private void SyncSelectedNavigationItem(Type? pageType)
        {
            var tag = pageType switch
            {
                var type when type == typeof(HomePage) => "Home",
                var type when type == typeof(ContinueWatchingPage) => "ContinueWatching",
                var type when type == typeof(MediaLibraryPage) => "MediaLibrary",
                var type when type == typeof(ChannelsPage) => "Channels",
                var type when type == typeof(MoviesPage) => "Movies",
                var type when type == typeof(SeriesPage) => "Series",
                var type when type == typeof(FavoritesPage) => "Favorites",
                var type when type == typeof(SourceListPage) => "Sources",
                var type when type == typeof(SourceOnboardingPage) => "Sources",
                var type when type == typeof(ChannelBrowserPage) => "Sources",
                var type when type == typeof(SettingsPage) => "Settings",
                var type when type == typeof(ProfilePage) => "Profile",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            var selected = RootNavView.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                RootNavView.SelectedItem = selected;
            }
        }

        private void NavigateTo(Type pageType)
        {
            LogStartupCheckpoint($"MW NAV 01: before navigate to {pageType.FullName}");
            var navigated = ContentFrame.Navigate(pageType);
            LogStartupCheckpoint($"MW NAV 02: after navigate to {pageType.FullName}, result={navigated}");

            if (!navigated)
            {
                throw new InvalidOperationException($"Navigation to {pageType.FullName} returned false.");
            }
        }

        public void NavigateToPlayback(PlaybackLaunchContext context)
        {
            if (!ContentFrame.Navigate(typeof(EmbeddedPlaybackPage), context))
            {
                throw new InvalidOperationException("Navigation to EmbeddedPlaybackPage returned false.");
            }
        }

        public void QueueInitialNavigation()
        {
            LogStartupCheckpoint("MW INIT 01: queueing initial navigation");
            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue == null)
            {
                throw new InvalidOperationException("UI dispatcher queue is unavailable for initial navigation.");
            }

            var enqueued = dispatcherQueue.TryEnqueue(() =>
            {
                LogStartupCheckpoint("MW INIT 02: starting initial navigation");
                var navigated = ContentFrame.Navigate(typeof(HomePage));
                LogStartupCheckpoint($"MW INIT 03: initial navigation result={navigated}");

                if (!navigated)
                {
                    throw new InvalidOperationException("Initial navigation to HomePage returned false.");
                }

                if (RootNavView.MenuItems.Count > 0)
                {
                    RootNavView.SelectedItem = RootNavView.MenuItems[0];
                    LogStartupCheckpoint("MW INIT 04: selected first nav item");
                }
            });

            if (!enqueued)
            {
                throw new InvalidOperationException("Failed to enqueue initial navigation.");
            }
        }

        private void LogStartupCheckpoint(string message)
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

        private void LogStartupException(string title, Exception ex)
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

        private static App RequireApp()
        {
            return Application.Current as App
                ?? throw new InvalidOperationException("Kroira application services are unavailable.");
        }
    }
}
