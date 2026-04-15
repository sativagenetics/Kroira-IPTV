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
                SaveProgress();
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
            else if (e.Parameter is PlaybackLaunchContext ctx)
            {
                _launchContext = ctx;

                if (ctx.ContentType == PlaybackContentType.Channel)
                {
                    ctx.StartPositionMs = 0; // Live channels strictly play natively.
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
                    catch { } // Logically suppress lookup errors falling back towards zero seamlessly.
                }

                _engine.Play(ctx.StreamUrl, ctx.StartPositionMs);
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
