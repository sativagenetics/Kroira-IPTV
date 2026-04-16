using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace Kroira.App.Services.Playback
{
    public sealed class MpvPlaybackEngine : IPlaybackEngine
    {
        private const string DllName = "mpv-1";
        private const int MpvEventShutdown = 1;
        private const int MpvEventEndFileId = 7;
        private const int MpvEventFileLoaded = 8;
        private const int MpvEventIdle = 11;
        private const int MpvEventPlaybackRestart = 21;
        private const int MpvEndFileReasonError = 4;

        private readonly object _sync = new();
        private readonly DispatcherQueue _dispatcherQueue;
        private IntPtr _handle;
        private IntPtr _videoHostHwnd;
        private CancellationTokenSource _eventLoopCts;
        private Task _eventLoopTask;
        private long _commandId;
        private bool _disposed;

        public MpvPlaybackEngine()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        }

        public PlaybackState State { get; private set; } = PlaybackState.Idle;

        public event EventHandler<PlaybackState> StateChanged;
        public event EventHandler<string> ErrorOccurred;

        public object MediaPlayerInstance => _handle;

        public long PositionMs => (long)Math.Max(0, GetDoubleProperty("time-pos") * 1000);
        public long LengthMs => (long)Math.Max(0, GetDoubleProperty("duration") * 1000);
        public bool IsPlaying => State == PlaybackState.Playing && !GetBooleanProperty("pause");
        public bool IsSeekable => GetBooleanProperty("seekable");
        public float PlaybackRate => (float)Math.Max(0.25, GetDoubleProperty("speed", 1));
        public int CurrentAudioTrackId => GetIntProperty("aid", -1);
        public int CurrentSubtitleTrackId => GetIntProperty("sid", -1);

        public void SetVideoHostHandle(IntPtr hwnd)
        {
            lock (_sync)
            {
                _videoHostHwnd = hwnd;

                if (_handle != IntPtr.Zero)
                {
                    SetOptionString("wid", hwnd == IntPtr.Zero ? "0" : hwnd.ToInt64().ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        public void Play(string sourceUrl) => Play(sourceUrl, 0);

        public void Play(string sourceUrl, long startPositionMs)
        {
            if (string.IsNullOrWhiteSpace(sourceUrl))
            {
                RaiseError("Playback source is empty.");
                return;
            }

            if (_videoHostHwnd == IntPtr.Zero)
            {
                RaiseError("Embedded mpv video surface is not ready.");
                return;
            }

            try
            {
                EnsureInitialized();
                UpdateState(PlaybackState.Loading);
                CommandAsync("loadfile", sourceUrl, "replace");

                if (startPositionMs > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(700).ConfigureAwait(false);
                        SeekTo(startPositionMs);
                    });
                }
            }
            catch (Exception ex)
            {
                RaiseError($"mpv playback failed to start: {ex.Message}");
            }
        }

        public void Resume()
        {
            SetPropertyString("pause", "no");
            UpdateState(PlaybackState.Playing);
        }

        public void Pause()
        {
            SetPropertyString("pause", "yes");
            UpdateState(PlaybackState.Paused);
        }

        public void Stop()
        {
            if (_disposed)
            {
                return;
            }

            CommandAsync("stop");
            UpdateState(PlaybackState.Stopped);
        }

        public void SeekTo(long positionMs)
        {
            if (!IsSeekable)
            {
                return;
            }

            var seconds = Math.Max(0, positionMs) / 1000d;
            SetPropertyString("time-pos", seconds.ToString("0.###", CultureInfo.InvariantCulture));
        }

        public void SeekBy(long deltaMs)
        {
            if (!IsSeekable)
            {
                return;
            }

            var seconds = deltaMs / 1000d;
            CommandAsync("seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "relative", "exact");
        }

        public bool SetPlaybackRate(float rate)
        {
            var normalizedRate = Math.Clamp(rate, 0.25f, 4f);
            return SetPropertyString("speed", normalizedRate.ToString("0.###", CultureInfo.InvariantCulture));
        }

        public IReadOnlyList<PlaybackTrack> GetAudioTracks()
        {
            return GetTracks("audio");
        }

        public IReadOnlyList<PlaybackTrack> GetSubtitleTracks()
        {
            var tracks = new List<PlaybackTrack> { new(-1, "Subtitles off") };
            tracks.AddRange(GetTracks("sub"));
            return tracks;
        }

        public bool SetAudioTrack(int trackId)
        {
            return SetPropertyString("aid", trackId <= 0 ? "auto" : trackId.ToString(CultureInfo.InvariantCulture));
        }

        public bool SetSubtitleTrack(int trackId)
        {
            return SetPropertyString("sid", trackId <= 0 ? "no" : trackId.ToString(CultureInfo.InvariantCulture));
        }

        public bool AddSubtitleFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            return CommandAsync("sub-add", filePath, "select");
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            IntPtr handle;
            lock (_sync)
            {
                handle = _handle;
                _handle = IntPtr.Zero;
            }

            try { _eventLoopCts?.Cancel(); } catch { }

            if (handle != IntPtr.Zero)
            {
                try { mpv_command_string(handle, "quit"); } catch { }
                try { _eventLoopTask?.Wait(250); } catch { }
                try { mpv_terminate_destroy(handle); } catch { }
            }

            _eventLoopCts?.Dispose();
        }

        private void EnsureInitialized()
        {
            lock (_sync)
            {
                if (_handle != IntPtr.Zero)
                {
                    SetOptionString("wid", _videoHostHwnd.ToInt64().ToString(CultureInfo.InvariantCulture));
                    return;
                }

                _handle = mpv_create();
                if (_handle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("mpv_create returned null.");
                }

                SetOptionString("terminal", "no");
                SetOptionString("msg-level", "all=warn");
                SetOptionString("input-default-bindings", "no");
                SetOptionString("input-vo-keyboard", "no");
                SetOptionString("osc", "no");
                SetOptionString("idle", "yes");
                SetOptionString("keep-open", "no");
                SetOptionString("force-window", "immediate");
                SetOptionString("hwdec", "auto-safe");
                SetOptionString("cache", "yes");
                SetOptionString("demuxer-readahead-secs", "20");
                SetOptionString("wid", _videoHostHwnd.ToInt64().ToString(CultureInfo.InvariantCulture));

                CheckError(mpv_initialize(_handle), "mpv_initialize");

                _eventLoopCts = new CancellationTokenSource();
                _eventLoopTask = Task.Run(() => RunEventLoop(_eventLoopCts.Token));
            }
        }

        private void RunEventLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var handle = _handle;
                if (handle == IntPtr.Zero)
                {
                    return;
                }

                var eventPtr = mpv_wait_event(handle, 0.1);
                if (eventPtr == IntPtr.Zero)
                {
                    continue;
                }

                var mpvEvent = Marshal.PtrToStructure<MpvEvent>(eventPtr);
                switch (mpvEvent.EventId)
                {
                    case MpvEventFileLoaded:
                    case MpvEventPlaybackRestart:
                        UpdateState(PlaybackState.Playing);
                        break;
                    case MpvEventEndFileId:
                        HandleEndFile(mpvEvent.Data);
                        break;
                    case MpvEventIdle:
                        if (State != PlaybackState.Loading && State != PlaybackState.Error)
                        {
                            UpdateState(PlaybackState.Stopped);
                        }
                        break;
                    case MpvEventShutdown:
                        return;
                }
            }
        }

        private void HandleEndFile(IntPtr data)
        {
            if (data != IntPtr.Zero)
            {
                var endFile = Marshal.PtrToStructure<MpvEventEndFile>(data);
                if (endFile.Reason == MpvEndFileReasonError)
                {
                    var message = endFile.Error < 0
                        ? $"mpv playback failed: {GetErrorString(endFile.Error)}"
                        : "mpv playback failed.";
                    RaiseError(message);
                    return;
                }
            }

            UpdateState(PlaybackState.Stopped);
        }

        private IReadOnlyList<PlaybackTrack> GetTracks(string type)
        {
            var count = GetIntProperty("track-list/count", 0);
            if (count <= 0)
            {
                return Array.Empty<PlaybackTrack>();
            }

            var tracks = new List<PlaybackTrack>();
            for (var i = 0; i < count; i++)
            {
                if (!string.Equals(GetStringProperty($"track-list/{i}/type"), type, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = GetIntProperty($"track-list/{i}/id", -1);
                if (id < 0)
                {
                    continue;
                }

                var title = GetStringProperty($"track-list/{i}/title");
                var lang = GetStringProperty($"track-list/{i}/lang");
                var fallback = type == "audio" ? $"Audio {id}" : $"Subtitle {id}";
                var name = string.IsNullOrWhiteSpace(title) ? fallback : title;
                if (!string.IsNullOrWhiteSpace(lang))
                {
                    name = $"{name} ({lang})";
                }

                tracks.Add(new PlaybackTrack(id, name));
            }

            return tracks;
        }

        private bool CommandAsync(params string[] args)
        {
            var handle = _handle;
            if (_disposed || handle == IntPtr.Zero)
            {
                return false;
            }

            IntPtr argv = IntPtr.Zero;
            var allocated = new IntPtr[args.Length];

            try
            {
                argv = Marshal.AllocHGlobal(IntPtr.Size * (args.Length + 1));
                for (var i = 0; i < args.Length; i++)
                {
                    allocated[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
                    Marshal.WriteIntPtr(argv, i * IntPtr.Size, allocated[i]);
                }

                Marshal.WriteIntPtr(argv, args.Length * IntPtr.Size, IntPtr.Zero);
                var id = (ulong)Interlocked.Increment(ref _commandId);
                return mpv_command_async(handle, id, argv) >= 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                for (var i = 0; i < allocated.Length; i++)
                {
                    if (allocated[i] != IntPtr.Zero)
                    {
                        Marshal.FreeCoTaskMem(allocated[i]);
                    }
                }

                if (argv != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(argv);
                }
            }
        }

        private bool SetOptionString(string name, string value)
        {
            var handle = _handle;
            return handle != IntPtr.Zero && mpv_set_option_string(handle, name, value) >= 0;
        }

        private bool SetPropertyString(string name, string value)
        {
            var handle = _handle;
            return handle != IntPtr.Zero && mpv_set_property_string(handle, name, value) >= 0;
        }

        private string GetStringProperty(string name)
        {
            var handle = _handle;
            if (handle == IntPtr.Zero)
            {
                return string.Empty;
            }

            var valuePtr = mpv_get_property_string(handle, name);
            if (valuePtr == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                return Marshal.PtrToStringUTF8(valuePtr) ?? string.Empty;
            }
            finally
            {
                mpv_free(valuePtr);
            }
        }

        private double GetDoubleProperty(string name, double fallback = 0)
        {
            return double.TryParse(GetStringProperty(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        private int GetIntProperty(string name, int fallback)
        {
            var raw = GetStringProperty(name);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        private bool GetBooleanProperty(string name)
        {
            var raw = GetStringProperty(name);
            return raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("1", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateState(PlaybackState newState)
        {
            EnsureUiThread(() =>
            {
                State = newState;
                StateChanged?.Invoke(this, newState);
            });
        }

        private void RaiseError(string message)
        {
            EnsureUiThread(() =>
            {
                State = PlaybackState.Error;
                StateChanged?.Invoke(this, State);
                ErrorOccurred?.Invoke(this, message);
            });
        }

        private void EnsureUiThread(Action action)
        {
            if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess)
            {
                action();
            }
            else
            {
                _dispatcherQueue.TryEnqueue(() => action());
            }
        }

        private static void CheckError(int result, string operation)
        {
            if (result < 0)
            {
                throw new InvalidOperationException($"{operation} failed: {GetErrorString(result)}");
            }
        }

        private static string GetErrorString(int error)
        {
            var ptr = mpv_error_string(error);
            return ptr == IntPtr.Zero ? $"mpv error {error}" : Marshal.PtrToStringUTF8(ptr) ?? $"mpv error {error}";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MpvEvent
        {
            public int EventId;
            public int Error;
            public ulong ReplyUserData;
            public IntPtr Data;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MpvEventEndFile
        {
            public int Reason;
            public int Error;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mpv_create();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_initialize(IntPtr ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void mpv_terminate_destroy(IntPtr ctx);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_set_option_string(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_set_property_string(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mpv_get_property_string(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void mpv_free(IntPtr data);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_command_async(IntPtr ctx, ulong replyUserData, IntPtr args);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int mpv_command_string(
            IntPtr ctx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string args);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mpv_error_string(int error);
    }
}
