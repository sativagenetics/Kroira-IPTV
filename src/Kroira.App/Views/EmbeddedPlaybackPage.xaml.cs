using System;
using System.Collections.Generic;
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
        private readonly IPlaybackEngine _engine;
        private readonly IWindowManagerService _windowManager;
        private readonly DispatcherTimer _positionTimer;
        private readonly DispatcherTimer _voutAdoptionTimer;
        private PlaybackLaunchContext _launchContext;
        private string _pendingUrl;
        private long _pendingStartMs;
        private IntPtr _videoHostHwnd;
        private IntPtr _adoptedVoutHwnd;
        private bool _isDisposed;
        private bool _isViewLoaded;
        private bool _isUserSeeking;
        private bool _isUpdatingSlider;
        private bool _isRefreshingTracks;
        private bool _tracksLoaded;
        private int _trackRefreshAttempts;
        private int _voutAdoptionAttempts;
        private DateTime _lastProgressSaveUtc = DateTime.MinValue;

        public EmbeddedPlaybackPage()
        {
            this.InitializeComponent();

            var services = ((App)Application.Current).Services;
            _engine = services.GetRequiredService<IPlaybackEngine>();
            _windowManager = services.GetRequiredService<IWindowManagerService>();

            _engine.StateChanged += Engine_StateChanged;
            _engine.ErrorOccurred += Engine_ErrorOccurred;
            _windowManager.FullscreenStateChanged += OnFullscreenStateChanged;

            this.Unloaded += OnPageUnloaded;

            _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _positionTimer.Tick += PositionTimer_Tick;

            _voutAdoptionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _voutAdoptionTimer.Tick += VoutAdoptionTimer_Tick;

            SyncFullscreenChrome();
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

            if (!string.IsNullOrWhiteSpace(url))
            {
                _pendingUrl = url;
                _pendingStartMs = startMs;
                StartPlaybackWhenReady();
            }
        }

        private void VideoHost_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureVideoHostWindow();
            _isViewLoaded = true;
            StartPlaybackWhenReady();
        }

        private void VideoHost_Unloaded(object sender, RoutedEventArgs e)
        {
            DestroyVideoHostWindow();
        }

        private void VideoHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateVideoHostBounds();
        }

        private void StartPlaybackWhenReady()
        {
            if (!_isViewLoaded || string.IsNullOrWhiteSpace(_pendingUrl))
            {
                return;
            }

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

                _engine.SetVideoHostHandle(_videoHostHwnd);
                ShowVideoHostWindow(true);
                UpdateVideoHostBounds();
                StartVoutAdoptionGuard();
                _engine.Play(_pendingUrl, _pendingStartMs);
                _positionTimer.Start();
                _pendingUrl = null;
                _pendingStartMs = 0;
            }
            catch (Exception ex)
            {
                ShowStatus($"Unable to start playback: {ex.Message}", false);
            }
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            try { SaveProgress(force: true); } catch { }

            _positionTimer.Stop();
            _voutAdoptionTimer.Stop();

            _engine.StateChanged -= Engine_StateChanged;
            _engine.ErrorOccurred -= Engine_ErrorOccurred;
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
                _engine.SetVideoHostHandle(IntPtr.Zero);
                _engine.Stop();
                DestroyVideoHostWindow();
            }
            catch { }
        }

        private void PositionTimer_Tick(object sender, object e)
        {
            if (_isDisposed)
            {
                return;
            }

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

        private void VoutAdoptionTimer_Tick(object sender, object e)
        {
            _voutAdoptionAttempts++;
            if (TryAdoptDetachedVoutWindow() || _voutAdoptionAttempts >= 80)
            {
                _voutAdoptionTimer.Stop();
            }
        }

        private void UpdatePositionUi()
        {
            var position = Math.Max(0, _engine.PositionMs);
            var length = Math.Max(0, _engine.LengthMs);
            var hasDuration = length > 0;

            PositionText.Text = FormatTime(position);
            DurationText.Text = hasDuration ? FormatTime(length) : "Live";
            PlaybackInfoText.Text = _engine.State == PlaybackState.Error
                ? "Playback error"
                : hasDuration
                    ? $"{FormatTime(position)} / {FormatTime(length)}"
                    : "Live stream";

            SeekSlider.IsEnabled = hasDuration && _engine.IsSeekable;

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
            if (_isDisposed)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                PlayPauseButton.Content = state == PlaybackState.Playing ? "Pause" : "Play";

                switch (state)
                {
                    case PlaybackState.Loading:
                        ShowVideoHostWindow(true);
                        ShowStatus("Loading stream...", true);
                        break;
                    case PlaybackState.Playing:
                        ShowVideoHostWindow(true);
                        ShowStatus(null, false);
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
            if (_isDisposed)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() => ShowStatus(message, false));
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

        private void EnsureVideoHostWindow()
        {
            if (_videoHostHwnd != IntPtr.Zero)
            {
                UpdateVideoHostBounds();
                return;
            }

            var parentHwnd = WinRT.Interop.WindowNative.GetWindowHandle(((App)Application.Current).MainWindow);
            _videoHostHwnd = CreateWindowEx(
                0,
                "STATIC",
                string.Empty,
                WindowStyles.WS_CHILD | WindowStyles.WS_VISIBLE | WindowStyles.WS_CLIPSIBLINGS | WindowStyles.WS_CLIPCHILDREN,
                0,
                0,
                1,
                1,
                parentHwnd,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (_videoHostHwnd == IntPtr.Zero)
            {
                ShowStatus("Unable to create embedded video surface.", false);
                return;
            }

            _engine.SetVideoHostHandle(_videoHostHwnd);
            UpdateVideoHostBounds();
            ShowVideoHostWindow(true);
        }

        private void UpdateVideoHostBounds()
        {
            if (_videoHostHwnd == IntPtr.Zero || VideoHost.XamlRoot == null || ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            var scale = VideoHost.XamlRoot.RasterizationScale;
            var origin = VideoHost.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
            var x = (int)Math.Round(origin.X * scale);
            var y = (int)Math.Round(origin.Y * scale);
            var width = Math.Max(1, (int)Math.Round(VideoHost.ActualWidth * scale));
            var height = Math.Max(1, (int)Math.Round(VideoHost.ActualHeight * scale));

            SetWindowPos(
                _videoHostHwnd,
                IntPtr.Zero,
                x,
                y,
                width,
                height,
                SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);

            if (_adoptedVoutHwnd != IntPtr.Zero)
            {
                SetWindowPos(
                    _adoptedVoutHwnd,
                    IntPtr.Zero,
                    0,
                    0,
                    width,
                    height,
                    SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_FRAMECHANGED);
            }
        }

        private void ShowVideoHostWindow(bool show)
        {
            if (_videoHostHwnd != IntPtr.Zero)
            {
                ShowWindow(_videoHostHwnd, show ? ShowWindowCommand.SW_SHOWNA : ShowWindowCommand.SW_HIDE);
            }
        }

        private void DestroyVideoHostWindow()
        {
            if (_videoHostHwnd == IntPtr.Zero)
            {
                return;
            }

            try
            {
                _engine.SetVideoHostHandle(IntPtr.Zero);
                DestroyWindow(_videoHostHwnd);
            }
            catch { }
            finally
            {
                _videoHostHwnd = IntPtr.Zero;
                _adoptedVoutHwnd = IntPtr.Zero;
            }
        }

        private void StartVoutAdoptionGuard()
        {
            _adoptedVoutHwnd = IntPtr.Zero;
            _voutAdoptionAttempts = 0;
            _voutAdoptionTimer.Stop();
            _voutAdoptionTimer.Start();
        }

        private bool TryAdoptDetachedVoutWindow()
        {
            if (_videoHostHwnd == IntPtr.Zero)
            {
                return false;
            }

            var currentProcessId = Environment.ProcessId;
            IntPtr detachedWindow = IntPtr.Zero;

            EnumWindows((hwnd, lParam) =>
            {
                if (hwnd == _videoHostHwnd || hwnd == _adoptedVoutHwnd)
                {
                    return true;
                }

                GetWindowThreadProcessId(hwnd, out var processId);
                if (processId != currentProcessId)
                {
                    return true;
                }

                var title = GetWindowTitle(hwnd);
                if (title.Contains("VLC", StringComparison.OrdinalIgnoreCase) &&
                    title.Contains("output", StringComparison.OrdinalIgnoreCase))
                {
                    detachedWindow = hwnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            if (detachedWindow == IntPtr.Zero)
            {
                return false;
            }

            var style = GetWindowLongPtr(detachedWindow, WindowLongIndex.GWL_STYLE).ToInt64();
            style &= ~((long)WindowStyles.WS_POPUP | (long)WindowStyles.WS_CAPTION | (long)WindowStyles.WS_THICKFRAME);
            style |= (long)WindowStyles.WS_CHILD | (long)WindowStyles.WS_VISIBLE | (long)WindowStyles.WS_CLIPSIBLINGS | (long)WindowStyles.WS_CLIPCHILDREN;

            SetWindowLongPtr(detachedWindow, WindowLongIndex.GWL_STYLE, new IntPtr(style));
            SetParent(detachedWindow, _videoHostHwnd);
            _adoptedVoutHwnd = detachedWindow;
            ShowWindow(_adoptedVoutHwnd, ShowWindowCommand.SW_SHOWNA);
            UpdateVideoHostBounds();

            return true;
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            var length = GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return string.Empty;
            }

            var buffer = new char[length + 1];
            GetWindowText(hwnd, buffer, buffer.Length);
            return new string(buffer).TrimEnd('\0');
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_engine.IsPlaying)
            {
                _engine.Pause();
            }
            else
            {
                _engine.Resume();
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            SaveProgress(force: true);
            _engine.Stop();
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
            {
                CommitSeekFromSlider();
            }
        }

        private void CommitSeekFromSlider()
        {
            if (_isUpdatingSlider)
            {
                return;
            }

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
            if (_engine == null)
            {
                return;
            }

            if (SpeedComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string value &&
                float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
            {
                if (!_engine.SetPlaybackRate(rate))
                {
                    ShowStatus("Playback speed could not be changed for this stream.", false);
                }
            }
        }

        private void AudioTrackComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingTracks || AudioTrackComboBox.SelectedItem is not PlaybackTrack track)
            {
                return;
            }

            if (!_engine.SetAudioTrack(track.Id))
            {
                ShowStatus("Audio track could not be changed for this stream.", false);
            }
        }

        private void SubtitleTrackComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingTracks || SubtitleTrackComboBox.SelectedItem is not PlaybackTrack track)
            {
                return;
            }

            if (!_engine.SetSubtitleTrack(track.Id))
            {
                ShowStatus("Subtitle track could not be changed for this stream.", false);
            }
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
                if (file == null)
                {
                    return;
                }

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
            ChromeOverlay.Opacity = 1;
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
            ChromeOverlay.Opacity = 1;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            SaveProgress(force: true);

            if (_windowManager.IsFullscreen)
            {
                _windowManager.ExitFullscreen();
            }

            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
            else
            {
                Frame.Navigate(typeof(ChannelsPage));
            }
        }

        private void SaveProgress(bool force)
        {
            if (_launchContext == null || _launchContext.ContentId <= 0 || (_isDisposed && !force))
            {
                return;
            }

            if (!force && (DateTime.UtcNow - _lastProgressSaveUtc).TotalSeconds < 10)
            {
                return;
            }

            try
            {
                var positionMs = _engine.PositionMs;
                var lengthMs = _engine.LengthMs;

                using var scope = ((App)Application.Current).Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var progress = db.PlaybackProgresses.FirstOrDefault(p => p.ContentId == _launchContext.ContentId && p.ContentType == _launchContext.ContentType);
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
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            WindowStyles dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            SetWindowPosFlags uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommand nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll", EntryPoint = "GetWindowTextW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

        [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, WindowLongIndex nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, WindowLongIndex nIndex, IntPtr dwNewLong);

        [Flags]
        private enum WindowStyles : uint
        {
            WS_CHILD = 0x40000000,
            WS_VISIBLE = 0x10000000,
            WS_POPUP = 0x80000000,
            WS_CAPTION = 0x00C00000,
            WS_THICKFRAME = 0x00040000,
            WS_CLIPCHILDREN = 0x02000000,
            WS_CLIPSIBLINGS = 0x04000000
        }

        private enum WindowLongIndex
        {
            GWL_STYLE = -16
        }

        [Flags]
        private enum SetWindowPosFlags : uint
        {
            SWP_NOZORDER = 0x0004,
            SWP_NOACTIVATE = 0x0010,
            SWP_FRAMECHANGED = 0x0020
        }

        private enum ShowWindowCommand
        {
            SW_HIDE = 0,
            SW_SHOWNA = 8
        }
    }
}
