using System;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.Services.Playback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using WinRT.Interop;

namespace Kroira.App.Views
{
    // Self-contained, page-scoped playback host. Each navigation constructs a fresh
    // page; OnNavigatedTo creates the mpv handle + child HWND, OnNavigatedFrom
    // synchronously tears them down. There is no app-lifetime playback singleton.
    public sealed partial class EmbeddedPlaybackPage : Page
    {
        private static readonly TimeSpan ControlsHideDelay = TimeSpan.FromMilliseconds(1350);

        private PlaybackLaunchContext _context;
        private MpvPlayer _player;
        private VideoSurface _surface;
        private IWindowManagerService _windowManager;
        private DispatcherTimer _controlsHideTimer;
        private bool _isUserSeeking;
        private bool _suppressSliderUpdates;
        private bool _controlsVisible = true;
        private bool _wasFullscreenOnEnter;
        private long _lastPositionMs;
        private bool _playbackStarted;
        private bool _teardownStarted;
        private bool _progressSaveQueued;
        private bool _isNavigatingBack;

        public EmbeddedPlaybackPage()
        {
            this.InitializeComponent();
            this.Loaded += OnPageLoaded;
            this.Unloaded += OnPageUnloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _context = e.Parameter as PlaybackLaunchContext;
            if (_context == null)
            {
                ShowError("Missing playback context.");
                return;
            }

            TitleText.Text = TitleForContext(_context);
            _windowManager = ((App)Application.Current).Services.GetRequiredService<IWindowManagerService>();
            _wasFullscreenOnEnter = _windowManager.IsFullscreen;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            TeardownPlayback();
            base.OnNavigatedFrom(e);

            // Leave the window in whatever fullscreen state the user had before entering.
            if (_windowManager != null && _windowManager.IsFullscreen && !_wasFullscreenOnEnter)
            {
                _windowManager.ExitFullscreen();
            }
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            if (_context == null) return;
            TryStartPlayback();
        }

        private void VideoHost_StartSizeChanged(object sender, SizeChangedEventArgs e)
        {
            TryStartPlayback();
        }

        private void TryStartPlayback()
        {
            if (_playbackStarted || _teardownStarted || _context == null) return;

            if (VideoHost.XamlRoot == null || VideoHost.ActualWidth <= 0 || VideoHost.ActualHeight <= 0)
            {
                VideoHost.SizeChanged -= VideoHost_StartSizeChanged;
                VideoHost.SizeChanged += VideoHost_StartSizeChanged;
                return;
            }

            VideoHost.SizeChanged -= VideoHost_StartSizeChanged;
            _playbackStarted = true;

            try
            {
                var parentHwnd = WindowNative.GetWindowHandle(((App)Application.Current).MainWindow);
                _surface = new VideoSurface(parentHwnd, VideoHost,
                    onClick: OnVideoClick,
                    onDoubleClick: OnVideoDoubleClick,
                    onMouseMoved: OnVideoMouseMoved);

                // Ensure the HWND is sized to the host rectangle before handing it to mpv.
                _surface.UpdatePlacement(force: true);

                _player = new MpvPlayer(DispatcherQueue.GetForCurrentThread(), _surface.Handle);
                _player.PositionChanged += OnPositionChanged;
                _player.DurationChanged += OnDurationChanged;
                _player.PauseChanged += OnPauseChanged;
                _player.SeekableChanged += OnSeekableChanged;
                _player.PlaybackEnded += OnPlaybackEnded;
                _player.FileLoaded += OnFileLoaded;
                _player.OutputReady += OnOutputReady;

                _controlsHideTimer = new DispatcherTimer { Interval = ControlsHideDelay };
                _controlsHideTimer.Tick += (_, __) => HideControls();

                _player.Play(_context.StreamUrl, _context.StartPositionMs);
                RestartControlsHideTimer();
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                TeardownPlayback();
            }
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            // Fallback guard: if the frame navigated away without calling OnNavigatedFrom
            // (e.g. window close), still tear everything down synchronously.
            TeardownPlayback();
        }

