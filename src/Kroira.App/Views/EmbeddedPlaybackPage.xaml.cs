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
    // cancels the old session and releases native playback resources without blocking
    // the UI thread. There is no app-lifetime playback singleton.
    public sealed partial class EmbeddedPlaybackPage : Page, IRemoteNavigationPage
    {
        private static readonly TimeSpan ControlsHideDelay = TimeSpan.FromMilliseconds(2250);
        private static readonly TimeSpan PointerHideSuppressDelay = TimeSpan.FromMilliseconds(350);
        private static readonly TimeSpan PointerTimerResetThrottle = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan MenuSurfaceClickSuppressDelay = TimeSpan.FromMilliseconds(450);
        private static readonly TimeSpan StreamLoadTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan StreamBufferTimeout = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan StreamRetryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ProgressPersistInterval = TimeSpan.FromSeconds(15);
        private const int MaxRetryAttempts = 3;
        private const double PointerMoveResetDistance = 3.0;
        private static long s_nextPlaybackSessionId;
        private static int s_nextSwitchGeneration;

        private readonly PlaybackSessionStateMachine _stateMachine = new();
        private readonly PlaybackProgressCoordinator _progressCoordinator;
        private readonly ICatchupPlaybackService _catchupPlaybackService;
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
        private bool _isPointerOverControls;
        private bool _suppressSliderUpdates;
        private bool _isOverlayVisible = true;
        private bool _overlayHiddenByInactivity;
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
        private bool _windowActivationSubscribed;
        private bool _launchOverridesApplied;
        private bool _isCursorHidden;
        private bool _isWindowActive = true;
        private double _lastNonZeroVolume = 100;
        private DateTime _ignorePointerUntilUtc = DateTime.MinValue;
        private DateTime _lastPointerTimerRestartUtc = DateTime.MinValue;
        private DateTime _ignoreSurfaceClickUntilUtc = DateTime.MinValue;
        private Point _lastPointerPosition;
        private bool _hasLastPointerPosition;
        private bool _menuSurfaceInputShieldActive;
        private bool _fullscreenSurfaceInputShieldActive;
        private bool _surfaceInputShieldApplied;
        private int _activeAttemptId;
        private int _retryAttempt;
        private int _switchGeneration;
        private int _loadTimeoutAttemptId;
        private int _bufferTimeoutAttemptId;
        private int _lastOpenSucceededAttemptId = -1;
        private int _lastOpenFailedAttemptId = -1;
        private int _fullscreenTransitionGeneration;
        private string _lastPlayerWarning = string.Empty;
        private string _lastStateMessage = string.Empty;
        private PlaybackAspectMode _selectedAspectMode = PlaybackAspectMode.Automatic;
        private PlaybackSessionState _currentState = PlaybackSessionState.Idle;
        private bool _isLiveTimeshiftActive;
        private DateTime _ignoreLivePlaybackEndedUntilUtc = DateTime.MinValue;
        private long _playbackSessionId;
        private CancellationTokenSource _playbackSessionCancellation = new();
        private readonly List<FlyoutBase> _openOverlayFlyouts = new();
        private readonly HashSet<int> _failedMirrorContentIds = new();
        private FlyoutBase _activeUtilityFlyout;
        private FlyoutBase _pendingUtilityFlyout;

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
            _catchupPlaybackService = services.GetRequiredService<ICatchupPlaybackService>();
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
            TopBar.PointerEntered += OverlayChrome_PointerEntered;
            TopBar.PointerExited += OverlayChrome_PointerExited;
            BottomBar.PointerEntered += OverlayChrome_PointerEntered;
            BottomBar.PointerExited += OverlayChrome_PointerExited;

            BuildAspectMenu();
            WireOverlayFlyout(AudioTrackFlyout);
            WireOverlayFlyout(SubtitleTrackFlyout);
            WireOverlayFlyout(TracksFlyout);
            WireOverlayFlyout(AspectFlyout);
            WireOverlayFlyout(SpeedFlyout);
            WireOverlayFlyout(AudioDelayFlyout);
            WireOverlayFlyout(SubtitleDelayFlyout);
            WireOverlayFlyout(SubtitleStyleFlyout);
            WireOverlayFlyout(ZoomFlyout);
            WireOverlayFlyout(RotationFlyout);
            WireOverlayFlyout(SleepTimerFlyout);
            InitializeEnhancedPlayerUi(services);

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

            TitleText.Text = TitleForContext(_context);
            StartNewPlaybackSession();
            _windowManager = ((App)Application.Current).Services.GetRequiredService<IWindowManagerService>();
            _isWindowActive = _windowManager?.IsWindowActive ?? true;
            ApplyLaunchOptions();

            if (!IsPictureInPictureMode())
            {
                _wasFullscreenOnEnter = _windowManager.IsFullscreen;
                _windowManager.FullscreenStateChanged += WindowManager_FullscreenStateChanged;
                _windowManager.WindowActivationChanged += WindowManager_WindowActivationChanged;
                _fullscreenSubscribed = true;
                _windowActivationSubscribed = true;
            }

            UpdateFullscreenUi();
            UpdatePictureInPictureUi();
            UpdateAspectUi();
            UpdateOverlayVisibility("navigated_to");
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

        private void StartNewPlaybackSession()
        {
            CancelPlaybackSession();
            _playbackSessionCancellation.Dispose();
            _playbackSessionCancellation = new CancellationTokenSource();
            _playbackSessionId = Interlocked.Increment(ref s_nextPlaybackSessionId);
            _switchGeneration = Interlocked.Increment(ref s_nextSwitchGeneration);
            _activeAttemptId = 0;
            _lastOpenSucceededAttemptId = -1;
            _lastOpenFailedAttemptId = -1;
            _currentState = _stateMachine.State;
            _lastStateMessage = _stateMachine.Message;
            _overlayHiddenByInactivity = false;
            _failedMirrorContentIds.Clear();
        }

        private CancellationToken CurrentPlaybackSessionToken =>
            _playbackSessionCancellation?.Token ?? CancellationToken.None;

        private void CancelPlaybackSession()
        {
            try
            {
                _playbackSessionCancellation?.Cancel();
            }
            catch
            {
            }
        }

        private bool IsPlaybackSessionActive(long playbackSessionId, CancellationToken cancellationToken)
        {
            return !_teardownStarted &&
                   !cancellationToken.IsCancellationRequested &&
                   playbackSessionId == _playbackSessionId;
        }

        private bool IsCurrentAttempt(int attemptId, long playbackSessionId, CancellationToken cancellationToken)
        {
            return attemptId == _activeAttemptId &&
                   IsPlaybackSessionActive(playbackSessionId, cancellationToken);
        }

        private async Task EnsureProfileIdAsync(CancellationToken cancellationToken)
        {
            if (_context == null || _context.ProfileId > 0)
            {
                return;
            }

            try
            {
                using var scope = ((App)Application.Current).Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var profileService = scope.ServiceProvider.GetRequiredService<IProfileStateService>();
                var profileId = await profileService.GetActiveProfileIdAsync(db);
                if (!cancellationToken.IsCancellationRequested && _context != null && _context.ProfileId <= 0)
                {
                    _context.ProfileId = profileId;
                }
            }
            catch
            {
                if (!cancellationToken.IsCancellationRequested && _context != null && _context.ProfileId <= 0)
                {
                    _context.ProfileId = 1;
                }
            }
        }

        private async void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            if (_context == null) return;
            LogPlaybackState("PAGE: loaded");
            RestorePlayerKeyboardFocus(force: true);
            await TryStartPlaybackAsync();
            RestorePlayerKeyboardFocus(force: true);
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

            var playbackSessionId = _playbackSessionId;
            var cancellationToken = CurrentPlaybackSessionToken;

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
                await EnsureProfileIdAsync(cancellationToken);
                if (!IsPlaybackSessionActive(playbackSessionId, cancellationToken) || _context == null)
                {
                    return;
                }

                await LoadEnhancedPlayerStateAsync(cancellationToken);
                if (!IsPlaybackSessionActive(playbackSessionId, cancellationToken) || _context == null)
                {
                    return;
                }

                if (!await ResolveCatchupContextIfNeededAsync(cancellationToken))
                {
                    return;
                }

                var parentHwnd = ResolveHostWindowHandle();
                LogPlaybackState($"SURFACE: creating parent_hwnd=0x{parentHwnd.ToInt64():X}");
                _surface = new VideoSurface(parentHwnd, VideoHost,
                    onClick: OnVideoClick,
                    onDoubleClick: OnVideoDoubleClick,
                    onMouseMoved: OnVideoMouseMoved);
                _surface.Present(force: true, reason: "surface_created");
                ApplySurfaceInputShield("surface_created");
                LogPlaybackState($"SURFACE: created hwnd=0x{_surface.Handle.ToInt64():X}");

                _player = CreatePlayer(_surface.Handle);
                LogPlaybackState($"PLAYER: created surface_hwnd=0x{_surface.Handle.ToInt64():X}");

                EnsureTimers();
                ResetTrackMenus();
                UpdateLiveAndSeekUi();
                UpdateInteractionState();
                ClearError();
                HideStatusOverlay();

                _lastPositionMs = _context.StartPositionMs > 0
                    ? _context.StartPositionMs
                    : await _progressCoordinator.ResolveResumePositionAsync(_context);
                if (!IsPlaybackSessionActive(playbackSessionId, cancellationToken) || _player == null || _context == null)
                {
                    return;
                }

                _playbackStarted = true;
                BeginPlaybackAttempt(isRetry: false, retryReason: null, startPositionMs: _lastPositionMs);
                StopInactivityTimer("channel_switch");
            }
            catch (Exception ex)
            {
                LogStructuredPlayback("startup_failed", $"message={SanitizeForLog(ex.Message)}");
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
            LogPlaybackState("PAGE: unloaded");
            TeardownPlayback();
        }

        private void TeardownPlayback()
        {
            if (_teardownStarted && _player == null && _surface == null)
            {
                return;
            }

            LogPlaybackState("PAGE: teardown begin");
            _teardownStarted = true;
            _recoveryInProgress = false;
            CancelPlaybackSession();
            VideoHost.SizeChanged -= VideoHost_StartSizeChanged;

            if (_fullscreenSubscribed && _windowManager != null)
            {
                _windowManager.FullscreenStateChanged -= WindowManager_FullscreenStateChanged;
                _fullscreenSubscribed = false;
            }

            if (_windowActivationSubscribed && _windowManager != null)
            {
                _windowManager.WindowActivationChanged -= WindowManager_WindowActivationChanged;
                _windowActivationSubscribed = false;
            }

            _controlsHideTimer?.Stop();
            StopTimer(ref _loadTimeoutTimer);
            StopTimer(ref _bufferTimeoutTimer);
            StopTimer(ref _progressPersistTimer);
            _sleepTimer?.Stop();
            _sleepDeadline = null;
            if (_zapBannerTimer != null)
            {
                _zapBannerTimer.Stop();
            }
            _overlayHiddenByInactivity = false;
            EnsureCursorVisible();
            DismissOverlayFlyoutsForTeardown();
            SetMenuSurfaceInputShield(false, "teardown");
            SetFullscreenSurfaceInputShield(false, "teardown");

            if (_context != null)
            {
                _ = PersistProgressOnTeardownAsync(_context, _lastPositionMs, _lastDurationMs);
            }

            DetachAndDisposePlayer();

            if (_surface != null)
            {
                var surface = _surface;
                _surface = null;
                var hwnd = surface.Handle;
                LogPlaybackState($"SURFACE: disposing hwnd=0x{hwnd.ToInt64():X}");
                try { surface.Dispose(); } catch { }
            }

            ResetTrackMenus();
            _stateMachine.Reset();
            LogPlaybackState("PAGE: teardown complete");
        }

        private async Task PersistProgressOnTeardownAsync(PlaybackLaunchContext context, long positionMs, long durationMs)
        {
            try
            {
                await _progressCoordinator.PersistAsync(context, positionMs, durationMs, force: true);
            }
            catch
            {
            }
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
            var allowHide = EvaluateAutoHideEligibility("timer_elapsed", out var denyReason);
            _controlsHideTimer?.Stop();
            LogStructuredPlayback(
                "inactivity_timer_disarmed",
                $"cause=timer_elapsed; timer_armed={BoolToLog(_controlsHideTimer?.IsEnabled == true)}; deny_reason={denyReason}");

            if (allowHide)
            {
                _overlayHiddenByInactivity = true;
                LogPlaybackState("OVERLAY: hide timer tick");
                HideControls();
                return;
            }

            _overlayHiddenByInactivity = false;
            LogPlaybackState($"OVERLAY: hide timer denied reason={denyReason}");
            UpdateOverlayVisibility("timer_elapsed_denied");
        }

        private void LoadTimeoutTimer_Tick(object sender, object e)
        {
            _loadTimeoutTimer?.Stop();
            if (_teardownStarted || _player == null) return;
            if (_loadTimeoutAttemptId != _activeAttemptId) return;

            LogStructuredPlayback("timeout_reason", "reason=open_timeout");
            _ = AttemptRecoveryAsync("Stream timed out while starting.", _activeAttemptId);
        }

        private void BufferTimeoutTimer_Tick(object sender, object e)
        {
            _bufferTimeoutTimer?.Stop();
            if (_teardownStarted || _player == null) return;
            if (_bufferTimeoutAttemptId != _activeAttemptId) return;

            LogStructuredPlayback("timeout_reason", "reason=buffer_timeout");
            _ = AttemptRecoveryAsync("Stream stalled while buffering.", _activeAttemptId);
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
                LogPlaybackState($"LIVECTL: start begin retry={(isRetry ? "true" : "false")} reason={retryReason ?? "direct"}");
            }

            ResetTrackMenus();
            ClearError();
            StopBufferTimeout();
            _overlayHiddenByInactivity = false;

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
            UpdateOverlayVisibility(isRetry ? "retry_open_started" : "open_started");
            LogStructuredPlayback(
                "open_started",
                $"attempt_id={_activeAttemptId}; retry={(isRetry ? "true" : "false")}; reason={SanitizeForLog(retryReason ?? "direct")}; start_position_ms={startPositionMs}");
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
            var player = new MpvPlayer(DispatcherQueue.GetForCurrentThread(), surfaceHandle, _context?.ProxyUrl);
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
            LogStructuredPlayback("player_created", $"surface_hwnd=0x{surfaceHandle.ToInt64():X}");
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
            LogPlaybackState("PLAYER: disposing");
            try { player.Stop(); } catch { }
            try { player.Dispose(); } catch { }
            LogPlaybackState("PLAYER: disposed");
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
            _overlayHiddenByInactivity = false;
            DetachAndDisposePlayer();
            _player = CreatePlayer(_surface.Handle);
            ResetTrackMenus();
            ClearError();
            HideStatusOverlay();
            BeginPlaybackAttempt(isRetry: false, retryReason: $"go-live:{reason}", startPositionMs: 0);
            StopInactivityTimer("click");
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

            var previousState = _stateMachine.State;
            var openSucceeded = false;
            if (_lastOpenSucceededAttemptId != _activeAttemptId)
            {
                _lastOpenSucceededAttemptId = _activeAttemptId;
                openSucceeded = true;
                LogStructuredPlayback("open_succeeded", $"attempt_id={_activeAttemptId}");
            }

            if (previousState != PlaybackSessionState.Playing)
            {
                _stateMachine.SetPlaying();
            }

            if (openSucceeded)
            {
                _failedMirrorContentIds.Clear();
                _ = MarkOperationalPlaybackSucceededAsync();
                if (IsLivePlayback())
                {
                    LogPlaybackState("LIVECTL: start completed");
                }
                ResetInactivityTimer("open_succeeded");
            }
            else if (previousState == PlaybackSessionState.Paused)
            {
                ResetInactivityTimer("resumed");
            }
            else if (previousState != PlaybackSessionState.Playing)
            {
                ResetInactivityTimer("entered_playing");
            }

            UpdateInteractionState();
        }

        private async Task AttemptRecoveryAsync(string reason, int attemptId)
        {
            var playbackSessionId = _playbackSessionId;
            var cancellationToken = CurrentPlaybackSessionToken;
            if (!IsCurrentAttempt(attemptId, playbackSessionId, cancellationToken) ||
                _player == null ||
                _context == null ||
                _recoveryInProgress)
            {
                return;
            }

            LogPlaybackState($"RECOVERY: requested reason={reason}");

            if (_context?.ContentId > 0)
            {
                _failedMirrorContentIds.Add(_context.ContentId);
            }

            if (_retryAttempt >= 1 &&
                await TrySwitchToFallbackMirrorAsync(reason, playbackSessionId, cancellationToken))
            {
                _recoveryInProgress = false;
                return;
            }

            if (_retryAttempt >= MaxRetryAttempts)
            {
                await MarkOperationalPlaybackFailureAsync(reason, allowFallback: false);
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
            if (!IsCurrentAttempt(attemptId, playbackSessionId, cancellationToken))
            {
                return;
            }

            try
            {
                try
                {
                    await Task.Delay(StreamRetryDelay, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (!IsCurrentAttempt(attemptId, playbackSessionId, cancellationToken) ||
                    _player == null ||
                    _context == null)
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

        private async Task<bool> TrySwitchToFallbackMirrorAsync(string reason, long playbackSessionId, CancellationToken cancellationToken)
        {
            var fallback = await MarkOperationalPlaybackFailureAsync(reason, allowFallback: true);
            if (fallback == null ||
                !IsPlaybackSessionActive(playbackSessionId, cancellationToken))
            {
                return false;
            }

            if (_context == null)
            {
                return false;
            }

            _context.ContentId = fallback.ContentId;
            _context.LogicalContentKey = fallback.LogicalContentKey;
            _context.PreferredSourceProfileId = fallback.SourceProfileId;
            _context.StreamUrl = fallback.StreamUrl;
            _context.ProxyScope = fallback.Routing.Scope;
            _context.ProxyUrl = fallback.Routing.UseProxy ? fallback.Routing.ProxyUrl : string.Empty;
            _context.RoutingSummary = fallback.Routing.Summary;
            _context.MirrorCandidateCount = fallback.CandidateCount;
            _context.OperationalSummary = fallback.RecoverySummary;
            _context.LiveStreamUrl = fallback.StreamUrl;
            _resolvedRoutingSummary = fallback.Routing.Summary;

            if (IsCatchupPlayback())
            {
                var catchupContext = _context.Clone();
                catchupContext.CatalogStreamUrl = fallback.CatalogStreamUrl;
                catchupContext.StreamUrl = fallback.StreamUrl;
                catchupContext.LiveStreamUrl = fallback.StreamUrl;
                var resolution = await ResolveCatchupContextAsync(catchupContext, cancellationToken);
                if (!resolution.Success)
                {
                    _context = catchupContext;
                    RefreshInfoPanel();
                    UpdatePlaybackHint();
                    ShowFatalError(resolution.Message);
                    return true;
                }

                _context = catchupContext;
            }
            else if (IsChannelPlayback())
            {
                ResetCatchupContext(_context, fallback.StreamUrl);
            }

            await LoadEnhancedPlayerStateAsync(cancellationToken);
            if (IsCatchupPlayback() && !await ResolveCatchupContextIfNeededAsync(cancellationToken))
            {
                return true;
            }

            if (!IsPlaybackSessionActive(playbackSessionId, cancellationToken))
            {
                return true;
            }

            ShowZapBanner(TitleText.Text, string.IsNullOrWhiteSpace(fallback.RecoverySummary) ? "Recovered via backup mirror." : fallback.RecoverySummary);
            RestartPlayerSession("mirror_fallback", IsLivePlayback() ? 0 : GetRetryPositionMs());
            return true;
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

        private async Task MarkOperationalPlaybackSucceededAsync()
        {
            if (_context == null || _teardownStarted)
            {
                return;
            }

            try
            {
                using var scope = ((App)Application.Current).Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var contentOperationalService = scope.ServiceProvider.GetRequiredService<IContentOperationalService>();
                await contentOperationalService.MarkPlaybackSucceededAsync(db, _context);
            }
            catch
            {
            }
        }

        private async Task<OperationalPlaybackResolution?> MarkOperationalPlaybackFailureAsync(string reason, bool allowFallback)
        {
            if (_context == null || _teardownStarted)
            {
                return null;
            }

            try
            {
                using var scope = ((App)Application.Current).Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var contentOperationalService = scope.ServiceProvider.GetRequiredService<IContentOperationalService>();
                return await contentOperationalService.MarkPlaybackFailedAsync(
                    db,
                    _context,
                    reason,
                    allowFallback ? _failedMirrorContentIds : Array.Empty<int>());
            }
            catch
            {
                return null;
            }
        }

        private void OnPlaybackStateChanged(PlaybackSessionStateSnapshot snapshot)
        {
            if (_teardownStarted)
            {
                return;
            }

            if (_currentState != snapshot.State || !string.Equals(_lastStateMessage, snapshot.Message, StringComparison.Ordinal))
            {
                LogStructuredPlayback(
                    "state_transition",
                    $"from={_currentState}; to={snapshot.State}; retry_attempt={snapshot.RetryAttempt}; message={SanitizeForLog(snapshot.Message)}");
                _currentState = snapshot.State;
                _lastStateMessage = snapshot.Message ?? string.Empty;
            }

            switch (snapshot.State)
            {
                case PlaybackSessionState.Opening:
                    ShowStatusOverlay(snapshot.RetryAttempt > 0 ? "Reconnecting" : "Loading", snapshot.Message);
                    ShowControls(persist: true);
                    break;
                case PlaybackSessionState.Buffering:
                    ShowStatusOverlay("Buffering", snapshot.Message);
                    ShowControls(persist: true, cause: "buffering");
                    break;
                case PlaybackSessionState.Paused:
                    HideStatusOverlay();
                    ShowControls(persist: true, cause: "paused");
                    break;
                case PlaybackSessionState.Error:
                    HideStatusOverlay();
                    ShowError(snapshot.Message);
                    ShowControls(persist: true, cause: "error");
                    break;
                default:
                    HideStatusOverlay();
                    ClearError();
                    break;
            }

            UpdateInteractionState();
            RefreshInfoPanel();
            UpdatePlaybackHint();
            UpdateOverlayVisibility($"state_{snapshot.State.ToString().ToLowerInvariant()}");
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

            RefreshInfoPanel();
            UpdatePlaybackHint();
        }

        private void OnDurationChanged(TimeSpan duration)
        {
            if (_teardownStarted) return;
            _lastDurationMs = (long)duration.TotalMilliseconds;
            DurationText.Text = FormatTime(duration);
            UpdateLiveAndSeekUi();
            RefreshInfoPanel();
            UpdatePlaybackHint();
        }

        private async void OnPauseChanged(bool isPaused)
        {
            if (_teardownStarted) return;
            var playbackSessionId = _playbackSessionId;
            var cancellationToken = CurrentPlaybackSessionToken;

            PlayPauseIcon.Glyph = isPaused ? "\uE768" : "\uE769";
            ToolTipService.SetToolTip(PlayPauseButton, isPaused ? "Play" : "Pause");
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
                if (!IsPlaybackSessionActive(playbackSessionId, cancellationToken))
                {
                    return;
                }
            }
            else if (!isPaused && _player?.IsBuffering != true)
            {
                MarkPlaybackActive();
            }

            LogPlaybackState($"LIVECTL: pause changed paused={isPaused}");
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
                _stateMachine.SetBuffering();
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

            LogPlaybackState("SURFACE: file loaded present");
            _surface?.Present(force: true, reason: "file_loaded");
            _ = RefreshTrackMenusAsync();
            RefreshInfoPanel();
            LogPlaybackState("LIVECTL: file loaded");
        }

        private void OnOutputReady()
        {
            if (_teardownStarted) return;

            LogPlaybackState("SURFACE: output ready present");
            _surface?.Present(force: true, reason: "output_ready");
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
            RefreshInfoPanel();
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
            var playbackSessionId = _playbackSessionId;
            var cancellationToken = CurrentPlaybackSessionToken;

            LogPlaybackState($"LIVECTL: playback ended signaled reason={endInfo?.Reason} error={endInfo?.ErrorCode ?? 0}");

            if (IsLivePlayback())
            {
                var nowUtc = DateTime.UtcNow;
                if (_stateMachine.State == PlaybackSessionState.Opening ||
                    nowUtc < _ignoreLivePlaybackEndedUntilUtc)
                {
                    LogPlaybackState(
                        $"LIVECTL: ignored playback-ended during live transition graceRemainingMs={Math.Max(0, (_ignoreLivePlaybackEndedUntilUtc - nowUtc).TotalMilliseconds):0}");
                    return;
                }
            }

            if (_stateMachine.State == PlaybackSessionState.Opening)
            {
                return;
            }

            if (!IsPlaybackComplete())
            {
                await AttemptRecoveryAsync("Stream ended unexpectedly.", _activeAttemptId);
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
            if (!IsPlaybackSessionActive(playbackSessionId, cancellationToken))
            {
                return;
            }

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
            ResetInactivityTimer("click");
        }

        private async void GoLive_Click(object sender, RoutedEventArgs e)
        {
            if (_teardownStarted) return;
            if (IsCatchupPlayback())
            {
                await ReturnToLivePlaybackAsync("button");
            }
            else
            {
                GoToLiveEdge("button");
            }

            ShowControls(cause: "click");
            ResetInactivityTimer("click");
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_teardownStarted) return;
            _player?.Stop();
            NavigateBack();
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            RequestFullscreenToggle("button");
        }

        private async void PictureInPicture_Click(object sender, RoutedEventArgs e)
        {
            if (_teardownStarted ||
                _context == null ||
                !CanUseFeature(EntitlementFeatureKeys.PlaybackPictureInPicture))
            {
                return;
            }

            var playbackSessionId = _playbackSessionId;
            var cancellationToken = CurrentPlaybackSessionToken;

            if (IsPictureInPictureMode())
            {
                await RestoreFromPictureInPictureAsync();
            }
            else
            {
                await EnterPictureInPictureAsync();
            }

            if (!IsPlaybackSessionActive(playbackSessionId, cancellationToken))
            {
                return;
            }

            ResetInactivityTimer("click");
        }

        private void Page_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;
            HandlePointerActivity(ToScreenPoint(e.GetCurrentPoint(RootGrid).Position), "page");
        }

        private void Page_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;

            if (_toolsPanelOpen && !IsToolsPanelInteractionSource(e.OriginalSource))
            {
                CloseToolsPanel();
            }

            if (IsOverlayControlSource(e.OriginalSource))
            {
                _isOverlayPointerInteractionActive = true;
                ShowControls(persist: true, cause: "click");
                return;
            }

            if (AreFlyoutMenusOpen)
            {
                ShowControls(persist: true, cause: "menu_click");
                return;
            }

            ShowControls(cause: "click");
            ResetInactivityTimer("click");
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
            if (HandleEnhancedKeyDown(e)) return;
            ShowControls(cause: "key_input");
            ResetInactivityTimer("key_input");
        }

        private void OnVideoClick()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_teardownStarted || DateTime.UtcNow < _ignoreSurfaceClickUntilUtc) return;
                if (_toolsPanelOpen)
                {
                    CloseToolsPanel();
                    ShowControls(cause: "click");
                    ResetInactivityTimer("click");
                    return;
                }

                if (AreFlyoutMenusOpen) return;
                TogglePlayPauseOrLive();
                RestorePlayerKeyboardFocus(force: true);
                ShowControls(cause: "click");
                ResetInactivityTimer("click");
            });
        }

        private void OnVideoDoubleClick()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_teardownStarted ||
                    DateTime.UtcNow < _ignoreSurfaceClickUntilUtc ||
                    IsPictureInPictureMode() ||
                    !CanUseFeature(EntitlementFeatureKeys.PlaybackFullscreen)) return;
                if (_toolsPanelOpen)
                {
                    CloseToolsPanel();
                    ShowControls(cause: "click");
                    ResetInactivityTimer("click");
                    return;
                }

                if (AreFlyoutMenusOpen) return;
                RequestFullscreenToggle("double_click");
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
            ShowControls(persist: true, cause: "click");
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
            var playbackSessionId = _playbackSessionId;
            var cancellationToken = CurrentPlaybackSessionToken;
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
            if (!IsPlaybackSessionActive(playbackSessionId, cancellationToken))
            {
                return;
            }

            ResetInactivityTimer("click");
        }

        private void VolumeSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted) return;
            _isUserAdjustingVolume = true;
            ShowControls(persist: true, cause: "click");
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
            _ = SavePlayerPreferencesAsync();
            RefreshInfoPanel();
            ResetInactivityTimer("click");
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
                ShowControls(persist: true, cause: "click");
            }
            else
            {
                ResetInactivityTimer("click");
            }

            RefreshInfoPanel();
        }

        private void WindowManager_FullscreenStateChanged(object sender, EventArgs e)
        {
            if (_teardownStarted) return;
            Interlocked.Increment(ref _fullscreenTransitionGeneration);
            SetFullscreenSurfaceInputShield(false, "fullscreen_changed");
            LogPlaybackState($"WINDOW: fullscreen changed active={BoolToLog(_windowManager?.IsFullscreen == true)}");
            _surface?.Present(force: true, reason: "fullscreen_changed");
            UpdateFullscreenUi();
        }

        private void WindowManager_WindowActivationChanged(object sender, EventArgs e)
        {
            if (_teardownStarted || _windowManager == null)
            {
                return;
            }

            _isWindowActive = _windowManager.IsWindowActive;
            if (_isWindowActive)
            {
                ResetInactivityTimer("focus_gained");
            }
            else
            {
                StopInactivityTimer("focus_lost");
            }
        }

        private void OverlayChrome_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted)
            {
                return;
            }

            _isPointerOverControls = true;
            ShowControls(persist: true, cause: "pointer_move");
        }

        private void OverlayChrome_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (_teardownStarted)
            {
                return;
            }

            _isPointerOverControls = false;
            ResumeOverlayAutoHide("pointer_move");
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
            _ = SavePlayerPreferencesAsync();
            ResetInactivityTimer("click");
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
            _ = SavePlayerPreferencesAsync();
            ResetInactivityTimer("click");
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
            BuildToolsFlyout();
            _ = SavePlayerPreferencesAsync();
            RefreshInfoPanel();
            ResetInactivityTimer("click");
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

        private void RequestFullscreenToggle(string source)
        {
            if (_teardownStarted ||
                IsPictureInPictureMode() ||
                !CanUseFeature(EntitlementFeatureKeys.PlaybackFullscreen) ||
                _windowManager == null)
            {
                return;
            }

            LogPlaybackState($"WINDOW: fullscreen toggle requested source={source}");
            SetFullscreenSurfaceInputShield(true, $"fullscreen_{source}_begin");
            SuppressSurfaceClicks(TimeSpan.FromMilliseconds(700));
            var generation = Interlocked.Increment(ref _fullscreenTransitionGeneration);
            _windowManager.ToggleFullscreen();
            _ = ReleaseFullscreenShieldFallbackAsync(generation, source);
            ResetInactivityTimer("click");
        }

        private async Task ReleaseFullscreenShieldFallbackAsync(int generation, string source)
        {
            try
            {
                await Task.Delay(900);
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_teardownStarted || generation != _fullscreenTransitionGeneration)
                    {
                        return;
                    }

                    LogPlaybackState($"WINDOW: fullscreen shield fallback release source={source}");
                    SetFullscreenSurfaceInputShield(false, $"fullscreen_{source}_fallback");
                    QueueSurfacePlacementUpdate();
                });
            }
            catch
            {
            }
        }

        private void TogglePlayPauseOrLive()
        {
            if (_player == null) return;
            if (_stateMachine.State == PlaybackSessionState.Opening)
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
            if (_stateMachine.State == PlaybackSessionState.Opening)
            {
                LogPlaybackState($"LIVECTL: go-live ignored while state={_stateMachine.State} reason={reason}");
                return;
            }

            _isLiveTimeshiftActive = false;
            LogPlaybackState($"LIVECTL: go-live requested reason={reason}; strategy=recreate-player");
            ResetLiveEdgePlayerSession(reason);

            UpdateLiveAndSeekUi();
        }

        private bool IsChannelPlayback()
        {
            return _context?.ContentType == PlaybackContentType.Channel;
        }

        private bool IsCatchupPlayback()
        {
            return IsChannelPlayback() && _context?.PlaybackMode == CatchupPlaybackMode.Catchup;
        }

        private bool IsLivePlayback()
        {
            return IsChannelPlayback() && !IsCatchupPlayback();
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
                          _stateMachine.State != PlaybackSessionState.Opening;

            TimelineSlider.IsEnabled = canSeek;
            LivePill.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
            CatchupPill.Visibility = IsCatchupPlayback() ? Visibility.Visible : Visibility.Collapsed;
            GoLiveButton.IsEnabled = IsChannelPlayback() && _player != null;

            ToolTipService.SetToolTip(
                TimelineSlider,
                isLive
                    ? canSeek ? "Seek within live buffer" : "Live stream has no seekable buffer"
                    : canSeek ? "Seek" : "Seeking unavailable");

            UpdateEnhancedControlState();
        }

        private void UpdateInteractionState()
        {
            var isReady = _player != null && !_teardownStarted;
            var trackSwitchEnabled = isReady &&
                                     _stateMachine.State != PlaybackSessionState.Opening;
            var canUseFullscreen = CanUseFeature(EntitlementFeatureKeys.PlaybackFullscreen);
            var canUsePictureInPicture = CanUseFeature(EntitlementFeatureKeys.PlaybackPictureInPicture);
            var canUseAudioTracks = CanUseFeature(EntitlementFeatureKeys.PlaybackAudioTrackSelection);
            var canUseSubtitles = CanUseFeature(EntitlementFeatureKeys.PlaybackSubtitleTrackSelection);
            var canUseAspectControls = CanUseFeature(EntitlementFeatureKeys.PlaybackAspectControls);
            var isPictureInPicture = IsPictureInPictureMode();

            PlayPauseButton.IsEnabled = isReady && _stateMachine.State != PlaybackSessionState.Opening;
            StopButton.IsEnabled = isReady;
            PictureInPictureButton.IsEnabled = isReady && canUsePictureInPicture;
            PictureInPictureButton.Visibility = canUsePictureInPicture ? Visibility.Visible : Visibility.Collapsed;
            FullscreenButton.IsEnabled = isReady && canUseFullscreen && !isPictureInPicture;
            FullscreenButton.Visibility = canUseFullscreen && !isPictureInPicture ? Visibility.Visible : Visibility.Collapsed;
            MuteButton.IsEnabled = isReady;
            VolumeSlider.IsEnabled = isReady;
            AudioTrackButton.IsEnabled = trackSwitchEnabled && canUseAudioTracks;
            SubtitleTrackButton.IsEnabled = trackSwitchEnabled && canUseSubtitles;
            TracksButton.IsEnabled = trackSwitchEnabled && (canUseAudioTracks || canUseSubtitles);
            AspectButton.IsEnabled = isReady && canUseAspectControls;
            AspectButton.Visibility = canUseAspectControls ? Visibility.Visible : Visibility.Collapsed;
            BottomGuideButton.IsEnabled = isReady && IsChannelPlayback();
            BottomChannelListButton.IsEnabled = isReady && IsChannelPlayback();
            UpdateLiveAndSeekUi();
            UpdateEnhancedControlState();
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

            var audioTracks = _player.GetAudioTracks();
            var subtitleTracks = _player.GetSubtitleTracks();
            PopulateAudioTrackMenu(audioTracks);
            PopulateSubtitleTrackMenu(subtitleTracks);
            PopulateCombinedTrackMenu(audioTracks, subtitleTracks);
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

        private void PopulateCombinedTrackMenu(IReadOnlyList<MpvTrackInfo> audioTracks, IReadOnlyList<MpvTrackInfo> subtitleTracks)
        {
            TracksFlyout.Items.Clear();

            var canUseAudioTracks = CanUseFeature(EntitlementFeatureKeys.PlaybackAudioTrackSelection);
            var canUseSubtitleTracks = CanUseFeature(EntitlementFeatureKeys.PlaybackSubtitleTrackSelection);
            var hasAudioChoices = canUseAudioTracks && audioTracks.Count > 1;
            var hasSubtitleChoices = canUseSubtitleTracks && subtitleTracks.Count > 0;
            if (!hasAudioChoices && !hasSubtitleChoices)
            {
                TracksButton.Visibility = Visibility.Collapsed;
                return;
            }

            if (hasAudioChoices)
            {
                var audioMenu = new MenuFlyoutSubItem { Text = "Audio" };
                foreach (var track in audioTracks)
                {
                    var item = new ToggleMenuFlyoutItem
                    {
                        Text = track.DisplayName,
                        Tag = track.Id,
                        IsChecked = track.IsSelected
                    };

                    item.Click += AudioTrackItem_Click;
                    audioMenu.Items.Add(item);
                }

                TracksFlyout.Items.Add(audioMenu);
            }

            if (hasSubtitleChoices)
            {
                var subtitleMenu = new MenuFlyoutSubItem { Text = "Subtitles" };
                var offItem = new ToggleMenuFlyoutItem
                {
                    Text = "Off",
                    Tag = null,
                    IsChecked = true
                };

                offItem.Click += SubtitleTrackItem_Click;
                subtitleMenu.Items.Add(offItem);

                foreach (var track in subtitleTracks)
                {
                    var item = new ToggleMenuFlyoutItem
                    {
                        Text = track.DisplayName,
                        Tag = track.Id,
                        IsChecked = track.IsSelected
                    };

                    item.Click += SubtitleTrackItem_Click;
                    subtitleMenu.Items.Add(item);
                }

                var selectedSubtitleTrack = FindSelectedTrack(subtitleTracks);
                offItem.IsChecked = selectedSubtitleTrack == null;

                if (TracksFlyout.Items.Count > 0)
                {
                    TracksFlyout.Items.Add(new MenuFlyoutSeparator());
                }

                TracksFlyout.Items.Add(subtitleMenu);
            }

            var selectedAudioTrack = FindSelectedTrack(audioTracks);
            var selectedSubtitle = FindSelectedTrack(subtitleTracks);
            var tooltip = selectedAudioTrack != null && selectedSubtitle != null
                ? $"Audio: {selectedAudioTrack.DisplayName} | Subs: {selectedSubtitle.DisplayName}"
                : selectedAudioTrack != null
                    ? $"Audio: {selectedAudioTrack.DisplayName}"
                    : selectedSubtitle != null
                        ? $"Subtitles: {selectedSubtitle.DisplayName}"
                        : "Audio and subtitles";

            TracksButton.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(TracksButton, tooltip);
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
            TracksFlyout.Items.Clear();
            AudioTrackButton.Visibility = Visibility.Collapsed;
            SubtitleTrackButton.Visibility = Visibility.Collapsed;
            TracksButton.Visibility = Visibility.Collapsed;
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
            RefreshSpeedUi();
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
            _player.SetPlaybackSpeed(_context.InitialPlaybackSpeed);
            _player.SetAudioDelaySeconds(_context.AudioDelaySeconds);
            _player.SetSubtitleDelaySeconds(_context.SubtitleDelaySeconds);
            _player.SetSubtitleScale(_context.SubtitleScale);
            _player.SetSubtitlePosition(_context.SubtitlePosition);
            _player.SetDeinterlace(_context.Deinterlace);

            if (_context.RestoreAudioTrackSelection && !string.IsNullOrWhiteSpace(_context.PreferredAudioTrackId))
            {
                _player.SelectAudioTrack(_context.PreferredAudioTrackId);
            }

            if (_context.RestoreSubtitleTrackSelection)
            {
                _player.SelectSubtitleTrack(_context.PreferredSubtitleTrackId);
            }
            else if (!_context.SubtitlesEnabled)
            {
                _player.SelectSubtitleTrack(string.Empty);
            }

            if (_context.StartPaused)
            {
                _player.Pause();
            }

            RefreshSpeedUi();
            RefreshToolToggleStates();
            RefreshInfoPanel();
            _launchOverridesApplied = true;
            _ = RefreshTrackMenusAsync();
        }

        private async Task EnterPictureInPictureAsync()
        {
            if (_context == null || _player == null)
            {
                return;
            }

            var playbackSessionId = _playbackSessionId;
            var cancellationToken = CurrentPlaybackSessionToken;

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
            if (!IsPlaybackSessionActive(playbackSessionId, cancellationToken))
            {
                return;
            }

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

            var playbackSessionId = _playbackSessionId;
            var cancellationToken = CurrentPlaybackSessionToken;

            var shouldResume = !_player.IsPaused;
            if (shouldResume)
            {
                _player.Pause();
            }

            await PersistProgressAsync(force: true);
            if (!IsPlaybackSessionActive(playbackSessionId, cancellationToken))
            {
                return;
            }

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
                LogicalContentKey = _context?.LogicalContentKey ?? string.Empty,
                PreferredSourceProfileId = _context?.PreferredSourceProfileId ?? 0,
                CatalogStreamUrl = _context?.CatalogStreamUrl ?? string.Empty,
                StreamUrl = _context?.StreamUrl ?? string.Empty,
                LiveStreamUrl = _context?.LiveStreamUrl ?? string.Empty,
                ProxyScope = _context?.ProxyScope ?? SourceProxyScope.Disabled,
                ProxyUrl = _context?.ProxyUrl ?? string.Empty,
                RoutingSummary = _context?.RoutingSummary ?? string.Empty,
                OperationalSummary = _context?.OperationalSummary ?? string.Empty,
                MirrorCandidateCount = _context?.MirrorCandidateCount ?? 0,
                StartPositionMs = Math.Max(_lastPositionMs, 0),
                OpenInPictureInPicture = openInPictureInPicture,
                StartPaused = startPaused,
                InitialVolume = VolumeSlider.Value,
                IsMuted = _isMuted,
                InitialAspectMode = _selectedAspectMode,
                InitialPlaybackSpeed = _player?.PlaybackSpeed ?? _playerPreferences.PlaybackSpeed,
                AudioDelaySeconds = _player?.AudioDelaySeconds ?? _playerPreferences.AudioDelaySeconds,
                SubtitleDelaySeconds = _player?.SubtitleDelaySeconds ?? _playerPreferences.SubtitleDelaySeconds,
                SubtitleScale = _player?.SubtitleScale ?? _playerPreferences.SubtitleScale,
                SubtitlePosition = _player?.SubtitlePosition ?? _playerPreferences.SubtitlePosition,
                SubtitlesEnabled = !string.IsNullOrWhiteSpace(GetSelectedTrackId(_player?.GetSubtitleTracks() ?? Array.Empty<MpvTrackInfo>())),
                Deinterlace = _player?.IsDeinterlaceEnabled ?? _playerPreferences.Deinterlace,
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
                ShowControls(persist: true, cause: "pointer_move");
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

            if (position.HasValue && !movedEnough)
            {
                LogPlaybackState($"OVERLAY: ignored sub-threshold pointer source={source}");
                return;
            }

            if (!position.HasValue && now - _lastPointerTimerRestartUtc < PointerTimerResetThrottle)
            {
                return;
            }

            ShowControls(cause: "pointer_move");
            if (wasHidden)
            {
                LogPlaybackState($"OVERLAY: pointer woke controls source={source} changed={positionChanged}");
            }

            ResetInactivityTimer("pointer_move", now);
        }

        private void ShowControls(bool persist = false, string cause = "show_controls")
        {
            _overlayHiddenByInactivity = false;
            if (persist || ShouldForceOverlayVisible())
            {
                StopInactivityTimer(cause);
                return;
            }

            UpdateOverlayVisibility(cause);
        }

        private void HideControls()
        {
            if (!CanAutoHideOverlay())
            {
                _overlayHiddenByInactivity = false;
            }

            UpdateOverlayVisibility("idle_timeout");
        }

        private void RestartControlsHideTimer([CallerMemberName] string source = "")
        {
            ResetInactivityTimer(source, DateTime.UtcNow);
        }

        private void RestartControlsHideTimer(DateTime now, string source)
        {
            ResetInactivityTimer(source, now);
        }

        private void ResetInactivityTimer(string source)
        {
            ResetInactivityTimer(source, DateTime.UtcNow);
        }

        private void ResetInactivityTimer(string source, DateTime now)
        {
            if (_controlsHideTimer == null)
            {
                return;
            }

            _overlayHiddenByInactivity = false;
            var wasArmed = _controlsHideTimer.IsEnabled;
            _controlsHideTimer.Stop();
            if (CanAutoHideOverlay())
            {
                _lastPointerTimerRestartUtc = now;
                _controlsHideTimer.Start();
                LogStructuredPlayback(
                    wasArmed ? "inactivity_timer_reset" : "inactivity_timer_armed",
                    $"cause={SanitizeForLog(source)}; timer_armed={BoolToLog(_controlsHideTimer.IsEnabled)}; deny_reason=none");
            }
            else
            {
                var denyReason = GetAutoHideDenyReason(timerArmed: false);
                LogStructuredPlayback(
                    "inactivity_timer_disarmed",
                    $"cause={SanitizeForLog(source)}; timer_armed={BoolToLog(_controlsHideTimer.IsEnabled)}; deny_reason={denyReason}");
            }

            EvaluateAutoHideEligibility($"timer_{source}");
            UpdateOverlayVisibility(source);
        }

        private void StopInactivityTimer(string source)
        {
            _controlsHideTimer?.Stop();
            _overlayHiddenByInactivity = false;
            LogStructuredPlayback(
                "inactivity_timer_disarmed",
                $"cause={SanitizeForLog(source)}; timer_armed={BoolToLog(_controlsHideTimer?.IsEnabled == true)}; deny_reason={GetAutoHideDenyReason(timerArmed: false)}");
            EvaluateAutoHideEligibility($"timer_{source}");
            UpdateOverlayVisibility(source);
        }

        private bool CanAutoHideOverlay()
        {
            return string.Equals(GetAutoHideDenyReason(timerArmed: true), "none", StringComparison.Ordinal);
        }

        private bool ShouldForceOverlayVisible()
        {
            return _stateMachine.State != PlaybackSessionState.Playing ||
                   _isPointerOverControls ||
                   IsMenuOpen ||
                   IsUserInteractingWithControls ||
                   !_isWindowActive;
        }

        private void UpdateOverlayVisibility(string reason)
        {
            if (_teardownStarted)
            {
                return;
            }

            if (ShouldForceOverlayVisible())
            {
                _overlayHiddenByInactivity = false;
                _controlsHideTimer?.Stop();
            }

            ApplyOverlayVisibility(ShouldForceOverlayVisible() || !_overlayHiddenByInactivity, reason);
        }

        private void ApplyOverlayVisibility(bool isVisible, string reason)
        {
            if (isVisible)
            {
                EnsureCursorVisible();
            }
            else
            {
                EnsureCursorHidden();
            }

            if (_isOverlayVisible == isVisible)
            {
                return;
            }

            TopRow.Height = isVisible ? GridLength.Auto : new GridLength(0);
            BottomRow.Height = isVisible ? GridLength.Auto : new GridLength(0);
            TopBar.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            BottomBar.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            _isOverlayVisible = isVisible;
            if (!isVisible)
            {
                _ignorePointerUntilUtc = DateTime.UtcNow + PointerHideSuppressDelay;
            }

            LogStructuredPlayback(
                isVisible ? "overlay_shown" : "overlay_hidden",
                $"reason={SanitizeForLog(reason)}");
            QueueSurfacePlacementUpdate();
        }

        private bool AreFlyoutMenusOpen => _openOverlayFlyouts.Count > 0 || _pendingUtilityFlyout != null;
        private bool IsMenuOpen => AreFlyoutMenusOpen || _toolsPanelOpen;

        private bool IsUserInteractingWithControls =>
            _isOverlayPointerInteractionActive ||
            _isUserSeeking ||
            _isUserAdjustingVolume;

        private bool EvaluateAutoHideEligibility(string reason)
        {
            return EvaluateAutoHideEligibility(reason, out _);
        }

        private bool EvaluateAutoHideEligibility(string reason, out string denyReason)
        {
            var timerArmed = _controlsHideTimer?.IsEnabled == true;
            denyReason = GetAutoHideDenyReason(timerArmed);
            var allowHide = string.Equals(denyReason, "none", StringComparison.Ordinal);
            LogStructuredPlayback(
                "autohide_evaluated",
                $"reason={SanitizeForLog(reason)}; state={_stateMachine.State}; window_active={BoolToLog(_isWindowActive)}; menu_open={BoolToLog(IsMenuOpen)}; pointer_over_controls={BoolToLog(_isPointerOverControls)}; overlay_visible={BoolToLog(_isOverlayVisible)}; cursor_visible={BoolToLog(!_isCursorHidden)}; timer_armed={BoolToLog(timerArmed)}; allow_hide={BoolToLog(allowHide)}; deny_reason={denyReason}");
            return allowHide;
        }

        private string GetAutoHideDenyReason(bool timerArmed)
        {
            return _stateMachine.State switch
            {
                PlaybackSessionState.Playing when !_isWindowActive => "window_inactive",
                PlaybackSessionState.Playing when IsMenuOpen => "menu_open",
                PlaybackSessionState.Playing when _isPointerOverControls => "pointer_over_controls",
                PlaybackSessionState.Playing when IsUserInteractingWithControls => "recent_user_activity",
                PlaybackSessionState.Playing when !timerArmed => "timer_not_armed",
                PlaybackSessionState.Playing => "none",
                PlaybackSessionState.Paused => "paused",
                PlaybackSessionState.Buffering => "buffering",
                PlaybackSessionState.Error => "error",
                _ => "not_playing"
            };
        }

        private static string BoolToLog(bool value)
        {
            return value ? "true" : "false";
        }

        private void ResumeOverlayAutoHide(string cause = "click")
        {
            if (IsUserInteractingWithControls)
            {
                ShowControls(persist: true, cause: cause);
                return;
            }

            ShowControls(cause: cause);
            ResetInactivityTimer(cause);
        }

        private void EndVolumeInteraction()
        {
            _isUserAdjustingVolume = false;
            ResumeOverlayAutoHide();
            _ = SavePlayerPreferencesAsync();
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
            if (sender is not FlyoutBase flyout)
            {
                return;
            }

            if (_teardownStarted)
            {
                try { flyout.Hide(); } catch { }
                return;
            }

            SuppressSurfaceClicks();
            _openOverlayFlyouts.Remove(flyout);
            _openOverlayFlyouts.Add(flyout);
            SetMenuSurfaceInputShield(true, "flyout_opened");
            if (IsUtilityChildFlyout(flyout))
            {
                _activeUtilityFlyout = flyout;
                _pendingUtilityFlyout = null;
            }

            LogPlaybackState("OVERLAY: flyout opened");
            ShowControls(persist: true, cause: "menu_opened");
        }

        private void OverlayFlyout_Closed(object sender, object e)
        {
            if (sender is FlyoutBase flyout)
            {
                _openOverlayFlyouts.Remove(flyout);
                if (ReferenceEquals(_activeUtilityFlyout, flyout))
                {
                    _activeUtilityFlyout = null;
                }

                if (ReferenceEquals(_pendingUtilityFlyout, flyout))
                {
                    _pendingUtilityFlyout = null;
                }
            }

            if (_teardownStarted)
            {
                return;
            }

            SuppressSurfaceClicks();
            SetMenuSurfaceInputShield(AreFlyoutMenusOpen, _pendingUtilityFlyout != null ? "flyout_transition" : "flyout_closed");
            LogPlaybackState("OVERLAY: flyout closed");
            if (_pendingUtilityFlyout != null)
            {
                ShowControls(persist: true, cause: "menu_transition");
                return;
            }

            RestorePlayerKeyboardFocus(force: true);
            ResumeOverlayAutoHide("menu_closed");
        }

        private bool IsUtilityChildFlyout(FlyoutBase flyout)
        {
            return ReferenceEquals(flyout, AspectFlyout) ||
                   ReferenceEquals(flyout, AudioDelayFlyout) ||
                   ReferenceEquals(flyout, SubtitleDelayFlyout) ||
                   ReferenceEquals(flyout, SubtitleStyleFlyout) ||
                   ReferenceEquals(flyout, ZoomFlyout) ||
                   ReferenceEquals(flyout, RotationFlyout) ||
                   ReferenceEquals(flyout, SleepTimerFlyout);
        }

        private void RestorePlayerKeyboardFocus(bool force = false)
        {
            if (_teardownStarted || RootGrid.XamlRoot == null || IsMenuOpen)
            {
                return;
            }

            if (!force && IsTextInputFocused())
            {
                return;
            }

            RootGrid.Focus(FocusState.Keyboard);
        }

        public bool TryFocusPrimaryTarget()
        {
            if (_guidePanelOpen || _channelPanelOpen || _episodePanelOpen || _infoPanelOpen)
            {
                FocusActivePanelTarget();
                return true;
            }

            RestorePlayerKeyboardFocus(force: true);
            return true;
        }

        public bool TryHandleBackRequest()
        {
            return HandleEscapeHotkey();
        }

        private void SuppressSurfaceClicks(TimeSpan? duration = null)
        {
            if (_teardownStarted)
            {
                return;
            }

            var next = DateTime.UtcNow + (duration ?? MenuSurfaceClickSuppressDelay);
            if (next > _ignoreSurfaceClickUntilUtc)
            {
                _ignoreSurfaceClickUntilUtc = next;
                LogStructuredPlayback(
                    "surface_clicks_suppressed",
                    $"until_utc={_ignoreSurfaceClickUntilUtc:O}");
            }
        }

        private void SetMenuSurfaceInputShield(bool enabled, string reason)
        {
            if (_menuSurfaceInputShieldActive == enabled)
            {
                return;
            }

            _menuSurfaceInputShieldActive = enabled;
            ApplySurfaceInputShield(reason);
        }

        private void SetFullscreenSurfaceInputShield(bool enabled, string reason)
        {
            if (_fullscreenSurfaceInputShieldActive == enabled)
            {
                return;
            }

            _fullscreenSurfaceInputShieldActive = enabled;
            ApplySurfaceInputShield(reason);
        }

        private void ApplySurfaceInputShield(string reason)
        {
            var shouldShield = _menuSurfaceInputShieldActive || _fullscreenSurfaceInputShieldActive;
            if (_surfaceInputShieldApplied == shouldShield)
            {
                return;
            }

            _surfaceInputShieldApplied = shouldShield;
            if (_teardownStarted)
            {
                _surface?.SetInputEnabled(false);
                _surface?.SetVisible(false, $"{reason}_teardown");
                LogStructuredPlayback(
                    "surface_input_shield",
                    $"enabled=true; reason={SanitizeForLog($"{reason}_teardown")}; menu={BoolToLog(_menuSurfaceInputShieldActive)}; fullscreen={BoolToLog(_fullscreenSurfaceInputShieldActive)}");
                return;
            }

            if (_fullscreenSurfaceInputShieldActive)
            {
                _surface?.SetInputEnabled(false);
                _surface?.SetVisible(false, reason);
            }
            else if (_menuSurfaceInputShieldActive)
            {
                _surface?.SetVisible(true, reason);
                _surface?.SetInputEnabled(false);
            }
            else
            {
                _surface?.SetVisible(true, reason);
                _surface?.SetInputEnabled(true);
            }
            LogStructuredPlayback(
                "surface_input_shield",
                $"enabled={BoolToLog(shouldShield)}; reason={SanitizeForLog(reason)}; menu={BoolToLog(_menuSurfaceInputShieldActive)}; fullscreen={BoolToLog(_fullscreenSurfaceInputShieldActive)}");
        }

        private void DismissOverlayFlyoutsForTeardown()
        {
            if (_openOverlayFlyouts.Count == 0 &&
                _activeUtilityFlyout == null &&
                _pendingUtilityFlyout == null)
            {
                return;
            }

            LogPlaybackState("OVERLAY: dismissing flyouts for teardown");
            var flyoutsToHide = new List<FlyoutBase>(_openOverlayFlyouts);
            if (_activeUtilityFlyout != null && !flyoutsToHide.Contains(_activeUtilityFlyout))
            {
                flyoutsToHide.Add(_activeUtilityFlyout);
            }

            if (_pendingUtilityFlyout != null && !flyoutsToHide.Contains(_pendingUtilityFlyout))
            {
                flyoutsToHide.Add(_pendingUtilityFlyout);
            }

            _openOverlayFlyouts.Clear();
            _activeUtilityFlyout = null;
            _pendingUtilityFlyout = null;
            _toolsPanelOpen = false;
            UpdateToolsPanelVisibility();
            SetMenuSurfaceInputShield(false, "flyouts_teardown");

            foreach (var flyout in flyoutsToHide)
            {
                try { flyout.Hide(); } catch { }
            }
        }

        private bool IsOverlayControlSource(object originalSource)
        {
            if (originalSource is not DependencyObject dependencyObject)
            {
                return false;
            }

            return IsDescendantOf(dependencyObject, TopBar) ||
                   IsDescendantOf(dependencyObject, BottomBar) ||
                   IsDescendantOf(dependencyObject, ToolsPanel);
        }

        private bool IsToolsPanelInteractionSource(object originalSource)
        {
            if (originalSource is not DependencyObject dependencyObject)
            {
                return false;
            }

            return IsDescendantOf(dependencyObject, ToolsPanel) ||
                   IsDescendantOf(dependencyObject, ToolsButton);
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

        private void LogPlaybackState(string message)
        {
            var player = _player;
            Log(
                $"playback_session_id={_playbackSessionId}; channel_id={_context?.ContentId ?? 0}; switch_generation={_switchGeneration}; {message}; mode={ResolvePlaybackModeLabel()}; state={_stateMachine.State}; paused={(player?.IsPaused.ToString() ?? "null")}; buffering={(player?.IsBuffering.ToString() ?? "null")}; seekable={(player?.IsSeekable.ToString() ?? "null")}; pos_ms={_lastPositionMs}; dur_ms={_lastDurationMs}; timeshift={_isLiveTimeshiftActive}; overlay_visible={_isOverlayVisible}; cursor_visible={!_isCursorHidden}; hide_timer_running={(_controlsHideTimer?.IsEnabled == true)}; pointer_over_controls={_isPointerOverControls}; menu_open={IsMenuOpen}; window_active={_isWindowActive}; interacting={IsUserInteractingWithControls}");
        }

        private void LogStructuredPlayback(string eventName, string details)
        {
            LogPlaybackState($"event={eventName}; {details}");
        }

        private static string SanitizeForLog(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "none";
            }

            return value
                .Replace(";", ",", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
        }

        // --- Helpers ---

        private void ShowStatusOverlay(string title, string message)
        {
            StatusTitle.Text = title;
            StatusMessage.Text = string.IsNullOrWhiteSpace(message) ? title : message;
            StatusRing.IsActive = true;
            StatusOverlay.Visibility = Visibility.Visible;
            ClearError();
            UpdateOverlayVisibility($"status_{title.ToLowerInvariant()}");
        }

        private void HideStatusOverlay()
        {
            StatusRing.IsActive = false;
            StatusOverlay.Visibility = Visibility.Collapsed;
            UpdateOverlayVisibility("status_hidden");
        }

        private void ShowError(string message)
        {
            ErrorMessage.Text = string.IsNullOrWhiteSpace(message) ? "Unable to start playback." : message;
            ErrorOverlay.Visibility = Visibility.Visible;
            UpdateOverlayVisibility("error_visible");
        }

        private void ClearError()
        {
            ErrorOverlay.Visibility = Visibility.Collapsed;
            UpdateOverlayVisibility("error_cleared");
        }

        private void ShowFatalError(string message)
        {
            if (_lastOpenFailedAttemptId != _activeAttemptId)
            {
                _lastOpenFailedAttemptId = _activeAttemptId;
                LogStructuredPlayback("open_failed", $"attempt_id={_activeAttemptId}; reason={SanitizeForLog(message)}");
            }

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

        private string ResolvePlaybackModeLabel()
        {
            if (IsCatchupPlayback())
            {
                return "catchup";
            }

            return IsLivePlayback() ? "live" : "vod";
        }

        private async Task<bool> ResolveCatchupContextIfNeededAsync(CancellationToken cancellationToken)
        {
            if (_context == null || !IsCatchupRequested(_context))
            {
                return true;
            }

            var resolution = await ResolveCatchupContextAsync(_context, cancellationToken);
            if (resolution.Success)
            {
                _resolvedRoutingSummary = _context.RoutingSummary;
                return true;
            }

            RefreshInfoPanel();
            UpdatePlaybackHint();
            ShowFatalError(resolution.Message);
            return false;
        }

        private async Task<CatchupPlaybackResolution> ResolveCatchupContextAsync(
            PlaybackLaunchContext context,
            CancellationToken cancellationToken)
        {
            using var scope = ((App)Application.Current).Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await _catchupPlaybackService.ResolveForContextAsync(db, context, cancellationToken);
        }

        private async Task ReturnToLivePlaybackAsync(string reason)
        {
            if (_context == null || !IsChannelPlayback())
            {
                return;
            }

            var playbackSessionId = _playbackSessionId;
            var cancellationToken = CurrentPlaybackSessionToken;

            if (string.IsNullOrWhiteSpace(_context.LiveStreamUrl))
            {
                try
                {
                    using var scope = ((App)Application.Current).Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var operationalService = scope.ServiceProvider.GetRequiredService<IContentOperationalService>();
                    await operationalService.ResolvePlaybackContextAsync(db, _context);
                    if (string.IsNullOrWhiteSpace(_context.LiveStreamUrl))
                    {
                        _context.LiveStreamUrl = _context.StreamUrl;
                    }
                }
                catch
                {
                }
            }

            var liveStreamUrl = string.IsNullOrWhiteSpace(_context.LiveStreamUrl)
                ? _context.StreamUrl
                : _context.LiveStreamUrl;
            if (string.IsNullOrWhiteSpace(liveStreamUrl))
            {
                ShowZapBanner("Live unavailable", "The live stream could not be resolved for this channel.");
                return;
            }

            ResetCatchupContext(_context, liveStreamUrl);
            await LoadEnhancedPlayerStateAsync(cancellationToken);
            if (!IsPlaybackSessionActive(playbackSessionId, cancellationToken))
            {
                return;
            }

            ShowZapBanner(TitleText.Text, "Returned to live playback.");
            RestartPlayerSession($"go_live:{reason}", 0);
        }

        private static bool IsCatchupRequested(PlaybackLaunchContext context)
        {
            return context.ContentType == PlaybackContentType.Channel &&
                   context.CatchupRequestKind != CatchupRequestKind.None &&
                   context.CatchupProgramStartTimeUtc.HasValue &&
                   context.CatchupProgramEndTimeUtc.HasValue;
        }

        private static void ResetCatchupContext(PlaybackLaunchContext context, string liveStreamUrl)
        {
            context.PlaybackMode = CatchupPlaybackMode.Live;
            context.CatchupRequestKind = CatchupRequestKind.None;
            context.CatchupResolutionStatus = CatchupResolutionStatus.None;
            context.CatchupStatusText = string.Empty;
            context.CatchupProgramTitle = string.Empty;
            context.CatchupProgramStartTimeUtc = null;
            context.CatchupProgramEndTimeUtc = null;
            context.CatchupRequestedAtUtc = null;
            context.CatalogStreamUrl = liveStreamUrl;
            context.LiveStreamUrl = liveStreamUrl;
            context.StreamUrl = liveStreamUrl;
            context.StartPositionMs = 0;
        }
    }
}
