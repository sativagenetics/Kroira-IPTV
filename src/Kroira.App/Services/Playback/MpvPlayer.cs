using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace Kroira.App.Services.Playback
{
    public enum PlaybackAspectMode
    {
        Automatic,
        FillWindow,
        Ratio16x9,
        Ratio4x3,
        Ratio185x1,
        Ratio235x1
    }

    public sealed class MpvTrackInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Language { get; init; } = string.Empty;
        public bool IsSelected { get; init; }
        public bool IsExternal { get; init; }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Language))
                {
                    return $"{Title} ({Language})";
                }

                if (!string.IsNullOrWhiteSpace(Title))
                {
                    return Title;
                }

                if (!string.IsNullOrWhiteSpace(Language))
                {
                    return Language;
                }

                return Type == "sub" ? $"Subtitle {Id}" : $"Audio {Id}";
            }
        }
    }

    internal sealed class MpvPlaybackEndedInfo
    {
        public NativeMpv.MpvEndFileReason Reason { get; init; } = NativeMpv.MpvEndFileReason.Unknown;
        public int ErrorCode { get; init; }
    }

    // Page-scoped wrapper around a single libmpv handle. One playback session = one
    // MpvPlayer instance. Owns its event-loop thread. Disposal is synchronous and
    // joins the event thread so that no native state or audio leaks into the next
    // session.
    public sealed class MpvPlayer : IDisposable
    {
        private const ulong PropIdTimePos = 1;
        private const ulong PropIdDuration = 2;
        private const ulong PropIdPause = 3;
        private const ulong PropIdSeekable = 4;
        private const ulong PropIdEof = 5;
        private const ulong PropIdBuffering = 6;

        private readonly DispatcherQueue _dispatcher;
        private readonly object _lifecycleLock = new();
        private readonly object _apiLock = new();
        private IntPtr _ctx;
        private Thread _eventThread;
        private bool _disposed;

        private static string LogPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kroira",
            "startup-log.txt");

        public event Action<TimeSpan> PositionChanged;
        public event Action<TimeSpan> DurationChanged;
        public event Action<bool> PauseChanged;
        public event Action<bool> SeekableChanged;
        public event Action<bool> BufferingChanged;
        public event Action FileLoaded;
        public event Action OutputReady;
        internal event Action<MpvPlaybackEndedInfo> PlaybackEnded;
        public event Action TrackListChanged;
        public event Action<string> WarningMessage;

        public TimeSpan Position { get; private set; }
        public TimeSpan Duration { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsSeekable { get; private set; }
        public bool IsBuffering { get; private set; }
        public double Volume { get; private set; } = 100;
        public bool IsMuted { get; private set; }

        public MpvPlayer(DispatcherQueue dispatcher, IntPtr videoHwnd)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            if (videoHwnd == IntPtr.Zero) throw new ArgumentException("videoHwnd must be non-null", nameof(videoHwnd));

            _ctx = NativeMpv.mpv_create();
            if (_ctx == IntPtr.Zero)
            {
                throw new InvalidOperationException("mpv_create() failed. Is mpv-1.dll present next to the executable?");
            }

            // Options that must be set before mpv_initialize.
            SetOption("wid", videoHwnd.ToInt64().ToString(CultureInfo.InvariantCulture));
            SetOption("terminal", "no");
            SetOption("msg-level", "all=warn");
            SetOption("input-default-bindings", "no");
            SetOption("input-vo-keyboard", "no");
            SetOption("osc", "no");
            SetOption("audio-display", "no");
            SetOption("aid", "auto");
            SetOption("mute", "no");
            SetOption("volume", "100");
            SetOption("keep-open", "no");
            SetOption("idle", "yes");
            SetOption("force-window", "no");
            SetOption("hwdec", "auto-safe");
            // Network defaults that make streaming less fragile without being exotic.
            SetOption("cache", "yes");
            SetOption("demuxer-max-bytes", "50MiB");
            SetOption("demuxer-readahead-secs", "20");

            int initResult = NativeMpv.mpv_initialize(_ctx);
            if (initResult < 0)
            {
                var ctx = _ctx;
                _ctx = IntPtr.Zero;
                NativeMpv.mpv_terminate_destroy(ctx);
                throw new InvalidOperationException($"mpv_initialize failed with code {initResult}");
            }

            NativeMpv.mpv_request_log_messages(_ctx, "warn");
            NativeMpv.mpv_observe_property(_ctx, PropIdTimePos, "time-pos", NativeMpv.MpvFormat.Double);
            NativeMpv.mpv_observe_property(_ctx, PropIdDuration, "duration", NativeMpv.MpvFormat.Double);
            NativeMpv.mpv_observe_property(_ctx, PropIdPause, "pause", NativeMpv.MpvFormat.Flag);
            NativeMpv.mpv_observe_property(_ctx, PropIdSeekable, "seekable", NativeMpv.MpvFormat.Flag);
            NativeMpv.mpv_observe_property(_ctx, PropIdBuffering, "paused-for-cache", NativeMpv.MpvFormat.Flag);

            _eventThread = new Thread(EventLoop)
            {
                Name = "mpv-events",
                IsBackground = true,
            };
            _eventThread.Start(_ctx);
        }

        private static void Log(string message)
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LogPath)!);
                System.IO.File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] MPV {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        public void Play(string url, long startPositionMs)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            if (TryGetCtx(out var ctx))
            {
                lock (_apiLock)
                {
                    if (IsDisposed) return;

                    if (startPositionMs > 0)
                    {
                        var start = (startPositionMs / 1000.0).ToString(CultureInfo.InvariantCulture);
                        NativeMpv.Command(ctx, "loadfile", url, "replace", $"start={start}");
                    }
                    else
                    {
                        NativeMpv.Command(ctx, "loadfile", url, "replace");
                    }
                    EnsurePlaybackActiveLocked(ctx, selectAudio: true);
                }
            }
        }

        public void TogglePause()
        {
            UseCtx(ctx => NativeMpv.Command(ctx, "cycle", "pause"));
        }

        public void Pause()
        {
            UseCtx(ctx => NativeMpv.mpv_set_property_string(ctx, "pause", "yes"));
        }

        public void Resume()
        {
            UseCtx(ctx => NativeMpv.mpv_set_property_string(ctx, "pause", "no"));
        }

        public void Stop()
        {
            UseCtx(ctx => NativeMpv.Command(ctx, "stop"));
        }

        public void SeekAbsoluteSeconds(double seconds)
        {
            if (!IsSeekable) return;
            UseCtx(ctx => NativeMpv.Command(ctx, "seek",
                seconds.ToString(CultureInfo.InvariantCulture), "absolute"));
        }

        public void SeekAbsolutePercent(double percent)
        {
            if (!IsSeekable) return;
            var value = Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture);
            UseCtx(ctx => NativeMpv.Command(ctx, "seek", value, "absolute-percent"));
        }

        public void SetVolume(double volume)
        {
            Volume = Math.Clamp(volume, 0, 100);
            UseCtx(ctx => NativeMpv.mpv_set_property_string(ctx, "volume",
                Volume.ToString(CultureInfo.InvariantCulture)));
        }

        public void SetMuted(bool muted)
        {
            IsMuted = muted;
            UseCtx(ctx => NativeMpv.mpv_set_property_string(ctx, "mute", muted ? "yes" : "no"));
        }

        public void ToggleMute()
        {
            SetMuted(!IsMuted);
        }

        public IReadOnlyList<MpvTrackInfo> GetAudioTracks()
        {
            return GetTracks("audio");
        }

        public IReadOnlyList<MpvTrackInfo> GetSubtitleTracks()
        {
            return GetTracks("sub");
        }

        public void SelectAudioTrack(string trackId)
        {
            SetTrackProperty("aid", string.IsNullOrWhiteSpace(trackId) ? "auto" : trackId);
        }

        public void SelectSubtitleTrack(string trackId)
        {
            SetTrackProperty("sid", string.IsNullOrWhiteSpace(trackId) ? "no" : trackId);
        }

        public void SetAspectMode(PlaybackAspectMode aspectMode)
        {
            UseCtx(ctx =>
            {
                switch (aspectMode)
                {
                    case PlaybackAspectMode.FillWindow:
                        NativeMpv.mpv_set_property_string(ctx, "keepaspect", "no");
                        NativeMpv.mpv_set_property_string(ctx, "video-aspect-override", "-1");
                        break;
                    case PlaybackAspectMode.Ratio16x9:
                        ApplyAspectOverride(ctx, "16:9");
                        break;
                    case PlaybackAspectMode.Ratio4x3:
                        ApplyAspectOverride(ctx, "4:3");
                        break;
                    case PlaybackAspectMode.Ratio185x1:
                        ApplyAspectOverride(ctx, "1.85");
                        break;
                    case PlaybackAspectMode.Ratio235x1:
                        ApplyAspectOverride(ctx, "2.35");
                        break;
                    default:
                        NativeMpv.mpv_set_property_string(ctx, "keepaspect", "yes");
                        NativeMpv.mpv_set_property_string(ctx, "video-aspect-override", "-1");
                        break;
                }
            });
        }

        private void SetOption(string name, string value)
        {
            NativeMpv.mpv_set_option_string(_ctx, name, value);
        }

        private void UseCtx(Action<IntPtr> action)
        {
            if (action == null || !TryGetCtx(out var ctx)) return;

            lock (_apiLock)
            {
                if (IsDisposed) return;
                action(ctx);
            }
        }

        private T UseCtx<T>(Func<IntPtr, T> action, T fallback)
        {
            if (action == null || !TryGetCtx(out var ctx)) return fallback;

            lock (_apiLock)
            {
                if (IsDisposed) return fallback;
                return action(ctx);
            }
        }

        private void EnsurePlaybackActive(IntPtr ctx, bool selectAudio)
        {
            lock (_apiLock)
            {
                if (IsDisposed) return;
                EnsurePlaybackActiveLocked(ctx, selectAudio);
            }
        }

        private static void EnsurePlaybackActiveLocked(IntPtr ctx, bool selectAudio)
        {
            NativeMpv.mpv_set_property_string(ctx, "mute", "no");
            NativeMpv.mpv_set_property_string(ctx, "volume", "100");
            if (selectAudio) NativeMpv.mpv_set_property_string(ctx, "aid", "auto");
            NativeMpv.mpv_set_property_string(ctx, "pause", "no");
        }

        private IReadOnlyList<MpvTrackInfo> GetTracks(string requestedType)
        {
            return UseCtx(ctx =>
            {
                var tracks = new List<MpvTrackInfo>();
                if (!TryReadIntProperty(ctx, "track-list/count", out var trackCount) || trackCount <= 0)
                {
                    return (IReadOnlyList<MpvTrackInfo>)tracks;
                }

                for (var index = 0; index < trackCount; index++)
                {
                    var baseName = $"track-list/{index}";
                    var type = NativeMpv.GetPropertyString(ctx, $"{baseName}/type");
                    if (string.IsNullOrWhiteSpace(type))
                    {
                        continue;
                    }

                    if (requestedType == "audio" && !string.Equals(type, "audio", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (requestedType == "sub" && !string.Equals(type, "sub", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var id = NativeMpv.GetPropertyString(ctx, $"{baseName}/id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    tracks.Add(new MpvTrackInfo
                    {
                        Id = id.Trim(),
                        Type = type.Trim(),
                        Title = (NativeMpv.GetPropertyString(ctx, $"{baseName}/title") ?? string.Empty).Trim(),
                        Language = (NativeMpv.GetPropertyString(ctx, $"{baseName}/lang") ?? string.Empty).Trim(),
                        IsSelected = IsTruthy(NativeMpv.GetPropertyString(ctx, $"{baseName}/selected")),
                        IsExternal = IsTruthy(NativeMpv.GetPropertyString(ctx, $"{baseName}/external")),
                    });
                }

                return (IReadOnlyList<MpvTrackInfo>)tracks;
            }, Array.Empty<MpvTrackInfo>());
        }

        private void SetTrackProperty(string propertyName, string propertyValue)
        {
            UseCtx(ctx => NativeMpv.mpv_set_property_string(ctx, propertyName, propertyValue));
            EnqueueCallback(() => TrackListChanged?.Invoke());
        }

        private bool TryGetCtx(out IntPtr ctx)
        {
            lock (_lifecycleLock)
            {
                ctx = _ctx;
                return ctx != IntPtr.Zero;
            }
        }

        private void EventLoop(object ctxBoxed)
        {
            var ctx = (IntPtr)ctxBoxed;
            while (true)
            {
                if (IsDisposed) break;

                var evPtr = NativeMpv.mpv_wait_event(ctx, 0.25);
                if (IsDisposed) break;
                if (evPtr == IntPtr.Zero) continue;
                var ev = NativeMpv.ReadEvent(evPtr);

                if (ev.EventId == NativeMpv.MpvEventId.Shutdown) break;
                if (ev.EventId == NativeMpv.MpvEventId.None) continue;

                switch (ev.EventId)
                {
                    case NativeMpv.MpvEventId.PropertyChange:
                        HandlePropertyChange(ev);
                        break;
                    case NativeMpv.MpvEventId.LogMessage:
                        HandleLogMessage(ev);
                        break;
                    case NativeMpv.MpvEventId.EndFile:
                        HandleEndFileEvent(ev);
                        break;
                    case NativeMpv.MpvEventId.FileLoaded:
                        EnqueueCallback(() =>
                        {
                            EnsurePlaybackActive(ctx, selectAudio: true);
                            FileLoaded?.Invoke();
                            TrackListChanged?.Invoke();
                            OutputReady?.Invoke();
                        });
                        break;
                    case NativeMpv.MpvEventId.PlaybackRestart:
                        EnqueueCallback(() =>
                        {
                            TrackListChanged?.Invoke();
                            OutputReady?.Invoke();
                        });
                        break;
                    case NativeMpv.MpvEventId.VideoReconfig:
                        EnqueueCallback(() => OutputReady?.Invoke());
                        break;
                    case NativeMpv.MpvEventId.AudioReconfig:
                        EnqueueCallback(() => TrackListChanged?.Invoke());
                        break;
                }
            }
        }

        private void HandlePropertyChange(NativeMpv.MpvEvent ev)
        {
            if (ev.Data == IntPtr.Zero) return;
            var prop = NativeMpv.ReadEventProperty(ev.Data);
            if (prop.Data == IntPtr.Zero) return;

            switch (ev.ReplyUserdata)
            {
                case PropIdTimePos:
                    {
                        var seconds = ReadDouble(prop.Data);
                        if (double.IsNaN(seconds) || seconds < 0) return;
                        var ts = TimeSpan.FromSeconds(seconds);
                        EnqueueCallback(() =>
                        {
                            Position = ts;
                            PositionChanged?.Invoke(ts);
                        });
                        break;
                    }
                case PropIdDuration:
                    {
                        var seconds = ReadDouble(prop.Data);
                        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
                        var ts = TimeSpan.FromSeconds(seconds);
                        EnqueueCallback(() =>
                        {
                            Duration = ts;
                            DurationChanged?.Invoke(ts);
                        });
                        break;
                    }
                case PropIdPause:
                    {
                        var flag = Marshal.ReadInt32(prop.Data) != 0;
                        EnqueueCallback(() =>
                        {
                            IsPaused = flag;
                            PauseChanged?.Invoke(flag);
                        });
                        break;
                    }
                case PropIdSeekable:
                    {
                        var flag = Marshal.ReadInt32(prop.Data) != 0;
                        EnqueueCallback(() =>
                        {
                            IsSeekable = flag;
                            SeekableChanged?.Invoke(flag);
                        });
                        break;
                    }
                case PropIdBuffering:
                    {
                        var flag = Marshal.ReadInt32(prop.Data) != 0;
                        EnqueueCallback(() =>
                        {
                            IsBuffering = flag;
                            BufferingChanged?.Invoke(flag);
                        });
                        break;
                    }
            }
        }

        private void HandleLogMessage(NativeMpv.MpvEvent ev)
        {
            if (ev.Data == IntPtr.Zero) return;
            var log = NativeMpv.ReadEventLogMessage(ev.Data);
            var message = Marshal.PtrToStringUTF8(log.Text);
            if (string.IsNullOrWhiteSpace(message)) return;

            EnqueueCallback(() => WarningMessage?.Invoke(message.Trim()));
        }

        private void HandleEndFileEvent(NativeMpv.MpvEvent ev)
        {
            var endFile = NativeMpv.ReadEventEndFile(ev.Data);
            Log($"ENDFILE: reason={endFile.Reason} error={endFile.Error}");
            var shouldSignalEnded =
                endFile.Reason == NativeMpv.MpvEndFileReason.Eof ||
                endFile.Reason == NativeMpv.MpvEndFileReason.Error ||
                endFile.Reason == NativeMpv.MpvEndFileReason.Unknown;

            if (!shouldSignalEnded)
            {
                Log($"ENDFILE: ignored reason={endFile.Reason} error={endFile.Error}");
                EnqueueCallback(() => WarningMessage?.Invoke(
                    $"mpv end-file ignored: reason={endFile.Reason} error={endFile.Error}"));
                return;
            }

            Log($"ENDFILE: dispatching playback-ended reason={endFile.Reason} error={endFile.Error}");
            EnqueueCallback(() => PlaybackEnded?.Invoke(new MpvPlaybackEndedInfo
            {
                Reason = endFile.Reason,
                ErrorCode = endFile.Error
            }));
        }

        private bool IsDisposed
        {
            get
            {
                lock (_lifecycleLock)
                {
                    return _disposed;
                }
            }
        }

        private void EnqueueCallback(Action callback)
        {
            if (callback == null || IsDisposed) return;
            _dispatcher.TryEnqueue(() =>
            {
                if (!IsDisposed) callback();
            });
        }

        private static double ReadDouble(IntPtr ptr)
        {
            long bits = Marshal.ReadInt64(ptr);
            return BitConverter.Int64BitsToDouble(bits);
        }

        private static void ApplyAspectOverride(IntPtr ctx, string ratio)
        {
            NativeMpv.mpv_set_property_string(ctx, "keepaspect", "yes");
            NativeMpv.mpv_set_property_string(ctx, "video-aspect-override", ratio);
        }

        private static bool IsTruthy(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadIntProperty(IntPtr ctx, string propertyName, out int value)
        {
            value = 0;
            var raw = NativeMpv.GetPropertyString(ctx, propertyName);
            return !string.IsNullOrWhiteSpace(raw) &&
                   int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public void Dispose()
        {
            IntPtr ctx;
            Thread thread;
            lock (_lifecycleLock)
            {
                if (_disposed) return;
                _disposed = true;
                ctx = _ctx;
                _ctx = IntPtr.Zero;
                thread = _eventThread;
                _eventThread = null;
            }

            if (ctx != IntPtr.Zero)
            {
                try { NativeMpv.mpv_wakeup(ctx); } catch { }
            }

            if (thread != null && thread != Thread.CurrentThread)
            {
                thread.Join(TimeSpan.FromSeconds(3));
            }

            // mpv_wait_event returns pointers that are valid only until the next
            // wait or handle destruction. Destroy only after the event thread exits.
            if (ctx != IntPtr.Zero && (thread == null || !thread.IsAlive))
            {
                lock (_apiLock)
                {
                    try { NativeMpv.Command(ctx, "stop"); } catch { }
                    NativeMpv.mpv_terminate_destroy(ctx);
                }
            }
        }
    }
}
