using System;
using Kroira.App.Services;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kroira.App.Views
{
    public sealed partial class ProfilePage : Page
    {
        public ProfileViewModel ViewModel { get; }

        public ProfilePage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Microsoft.UI.Xaml.Application.Current).Services.GetRequiredService<ProfileViewModel>();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadCommand.ExecuteAsync(null);
        }

        private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.CanDeleteSelectedProfile)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = LocalizedStrings.Get("Profile.DeleteConfirmTitle"),
                Content = LocalizedStrings.Get("Profile.DeleteConfirmMessage"),
                PrimaryButtonText = LocalizedStrings.Get("General.Delete"),
                CloseButtonText = LocalizedStrings.Get("General.Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            await ViewModel.DeleteSelectedProfileCommand.ExecuteAsync(null);
        }
    }
}
