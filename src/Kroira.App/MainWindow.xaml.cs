using System;
using System.Diagnostics;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Kroira.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }
        private readonly IWindowManagerService _windowManager;

        public MainWindow()
        {
            Debug.WriteLine("MW 01: constructor entered");

            try
            {
                Debug.WriteLine("MW 02: before InitializeComponent");
                InitializeComponent();
                Debug.WriteLine("MW 03: after InitializeComponent");

                ContentFrame.NavigationFailed += ContentFrame_NavigationFailed;

                Debug.WriteLine("MW 04: before resolving MainViewModel");
                ViewModel = ((App)Application.Current).Services.GetRequiredService<MainViewModel>();
                Debug.WriteLine("MW 05: after resolving MainViewModel");

                Debug.WriteLine("MW 06: before resolving IWindowManagerService");
                _windowManager = ((App)Application.Current).Services.GetRequiredService<IWindowManagerService>();
                Debug.WriteLine("MW 07: after resolving IWindowManagerService");

                Title = "Kroira IPTV";
                Debug.WriteLine("MW 08: title set");

                Debug.WriteLine("MW 09: before initial navigation to HomePage");
                var navigated = ContentFrame.Navigate(typeof(HomePage));
                Debug.WriteLine($"MW 10: after initial navigation to HomePage, result={navigated}");

                if (!navigated)
                {
                    throw new InvalidOperationException("Initial navigation to HomePage returned false.");
                }

                if (RootNavView.MenuItems.Count > 0)
                {
                    RootNavView.SelectedItem = RootNavView.MenuItems[0];
                    Debug.WriteLine("MW 11: selected first nav item");
                }

                _windowManager.FullscreenStateChanged += WindowManager_FullscreenStateChanged;
                Debug.WriteLine("MW 12: subscribed to FullscreenStateChanged");
                UpdatePaneHeader(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("MW FATAL: exception in MainWindow constructor");
                Debug.WriteLine(ex.ToString());
                throw;
            }
        }

        private void WindowManager_FullscreenStateChanged(object? sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine($"MW FS: fullscreen changed, isFullscreen={_windowManager.IsFullscreen}");

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
                Debug.WriteLine("MW FS ERROR:");
                Debug.WriteLine(ex.ToString());
                throw;
            }
        }

        private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            Debug.WriteLine("MW NAV FAILED:");
            Debug.WriteLine($"SourcePageType: {e.SourcePageType?.FullName}");
            Debug.WriteLine(e.Exception?.ToString());

            throw new InvalidOperationException(
                $"Navigation to {e.SourcePageType?.FullName} failed.",
                e.Exception);
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
                Debug.WriteLine($"MW NAV: item invoked, tag={tag}");

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
                Debug.WriteLine("MW NAV ERROR:");
                Debug.WriteLine(ex.ToString());
                throw;
            }
        }

        private void NavigateTo(Type pageType)
        {
            Debug.WriteLine($"MW NAV 01: before navigate to {pageType.FullName}");
            var navigated = ContentFrame.Navigate(pageType);
            Debug.WriteLine($"MW NAV 02: after navigate to {pageType.FullName}, result={navigated}");

            if (!navigated)
            {
                throw new InvalidOperationException($"Navigation to {pageType.FullName} returned false.");
            }
        }
    }
}
