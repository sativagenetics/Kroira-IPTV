using Kroira.App.Services;
using Kroira.App.ViewModels;
using Kroira.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kroira.App
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }
        private readonly object _homeContent;
        private readonly IWindowManagerService _windowManager;

        public MainWindow()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<MainViewModel>();
            _windowManager = ((App)Application.Current).Services.GetRequiredService<IWindowManagerService>();

            this.Title = "Kroira IPTV";
            _homeContent = ContentFrame.Content;

            _windowManager.FullscreenStateChanged += (s, e) =>
            {
                if (_windowManager.IsFullscreen)
                {
                    RootNavView.IsPaneVisible = false;
                    RootNavView.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
                }
                else
                {
                    RootNavView.IsPaneVisible = true;
                    RootNavView.PaneDisplayMode = NavigationViewPaneDisplayMode.Auto;
                }
            };
        }

        private void RootNavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                ContentFrame.Navigate(typeof(SettingsPage));
            }
            else if (args.InvokedItemContainer?.Tag?.ToString() == "Sources")
            {
                ContentFrame.Navigate(typeof(Views.SourceListPage));
            }
            else if (args.InvokedItemContainer?.Tag?.ToString() == "Channels")
            {
                ContentFrame.Navigate(typeof(Views.ChannelsPage));
            }
            else if (args.InvokedItemContainer?.Tag?.ToString() == "Home")
            {
                ContentFrame.Content = _homeContent;
            }
            else if (args.InvokedItemContainer?.Tag?.ToString() == "Favorites")
            {
                ContentFrame.Navigate(typeof(Views.FavoritesPage));
            }
            else if (args.InvokedItemContainer?.Tag?.ToString() == "ContinueWatching")
            {
                ContentFrame.Navigate(typeof(Views.ContinueWatchingPage));
            }
            else if (args.InvokedItemContainer?.Tag?.ToString() == "Movies")
            {
                ContentFrame.Navigate(typeof(Views.MoviesPage));
            }
            else if (args.InvokedItemContainer?.Tag?.ToString() == "Series")
            {
                ContentFrame.Navigate(typeof(Views.SeriesPage));
            }
        }
    }
}