        private void TeardownPlayback()
        {
            if (_teardownStarted && _player == null && _surface == null) return;
            _teardownStarted = true;
            VideoHost.SizeChanged -= VideoHost_StartSizeChanged;

            if (_controlsHideTimer != null)
            {
                _controlsHideTimer.Stop();
                _controlsHideTimer = null;
            }

            // Save VOD progress before unloading the player so we still have a valid
            // _lastPositionMs to persist.
            if (!_progressSaveQueued && _context != null && _context.ContentType != PlaybackContentType.Channel)
            {
                _progressSaveQueued = true;
                _ = SaveProgressAsync(_context, _lastPositionMs);
            }

            if (_player != null)
            {
                var player = _player;
                _player = null;
                player.PositionChanged -= OnPositionChanged;
                player.DurationChanged -= OnDurationChanged;
                player.PauseChanged -= OnPauseChanged;
                player.SeekableChanged -= OnSeekableChanged;
                player.PlaybackEnded -= OnPlaybackEnded;
                player.FileLoaded -= OnFileLoaded;
                player.OutputReady -= OnOutputReady;
                try { player.Dispose(); } catch { }
            }

            if (_surface != null)
            {
                var surface = _surface;
                _surface = null;
                try { surface.Dispose(); } catch { }
            }
        }

        // --- Player event handlers (already on UI thread via MpvPlayer dispatcher) ---

        private void OnPositionChanged(TimeSpan position)
        {
            if (_teardownStarted) return;
            _lastPositionMs = (long)position.TotalMilliseconds;
            PositionText.Text = FormatTime(position);

            if (_isUserSeeking || _player == null) return;
            if (_player.Duration.TotalSeconds > 0)
            {
                _suppressSliderUpdates = true;
                TimelineSlider.Value = Math.Clamp(
                    position.TotalSeconds / _player.Duration.TotalSeconds * 1000.0,
                    0, 1000);
                _suppressSliderUpdates = false;
            }
        }

        private void OnDurationChanged(TimeSpan duration)
        {
            if (_teardownStarted) return;
            DurationText.Text = FormatTime(duration);
        }

        private void OnPauseChanged(bool isPaused)
        {
            if (_teardownStarted) return;
            PlayPauseIcon.Glyph = isPaused ? "\uE768" /* Play */ : "\uE769" /* Pause */;
            if (isPaused) ShowControls(persist: true);
            else RestartControlsHideTimer();
        }

