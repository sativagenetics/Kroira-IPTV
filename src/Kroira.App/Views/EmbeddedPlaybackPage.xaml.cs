using System;
using System.Collections.Generic;
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
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using WinRT.Interop;

namespace Kroira.App.Views
{
    // Self-contained, page-scoped playback host. Each navigation constructs a fresh
    // page; OnNavigatedTo creates the mpv handle + child HWND, OnNavigatedFrom
    // synchronously tears them down. There is no app-lifetime playback singleton.
    public sealed partial class EmbeddedPlaybackPage : Page
    {
        private static readonly TimeSpan ControlsHideDelay = TimeSpan.FromMilliseconds(1350);
        private static readonly TimeSpan PointerHideSuppressDelay = TimeSpan.FromMilliseconds(350);
        private static readonly TimeSpan PointerTimerResetThrottle = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan StreamLoadTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan StreamBufferTimeout = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan StreamRetryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ProgressPersistInterval = TimeSpan.FromSeconds(15);
        private const int MaxRetryAttempts = 3;
        private const double PointerMoveResetDistance = 4.0;

        private readonly PlaybackSessionStateMachine _stateMachine = new();
        private readonly PlaybackProgressCoordinator _progressCoordinator;

        private PlaybackLaunchContext _context;
        private MpvPlayer _player;
        private VideoSurface _surface;
        private IWindowManagerService _windowManager;
        private DispatcherTimer _controlsHideTimer;
        private DispatcherTimer _loadTimeoutTimer;
        private DispatcherTimer _bufferTimeoutTimer;
        private DispatcherTimer _progressPersistTimer;
        private bool _isUserSeeking;
        private bool _suppressSliderUpdates;
        private bool _controlsVisible = true;
        private bool _wasFullscreenOnEnter;
        private long _lastPositionMs;
        private long _lastDurationMs;
        private bool _playbackStarted;
        private bool _isStartingPlayback;
        private bool _teardownStarted;
        private bool _isNavigatingBack;
        private bool _isMuted;
        private bool _isVolumeSliderUpdating;
        private bool _recoveryInProgress;
        private bool _fullscreenSubscribed;
        private double _lastNonZeroVolume = 100;
        private DateTime _ignorePointerUntilUtc = DateTime.MinValue;
        private DateTime _lastPointerTimerRestartUtc = DateTime.MinValue;
        private Point _lastPointerPosition;
        private bool _hasLastPointerPosition;
        private int _activeAttemptId;
        private int _retryAttempt;
        private int _loadTimeoutAttemptId;
        private int _bufferTimeoutAttemptId;
        private string _lastPlayerWarning = string.Empty;
        private PlaybackAspectMode _selectedAspectMode = PlaybackAspectMode.Automatic;

        public EmbeddedPlaybackPage()
        {
            InitializeComponent();
            _progressCoordinator = new PlaybackProgressCoordinator(((App)Application.Current).Services);
            _stateMachine.StateChanged += OnPlaybackStateChanged;

            TimelineSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(TimelineSlider_PointerPressed), true);
            TimelineSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(TimelineSlider_PointerReleased), true);

            BuildAspectMenu();

            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _context = e.Parameter as PlaybackLaunchContext;
            if (_context == null)
            {
                ShowFatalError("Missing playback context.");
                return;
            }

            if (_context.ProfileId <= 0)
            {
                try
                {
                    using var scope = ((App)Application.Current).Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
                    _context.ProfileId = profileService.GetActiveProfileIdAsync(db).GetAwaiter().GetResult();
                }
                catch
                {
                    _context.ProfileId = 1;
                }
            }

            TitleText.Text = TitleForContext(_context);
            _windowManager = ((App)Application.Current).Services.GetRequiredService<IWindowManagerService>();
            _wasFullscreenOnEnter = _windowManager.IsFullscreen;
            _windowManager.FullscreenStateChanged += WindowManager_FullscreenStateChanged;
            _fullscreenSubscribed = true;
            UpdateFullscreenUi();
            UpdateAspectUi();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            TeardownPlayback();
            base.OnNavigatedFrom(e);

