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
using Windows.Foundation;
using Windows.Media.Playback;

namespace Kroira.App.Views
{
    public sealed partial class EmbeddedPlaybackPage : Page
    {
        private PlaybackLaunchContext _launchContext;
        private MediaPlayer _mediaPlayer;
        private readonly IWindowManagerService _windowManager;
        private bool _isDisposed;

        public EmbeddedPlaybackPage()
        {
            this.InitializeComponent();

            _mediaPlayer = new MediaPlayer();
            PlayerElement.SetMediaPlayer(_mediaPlayer);

            _windowManager = ((App)Application.Current).Services.GetRequiredService<IWindowManagerService>();
            _windowManager.FullscreenStateChanged += OnFullscreenStateChanged;
            SyncBackButtonVisibility();

            this.Unloaded += OnPageUnloaded;
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try { SaveProgress(); } catch { }
            _windowManager.FullscreenStateChanged -= OnFullscreenStateChanged;

            try
            {
                if (_windowManager.IsFullscreen)
                {
                    _windowManager.ExitFullscreen();
                }
            }
            catch { }

            try
            {
                PlayerElement.SetMediaPlayer(null);
                _mediaPlayer.Pause();
                _mediaPlayer.Source = null;
                _mediaPlayer.Dispose();
            }
            catch { }
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
                    ctx.StartPositionMs = 0;
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
                    catch { }
                }

                url = ctx.StreamUrl;
                startMs = ctx.StartPositionMs;
            }

            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    var mediaSource = MediaSource.CreateFromUri(new Uri(url));
                    var playbackItem = new MediaPlaybackItem(mediaSource);
                    
                    _mediaPlayer.Source = playbackItem;
                    
                    if (startMs > 0)
                    {
                        // Use a local handler reference so we can detach it after firing
                        TypedEventHandler<MediaPlayer, object> handler = null;
                        handler = (s, args) =>
                        {
                            _mediaPlayer.MediaOpened -= handler;
                            if (_isDisposed) return;
                            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                            dispatcher?.TryEnqueue(() =>
                            {
                                if (!_isDisposed)
                                {
                                    try { _mediaPlayer.PlaybackSession.Position = TimeSpan.FromMilliseconds(startMs); } catch { }
                                }
                            });
                        };
                        _mediaPlayer.MediaOpened += handler;
                    }

                    _mediaPlayer.Play();
                }
                catch { }
            }
        }

        private void SaveProgress()
        {
            if (_launchContext == null || _launchContext.ContentId <= 0 || _mediaPlayer == null || _isDisposed)
                return;

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
            if (_windowManager.IsFullscreen)
            {
                _windowManager.ExitFullscreen();
            }

            if (this.Frame.CanGoBack)
            {
                this.Frame.GoBack();
            }
            else
            {
                // Fallback: if no back stack, navigate to Channels instead of leaving the user stuck
                this.Frame.Navigate(typeof(ChannelsPage));
            }
        }
    }
}

