using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kroira.App.Views
{
    public sealed class PictureInPictureWindow : Window
    {
        private readonly Frame _contentFrame = new();

        public PictureInPictureWindow()
        {
            Title = LocalizedStrings.Get("Player.PipWindowTitle");
            Content = _contentFrame;
        }

        public bool NavigateToPlayback(PlaybackLaunchContext context)
        {
            return _contentFrame.Navigate(typeof(EmbeddedPlaybackPage), context);
        }
    }
}
