using Kroira.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kroira.App.Views
{
    public sealed partial class SourceOnboardingPage : Page
    {
        public SourceOnboardingViewModel ViewModel { get; }

        public SourceOnboardingPage()
        {
            this.InitializeComponent();
            ViewModel = ((App)Application.Current).Services.GetRequiredService<SourceOnboardingViewModel>();
        }
    }
}
