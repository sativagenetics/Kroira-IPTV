using Kroira.App.Services.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using Kroira.App.Models;
using Kroira.App.Data;
using System.Linq;
using System;

namespace Kroira.App.Views
{
    public sealed partial class DevPlaybackPage : Page
    {
        private readonly IPlaybackEngine _engine;
        private PlaybackLaunchContext _launchContext;
        private string _pendingUrl;
        private long _pendingStartMs;
        private bool _isViewLoaded;

        public DevPlaybackPage()
        {
            this.InitializeComponent();
            _engine = ((App)Application.Current).Services.GetRequiredService<IPlaybackEngine>();
            
            _engine.StateChanged += Engine_StateChanged;

            // Defer MediaPlayer binding until the VideoView is in the visual tree with a valid HWND.
            // Without this, LibVLC opens an external window for rendering.
            PlayerView.Loaded += (s, e) =>
            {
                PlayerView.MediaPlayer = (LibVLCSharp.Shared.MediaPlayer)_engine.MediaPlayerInstance;
                _isViewLoaded = true;

                // If a play request arrived before the view was ready, fire it now
                if (!string.IsNullOrEmpty(_pendingUrl))
                {
                    _engine.Play(_pendingUrl, _pendingStartMs);
                    _pendingUrl = null;
                }
            };

            this.Unloaded += (s, e) =>
            {
                SaveProgress();
                _engine.Stop();
                _engine.StateChanged -= Engine_StateChanged;
            };
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
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
                if (_isViewLoaded)
                {
                    _engine.Play(url, startMs);
                }
                else
                {
                    _pendingUrl = url;
                    _pendingStartMs = startMs;
                }
            }
        }

        private void SaveProgress()
        {
            if (_launchContext != null && _launchContext.ContentId > 0 && _engine != null)
            {
                try
                {
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

                    if (_engine.LengthMs > 0)
                    {
                        prog.PositionMs = _engine.PositionMs;
                        prog.IsCompleted = _engine.PositionMs >= (_engine.LengthMs * 0.95);
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

        private void Engine_StateChanged(object sender, PlaybackState e)
        {
            StateText.Text = e.ToString();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (this.Frame.CanGoBack)
            {
                this.Frame.GoBack();
            }
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
