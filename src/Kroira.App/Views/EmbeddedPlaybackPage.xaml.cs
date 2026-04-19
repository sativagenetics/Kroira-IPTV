using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
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
using Windows.Foundation;
using WinRT.Interop;

namespace Kroira.App.Views
{
    // Self-contained, page-scoped playback host. Each navigation constructs a fresh
    // page; OnNavigatedTo creates the mpv handle + child HWND, OnNavigatedFrom
    // synchronously tears them down. There is no app-lifetime playback singleton.
    public sealed partial class EmbeddedPlaybackPage : Page
    {
        private static readonly TimeSpan ControlsHideDelay = TimeSpan.FromMilliseconds(2250);
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
        private readonly IEntitlementService _entitlementService;
        private readonly IPictureInPictureService _pictureInPictureService;

        private PlaybackLaunchContext _context;
        private MpvPlayer _player;
        private VideoSurface _surface;
        private IWindowManagerService _windowManager;
        private DispatcherTimer _controlsHideTimer;
        private DispatcherTimer _loadTimeoutTimer;
        private DispatcherTimer _bufferTimeoutTimer;
        private DispatcherTimer _progressPersistTimer;
        private bool _isUserSeeking;
        private bool _isUserAdjustingVolume;
        private bool _isOverlayPointerInteractionActive;
        private bool _suppressSliderUpdates;
        private bool _isOverlayVisible = true;
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
        private bool _launchOverridesApplied;
        private bool _isCursorHidden;
        private double _lastNonZeroVolume = 100;
        private DateTime _ignorePointerUntilUtc = DateTime.MinValue;
        private DateTime _lastPointerTimerRestartUtc = DateTime.MinValue;
        private Point _lastPointerPosition;
        private bool _hasLastPointerPosition;
        private int _activeAttemptId;
        private int _openOverlayFlyoutCount;
        private int _retryAttempt;
        private int _loadTimeoutAttemptId;
        private int _bufferTimeoutAttemptId;
        private int _controlsHideScheduleId;
        private string _lastPlayerWarning = string.Empty;
        private string _lastOverlayRestartSource = string.Empty;
        private PlaybackAspectMode _selectedAspectMode = PlaybackAspectMode.Automatic;
        private bool _isLiveTimeshiftActive;
        private DateTime _ignoreLivePlaybackEndedUntilUtc = DateTime.MinValue;
        private DateTime _controlsHideDeadlineUtc = DateTime.MinValue;
        private CancellationTokenSource _controlsHideCancellation;

        private static string LogPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kroira",
            "startup-log.txt");

