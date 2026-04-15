using Kroira.App.Models;
using Kroira.App.Data;
using Kroira.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Kroira.App.Views
{
    public sealed partial class EmbeddedPlaybackPage : Page
    {
        private PlaybackLaunchContext _launchContext;
        private MediaPlayer _mediaPlayer;
        private readonly IWindowManagerService _windowManager;

        public EmbeddedPlaybackPage()
        {
            this.InitializeComponent();

            _mediaPlayer = new MediaPlayer();
            PlayerElement.SetMediaPlayer(_mediaPlayer);

            _windowManager = ((App)Application.Current).Services.GetRequiredService<IWindowManagerService>();
            _windowManager.FullscreenStateChanged += OnFullscreenStateChanged;
            SyncBackButtonVisibility();

            this.Unloaded += (s, e) => 
            {
                SaveProgress();
                _mediaPlayer.Pause();
                _mediaPlayer.Source = null;
                _mediaPlayer.Dispose();
                _windowManager.FullscreenStateChanged -= OnFullscreenStateChanged;

                // Exit fullscreen when leaving the playback page so the shell is restored
                if (_windowManager.IsFullscreen)
                {
                    _windowManager.ExitFullscreen();
                }
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            string url = null;
            long startMs = 0;

            if (e.Parameter is string rawUrl && !string.IsNullOrWhiteSpace(rawUrl))
            {
                url = rawUrl;
            }
            else if (e.Parameter is PlaybackLaunchContext ctx)
            {
                _launchContext = ctx;

                if (ctx.ContentType == PlaybackContentType.Channel)
                {
                    ctx.StartPositionMs = 0; // Live channels strictly play natively without resume.
                }
                else if (ctx.StartPositionMs <= 0)
                {
                    try
                    {
                        using var scope = ((App)Application.Current).Services.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var prog = db.PlaybackProgresses.FirstOrDefault(p => p.ContentId == ctx.ContentId && p.ContentType == ctx.ContentType);
                        if (prog != null && !prog.IsCompleted && prog.PositionMs > 5000)
                        {
                            ctx.StartPositionMs = prog.PositionMs;
                        }
                    }
                    catch { } // Suppress DB errors falling back toward 0 ms easily.
                }

                url = ctx.StreamUrl;
                startMs = ctx.StartPositionMs;
            }

            if (!string.IsNullOrEmpty(url))
            {
                var mediaSource = MediaSource.CreateFromUri(new Uri(url));
                var playbackItem = new MediaPlaybackItem(mediaSource);
                
                _mediaPlayer.Source = playbackItem;
                
                if (startMs > 0)
                {
                    _mediaPlayer.MediaOpened += (s, args) =>
                    {
                        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                        dispatcher?.TryEnqueue(() => 
                        {
                            _mediaPlayer.PlaybackSession.Position = TimeSpan.FromMilliseconds(startMs);
                        });
                    };
                }

                _mediaPlayer.Play();
            }
        }

        private void SaveProgress()
        {
            if (_launchContext != null && _launchContext.ContentId > 0 && _mediaPlayer != null)
            {
                try
                {
                    var pos = _mediaPlayer.PlaybackSession.Position;
                    var dur = _mediaPlayer.PlaybackSession.NaturalDuration;
                    
                    var positionMs = pos.TotalMilliseconds;
                    var lengthMs = dur.TotalMilliseconds;

                    using var scope = ((App)Application.Current).Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var prog = db.PlaybackProgresses.FirstOrDefault(p => p.ContentId == _launchContext.ContentId && p.ContentType == _launchContext.ContentType);
                    if (prog == null)
                    {
                        prog = new PlaybackProgress
                        {
                            ContentId = _launchContext.ContentId,
                            ContentType = _launchContext.ContentType
                        };
                        db.PlaybackProgresses.Add(prog);
                    }

                    if (lengthMs > 0 && !double.IsNaN(lengthMs) && !double.IsInfinity(lengthMs))
                    {
                        prog.PositionMs = (long)positionMs;
                        prog.IsCompleted = positionMs >= (lengthMs * 0.95);
                    }
                    else
                    {
                        prog.PositionMs = 0;
                        prog.IsCompleted = false;
                    }

                    prog.LastWatched = DateTime.UtcNow;
                    db.SaveChanges();
                }
                catch { }
            }
        }

        private void DoubleTapOverlay_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            _windowManager.ToggleFullscreen();
            e.Handled = true;
        }

        private void OnFullscreenStateChanged(object sender, EventArgs e)
        {
            SyncBackButtonVisibility();
        }

        private void SyncBackButtonVisibility()
        {
            BackButton.Visibility = _windowManager.IsFullscreen
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            // If fullscreen, exit it first so the shell is visible before navigating
            if (_windowManager.IsFullscreen)
            {
                _windowManager.ExitFullscreen();
            }

            if (this.Frame.CanGoBack)
            {
                this.Frame.GoBack();
            }
        }
    }
}