            if (_windowManager != null && _windowManager.IsFullscreen && !_wasFullscreenOnEnter)
            {
                _windowManager.ExitFullscreen();
            }
        }

        private async void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            if (_context == null) return;
            await TryStartPlaybackAsync();
        }

        private async void VideoHost_StartSizeChanged(object sender, SizeChangedEventArgs e)
        {
            await TryStartPlaybackAsync();
        }

        private async Task TryStartPlaybackAsync()
        {
            if (_playbackStarted || _isStartingPlayback || _teardownStarted || _context == null)
            {
                return;
            }

            if (VideoHost.XamlRoot == null || VideoHost.ActualWidth <= 0 || VideoHost.ActualHeight <= 0)
            {
                VideoHost.SizeChanged -= VideoHost_StartSizeChanged;
                VideoHost.SizeChanged += VideoHost_StartSizeChanged;
                return;
            }

            VideoHost.SizeChanged -= VideoHost_StartSizeChanged;
            _isStartingPlayback = true;

            try
            {
                var parentHwnd = WindowNative.GetWindowHandle(((App)Application.Current).MainWindow);
                _surface = new VideoSurface(parentHwnd, VideoHost,
                    onClick: OnVideoClick,
                    onDoubleClick: OnVideoDoubleClick,
                    onMouseMoved: OnVideoMouseMoved);
                _surface.UpdatePlacement(force: true);

                _player = new MpvPlayer(DispatcherQueue.GetForCurrentThread(), _surface.Handle);
                _player.PositionChanged += OnPositionChanged;
                _player.DurationChanged += OnDurationChanged;
                _player.PauseChanged += OnPauseChanged;
                _player.SeekableChanged += OnSeekableChanged;
                _player.BufferingChanged += OnBufferingChanged;
                _player.PlaybackEnded += OnPlaybackEnded;
                _player.FileLoaded += OnFileLoaded;
                _player.OutputReady += OnOutputReady;
                _player.TrackListChanged += OnTrackListChanged;
                _player.WarningMessage += OnWarningMessage;

                EnsureTimers();
                ResetTrackMenus();
                UpdateLiveAndSeekUi();
                UpdateInteractionState();
                ClearError();
                HideStatusOverlay();

                _lastPositionMs = await _progressCoordinator.ResolveResumePositionAsync(_context);
                if (_teardownStarted || _player == null || _context == null)
                {
                    return;
                }

                _playbackStarted = true;
                BeginPlaybackAttempt(isRetry: false, retryReason: null, startPositionMs: _lastPositionMs);
                RestartControlsHideTimer();
            }
            catch (Exception ex)
            {
                ShowFatalError(ex.Message);
                TeardownPlayback();
            }
            finally
            {
                _isStartingPlayback = false;
            }
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            TeardownPlayback();
        }

        private void TeardownPlayback()
        {
            if (_teardownStarted && _player == null && _surface == null)
            {
                return;
            }

            _teardownStarted = true;
            _recoveryInProgress = false;
            VideoHost.SizeChanged -= VideoHost_StartSizeChanged;

            if (_fullscreenSubscribed && _windowManager != null)
            {
                _windowManager.FullscreenStateChanged -= WindowManager_FullscreenStateChanged;
                _fullscreenSubscribed = false;
            }

            StopTimer(ref _controlsHideTimer);
            StopTimer(ref _loadTimeoutTimer);
            StopTimer(ref _bufferTimeoutTimer);
            StopTimer(ref _progressPersistTimer);

            if (_context != null)
            {
                _progressCoordinator.PersistBlocking(_context, _lastPositionMs, _lastDurationMs, force: true);
            }

            if (_player != null)
            {
                var player = _player;
                _player = null;
                player.PositionChanged -= OnPositionChanged;
                player.DurationChanged -= OnDurationChanged;
                player.PauseChanged -= OnPauseChanged;
                player.SeekableChanged -= OnSeekableChanged;
                player.BufferingChanged -= OnBufferingChanged;
                player.PlaybackEnded -= OnPlaybackEnded;
                player.FileLoaded -= OnFileLoaded;
                player.OutputReady -= OnOutputReady;
                player.TrackListChanged -= OnTrackListChanged;
                player.WarningMessage -= OnWarningMessage;
                try { player.Dispose(); } catch { }
            }

            if (_surface != null)
            {
                var surface = _surface;
                _surface = null;
                try { surface.Dispose(); } catch { }
            }

            ResetTrackMenus();
            _stateMachine.Reset();
        }

        private void EnsureTimers()
        {
            if (_controlsHideTimer == null)
            {
                _controlsHideTimer = new DispatcherTimer { Interval = ControlsHideDelay };
                _controlsHideTimer.Tick += ControlsHideTimer_Tick;
            }

            if (_loadTimeoutTimer == null)
            {
                _loadTimeoutTimer = new DispatcherTimer { Interval = StreamLoadTimeout };
                _loadTimeoutTimer.Tick += LoadTimeoutTimer_Tick;
            }

            if (_bufferTimeoutTimer == null)
            {
                _bufferTimeoutTimer = new DispatcherTimer { Interval = StreamBufferTimeout };
                _bufferTimeoutTimer.Tick += BufferTimeoutTimer_Tick;
            }

            if (_progressPersistTimer == null)
            {
                _progressPersistTimer = new DispatcherTimer { Interval = ProgressPersistInterval };
                _progressPersistTimer.Tick += ProgressPersistTimer_Tick;
            }
        }

        private void ControlsHideTimer_Tick(object sender, object e)
        {
            HideControls();
        }

        private void LoadTimeoutTimer_Tick(object sender, object e)
        {
            _loadTimeoutTimer?.Stop();
            if (_teardownStarted || _player == null) return;
            if (_loadTimeoutAttemptId != _activeAttemptId) return;

            _ = AttemptRecoveryAsync("Stream timed out while starting.");
        }

        private void BufferTimeoutTimer_Tick(object sender, object e)
        {
            _bufferTimeoutTimer?.Stop();
            if (_teardownStarted || _player == null) return;
            if (_bufferTimeoutAttemptId != _activeAttemptId) return;

            _ = AttemptRecoveryAsync("Stream stalled while buffering.");
        }

        private async void ProgressPersistTimer_Tick(object sender, object e)
        {
            await PersistProgressAsync(force: false);
        }

        private void BeginPlaybackAttempt(bool isRetry, string retryReason, long startPositionMs)
        {
            if (_player == null || _context == null || _teardownStarted)
            {
                return;
            }

            _activeAttemptId++;
            _lastPlayerWarning = string.Empty;
            _lastPositionMs = Math.Max(startPositionMs, 0);

            ResetTrackMenus();
            ClearError();
            StopBufferTimeout();

            if (isRetry)
            {
                _stateMachine.BeginReconnect(_retryAttempt, MaxRetryAttempts, retryReason ?? "Trying to recover stream.");
            }
            else
            {
                _retryAttempt = 0;
                _stateMachine.BeginLoad();
            }

            _player.Play(_context.StreamUrl, IsLivePlayback() ? 0 : _lastPositionMs);
            _player.SetVolume(VolumeSlider.Value);
            _player.SetMuted(_isMuted);
            _player.SetAspectMode(_selectedAspectMode);
            _progressPersistTimer?.Start();
            StartLoadTimeout();
            UpdateLiveAndSeekUi();
            UpdateInteractionState();
        }

        private void StartLoadTimeout()
        {
            if (_loadTimeoutTimer == null) return;
            _loadTimeoutAttemptId = _activeAttemptId;
            _loadTimeoutTimer.Stop();
            _loadTimeoutTimer.Start();
        }

        private void StopLoadTimeout()
        {
            _loadTimeoutTimer?.Stop();
        }

        private void StartBufferTimeout()
        {
            if (_bufferTimeoutTimer == null) return;
            _bufferTimeoutAttemptId = _activeAttemptId;
            _bufferTimeoutTimer.Stop();
            _bufferTimeoutTimer.Start();
        }

        private void StopBufferTimeout()
        {
            _bufferTimeoutTimer?.Stop();
        }

        private void MarkPlaybackActive()
        {
            if (_teardownStarted || _player == null)
            {
                return;
            }

            StopLoadTimeout();
            StopBufferTimeout();
            if (_retryAttempt > 0)
            {
                _retryAttempt = 0;
            }

            if (_stateMachine.State != PlaybackSessionState.Paused)
            {
                _stateMachine.SetPlaying();
            }
            UpdateInteractionState();
        }

        private async Task AttemptRecoveryAsync(string reason)
        {
            if (_teardownStarted || _player == null || _context == null || _recoveryInProgress)
            {
                return;
            }

            if (_retryAttempt >= MaxRetryAttempts)
            {
                ShowFatalError(reason);
                await PersistProgressAsync(force: true);
                return;
            }

            _recoveryInProgress = true;
            _retryAttempt++;
            StopLoadTimeout();
            StopBufferTimeout();
            _stateMachine.BeginReconnect(_retryAttempt, MaxRetryAttempts, BuildFailureMessage(reason));
            ShowControls(persist: true);
            UpdateInteractionState();

            await PersistProgressAsync(force: true);

            try
            {
                await Task.Delay(StreamRetryDelay);
                if (_teardownStarted || _player == null || _context == null)
                {
                    return;
                }

                var retryPositionMs = GetRetryPositionMs();
                BeginPlaybackAttempt(isRetry: true, retryReason: reason, startPositionMs: retryPositionMs);
            }
            finally
            {
                _recoveryInProgress = false;
            }
        }

        private long GetRetryPositionMs()
        {
            if (IsLivePlayback())
            {
                return 0;
            }

            return Math.Max(_lastPositionMs - 2_000, 0);
        }

        private async Task PersistProgressAsync(bool force)
        {
            if (_context == null || _teardownStarted)
            {
                return;
            }

            await _progressCoordinator.PersistAsync(_context, _lastPositionMs, _lastDurationMs, force);
        }

        private void OnPlaybackStateChanged(PlaybackSessionStateSnapshot snapshot)
        {
            if (_teardownStarted)
            {
                return;
            }

            switch (snapshot.State)
            {
                case PlaybackSessionState.Loading:
                    ShowStatusOverlay("Loading", snapshot.Message);
                    ShowControls(persist: true);
                    break;
                case PlaybackSessionState.Buffering:
                    ShowStatusOverlay("Buffering", snapshot.Message);
                    ShowControls(persist: true);
                    break;
                case PlaybackSessionState.Reconnecting:
                    ShowStatusOverlay("Reconnecting", snapshot.Message);
                    ShowControls(persist: true);
                    break;
                case PlaybackSessionState.Paused:
                    HideStatusOverlay();
                    ShowControls(persist: true);
                    break;
                case PlaybackSessionState.Error:
                    HideStatusOverlay();
                    ShowError(snapshot.Message);
                    ShowControls(persist: true);
                    break;
                default:
                    HideStatusOverlay();
                    ClearError();
                    if (snapshot.State == PlaybackSessionState.Playing)
                    {
                        RestartControlsHideTimer();
                    }
                    break;
            }

            UpdateInteractionState();
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

            if (!_player.IsPaused && !_player.IsBuffering)
            {
                MarkPlaybackActive();
            }
        }

        private void OnDurationChanged(TimeSpan duration)
        {
            if (_teardownStarted) return;
            _lastDurationMs = (long)duration.TotalMilliseconds;
            DurationText.Text = FormatTime(duration);
            UpdateLiveAndSeekUi();
        }

        private async void OnPauseChanged(bool isPaused)
        {
            if (_teardownStarted) return;

            PlayPauseIcon.Glyph = isPaused ? "\uE768" : "\uE769";
            if (isPaused && _player?.IsBuffering != true)
            {
                _stateMachine.SetPaused();
                await PersistProgressAsync(force: true);
            }
            else if (!isPaused && _player?.IsBuffering != true)
            {
                MarkPlaybackActive();
            }

            if (isPaused) ShowControls(persist: true);
            else RestartControlsHideTimer();
        }

        private void OnSeekableChanged(bool seekable)
        {
            if (_teardownStarted) return;
            UpdateLiveAndSeekUi();
        }

        private void OnBufferingChanged(bool isBuffering)
        {
            if (_teardownStarted || _player == null) return;

            if (isBuffering)
            {
                StopLoadTimeout();
                StartBufferTimeout();
                if (_stateMachine.State != PlaybackSessionState.Reconnecting)
                {
                    _stateMachine.SetBuffering();
                }
            }
            else
            {
                StopBufferTimeout();
                if (!_player.IsPaused)
                {
                    MarkPlaybackActive();
                }
            }

            UpdateInteractionState();
        }

        private void OnFileLoaded()
        {
            if (_teardownStarted) return;

            _surface?.UpdatePlacement(force: true);
            _ = RefreshTrackMenusAsync();
        }

        private void OnOutputReady()
        {
            if (_teardownStarted) return;

            _surface?.UpdatePlacement(force: true);
            _ = RefreshTrackMenusAsync();
            if (_player != null && !_player.IsPaused && !_player.IsBuffering)
            {
                MarkPlaybackActive();
            }
        }

        private void OnTrackListChanged()
        {
            if (_teardownStarted) return;
            _ = RefreshTrackMenusAsync();
        }

        private void OnWarningMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            _lastPlayerWarning = NormalizePlayerMessage(message);
        }

        private async void OnPlaybackEnded()
        {
            if (_teardownStarted) return;

            if (_stateMachine.State == PlaybackSessionState.Reconnecting)
            {
                return;
            }

            if (!IsPlaybackComplete())
            {
                await AttemptRecoveryAsync("Stream ended unexpectedly.");
                return;
            }

            _stateMachine.SetEnded();
            if (_player != null)
            {
                var finalMs = (long)_player.Duration.TotalMilliseconds;
                if (finalMs > 0)
                {
                    _lastPositionMs = finalMs;
                }
            }

            await PersistProgressAsync(force: true);
            NavigateBack();
        }

        // --- UI handlers ---

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            NavigateBack();
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_teardownStarted) return;
            TogglePlayPauseOrLive();
            RestartControlsHideTimer();
        }

        private void GoLive_Click(object sender, RoutedEventArgs e)
        {
            if (_teardownStarted) return;
            GoToLiveEdge();
            ShowControls();
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
            HandlePointerActivity(e.GetCurrentPoint(RootGrid).Position);
        }

        private void OnVideoClick()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_teardownStarted) return;
                TogglePlayPauseOrLive();
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
                HandlePointerActivity(null);
            });
        }

        private void TimelineSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;
            _isUserSeeking = true;
        }

        private void TimelineSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            CommitTimelineSeek();
        }

        private void TimelineSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            CommitTimelineSeek();
        }

        private void TimelineSlider_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitTimelineSeek();
        }

        private void TimelineSlider_KeyUp(object sender, KeyRoutedEventArgs e)
        {
            CommitTimelineSeek(force: true);
        }

        private async void CommitTimelineSeek(bool force = false)
        {
            if (_teardownStarted) return;
            if (!_isUserSeeking && !force) return;
            _isUserSeeking = false;

            if (_player == null || !IsTimelineSeekAllowed()) return;
            if (_suppressSliderUpdates) return;

            var durationSeconds = _player.Duration.TotalSeconds;
            if (durationSeconds <= 0) return;

            var fraction = TimelineSlider.Value / 1000.0;
            var targetSeconds = fraction * durationSeconds;
            _lastPositionMs = (long)(targetSeconds * 1000);
            _player.SeekAbsoluteSeconds(targetSeconds);
            await PersistProgressAsync(force: true);
            RestartControlsHideTimer();
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            if (_teardownStarted || _player == null) return;

            var muted = !_isMuted;
            if (!muted && VolumeSlider.Value <= 0)
            {
                SetVolumeSliderValue(_lastNonZeroVolume);
                _player.SetVolume(_lastNonZeroVolume);
            }

            _player.SetMuted(muted);
            SetMutedUi(muted);
            RestartControlsHideTimer();
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isVolumeSliderUpdating || _teardownStarted || _player == null) return;

            var volume = Math.Clamp(e.NewValue, 0, 100);
            if (volume > 0) _lastNonZeroVolume = volume;

            _player.SetVolume(volume);
            _player.SetMuted(volume <= 0);
            SetMutedUi(volume <= 0);
            RestartControlsHideTimer();
        }

        private void WindowManager_FullscreenStateChanged(object sender, EventArgs e)
        {
            if (_teardownStarted) return;
            UpdateFullscreenUi();
        }

        private void AudioTrackItem_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null || sender is not ToggleMenuFlyoutItem item || item.Tag is not string trackId)
            {
                return;
            }

            _player.SelectAudioTrack(trackId);
            _ = RefreshTrackMenusAsync();
            RestartControlsHideTimer();
        }

        private void SubtitleTrackItem_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null || sender is not ToggleMenuFlyoutItem item)
            {
                return;
            }

            var trackId = item.Tag as string;
            _player.SelectSubtitleTrack(trackId);
            _ = RefreshTrackMenusAsync();
            RestartControlsHideTimer();
        }

        private void AspectItem_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null || sender is not ToggleMenuFlyoutItem item || item.Tag is not PlaybackAspectMode aspectMode)
            {
                return;
            }

            _selectedAspectMode = aspectMode;
            _player.SetAspectMode(aspectMode);
            UpdateAspectUi();
            RestartControlsHideTimer();
        }

        private void SetMutedUi(bool muted)
        {
            _isMuted = muted;
            MuteIcon.Glyph = muted ? "\uE74F" : "\uE767";
            ToolTipService.SetToolTip(MuteButton, muted ? "Unmute" : "Mute");
        }

        private void SetVolumeSliderValue(double value)
        {
            _isVolumeSliderUpdating = true;
            VolumeSlider.Value = Math.Clamp(value, 0, 100);
            _isVolumeSliderUpdating = false;
        }

        private void TogglePlayPauseOrLive()
        {
            if (_player == null) return;
            if (_stateMachine.State == PlaybackSessionState.Loading ||
                _stateMachine.State == PlaybackSessionState.Reconnecting)
            {
                return;
            }

            if (IsLivePlayback() && _player.IsPaused)
            {
                GoToLiveEdge();
                return;
            }

            _player.TogglePause();
        }

        private void GoToLiveEdge()
        {
            if (!IsLivePlayback() || _player == null || _context == null) return;

            var volume = VolumeSlider.Value;
            var muted = _isMuted;

            if (_player.IsSeekable)
            {
                _player.SeekAbsolutePercent(100);
                _player.Resume();
                MarkPlaybackActive();
            }
            else
            {
                BeginPlaybackAttempt(isRetry: false, retryReason: null, startPositionMs: 0);
                _player.SetVolume(volume);
                _player.SetMuted(muted);
            }

            UpdateLiveAndSeekUi();
        }

        private bool IsLivePlayback()
        {
            return _context?.ContentType == PlaybackContentType.Channel;
        }

        private bool IsTimelineSeekAllowed()
        {
            if (_player == null || !_player.IsSeekable) return false;
            return !IsLivePlayback() || _player.Duration.TotalSeconds > 0;
        }

        private void UpdateLiveAndSeekUi()
        {
            if (_teardownStarted) return;

            var isLive = IsLivePlayback();
            var canSeek = IsTimelineSeekAllowed() &&
                          _stateMachine.State != PlaybackSessionState.Loading &&
                          _stateMachine.State != PlaybackSessionState.Reconnecting;

            TimelineSlider.IsEnabled = canSeek;
            LivePill.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
            GoLiveButton.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
            GoLiveButton.IsEnabled = isLive && _player != null;

            ToolTipService.SetToolTip(
                TimelineSlider,
                isLive
                    ? canSeek ? "Seek within live buffer" : "Live stream has no seekable buffer"
                    : canSeek ? "Seek" : "Seeking unavailable");
        }

        private void UpdateInteractionState()
        {
            var isReady = _player != null && !_teardownStarted;
            var trackSwitchEnabled = isReady &&
                                     _stateMachine.State != PlaybackSessionState.Loading &&
                                     _stateMachine.State != PlaybackSessionState.Reconnecting;

            PlayPauseButton.IsEnabled = isReady && _stateMachine.State != PlaybackSessionState.Reconnecting;
            StopButton.IsEnabled = isReady;
            FullscreenButton.IsEnabled = isReady;
            MuteButton.IsEnabled = isReady;
            VolumeSlider.IsEnabled = isReady;
            AudioTrackButton.IsEnabled = trackSwitchEnabled;
            SubtitleTrackButton.IsEnabled = trackSwitchEnabled;
            AspectButton.IsEnabled = isReady;
            UpdateLiveAndSeekUi();
        }

        private void UpdateFullscreenUi()
        {
            var isFullscreen = _windowManager?.IsFullscreen == true;
            FullscreenIcon.Glyph = isFullscreen ? "\uE73F" : "\uE740";
            ToolTipService.SetToolTip(FullscreenButton, isFullscreen ? "Exit fullscreen" : "Fullscreen");
        }

        private void BuildAspectMenu()
        {
            AspectFlyout.Items.Clear();
            AddAspectItem("Automatic", PlaybackAspectMode.Automatic);
            AddAspectItem("Fill window", PlaybackAspectMode.FillWindow);
            AddAspectItem("16:9", PlaybackAspectMode.Ratio16x9);
            AddAspectItem("4:3", PlaybackAspectMode.Ratio4x3);
            AddAspectItem("1.85:1", PlaybackAspectMode.Ratio185x1);
            AddAspectItem("2.35:1", PlaybackAspectMode.Ratio235x1);
            UpdateAspectUi();
        }

        private void AddAspectItem(string label, PlaybackAspectMode aspectMode)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = label,
                Tag = aspectMode,
                IsChecked = _selectedAspectMode == aspectMode
            };
            item.Click += AspectItem_Click;
            AspectFlyout.Items.Add(item);
        }

        private void UpdateAspectUi()
        {
            foreach (var entry in AspectFlyout.Items)
            {
                if (entry is ToggleMenuFlyoutItem item && item.Tag is PlaybackAspectMode aspectMode)
                {
                    item.IsChecked = aspectMode == _selectedAspectMode;
                }
            }

            ToolTipService.SetToolTip(AspectButton, $"Aspect: {AspectModeLabel(_selectedAspectMode)}");
        }

        private Task RefreshTrackMenusAsync()
        {
            if (_teardownStarted || _player == null)
            {
                ResetTrackMenus();
                return Task.CompletedTask;
            }

            PopulateAudioTrackMenu(_player.GetAudioTracks());
            PopulateSubtitleTrackMenu(_player.GetSubtitleTracks());
            return Task.CompletedTask;
        }

        private void PopulateAudioTrackMenu(IReadOnlyList<MpvTrackInfo> audioTracks)
        {
            AudioTrackFlyout.Items.Clear();
            foreach (var track in audioTracks)
            {
                var item = new ToggleMenuFlyoutItem
                {
                    Text = track.DisplayName,
                    Tag = track.Id,
                    IsChecked = track.IsSelected
                };
                item.Click += AudioTrackItem_Click;
                AudioTrackFlyout.Items.Add(item);
            }

            AudioTrackButton.Visibility = audioTracks.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            var selectedTrack = FindSelectedTrack(audioTracks);
            ToolTipService.SetToolTip(AudioTrackButton, selectedTrack != null ? $"Audio: {selectedTrack.DisplayName}" : "Audio track");
        }

        private void PopulateSubtitleTrackMenu(IReadOnlyList<MpvTrackInfo> subtitleTracks)
        {
            SubtitleTrackFlyout.Items.Clear();

            var offItem = new ToggleMenuFlyoutItem
            {
                Text = "Off",
                Tag = null,
                IsChecked = true
            };
            offItem.Click += SubtitleTrackItem_Click;
            SubtitleTrackFlyout.Items.Add(offItem);

            foreach (var track in subtitleTracks)
            {
                var item = new ToggleMenuFlyoutItem
                {
                    Text = track.DisplayName,
                    Tag = track.Id,
                    IsChecked = track.IsSelected
                };
                item.Click += SubtitleTrackItem_Click;
                SubtitleTrackFlyout.Items.Add(item);
            }

            var selectedTrack = FindSelectedTrack(subtitleTracks);
            offItem.IsChecked = selectedTrack == null;
            SubtitleTrackButton.Visibility = subtitleTracks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ToolTipService.SetToolTip(SubtitleTrackButton, selectedTrack != null ? $"Subtitles: {selectedTrack.DisplayName}" : "Subtitles off");
        }

        private static MpvTrackInfo FindSelectedTrack(IReadOnlyList<MpvTrackInfo> tracks)
        {
            foreach (var track in tracks)
            {
                if (track.IsSelected)
                {
                    return track;
                }
            }

            return null;
        }

        private void ResetTrackMenus()
        {
            AudioTrackFlyout.Items.Clear();
            SubtitleTrackFlyout.Items.Clear();
            AudioTrackButton.Visibility = Visibility.Collapsed;
            SubtitleTrackButton.Visibility = Visibility.Collapsed;
        }

        // --- Controls auto-hide ---

        private void HandlePointerActivity(Point? position)
        {
            if (_teardownStarted) return;

            var now = DateTime.UtcNow;
            var movedEnough = false;
            if (position.HasValue)
            {
                movedEnough = !_hasLastPointerPosition ||
                    Distance(position.Value, _lastPointerPosition) >= PointerMoveResetDistance;

                if (movedEnough)
                {
                    _lastPointerPosition = position.Value;
                    _hasLastPointerPosition = true;
                }
            }

            if (now < _ignorePointerUntilUtc && (!position.HasValue || !movedEnough))
            {
                return;
            }

            if (!_controlsVisible && position.HasValue && !movedEnough)
            {
                return;
            }

            var wasHidden = !_controlsVisible;
            ShowControls();

            if (wasHidden || movedEnough ||
                (!position.HasValue && now - _lastPointerTimerRestartUtc >= PointerTimerResetThrottle))
            {
                RestartControlsHideTimer(now);
            }
        }

        private void ShowControls(bool persist = false)
        {
            if (!_controlsVisible)
            {
                TopRow.Height = GridLength.Auto;
                BottomRow.Height = GridLength.Auto;
                TopBar.Visibility = Visibility.Visible;
                BottomBar.Visibility = Visibility.Visible;
                _controlsVisible = true;
                QueueSurfacePlacementUpdate();
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
            if (StatusOverlay.Visibility == Visibility.Visible) return;
            if (!_controlsVisible) return;

            TopRow.Height = new GridLength(0);
            BottomRow.Height = new GridLength(0);
            TopBar.Visibility = Visibility.Collapsed;
            BottomBar.Visibility = Visibility.Collapsed;
            _controlsVisible = false;
            _ignorePointerUntilUtc = DateTime.UtcNow + PointerHideSuppressDelay;
            _controlsHideTimer?.Stop();
            QueueSurfacePlacementUpdate();
        }

        private void RestartControlsHideTimer()
        {
            RestartControlsHideTimer(DateTime.UtcNow);
        }

        private void RestartControlsHideTimer(DateTime now)
        {
            if (_controlsHideTimer == null) return;
            _controlsHideTimer.Stop();
            if (_player != null && _player.IsPaused) return;
            if (ErrorOverlay.Visibility == Visibility.Visible) return;
            if (StatusOverlay.Visibility == Visibility.Visible) return;
            _lastPointerTimerRestartUtc = now;
            _controlsHideTimer.Start();
        }

        private void QueueSurfacePlacementUpdate()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_teardownStarted) return;
                _surface?.UpdatePlacement(force: true);
            });
        }

        private static double Distance(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // --- Helpers ---

        private void ShowStatusOverlay(string title, string message)
        {
            StatusTitle.Text = title;
            StatusMessage.Text = string.IsNullOrWhiteSpace(message) ? title : message;
            StatusRing.IsActive = true;
            StatusOverlay.Visibility = Visibility.Visible;
            ClearError();
        }

        private void HideStatusOverlay()
        {
            StatusRing.IsActive = false;
            StatusOverlay.Visibility = Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            ErrorMessage.Text = string.IsNullOrWhiteSpace(message) ? "Unable to start playback." : message;
            ErrorOverlay.Visibility = Visibility.Visible;
        }

        private void ClearError()
        {
            ErrorOverlay.Visibility = Visibility.Collapsed;
        }

        private void ShowFatalError(string message)
        {
            _stateMachine.SetError(BuildFailureMessage(message));
        }

        private string BuildFailureMessage(string fallbackMessage)
        {
            return string.IsNullOrWhiteSpace(_lastPlayerWarning)
                ? fallbackMessage
                : $"{fallbackMessage} {_lastPlayerWarning}";
        }

        private bool IsPlaybackComplete()
        {
            return _lastDurationMs > 0 && _lastPositionMs >= _lastDurationMs * 0.95;
        }

        private static string NormalizePlayerMessage(string message)
        {
            var normalized = message.Replace("\r", " ").Replace("\n", " ").Trim();
            if (normalized.Length > 180)
            {
                normalized = normalized[..180].TrimEnd() + "...";
            }

            return normalized;
        }

        private void NavigateBack()
        {
            if (_isNavigatingBack) return;
            _isNavigatingBack = true;

            TeardownPlayback();
            if (Frame != null && Frame.CanGoBack) Frame.GoBack();
        }

        private static void StopTimer(ref DispatcherTimer timer)
        {
            timer?.Stop();
        }

        private static string AspectModeLabel(PlaybackAspectMode aspectMode)
        {
            return aspectMode switch
            {
                PlaybackAspectMode.FillWindow => "Fill window",
                PlaybackAspectMode.Ratio16x9 => "16:9",
                PlaybackAspectMode.Ratio4x3 => "4:3",
                PlaybackAspectMode.Ratio185x1 => "1.85:1",
                PlaybackAspectMode.Ratio235x1 => "2.35:1",
                _ => "Automatic",
            };
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
    }
}
