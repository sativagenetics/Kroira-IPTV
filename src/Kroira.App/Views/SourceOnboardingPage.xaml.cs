#nullable enable
using System;
using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Kroira.App.Views
{
    public sealed partial class SourceOnboardingPage : Page
    {
        public SourceOnboardingViewModel ViewModel { get; }

        public SourceOnboardingPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<SourceOnboardingViewModel>();
            ViewModel.SourceImportSucceeded += ViewModel_SourceImportSucceeded;
            Unloaded += SourceOnboardingPage_Unloaded;
        }

        private void SourceOnboardingPage_Unloaded(object sender, RoutedEventArgs e)
        {
            ViewModel.SourceImportSucceeded -= ViewModel_SourceImportSucceeded;
            Unloaded -= SourceOnboardingPage_Unloaded;
        }

        private void ViewModel_SourceImportSucceeded(object? sender, int sourceId)
        {
            Frame.Navigate(typeof(SourceListPage));
        }

        private void CopyValidationReport_Click(object sender, RoutedEventArgs e)
        {
            var report = ViewModel.GetSafeValidationReport();
            if (string.IsNullOrWhiteSpace(report))
            {
                return;
            }

            var package = new DataPackage();
            package.SetText(report);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
    }
}
