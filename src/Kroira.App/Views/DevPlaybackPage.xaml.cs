using Kroira.App.Services.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Views
{
    public sealed partial class DevPlaybackPage : Page
    {
        private readonly IPlaybackEngine _engine;

        public DevPlaybackPage()
        {
            this.InitializeComponent();
            _engine = ((App)Application.Current).Services.GetRequiredService<IPlaybackEngine>();
            
            // Connect native LibVLC render view strictly bound by isolation abstractions
            PlayerView.MediaPlayer = (LibVLCSharp.Shared.MediaPlayer)_engine.MediaPlayerInstance;
            
            _engine.StateChanged += Engine_StateChanged;

            // Unhook instance when navigating away
            this.Unloaded += (s, e) => 
            {
                _engine.Stop();
                _engine.StateChanged -= Engine_StateChanged;
            };
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is string url && !string.IsNullOrWhiteSpace(url))
            {
                _engine.Play(url);
            }
        }

        private void Engine_StateChanged(object sender, PlaybackState e)
        {
            StateText.Text = e.ToString();
        }

        private void PlayTest_Click(object sender, RoutedEventArgs e)
        {
            _engine.Play("http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4");
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            _engine.Pause();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _engine.Stop();
        }
    }
}
