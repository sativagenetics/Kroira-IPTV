using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Kroira.App.Views;

namespace Kroira.App
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }
        private readonly object _homeContent;

        public MainWindow()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<MainViewModel>();
            
            this.Title = "Kroira IPTV";
            _homeContent = ContentFrame.Content;
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
            else if (args.InvokedItemContainer?.Tag?.ToString() == "DevPlayer")
            {
                ContentFrame.Navigate(typeof(Views.DevPlaybackPage));
            }
            else if (args.InvokedItemContainer?.Tag?.ToString() == "Home")
            {
                ContentFrame.Content = _homeContent;
            }
        }
    }
}
