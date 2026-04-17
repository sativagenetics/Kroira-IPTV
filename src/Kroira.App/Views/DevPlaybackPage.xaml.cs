using Kroira.App.Services.Playback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kroira.App.Views
{
    public sealed partial class DevPlaybackPage : Page
    {
        private readonly IPlaybackEngine _engine;

        public DevPlaybackPage()
        {
            this.InitializeComponent();
            _engine = ((App)Application.Current).Services.GetRequiredService<IPlaybackEngine>();
            _engine.StateChanged += (s, e) => StateText.Text = e.ToString();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }

        private void Pause_Click(object sender, RoutedEventArgs e) => _engine.Pause();

        private void Stop_Click(object sender, RoutedEventArgs e) => _engine.Stop();
    }
}
