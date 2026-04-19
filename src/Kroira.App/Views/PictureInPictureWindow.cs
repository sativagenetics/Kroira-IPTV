using Kroira.App.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kroira.App.Views
{
    public sealed class PictureInPictureWindow : Window
    {
        private readonly Frame _contentFrame = new();

        public PictureInPictureWindow()
        {
            Title = "Kroira PiP";
            Content = _contentFrame;
        }

        public bool NavigateToPlayback(PlaybackLaunchContext context)
        {
            return _contentFrame.Navigate(typeof(EmbeddedPlaybackPage), context);
        }
    }
}