        private static void Log(string message)
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogPath)!);
                System.IO.File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] PLAYBACK {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        public EmbeddedPlaybackPage()
        {
            InitializeComponent();
            var services = ((App)Application.Current).Services;
            _progressCoordinator = new PlaybackProgressCoordinator(services);
            _entitlementService = services.GetRequiredService<IEntitlementService>();
            _pictureInPictureService = services.GetRequiredService<IPictureInPictureService>();
            _stateMachine.StateChanged += OnPlaybackStateChanged;

            TimelineSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(TimelineSlider_PointerPressed), true);
            TimelineSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(TimelineSlider_PointerReleased), true);
            VolumeSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(VolumeSlider_PointerPressed), true);
            VolumeSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(VolumeSlider_PointerReleased), true);
            VolumeSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(VolumeSlider_PointerCaptureLost), true);
            AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Page_PointerPressed), true);
            AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Page_PointerReleased), true);
            AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Page_PointerCaptureLost), true);
            AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Page_KeyDown), true);

            BuildAspectMenu();
            WireOverlayFlyout(AudioTrackFlyout);
            WireOverlayFlyout(SubtitleTrackFlyout);
            WireOverlayFlyout(AspectFlyout);

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
            ApplyLaunchOptions();

            if (!IsPictureInPictureMode())
            {
                _wasFullscreenOnEnter = _windowManager.IsFullscreen;
                _windowManager.FullscreenStateChanged += WindowManager_FullscreenStateChanged;
                _fullscreenSubscribed = true;
            }

            UpdateFullscreenUi();
            UpdatePictureInPictureUi();
            UpdateAspectUi();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            TeardownPlayback();
            base.OnNavigatedFrom(e);

            if (!IsPictureInPictureMode() && _windowManager != null && _windowManager.IsFullscreen && !_wasFullscreenOnEnter)
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
                var parentHwnd = ResolveHostWindowHandle();
                _surface = new VideoSurface(parentHwnd, VideoHost,
                    onClick: OnVideoClick,
                    onDoubleClick: OnVideoDoubleClick,
                    onMouseMoved: OnVideoMouseMoved);
                _surface.UpdatePlacement(force: true);

                _player = CreatePlayer(_surface.Handle);

                EnsureTimers();
                ResetTrackMenus();
                UpdateLiveAndSeekUi();
                UpdateInteractionState();
                ClearError();
                HideStatusOverlay();

                _lastPositionMs = _context.StartPositionMs > 0
                    ? _context.StartPositionMs
                    : await _progressCoordinator.ResolveResumePositionAsync(_context);
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

            _controlsHideTimer?.Stop();
            StopTimer(ref _loadTimeoutTimer);
            StopTimer(ref _bufferTimeoutTimer);
            StopTimer(ref _progressPersistTimer);
            CancelControlsHideSchedule();
            EnsureCursorVisible();

            if (_context != null)
            {
                _progressCoordinator.PersistBlocking(_context, _lastPositionMs, _lastDurationMs, force: true);
            }

            DetachAndDisposePlayer();

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
            LogPlaybackState("OVERLAY: hide timer tick");
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
            _launchOverridesApplied = false;
            if (IsLivePlayback())
            {
                _isLiveTimeshiftActive = false;
                _ignoreLivePlaybackEndedUntilUtc = DateTime.UtcNow.AddSeconds(5);
                _lastDurationMs = 0;
                DurationText.Text = "0:00";
            }

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
            LogPlaybackState($"ATTEMPT: begin {(isRetry ? "retry" : "start")} reason={retryReason ?? "direct"} startMs={startPositionMs}");
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

        private MpvPlayer CreatePlayer(IntPtr surfaceHandle)
        {
            var player = new MpvPlayer(DispatcherQueue.GetForCurrentThread(), surfaceHandle);
            player.PositionChanged += OnPositionChanged;
            player.DurationChanged += OnDurationChanged;
            player.PauseChanged += OnPauseChanged;
            player.SeekableChanged += OnSeekableChanged;
            player.BufferingChanged += OnBufferingChanged;
            player.PlaybackEnded += OnPlaybackEnded;
            player.FileLoaded += OnFileLoaded;
            player.OutputReady += OnOutputReady;
            player.TrackListChanged += OnTrackListChanged;
            player.WarningMessage += OnWarningMessage;
            return player;
        }

        private void DetachAndDisposePlayer()
        {
            if (_player == null)
            {
                return;
            }

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

        private void ResetLiveEdgePlayerSession(string reason)
        {
            if (_surface == null)
            {
                BeginPlaybackAttempt(isRetry: false, retryReason: $"go-live:{reason}", startPositionMs: 0);
                return;
            }

            LogPlaybackState($"LIVECTL: recreating live player session reason={reason}");
            StopLoadTimeout();
            StopBufferTimeout();
            _progressPersistTimer?.Stop();
            CancelControlsHideSchedule();
            DetachAndDisposePlayer();
            _player = CreatePlayer(_surface.Handle);
            ResetTrackMenus();
            ClearError();
            HideStatusOverlay();
            BeginPlaybackAttempt(isRetry: false, retryReason: $"go-live:{reason}", startPositionMs: 0);
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

            if (_stateMachine.State != PlaybackSessionState.Playing)
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

            LogPlaybackState($"RECOVERY: requested reason={reason}");

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

            MaybeHideControlsFromPlaybackPulse();
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
            if (isPaused && IsLivePlayback())
            {
                _isLiveTimeshiftActive = true;
                StopLoadTimeout();
                StopBufferTimeout();
            }

            if (isPaused && _player?.IsBuffering != true)
            {
                _stateMachine.SetPaused();
                await PersistProgressAsync(force: true);
            }
            else if (!isPaused && _player?.IsBuffering != true)
            {
                MarkPlaybackActive();
            }

            LogPlaybackState($"LIVECTL: pause changed paused={isPaused}");
            if (isPaused) ShowControls(persist: true);
            else RestartControlsHideTimer();
        }

        private void OnSeekableChanged(bool seekable)
        {
            if (_teardownStarted) return;
            LogPlaybackState($"LIVECTL: seekable changed seekable={seekable}");
            UpdateLiveAndSeekUi();
        }

        private void OnBufferingChanged(bool isBuffering)
        {
            if (_teardownStarted || _player == null) return;

            if (isBuffering && _player.IsPaused)
            {
                StopLoadTimeout();
                StopBufferTimeout();
                LogPlaybackState("LIVECTL: buffering while paused ignored for timeout/state");
                UpdateInteractionState();
                return;
            }

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

            LogPlaybackState($"LIVECTL: buffering changed buffering={isBuffering}");
            UpdateInteractionState();
        }

        private void OnFileLoaded()
        {
            if (_teardownStarted) return;

            if (IsLivePlayback())
            {
                _ignoreLivePlaybackEndedUntilUtc = DateTime.UtcNow.AddSeconds(3);
            }

            _surface?.UpdatePlacement(force: true);
            _ = RefreshTrackMenusAsync();
            LogPlaybackState("LIVECTL: file loaded");
        }

        private void OnOutputReady()
        {
            if (_teardownStarted) return;

            _surface?.UpdatePlacement(force: true);
            _ = RefreshTrackMenusAsync();
            ApplyLaunchOverridesIfNeeded();
            if (_player != null && !_player.IsPaused && !_player.IsBuffering)
            {
                MarkPlaybackActive();
            }

            LogPlaybackState("LIVECTL: output ready");
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

        private async void OnPlaybackEnded(MpvPlaybackEndedInfo endInfo)
        {
            if (_teardownStarted) return;

            LogPlaybackState($"LIVECTL: playback ended signaled reason={endInfo?.Reason} error={endInfo?.ErrorCode ?? 0}");

            if (IsLivePlayback())
            {
                var nowUtc = DateTime.UtcNow;
                if (_stateMachine.State == PlaybackSessionState.Loading ||
                    _stateMachine.State == PlaybackSessionState.Reconnecting ||
                    nowUtc < _ignoreLivePlaybackEndedUntilUtc)
                {
                    LogPlaybackState(
                        $"LIVECTL: ignored playback-ended during live transition graceRemainingMs={Math.Max(0, (_ignoreLivePlaybackEndedUntilUtc - nowUtc).TotalMilliseconds):0}");
                    return;
                }
            }

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
            GoToLiveEdge("button");
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
            if (_teardownStarted || IsPictureInPictureMode() || !CanUseFeature(EntitlementFeatureKeys.PlaybackFullscreen)) return;
            _windowManager?.ToggleFullscreen();
            RestartControlsHideTimer();
        }

        private async void PictureInPicture_Click(object sender, RoutedEventArgs e)
        {
            if (_teardownStarted ||
                _context == null ||
                !CanUseFeature(EntitlementFeatureKeys.PlaybackPictureInPicture))
            {
                return;
            }

            if (IsPictureInPictureMode())
            {
                await RestoreFromPictureInPictureAsync();
            }
            else
            {
                await EnterPictureInPictureAsync();
            }

            RestartControlsHideTimer();
        }

        private void Page_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;
            HandlePointerActivity(ToScreenPoint(e.GetCurrentPoint(RootGrid).Position), "page");
        }

        private void Page_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;

            if (IsOverlayControlSource(e.OriginalSource))
            {
                _isOverlayPointerInteractionActive = true;
                ShowControls(persist: true);
                return;
            }

            ShowControls();
            RestartControlsHideTimer();
        }

        private void Page_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;
            ReleaseOverlayPointerInteraction();
        }

        private void Page_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;
            ReleaseOverlayPointerInteraction();
        }

        private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
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
                TogglePlayPauseOrLive();
                ShowControls();
                RestartControlsHideTimer();
            });
        }

        private void OnVideoDoubleClick()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_teardownStarted || IsPictureInPictureMode() || !CanUseFeature(EntitlementFeatureKeys.PlaybackFullscreen)) return;
                _windowManager?.ToggleFullscreen();
                RestartControlsHideTimer();
            });
        }

        private void OnVideoMouseMoved(Point screenPosition)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_teardownStarted) return;
                HandlePointerActivity(screenPosition, "video-surface");
            });
        }

        private void TimelineSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;
            _isUserSeeking = true;
            ShowControls(persist: true);
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
            ResumeOverlayAutoHide();

            if (_player == null || !IsTimelineSeekAllowed()) return;
            if (_suppressSliderUpdates) return;

            var durationSeconds = _player.Duration.TotalSeconds;
            if (durationSeconds <= 0) return;

            var fraction = TimelineSlider.Value / 1000.0;
            var targetSeconds = fraction * durationSeconds;
            if (IsLivePlayback())
            {
                _isLiveTimeshiftActive = true;
            }

            _lastPositionMs = (long)(targetSeconds * 1000);
            LogPlaybackState($"LIVECTL: timeline seek commit targetSeconds={targetSeconds:F3}");
            _player.SeekAbsoluteSeconds(targetSeconds);
            await PersistProgressAsync(force: true);
            RestartControlsHideTimer();
        }

        private void VolumeSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;
            _isUserAdjustingVolume = true;
            ShowControls(persist: true);
        }

        private void VolumeSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;
            EndVolumeInteraction();
        }

        private void VolumeSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;
            EndVolumeInteraction();
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
            if (_isUserAdjustingVolume)
            {
                ShowControls(persist: true);
            }
            else
            {
                RestartControlsHideTimer();
            }
        }

        private void WindowManager_FullscreenStateChanged(object sender, EventArgs e)
        {
            if (_teardownStarted) return;
            UpdateFullscreenUi();
        }

        private void AudioTrackItem_Click(object sender, RoutedEventArgs e)
        {
            if (!CanUseFeature(EntitlementFeatureKeys.PlaybackAudioTrackSelection) ||
                _player == null ||
                sender is not ToggleMenuFlyoutItem item ||
                item.Tag is not string trackId)
            {
                return;
            }

            _player.SelectAudioTrack(trackId);
            if (_context != null)
            {
                _context.RestoreAudioTrackSelection = true;
                _context.PreferredAudioTrackId = trackId;
            }
            _ = RefreshTrackMenusAsync();
            RestartControlsHideTimer();
        }

        private void SubtitleTrackItem_Click(object sender, RoutedEventArgs e)
        {
            if (!CanUseFeature(EntitlementFeatureKeys.PlaybackSubtitleTrackSelection) ||
                _player == null ||
                sender is not ToggleMenuFlyoutItem item)
            {
                return;
            }

            var trackId = item.Tag as string;
            _player.SelectSubtitleTrack(trackId);
            if (_context != null)
            {
                _context.RestoreSubtitleTrackSelection = true;
                _context.PreferredSubtitleTrackId = trackId ?? string.Empty;
            }
            _ = RefreshTrackMenusAsync();
            RestartControlsHideTimer();
        }

        private void AspectItem_Click(object sender, RoutedEventArgs e)
        {
            if (!CanUseFeature(EntitlementFeatureKeys.PlaybackAspectControls) ||
                _player == null ||
                sender is not ToggleMenuFlyoutItem item ||
                item.Tag is not PlaybackAspectMode aspectMode)
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
                GoToLiveEdge("resume-from-pause");
                return;
            }

            if (IsLivePlayback())
            {
                _isLiveTimeshiftActive = true;
                LogPlaybackState("LIVECTL: live pause requested");
                _player.Pause();
                return;
            }

            LogPlaybackState("PLAYCTL: toggle pause requested");
            _player.TogglePause();
        }

        private void GoToLiveEdge(string reason)
        {
            if (!IsLivePlayback() || _player == null || _context == null) return;
            if (_stateMachine.State == PlaybackSessionState.Loading ||
                _stateMachine.State == PlaybackSessionState.Reconnecting)
            {
                LogPlaybackState($"LIVECTL: go-live ignored while state={_stateMachine.State} reason={reason}");
                return;
            }

            _isLiveTimeshiftActive = false;
            LogPlaybackState($"LIVECTL: go-live requested reason={reason}; strategy=recreate-player");
            ResetLiveEdgePlayerSession(reason);

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
            var canUseFullscreen = CanUseFeature(EntitlementFeatureKeys.PlaybackFullscreen);
            var canUsePictureInPicture = CanUseFeature(EntitlementFeatureKeys.PlaybackPictureInPicture);
            var canUseAudioTracks = CanUseFeature(EntitlementFeatureKeys.PlaybackAudioTrackSelection);
            var canUseSubtitles = CanUseFeature(EntitlementFeatureKeys.PlaybackSubtitleTrackSelection);
            var canUseAspectControls = CanUseFeature(EntitlementFeatureKeys.PlaybackAspectControls);
            var isPictureInPicture = IsPictureInPictureMode();

            PlayPauseButton.IsEnabled = isReady && _stateMachine.State != PlaybackSessionState.Reconnecting;
            StopButton.IsEnabled = isReady;
            PictureInPictureButton.IsEnabled = isReady && canUsePictureInPicture;
            PictureInPictureButton.Visibility = canUsePictureInPicture ? Visibility.Visible : Visibility.Collapsed;
            FullscreenButton.IsEnabled = isReady && canUseFullscreen && !isPictureInPicture;
            FullscreenButton.Visibility = canUseFullscreen && !isPictureInPicture ? Visibility.Visible : Visibility.Collapsed;
            MuteButton.IsEnabled = isReady;
            VolumeSlider.IsEnabled = isReady;
            AudioTrackButton.IsEnabled = trackSwitchEnabled && canUseAudioTracks;
            SubtitleTrackButton.IsEnabled = trackSwitchEnabled && canUseSubtitles;
            AspectButton.IsEnabled = isReady && canUseAspectControls;
            AspectButton.Visibility = canUseAspectControls ? Visibility.Visible : Visibility.Collapsed;
            UpdateLiveAndSeekUi();
        }

        private void UpdateFullscreenUi()
        {
            if (IsPictureInPictureMode())
            {
                FullscreenIcon.Glyph = "\uE740";
                ToolTipService.SetToolTip(FullscreenButton, "Fullscreen");
                return;
            }

            var isFullscreen = _windowManager?.IsFullscreen == true;
            FullscreenIcon.Glyph = isFullscreen ? "\uE73F" : "\uE740";
            ToolTipService.SetToolTip(FullscreenButton, isFullscreen ? "Exit fullscreen" : "Fullscreen");
        }

        private void UpdatePictureInPictureUi()
        {
            var inPictureInPicture = IsPictureInPictureMode();
            PictureInPictureButtonText.Text = inPictureInPicture ? "Return" : "PIP";
            ToolTipService.SetToolTip(PictureInPictureButton, inPictureInPicture ? "Return to player" : "Picture in Picture");
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
            if (!CanUseFeature(EntitlementFeatureKeys.PlaybackAudioTrackSelection))
            {
                AudioTrackButton.Visibility = Visibility.Collapsed;
                return;
            }

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
            if (!CanUseFeature(EntitlementFeatureKeys.PlaybackSubtitleTrackSelection))
            {
                SubtitleTrackButton.Visibility = Visibility.Collapsed;
                return;
            }

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

        private bool CanUseFeature(string featureKey)
        {
            return _entitlementService.IsFeatureEnabled(featureKey);
        }

        private bool IsPictureInPictureMode()
        {
            return _context?.OpenInPictureInPicture == true;
        }

        private void ApplyLaunchOptions()
        {
            if (_context == null)
            {
                return;
            }

            _selectedAspectMode = _context.InitialAspectMode;
            SetVolumeSliderValue(Math.Clamp(_context.InitialVolume, 0, 100));
            if (VolumeSlider.Value > 0)
            {
                _lastNonZeroVolume = VolumeSlider.Value;
            }
            _isMuted = _context.IsMuted || VolumeSlider.Value <= 0;
            SetMutedUi(_isMuted);
        }

        private void ApplyLaunchOverridesIfNeeded()
        {
            if (_launchOverridesApplied || _player == null || _context == null)
            {
                return;
            }

            _player.SetAspectMode(_selectedAspectMode);
            _player.SetVolume(VolumeSlider.Value);
            _player.SetMuted(_isMuted);

            if (_context.RestoreAudioTrackSelection && !string.IsNullOrWhiteSpace(_context.PreferredAudioTrackId))
            {
                _player.SelectAudioTrack(_context.PreferredAudioTrackId);
            }

            if (_context.RestoreSubtitleTrackSelection)
            {
                _player.SelectSubtitleTrack(_context.PreferredSubtitleTrackId);
            }

            if (_context.StartPaused)
            {
                _player.Pause();
            }

            _launchOverridesApplied = true;
            _ = RefreshTrackMenusAsync();
        }

        private async Task EnterPictureInPictureAsync()
        {
            if (_context == null || _player == null)
            {
                return;
            }

            if (_windowManager?.IsFullscreen == true)
            {
                _windowManager.ExitFullscreen();
            }

            var shouldResume = !_player.IsPaused;
            if (shouldResume)
            {
                _player.Pause();
            }

            await PersistProgressAsync(force: true);
            var launchContext = CreateTransferContext(openInPictureInPicture: true, startPaused: !shouldResume);
            if (_pictureInPictureService.Enter(launchContext))
            {
                NavigateBack();
                return;
            }

            if (shouldResume && _player != null && !_teardownStarted)
            {
                _player.Resume();
            }
        }

        private async Task RestoreFromPictureInPictureAsync()
        {
            if (_context == null || _player == null)
            {
                return;
            }

            var shouldResume = !_player.IsPaused;
            if (shouldResume)
            {
                _player.Pause();
            }

            await PersistProgressAsync(force: true);
            var launchContext = CreateTransferContext(openInPictureInPicture: false, startPaused: !shouldResume);
            if (_pictureInPictureService.RestoreToMainWindow(launchContext))
            {
                return;
            }

            if (shouldResume && _player != null && !_teardownStarted)
            {
                _player.Resume();
            }
        }

        private PlaybackLaunchContext CreateTransferContext(bool openInPictureInPicture, bool startPaused)
        {
            return new PlaybackLaunchContext
            {
                ProfileId = _context?.ProfileId ?? 0,
                ContentId = _context?.ContentId ?? 0,
                ContentType = _context?.ContentType ?? PlaybackContentType.Channel,
                StreamUrl = _context?.StreamUrl ?? string.Empty,
                StartPositionMs = Math.Max(_lastPositionMs, 0),
                OpenInPictureInPicture = openInPictureInPicture,
                StartPaused = startPaused,
                InitialVolume = VolumeSlider.Value,
                IsMuted = _isMuted,
                InitialAspectMode = _selectedAspectMode,
                RestoreAudioTrackSelection = true,
                PreferredAudioTrackId = GetSelectedTrackId(_player?.GetAudioTracks() ?? Array.Empty<MpvTrackInfo>()),
                RestoreSubtitleTrackSelection = true,
                PreferredSubtitleTrackId = GetSelectedTrackId(_player?.GetSubtitleTracks() ?? Array.Empty<MpvTrackInfo>())
            };
        }

        private IntPtr ResolveHostWindowHandle()
        {
            if (_context != null && _context.HostWindowHandle != 0)
            {
                return new IntPtr(_context.HostWindowHandle);
            }

            return WindowNative.GetWindowHandle(((App)Application.Current).MainWindow);
        }

        private static string GetSelectedTrackId(IReadOnlyList<MpvTrackInfo> tracks)
        {
            foreach (var track in tracks)
            {
                if (track.IsSelected)
                {
                    return track.Id;
                }
            }

            return string.Empty;
        }

        // --- Controls auto-hide ---

        private void HandlePointerActivity(Point? position, string source)
        {
            if (_teardownStarted) return;

            if (IsUserInteractingWithControls)
            {
                ShowControls(persist: true);
                return;
            }

            var now = DateTime.UtcNow;
            var positionChanged = false;
            var movedEnough = false;
            if (position.HasValue)
            {
                positionChanged = !_hasLastPointerPosition ||
                    Distance(position.Value, _lastPointerPosition) > 0.5;

                movedEnough = !_hasLastPointerPosition ||
                    Distance(position.Value, _lastPointerPosition) >= PointerMoveResetDistance;

                if (positionChanged)
                {
                    _lastPointerPosition = position.Value;
                    _hasLastPointerPosition = true;
                }
            }

            if (now < _ignorePointerUntilUtc)
            {
                if (!_isOverlayVisible && position.HasValue)
                {
                    LogPlaybackState($"OVERLAY: pointer suppressed source={source}");
                }
                return;
            }

            var wasHidden = !_isOverlayVisible;
            if (wasHidden && position.HasValue && !positionChanged)
            {
                LogPlaybackState($"OVERLAY: ignored stationary pointer source={source}");
                return;
            }

            ShowControls();

            if (wasHidden)
            {
                LogPlaybackState($"OVERLAY: pointer woke controls source={source} changed={positionChanged}");
            }

            if ((wasHidden && (!position.HasValue || positionChanged)) ||
                movedEnough ||
                (!position.HasValue && now - _lastPointerTimerRestartUtc >= PointerTimerResetThrottle))
            {
                RestartControlsHideTimer(now, $"HandlePointerActivity:{source}");
            }
        }

        private void ShowControls(bool persist = false)
        {
            EnsureCursorVisible();

            if (!_isOverlayVisible)
            {
                TopRow.Height = GridLength.Auto;
                BottomRow.Height = GridLength.Auto;
                TopBar.Visibility = Visibility.Visible;
                BottomBar.Visibility = Visibility.Visible;
                _isOverlayVisible = true;
                LogPlaybackState($"OVERLAY: shown persist={persist}");
                QueueSurfacePlacementUpdate();
            }

            if (persist || IsUserInteractingWithControls)
            {
                _controlsHideTimer?.Stop();
                CancelControlsHideSchedule();
                LogPlaybackState($"OVERLAY: timer stopped persist={persist} interacting={IsUserInteractingWithControls}");
            }
        }

        private void HideControls()
        {
            if (_player != null && _player.IsPaused)
            {
                LogPlaybackState("OVERLAY: hide skipped because player is paused");
                return;
            }

            if (ErrorOverlay.Visibility == Visibility.Visible)
            {
                LogPlaybackState("OVERLAY: hide skipped because error overlay is visible");
                return;
            }

            if (StatusOverlay.Visibility == Visibility.Visible)
            {
                LogPlaybackState("OVERLAY: hide skipped because status overlay is visible");
                return;
            }

            if (IsUserInteractingWithControls)
            {
                LogPlaybackState("OVERLAY: hide skipped because user is interacting");
                return;
            }

            if (!_isOverlayVisible) return;

            TopRow.Height = new GridLength(0);
            BottomRow.Height = new GridLength(0);
            TopBar.Visibility = Visibility.Collapsed;
            BottomBar.Visibility = Visibility.Collapsed;
            _isOverlayVisible = false;
            _ignorePointerUntilUtc = DateTime.UtcNow + PointerHideSuppressDelay;
            _controlsHideDeadlineUtc = DateTime.MinValue;
            _controlsHideTimer?.Stop();
            CancelControlsHideSchedule();
            EnsureCursorHidden();
            LogPlaybackState("OVERLAY: hidden by idle timer");
            QueueSurfacePlacementUpdate();
        }

        private void RestartControlsHideTimer([CallerMemberName] string source = "")
        {
            RestartControlsHideTimer(DateTime.UtcNow, source);
        }

        private void RestartControlsHideTimer(DateTime now, string source)
        {
            if (_controlsHideTimer == null) return;
            var wasRunning = _controlsHideTimer.IsEnabled;
            _controlsHideTimer.Stop();
            CancelControlsHideSchedule();
            _lastOverlayRestartSource = source ?? string.Empty;
            if (_player != null && _player.IsPaused)
            {
                LogPlaybackState("OVERLAY: timer restart skipped because player is paused");
                return;
            }

            if (ErrorOverlay.Visibility == Visibility.Visible)
            {
                LogPlaybackState("OVERLAY: timer restart skipped because error overlay is visible");
                return;
            }

            if (StatusOverlay.Visibility == Visibility.Visible)
            {
                LogPlaybackState("OVERLAY: timer restart skipped because status overlay is visible");
                return;
            }

            if (IsUserInteractingWithControls)
            {
                LogPlaybackState("OVERLAY: timer restart skipped because user is interacting");
                return;
            }

            _lastPointerTimerRestartUtc = now;
            _controlsHideTimer.Start();
            ScheduleControlsHideFallback();
            _controlsHideDeadlineUtc = now + ControlsHideDelay;
            LogPlaybackState($"OVERLAY: timer restart source={_lastOverlayRestartSource}");
            if (!wasRunning)
            {
                LogPlaybackState($"OVERLAY: hide timer started delayMs={ControlsHideDelay.TotalMilliseconds:0}");
            }
        }

        private bool IsUserInteractingWithControls =>
            _isOverlayPointerInteractionActive ||
            _isUserSeeking ||
            _isUserAdjustingVolume ||
            _openOverlayFlyoutCount > 0;

        private void ResumeOverlayAutoHide()
        {
            if (IsUserInteractingWithControls)
            {
                ShowControls(persist: true);
                return;
            }

            ShowControls();
            RestartControlsHideTimer();
        }

        private void EndVolumeInteraction()
        {
            _isUserAdjustingVolume = false;
            ResumeOverlayAutoHide();
        }

        private void ReleaseOverlayPointerInteraction()
        {
            if (!_isOverlayPointerInteractionActive)
            {
                return;
            }

            _isOverlayPointerInteractionActive = false;
            ResumeOverlayAutoHide();
        }

        private void WireOverlayFlyout(FlyoutBase flyout)
        {
            if (flyout == null)
            {
                return;
            }

            flyout.Opened += OverlayFlyout_Opened;
            flyout.Closed += OverlayFlyout_Closed;
        }

        private void OverlayFlyout_Opened(object sender, object e)
        {
            _openOverlayFlyoutCount++;
            LogPlaybackState("OVERLAY: flyout opened");
            ShowControls(persist: true);
        }

        private void OverlayFlyout_Closed(object sender, object e)
        {
            _openOverlayFlyoutCount = Math.Max(0, _openOverlayFlyoutCount - 1);
            LogPlaybackState("OVERLAY: flyout closed");
            ResumeOverlayAutoHide();
        }

        private bool IsOverlayControlSource(object originalSource)
        {
            if (originalSource is not DependencyObject dependencyObject)
            {
                return false;
            }

            return IsDescendantOf(dependencyObject, TopBar) ||
                   IsDescendantOf(dependencyObject, BottomBar);
        }

        private static bool IsDescendantOf(DependencyObject candidate, DependencyObject ancestor)
        {
            var current = candidate;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void EnsureCursorHidden()
        {
            if (_isCursorHidden)
            {
                return;
            }

            while (ShowCursor(false) >= 0)
            {
            }

            _isCursorHidden = true;
        }

        private void EnsureCursorVisible()
        {
            if (!_isCursorHidden)
            {
                return;
            }

            while (ShowCursor(true) < 0)
            {
            }

            _isCursorHidden = false;
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

        private Point ToScreenPoint(Point point)
        {
            var hwnd = ResolveHostWindowHandle();
            if (hwnd == IntPtr.Zero || RootGrid.XamlRoot == null)
            {
                return point;
            }

            var scale = RootGrid.XamlRoot.RasterizationScale;
            if (scale <= 0)
            {
                scale = 1.0;
            }

            var nativePoint = new NativePoint
            {
                X = (int)Math.Round(point.X * scale),
                Y = (int)Math.Round(point.Y * scale)
            };

            return ClientToScreen(hwnd, ref nativePoint)
                ? new Point(nativePoint.X, nativePoint.Y)
                : point;
        }

        private void ScheduleControlsHideFallback()
        {
            CancelControlsHideSchedule();
            var scheduleId = Interlocked.Increment(ref _controlsHideScheduleId);
            var cancellation = new CancellationTokenSource();
            _controlsHideCancellation = cancellation;
            LogPlaybackState($"OVERLAY: fallback scheduled scheduleId={scheduleId}");

            _ = AwaitControlsHideAsync(scheduleId, cancellation.Token);
        }

        private async Task AwaitControlsHideAsync(int scheduleId, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(ControlsHideDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested || scheduleId != _controlsHideScheduleId)
            {
                return;
            }

            var enqueued = DispatcherQueue.TryEnqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested || scheduleId != _controlsHideScheduleId)
                {
                    return;
                }

                LogPlaybackState($"OVERLAY: hide delay elapsed scheduleId={scheduleId}");
                HideControls();
            });

            if (!enqueued)
            {
                Log($"OVERLAY: fallback enqueue failed scheduleId={scheduleId}");
            }
        }

        private void CancelControlsHideSchedule()
        {
            var cancellation = _controlsHideCancellation;
            _controlsHideCancellation = null;
            _controlsHideDeadlineUtc = DateTime.MinValue;
            if (cancellation == null)
            {
                return;
            }

            LogPlaybackState("OVERLAY: fallback cancelled");

            try
            {
                cancellation.Cancel();
            }
            catch
            {
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        private void MaybeHideControlsFromPlaybackPulse()
        {
            if (!_isOverlayVisible ||
                _player == null ||
                _player.IsPaused ||
                IsUserInteractingWithControls ||
                ErrorOverlay.Visibility == Visibility.Visible ||
                StatusOverlay.Visibility == Visibility.Visible ||
                _controlsHideDeadlineUtc == DateTime.MinValue)
            {
                return;
            }

            if (DateTime.UtcNow < _controlsHideDeadlineUtc)
            {
                return;
            }

            LogPlaybackState("OVERLAY: hide deadline reached on playback pulse");
            HideControls();
        }

        private void LogPlaybackState(string message)
        {
            var player = _player;
            var hideDeadlineMs = _controlsHideDeadlineUtc == DateTime.MinValue
                ? "none"
                : Math.Max(0, (_controlsHideDeadlineUtc - DateTime.UtcNow).TotalMilliseconds).ToString("0");
            Log(
                $"{message}; mode={(IsLivePlayback() ? "live" : "vod")}; state={_stateMachine.State}; paused={(player?.IsPaused.ToString() ?? "null")}; buffering={(player?.IsBuffering.ToString() ?? "null")}; seekable={(player?.IsSeekable.ToString() ?? "null")}; posMs={_lastPositionMs}; durMs={_lastDurationMs}; timeshift={_isLiveTimeshiftActive}; overlayVisible={_isOverlayVisible}; cursorVisible={!_isCursorHidden}; hideDeadlineMs={hideDeadlineMs}; hideTimerRunning={(_controlsHideTimer?.IsEnabled == true)}; hideScheduleId={_controlsHideScheduleId}; interacting={IsUserInteractingWithControls}");
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
            if (IsPictureInPictureMode())
            {
                _pictureInPictureService.Close();
                return;
            }

            if (Frame != null && Frame.CanGoBack) Frame.GoBack();
        }

        private static void StopTimer(ref DispatcherTimer timer)
        {
            timer?.Stop();
        }

        [DllImport("user32.dll")]
        private static extern int ShowCursor(bool bShow);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);

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
