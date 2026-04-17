using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace Kroira.App.Services.Playback
{
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

        private readonly DispatcherQueue _dispatcher;
        private readonly object _lifecycleLock = new();
        private readonly object _apiLock = new();
        private IntPtr _ctx;
        private Thread _eventThread;
        private bool _disposed;

        public event Action<TimeSpan> PositionChanged;
        public event Action<TimeSpan> DurationChanged;
        public event Action<bool> PauseChanged;
        public event Action<bool> SeekableChanged;
        public event Action FileLoaded;
        public event Action OutputReady;
        public event Action PlaybackEnded;

        public TimeSpan Position { get; private set; }
        public TimeSpan Duration { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsSeekable { get; private set; }

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

            NativeMpv.mpv_observe_property(_ctx, PropIdTimePos, "time-pos", NativeMpv.MpvFormat.Double);
            NativeMpv.mpv_observe_property(_ctx, PropIdDuration, "duration", NativeMpv.MpvFormat.Double);
            NativeMpv.mpv_observe_property(_ctx, PropIdPause, "pause", NativeMpv.MpvFormat.Flag);
            NativeMpv.mpv_observe_property(_ctx, PropIdSeekable, "seekable", NativeMpv.MpvFormat.Flag);
            NativeMpv.mpv_observe_property(_ctx, PropIdEof, "eof-reached", NativeMpv.MpvFormat.Flag);

            _eventThread = new Thread(EventLoop)
            {
                Name = "mpv-events",
                IsBackground = true,
            };
            _eventThread.Start(_ctx);
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
                    case NativeMpv.MpvEventId.EndFile:
                        EnqueueCallback(() => PlaybackEnded?.Invoke());
                        break;
                    case NativeMpv.MpvEventId.FileLoaded:
                        EnqueueCallback(() =>
                        {
                            EnsurePlaybackActive(ctx, selectAudio: true);
                            FileLoaded?.Invoke();
                            OutputReady?.Invoke();
                        });
                        break;
                    case NativeMpv.MpvEventId.PlaybackRestart:
                        EnqueueCallback(() => OutputReady?.Invoke());
                        break;
                    case NativeMpv.MpvEventId.VideoReconfig:
                        EnqueueCallback(() => OutputReady?.Invoke());
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
                case PropIdEof:
                    {
                        var flag = Marshal.ReadInt32(prop.Data) != 0;
                        if (flag) EnqueueCallback(() => PlaybackEnded?.Invoke());
                        break;
                    }
            }
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
