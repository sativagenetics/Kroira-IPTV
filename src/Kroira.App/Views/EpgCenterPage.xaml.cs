using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App.Views
{
    public sealed partial class EpgCenterPage : Page, IRemoteNavigationPage
    {
        public EpgCenterViewModel ViewModel { get; }

        public EpgCenterPage()
        {
            InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<EpgCenterViewModel>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.LoadCommand.Execute(null);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RefreshCommand.Execute(null);
        }

        private void AddPresetGuide_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.AddSelectedPublicGuideCommand.Execute(null);
        }

        private void AddCustomGuide_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.AddCustomPublicGuideCommand.Execute(null);
        }

        private void ReplaceWithAutoDetected_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ReplaceWithAutoDetectedPublicGuidesCommand.Execute(null);
        }

        private void ClearPublicGuidePresets_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ClearPublicGuidePresetsCommand.Execute(null);
        }

        public bool TryFocusPrimaryTarget()
        {
            return RemoteNavigationHelper.TryFocusElement(RefreshButton);
        }

        public bool TryHandleBackRequest()
        {
            return false;
        }
    }
}
