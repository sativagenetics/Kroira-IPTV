using Kroira.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Views
{
    public sealed partial class SourceListPage : Page
    {
        public SourceListViewModel ViewModel { get; }

        public SourceListPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<SourceListViewModel>();
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.LoadSourcesCommand.Execute(null);
        }

        private void AddNewSource_Click(object sender, RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(SourceOnboardingPage));
        }

        private void DeleteSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                ViewModel.DeleteSourceCommand.Execute(id);
            }
        }

        private void ParseSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                ViewModel.ParseSourceCommand.Execute(id);
            }
        }

        private void BrowseSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                this.Frame.Navigate(typeof(ChannelBrowserPage), id);
            }
        }

        private void SyncEpgSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                ViewModel.SyncEpgCommand.Execute(id);
            }
        }
    }
}
