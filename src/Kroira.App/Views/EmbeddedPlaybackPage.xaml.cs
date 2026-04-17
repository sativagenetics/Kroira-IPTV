using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.Services.Playback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace Kroira.App.Views
{
    public sealed partial class EmbeddedPlaybackPage : Page
    {
        private readonly MpvPlaybackEngine _engine;
        private readonly IWindowManagerService _windowManager;
        private readonly DispatcherTimer _positionTimer;
        private readonly DispatcherTimer _layoutStabilizationTimer;
        private readonly DispatcherTimer _chromeAutoHideTimer;
        private PlaybackLaunchContext _launchContext;
        private string _pendingUrl;
        private long _pendingStartMs;
        private IntPtr _videoHostHwnd;
        private IntPtr _videoHostOriginalWndProc;
        private readonly WndProcDelegate _videoHostWndProc;
        private bool _isHostHandleAssigned;
        private bool _isDisposed;
        private bool _isViewLoaded;
        private bool _isUserSeeking;
        private bool _isUpdatingSlider;
        private PlaybackState _lastKnownState = PlaybackState.Idle;
        private bool _isRefreshingTracks;
        private bool _tracksLoaded;
        private int _trackRefreshAttempts;
        private int _layoutStabilizationTicks;
        private DateTime _lastProgressSaveUtc = DateTime.MinValue;

        public EmbeddedPlaybackPage()
        {
            this.InitializeComponent();

            var services = ((App)Application.Current).Services;
            _engine = services.GetRequiredService<MpvPlaybackEngine>();
            _windowManager = services.GetRequiredService<IWindowManagerService>();
            _videoHostWndProc = VideoHostWndProc;
            Log("page created");

            _engine.StateChanged += Engine_StateChanged;
            _engine.ErrorOccurred += Engine_ErrorOccurred;
            _windowManager.FullscreenStateChanged += OnFullscreenStateChanged;

            this.Unloaded += OnPageUnloaded;

            _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _positionTimer.Tick += PositionTimer_Tick;

            _layoutStabilizationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _layoutStabilizationTimer.Tick += LayoutStabilizationTimer_Tick;

            _chromeAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _chromeAutoHideTimer.Tick += ChromeAutoHideTimer_Tick;

            SyncFullscreenChrome();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Log("page entered");

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
                        var prog = db.PlaybackProgresses.FirstOrDefault(p =>
                            p.ContentId == ctx.ContentId && p.ContentType == ctx.ContentType);
                        if (prog != null && !prog.IsCompleted && prog.PositionMs > 5000)
                            ctx.StartPositionMs = prog.PositionMs;
                    }
                    catch { }
                }

                url = ctx.StreamUrl;
                startMs = ctx.StartPositionMs;
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                _pendingUrl = url;
                _pendingStartMs = startMs;
                StartPlaybackWhenReady();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Log("page navigated from");
            FullTeardown();
            base.OnNavigatedFrom(e);
        }

        private void VideoHost_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureVideoHostWindow();
            _isViewLoaded = true;
            StartPlaybackWhenReady();
        }

        private void VideoHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateVideoHostBounds();
        }

        private void StartPlaybackWhenReady()
        {
            if (_isDisposed || !_isViewLoaded || string.IsNullOrWhiteSpace(_pendingUrl)) return;

            _tracksLoaded = false;
            _trackRefreshAttempts = 0;
            RefreshTrackControls(Array.Empty<PlaybackTrack>(), Array.Empty<PlaybackTrack>());
            ShowStatus("Loading stream...", true);

            try
            {
                EnsureVideoHostWindow();
                if (_videoHostHwnd == IntPtr.Zero)
                {
                    ShowStatus("Unable to create embedded video surface.", false);
                    return;
                }

                EnsureEngineHostBinding();
                if (!_isHostHandleAssigned)
                {
                    ShowStatus("Embedded video host is not bound.", false);
                    return;
                }

                ShowVideoHostWindow(true);
                UpdateVideoHostBounds();
                StartLayoutStabilization();
                Log("play requested");
                _engine.Play(_pendingUrl, _pendingStartMs);
                ShowChrome();
                _positionTimer.Start();
                _pendingUrl = null;
                _pendingStartMs = 0;
            }
            catch (Exception ex)
            {
                ShowStatus($"Unable to start playback: {ex.Message}", false);
            }
        }

        // Single idempotent teardown. All exit paths (Back, Stop, page Unloaded) call this.
        private void FullTeardown()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Log("teardown started");

            _positionTimer.Stop();
            _layoutStabilizationTimer.Stop();
            _chromeAutoHideTimer.Stop();
            _pendingUrl = null;
            _pendingStartMs = 0;

            this.Unloaded -= OnPageUnloaded;
            _engine.StateChanged -= Engine_StateChanged;
            _engine.ErrorOccurred -= Engine_ErrorOccurred;
            _windowManager.FullscreenStateChanged -= OnFullscreenStateChanged;

            try { SaveProgress(force: true); } catch { }

            try
            {
                if (_windowManager.IsFullscreen)
                    _windowManager.ExitFullscreen();
            }
            catch { }

            try { _engine.Stop(); } catch { }

            ShowVideoHostWindow(false);

            if (_isHostHandleAssigned)
            {
                try { _engine.SetVideoHostHandle(IntPtr.Zero); } catch { }
                _isHostHandleAssigned = false;
                Log("engine host handle cleared");
            }

            try { _engine.DetachAndDispose(() => { }); } catch { }
            try { _engine.Dispose(); } catch { }
            DestroyVideoHostWindow();
            Log("teardown completed");
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            FullTeardown();
        }

        private void PositionTimer_Tick(object sender, object e)
        {
            if (_isDisposed) return;

            UpdatePositionUi();

            if (!_tracksLoaded && (_engine.State == PlaybackState.Playing || _engine.LengthMs > 0))
            {
                var audioTracks = _engine.GetAudioTracks();
                var subtitleTracks = _engine.GetSubtitleTracks();
                RefreshTrackControls(audioTracks, subtitleTracks);
                _trackRefreshAttempts++;
                _tracksLoaded = audioTracks.Count > 0 || subtitleTracks.Count > 0 || _trackRefreshAttempts >= 16;
            }

            SaveProgress(force: false);
        }

        private void LayoutStabilizationTimer_Tick(object sender, object e)
        {
            _layoutStabilizationTicks++;
            UpdateVideoHostBounds();

            if (_layoutStabilizationTicks >= 12)
                _layoutStabilizationTimer.Stop();
        }

        private void UpdatePositionUi()
        {
            var position = Math.Max(0, _engine.PositionMs);
            var length = Math.Max(0, _engine.LengthMs);
            var hasDuration = length > 0;
            var isSeekable = _engine.IsSeekable;

            PositionText.Text = FormatTime(position);
            DurationText.Text = hasDuration ? FormatTime(length) : "Live";
            PlaybackInfoText.Text = _engine.State == PlaybackState.Error
                ? "Playback error"
                : hasDuration
                    ? $"{FormatTime(position)} / {FormatTime(length)}"
                    : "Live stream";

            SeekSlider.IsEnabled = hasDuration && isSeekable;
            SkipBackButton.IsEnabled = isSeekable;
            SkipForwardButton.IsEnabled = isSeekable;

            if (!_isUserSeeking)
            {
                _isUpdatingSlider = true;
                SeekSlider.Maximum = hasDuration ? length : 1;
                SeekSlider.Value = hasDuration ? Math.Min(position, length) : 0;
                _isUpdatingSlider = false;
            }
        }

        private void RefreshTrackControls(IReadOnlyList<PlaybackTrack> audioTracks, IReadOnlyList<PlaybackTrack> subtitleTracks)
        {
            _isRefreshingTracks = true;

            AudioTrackComboBox.ItemsSource = audioTracks;
            AudioTrackComboBox.IsEnabled = audioTracks.Count > 0;
            AudioTrackComboBox.SelectedItem = audioTracks.FirstOrDefault(track => track.Id == _engine.CurrentAudioTrackId);

            SubtitleTrackComboBox.ItemsSource = subtitleTracks;
            SubtitleTrackComboBox.IsEnabled = subtitleTracks.Count > 0;
            SubtitleTrackComboBox.SelectedItem = subtitleTracks.FirstOrDefault(track => track.Id == _engine.CurrentSubtitleTrackId);

            _isRefreshingTracks = false;
        }

        private void Engine_StateChanged(object sender, PlaybackState state)
        {
            if (_isDisposed) return;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;

                var wasPlaying = _lastKnownState == PlaybackState.Playing;
                _lastKnownState = state;

                PlayPauseButton.Content = state == PlaybackState.Playing ? "Pause" : "Play";

                switch (state)
                {
                    case PlaybackState.Loading:
                        ShowVideoHostWindow(true);
                        ShowStatus("Loading stream...", true);
                        break;
                    case PlaybackState.Playing:
                        ShowVideoHostWindow(true);
                        UpdateVideoHostBounds();
                        StartLayoutStabilization();
                        ShowStatus(null, false);
                        // Only reset the auto-hide timer on a genuine new-play transition.
                        // MpvEventPlaybackRestart fires repeatedly on live-stream buffer
                        // recovery, causing Playing→Playing transitions that would otherwise
                        // reset the timer and prevent chrome from ever hiding.
                        if (!wasPlaying)
                            RestartChromeAutoHide();
                        _positionTimer.Start();
                        _tracksLoaded = false;
                        _trackRefreshAttempts = 0;
                        break;
                    case PlaybackState.Paused:
                        ShowStatus(null, false);
                        break;
                    case PlaybackState.Stopped:
                        ShowStatus("Playback stopped", false);
                        break;
                    case PlaybackState.Error:
                        ShowVideoHostWindow(false);
                        ShowStatus("This stream could not be played. Try another track or source.", false);
                        break;
                }
            });
        }

        private void Engine_ErrorOccurred(object sender, string message)
        {
            if (_isDisposed) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isDisposed)
                    ShowStatus(message, false);
            });
        }

        private void ShowStatus(string message, bool isLoading)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                StatusOverlay.Visibility = Visibility.Collapsed;
                LoadingRing.IsActive = false;
                return;
            }

            StatusText.Text = message;
            LoadingRing.IsActive = isLoading;
            LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            StatusOverlay.Visibility = Visibility.Visible;
        }

        private void ShowChrome()
        {
            TopChrome.Opacity = 1;
            BottomChrome.Opacity = 1;
            TopChrome.IsHitTestVisible = true;
            BottomChrome.IsHitTestVisible = true;
        }

        private void HideChrome()
        {
            if (_engine.State != PlaybackState.Playing || StatusOverlay.Visibility == Visibility.Visible)
                return;

            TopChrome.Opacity = 0;
            BottomChrome.Opacity = 0;
            TopChrome.IsHitTestVisible = false;
            BottomChrome.IsHitTestVisible = false;
        }

        private void RestartChromeAutoHide()
        {
            ShowChrome();
            _chromeAutoHideTimer.Stop();

            if (_engine.State == PlaybackState.Playing)
                _chromeAutoHideTimer.Start();
        }

        private void ChromeAutoHideTimer_Tick(object sender, object e)
        {
            _chromeAutoHideTimer.Stop();
            HideChrome();
        }

        private void EnsureVideoHostWindow()
        {
            if (_isDisposed)
                return;

            if (_videoHostHwnd != IntPtr.Zero)
            {
                EnsureEngineHostBinding();
                UpdateVideoHostBounds();
                return;
            }

            var parentHwnd = WinRT.Interop.WindowNative.GetWindowHandle(((App)Application.Current).MainWindow);
            _videoHostHwnd = CreateWindowEx(
                0,
                "STATIC",
                string.Empty,
                WindowStyles.WS_CHILD | WindowStyles.WS_VISIBLE | WindowStyles.WS_CLIPSIBLINGS | WindowStyles.WS_CLIPCHILDREN,
                0, 0, 1, 1,
                parentHwnd,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            if (_videoHostHwnd == IntPtr.Zero)
            {
                ShowStatus("Unable to create embedded video surface.", false);
                return;
            }

            Log($"host created; hwnd={_videoHostHwnd}");

            _videoHostOriginalWndProc = SetWindowLongPtr(
                _videoHostHwnd,
                WindowLongIndex.GWLP_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(_videoHostWndProc));
            EnsureEngineHostBinding();
            UpdateVideoHostBounds();
            ShowVideoHostWindow(true);
        }

        private void EnsureEngineHostBinding()
        {
            if (_isDisposed || _isHostHandleAssigned || _videoHostHwnd == IntPtr.Zero)
                return;

            _engine.SetVideoHostHandle(_videoHostHwnd);
            _isHostHandleAssigned = true;
            Log("engine host handle assigned");
        }

        private void UpdateVideoHostBounds()
        {
            if (_videoHostHwnd == IntPtr.Zero || VideoHost.XamlRoot == null || ActualWidth <= 0 || ActualHeight <= 0)
                return;

            var scale = VideoHost.XamlRoot.RasterizationScale;
            var origin = VideoHost.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
            var x = (int)Math.Round(origin.X * scale);
            var y = (int)Math.Round(origin.Y * scale);
            var width = Math.Max(1, (int)Math.Round(VideoHost.ActualWidth * scale));
            var height = Math.Max(1, (int)Math.Round(VideoHost.ActualHeight * scale));

            SetWindowPos(
                _videoHostHwnd,
                HwndTop,
                x, y, width, height,
                SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_SHOWWINDOW);
        }

        private void ShowVideoHostWindow(bool show)
        {
            if (_videoHostHwnd != IntPtr.Zero)
                ShowWindow(_videoHostHwnd, show ? ShowWindowCommand.SW_SHOWNA : ShowWindowCommand.SW_HIDE);
        }

        private void DestroyVideoHostWindow()
        {
            if (_videoHostHwnd == IntPtr.Zero) return;

            try
            {
                RestoreWindowProc(_videoHostHwnd, ref _videoHostOriginalWndProc);
                DestroyWindow(_videoHostHwnd);
                Log("host destroyed");
            }
            catch { }
            finally
            {
                _videoHostHwnd = IntPtr.Zero;
            }
        }

        private void StartLayoutStabilization()
        {
            _layoutStabilizationTicks = 0;
            _layoutStabilizationTimer.Stop();
            _layoutStabilizationTimer.Start();
        }

        private IntPtr VideoHostWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            HandleNativeVideoMouseMessage(msg);
            return CallWindowProc(_videoHostOriginalWndProc, hwnd, msg, wParam, lParam);
        }

        private void HandleNativeVideoMouseMessage(uint message)
        {
            if (message == NativeMessages.WM_MOUSEMOVE || message == NativeMessages.WM_LBUTTONDOWN)
                DispatcherQueue.TryEnqueue(RestartChromeAutoHide);
            else if (message == NativeMessages.WM_LBUTTONDBLCLK)
                DispatcherQueue.TryEnqueue(() => _windowManager.ToggleFullscreen());
        }

        private static void RestoreWindowProc(IntPtr hwnd, ref IntPtr originalWndProc)
        {
            if (hwnd != IntPtr.Zero && originalWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(hwnd, WindowLongIndex.GWLP_WNDPROC, originalWndProc);
                originalWndProc = IntPtr.Zero;
            }
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_engine.IsPlaying)
                _engine.Pause();
            else
                _engine.Resume();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            ExitPlaybackPage();
        }

        private void SkipBack_Click(object sender, RoutedEventArgs e)
        {
            SeekBy(-10000);
        }

        private void SkipForward_Click(object sender, RoutedEventArgs e)
        {
            SeekBy(30000);
        }

        private void SeekBy(long deltaMs)
        {
            if (!_engine.IsSeekable)
            {
                ShowStatus("This stream does not support seeking.", false);
                return;
            }

            _engine.SeekBy(deltaMs);
            _engine.Resume();
            UpdatePositionUi();
        }

        private void SeekSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isUserSeeking = true;
        }

        private void SeekSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            CommitSeekFromSlider();
        }

        private void SeekSlider_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            CommitSeekFromSlider();
        }

        private void SeekSlider_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUserSeeking)
                CommitSeekFromSlider();
        }

        private void CommitSeekFromSlider()
        {
            if (_isUpdatingSlider) return;

            _isUserSeeking = false;

            if (!_engine.IsSeekable || _engine.LengthMs <= 0)
            {
                ShowStatus("This stream does not support seeking.", false);
                return;
            }

            _engine.SeekTo((long)SeekSlider.Value);
            _engine.Resume();
            UpdatePositionUi();
        }

        private void SpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_engine == null) return;

            if (SpeedComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string value &&
                float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
            {
                if (!_engine.SetPlaybackRate(rate))
                    ShowStatus("Playback speed could not be changed for this stream.", false);
            }
        }

        private void AudioTrackComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingTracks || AudioTrackComboBox.SelectedItem is not PlaybackTrack track) return;

            if (!_engine.SetAudioTrack(track.Id))
                ShowStatus("Audio track could not be changed for this stream.", false);
        }

        private void SubtitleTrackComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingTracks || SubtitleTrackComboBox.SelectedItem is not PlaybackTrack track) return;

            if (!_engine.SetSubtitleTrack(track.Id))
                ShowStatus("Subtitle track could not be changed for this stream.", false);
        }

        private async void LoadSubtitles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".srt");
                picker.FileTypeFilter.Add(".ass");
                picker.FileTypeFilter.Add(".ssa");
                picker.FileTypeFilter.Add(".vtt");
                picker.FileTypeFilter.Add(".sub");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(((App)Application.Current).MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                if (_engine.AddSubtitleFile(file.Path))
                {
                    RefreshTrackControls(_engine.GetAudioTracks(), _engine.GetSubtitleTracks());
                    ShowStatus("Subtitle file loaded.", false);
                }
                else
                {
                    ShowStatus("Subtitle file could not be loaded.", false);
                }
            }
            catch (Exception ex)
            {
                ShowStatus($"Subtitle load failed: {ex.Message}", false);
            }
        }

        private void PlayerRoot_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            RestartChromeAutoHide();
        }

        private void PlayerRoot_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            _windowManager.ToggleFullscreen();
            e.Handled = true;
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            _windowManager.ToggleFullscreen();
        }

        private void OnFullscreenStateChanged(object sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(SyncFullscreenChrome);
        }

        private void SyncFullscreenChrome()
        {
            BackButton.Visibility = _windowManager.IsFullscreen ? Visibility.Collapsed : Visibility.Visible;
            ShowChrome();
            UpdateVideoHostBounds();
            StartLayoutStabilization();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            ExitPlaybackPage();
        }

        private void ExitPlaybackPage()
        {
            FullTeardown();

            if (Frame.CanGoBack)
                Frame.GoBack();
            else
                Frame.Navigate(typeof(ChannelsPage));
        }

        private static void Log(string message)
        {
            Debug.WriteLine($"[Playback:page] {message}");
        }

        private void SaveProgress(bool force)
        {
            if (_launchContext == null || _launchContext.ContentId <= 0) return;

            if (!force && (DateTime.UtcNow - _lastProgressSaveUtc).TotalSeconds < 10) return;

            try
            {
                var positionMs = _engine.PositionMs;
                var lengthMs = _engine.LengthMs;

                using var scope = ((App)Application.Current).Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var progress = db.PlaybackProgresses.FirstOrDefault(p =>
                    p.ContentId == _launchContext.ContentId && p.ContentType == _launchContext.ContentType);
                if (progress == null)
                {
                    progress = new PlaybackProgress
                    {
                        ContentId = _launchContext.ContentId,
                        ContentType = _launchContext.ContentType
                    };
                    db.PlaybackProgresses.Add(progress);
                }

                if (_launchContext.ContentType == PlaybackContentType.Channel || lengthMs <= 0)
                {
                    progress.PositionMs = 0;
                    progress.IsCompleted = false;
                }
                else
                {
                    progress.PositionMs = Math.Max(0, positionMs);
                    progress.IsCompleted = positionMs >= lengthMs * 0.95;
                }

                progress.LastWatched = DateTime.UtcNow;
                db.SaveChanges();
                _lastProgressSaveUtc = DateTime.UtcNow;
            }
            catch { }
        }

        private static string FormatTime(long milliseconds)
        {
            var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
            return time.TotalHours >= 1
                ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
                : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        }

        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName, WindowStyles dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, SetWindowPosFlags uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommand nCmdShow);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, WindowLongIndex nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private static readonly IntPtr HwndTop = IntPtr.Zero;

        [Flags]
        private enum WindowStyles : uint
        {
            WS_CHILD = 0x40000000,
            WS_VISIBLE = 0x10000000,
            WS_CLIPCHILDREN = 0x02000000,
            WS_CLIPSIBLINGS = 0x04000000
        }

        private enum WindowLongIndex { GWLP_WNDPROC = -4 }

        private static class NativeMessages
        {
            public const uint WM_MOUSEMOVE = 0x0200;
            public const uint WM_LBUTTONDOWN = 0x0201;
            public const uint WM_LBUTTONDBLCLK = 0x0203;
        }

        [Flags]
        private enum SetWindowPosFlags : uint
        {
            SWP_NOZORDER = 0x0004,
            SWP_NOACTIVATE = 0x0010,
            SWP_SHOWWINDOW = 0x0040
        }

        private enum ShowWindowCommand { SW_HIDE = 0, SW_SHOWNA = 8 }
    }
}