        private void OnSeekableChanged(bool seekable)
        {
            if (_teardownStarted) return;
            TimelineSlider.IsEnabled = seekable;
            LivePill.Visibility = seekable ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnFileLoaded()
        {
            if (_teardownStarted) return;
            // Once the first frame is on-screen, repositioning the HWND resolves the
            // rare case where mpv's output size lags layout.
            _surface?.UpdatePlacement(force: true);
        }

        private void OnOutputReady()
        {
            if (_teardownStarted) return;
            _surface?.UpdatePlacement(force: true);
        }

        private void OnPlaybackEnded()
        {
            if (_teardownStarted) return;
            // Mark VOD as completed (by saving near-duration) and navigate back.
            if (_context != null && _context.ContentType != PlaybackContentType.Channel && _player != null)
            {
                var finalMs = (long)_player.Duration.TotalMilliseconds;
                if (finalMs > 0) _lastPositionMs = finalMs;
            }

            NavigateBack();
        }

        private void NavigateBack()
        {
            if (_isNavigatingBack) return;
            _isNavigatingBack = true;

            TeardownPlayback();
            if (Frame != null && Frame.CanGoBack) Frame.GoBack();
        }

        // --- UI handlers ---

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            NavigateBack();
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_teardownStarted) return;
            _player?.TogglePause();
            RestartControlsHideTimer();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_teardownStarted) return;
            _player?.Stop();
            NavigateBack();
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (_teardownStarted) return;
            _windowManager?.ToggleFullscreen();
            RestartControlsHideTimer();
        }

        private void Page_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;
            ShowControls();
            RestartControlsHideTimer();
        }

        private void OnVideoClick()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_teardownStarted) return;
                _player?.TogglePause();
                ShowControls();
                RestartControlsHideTimer();
            });
        }

        private void OnVideoDoubleClick()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_teardownStarted) return;
                _windowManager?.ToggleFullscreen();
                RestartControlsHideTimer();
            });
        }

        private void OnVideoMouseMoved()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_teardownStarted) return;
                ShowControls();
                RestartControlsHideTimer();
            });
        }

        private void TimelineSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;
            _isUserSeeking = true;
        }

        private void TimelineSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;
            if (!_isUserSeeking) return;
            _isUserSeeking = false;

            if (_player == null || !_player.IsSeekable) return;
            if (_suppressSliderUpdates) return;

            var durationSeconds = _player.Duration.TotalSeconds;
            if (durationSeconds <= 0) return;
            var fraction = TimelineSlider.Value / 1000.0;
            _player.SeekAbsoluteSeconds(fraction * durationSeconds);
            RestartControlsHideTimer();
        }

        // --- Controls auto-hide ---

        private void ShowControls(bool persist = false)
        {
            if (!_controlsVisible)
            {
                TopBar.Visibility = Visibility.Visible;
                BottomBar.Visibility = Visibility.Visible;
                _controlsVisible = true;
                // Changing row heights will cause VideoHost to resize; the surface
                // listens for SizeChanged/LayoutUpdated and repositions the HWND.
            }

            if (persist)
            {
                _controlsHideTimer?.Stop();
            }
        }

        private void HideControls()
        {
            if (_player != null && _player.IsPaused) return;
            if (ErrorOverlay.Visibility == Visibility.Visible) return;

            TopBar.Visibility = Visibility.Collapsed;
            BottomBar.Visibility = Visibility.Collapsed;
            _controlsVisible = false;
            _controlsHideTimer?.Stop();
        }

        private void RestartControlsHideTimer()
        {
            if (_controlsHideTimer == null) return;
            _controlsHideTimer.Stop();
            if (_player != null && _player.IsPaused) return;
            if (ErrorOverlay.Visibility == Visibility.Visible) return;
            _controlsHideTimer.Start();
        }

        // --- Helpers ---

        private void ShowError(string message)
        {
            ErrorMessage.Text = string.IsNullOrWhiteSpace(message) ? "Unable to start playback." : message;
            ErrorOverlay.Visibility = Visibility.Visible;
        }

        private static string FormatTime(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1
                ? t.ToString(@"h\:mm\:ss")
                : t.ToString(@"m\:ss");
        }

        private static string TitleForContext(PlaybackLaunchContext ctx)
        {
            return ctx.ContentType switch
            {
                PlaybackContentType.Channel => "Live",
                PlaybackContentType.Movie => "Movie",
                PlaybackContentType.Episode => "Episode",
                _ => string.Empty,
            };
        }

        private static async Task SaveProgressAsync(PlaybackLaunchContext ctx, long positionMs)
        {
            if (ctx == null) return;
            // Only bother persisting progress if the user got past the first few seconds.
            if (positionMs < 5_000) return;

            try
            {
                using var scope = ((App)Application.Current).Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var existing = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                    db.PlaybackProgresses,
                    p => p.ContentType == ctx.ContentType && p.ContentId == ctx.ContentId);

                if (existing == null)
                {
                    db.PlaybackProgresses.Add(new PlaybackProgress
                    {
                        ContentType = ctx.ContentType,
                        ContentId = ctx.ContentId,
                        PositionMs = positionMs,
                        IsCompleted = false,
                        LastWatched = DateTime.UtcNow,
                    });
                }
                else
                {
                    existing.PositionMs = positionMs;
                    existing.LastWatched = DateTime.UtcNow;
                }
                await db.SaveChangesAsync();
            }
            catch
            {
                // Progress persistence is best-effort.
            }
        }
    }
}
