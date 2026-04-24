#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using WinRT.Interop;

namespace Kroira.App
{
    public sealed partial class MainWindow : Window
    {
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

                ContentFrame.Navigating += ContentFrame_Navigating;
                ContentFrame.NavigationFailed += ContentFrame_NavigationFailed;
                ContentFrame.Navigated += ContentFrame_Navigated;
                var app = RequireApp();

                LogStartupCheckpoint("MW 06: before resolving IWindowManagerService");
                _windowManager = app.Services.GetRequiredService<IWindowManagerService>();
                LogStartupCheckpoint("MW 07: after resolving IWindowManagerService");
                _remoteNavigationService = app.Services.GetRequiredService<IRemoteNavigationService>();

                Title = "Kroira IPTV";
                LogStartupCheckpoint("MW 08: title set");
                ConfigureDarkTitleBar();

                _windowManager.FullscreenStateChanged += WindowManager_FullscreenStateChanged;
                LogStartupCheckpoint("MW 09: subscribed to FullscreenStateChanged");
                ApplyShellStateForPageType(null, forceLayout: false);

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
                ApplyShellStateForCurrentPage(forceLayout: true);
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

        private void ContentFrame_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            if (e.Cancel)
            {
                return;
            }

            ApplyShellStateForPageType(e.SourcePageType, forceLayout: true);
            QueueShellStateRefresh(e.SourcePageType);
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            SyncSelectedNavigationItem(e.SourcePageType);
            ApplyShellStateForPageType(e.SourcePageType, forceLayout: true);
            QueueShellStateRefresh(e.SourcePageType);
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
            PaneHeaderRoot.Margin = isOpen ? new Thickness(18, 28, 14, 30) : new Thickness(17, 28, 0, 30);
        }

        private void ApplyShellStateForCurrentPage(bool forceLayout)
        {
            ApplyShellStateForPageType(ContentFrame.Content?.GetType(), forceLayout);
        }

        private void ApplyShellStateForPageType(Type? pageType, bool forceLayout)
        {
            var isPlaybackRoute = pageType == typeof(EmbeddedPlaybackPage);
            var isFullscreen = _windowManager?.IsFullscreen == true;
            var suppressNormalChrome = isPlaybackRoute || isFullscreen;

            if (suppressNormalChrome)
            {
                RootNavView.IsPaneOpen = false;
                RootNavView.IsPaneVisible = false;
                RootNavView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
            }
            else
            {
                RootNavView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftCompact;
                RootNavView.IsPaneVisible = true;
                RootNavView.IsPaneOpen = false;
            }

            ContentHostBorder.BorderThickness = suppressNormalChrome
                ? new Thickness(0)
                : new Thickness(1, 0, 0, 0);

            UpdatePaneHeader(!suppressNormalChrome && RootNavView.IsPaneOpen);

            RootNavView.InvalidateMeasure();
            ContentHostBorder.InvalidateMeasure();
            if (forceLayout)
            {
                RootNavView.UpdateLayout();
                ContentHostBorder.UpdateLayout();
            }
        }

        private void QueueShellStateRefresh(Type? pageType)
        {
            var dispatcherQueue = RootNavView.DispatcherQueue;
            if (dispatcherQueue == null)
            {
                return;
            }

            dispatcherQueue.TryEnqueue(() =>
            {
                ApplyShellStateForPageType(pageType ?? ContentFrame.Content?.GetType(), forceLayout: true);
                dispatcherQueue.TryEnqueue(() =>
                {
                    ApplyShellStateForPageType(ContentFrame.Content?.GetType() ?? pageType, forceLayout: true);
                });
            });
        }

        private void ConfigureDarkTitleBar()
        {
            try
            {
                if (!AppWindowTitleBar.IsCustomizationSupported())
                {
                    LogStartupCheckpoint("MW TITLEBAR: customization unsupported");
                    return;
                }

                var hwnd = WindowNative.GetWindowHandle(this);
                var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);
                var titleBar = appWindow.TitleBar;

                var background = Color.FromArgb(255, 5, 6, 10);
                var inactiveBackground = Color.FromArgb(255, 8, 10, 16);
                var foreground = Color.FromArgb(255, 238, 241, 246);
                var mutedForeground = Color.FromArgb(255, 140, 148, 162);
                var hover = Color.FromArgb(255, 22, 27, 39);
                var pressed = Color.FromArgb(255, 36, 26, 52);

                titleBar.BackgroundColor = background;
                titleBar.InactiveBackgroundColor = inactiveBackground;
                titleBar.ForegroundColor = foreground;
                titleBar.InactiveForegroundColor = mutedForeground;
                titleBar.ButtonBackgroundColor = background;
                titleBar.ButtonInactiveBackgroundColor = inactiveBackground;
                titleBar.ButtonForegroundColor = foreground;
                titleBar.ButtonInactiveForegroundColor = mutedForeground;
                titleBar.ButtonHoverBackgroundColor = hover;
                titleBar.ButtonHoverForegroundColor = foreground;
                titleBar.ButtonPressedBackgroundColor = pressed;
                titleBar.ButtonPressedForegroundColor = foreground;
                LogStartupCheckpoint("MW TITLEBAR: dark colors applied");
            }
            catch (Exception ex)
            {
                LogStartupCheckpoint("MW TITLEBAR ERROR");
                LogStartupException("MAINWINDOW TITLEBAR ERROR", ex);
            }
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
                    case "Guide":
                        NavigateTo(typeof(EpgCenterPage));
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

            foreach (var item in EnumerateNavigationItems())
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
                var type when type == typeof(EpgCenterPage) => "Guide",
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

            var selected = EnumerateNavigationItems()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                RootNavView.SelectedItem = selected;
            }
        }

        private System.Collections.Generic.IEnumerable<NavigationViewItem> EnumerateNavigationItems()
        {
            return RootNavView.MenuItems
                .OfType<NavigationViewItem>()
                .Concat(RootNavView.FooterMenuItems.OfType<NavigationViewItem>());
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
                ApplyShellStateForPageType(typeof(HomePage), forceLayout: true);
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

                ApplyShellStateForPageType(typeof(HomePage), forceLayout: true);
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
