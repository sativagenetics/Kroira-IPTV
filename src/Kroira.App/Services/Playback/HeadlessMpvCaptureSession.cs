using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Kroira.App.Services.Playback
{
    internal sealed class HeadlessMpvCaptureResult
    {
        public bool IsSuccess { get; init; }
        public bool IsCanceled { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    internal sealed class HeadlessMpvCaptureSession : IDisposable
    {
        private readonly object _lifecycleLock = new();
        private readonly TaskCompletionSource<HeadlessMpvCaptureResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private IntPtr _ctx;
        private Thread _eventThread;
        private CancellationTokenRegistration _cancellationRegistration;
        private string _outputPath = string.Empty;
        private bool _disposed;
        private bool _stopRequested;
        private bool _userCanceled;
        private string _lastWarning = string.Empty;

        public Task<HeadlessMpvCaptureResult> RunAsync(
            string streamUrl,
            string outputPath,
            TimeSpan? stopAfter,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                throw new ArgumentException("Stream URL is required.", nameof(streamUrl));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output path is required.", nameof(outputPath));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            _outputPath = outputPath;
            CreateContext();

            _eventThread = new Thread(EventLoop)
            {
                Name = "mpv-capture",
                IsBackground = true
            };
            _eventThread.Start();

            _cancellationRegistration = cancellationToken.Register(() => RequestStop(userCanceled: true));
            if (stopAfter.HasValue && stopAfter.Value > TimeSpan.Zero)
            {
                _ = StopWhenDueAsync(stopAfter.Value);
            }

            NativeMpv.Command(_ctx, "loadfile", streamUrl, "replace");
            return _completion.Task;
        }

        private async Task StopWhenDueAsync(TimeSpan delay)
        {
            try
            {
                await Task.Delay(delay);
                RequestStop(userCanceled: false);
            }
            catch
            {
            }
        }

        private void CreateContext()
        {
            _ctx = NativeMpv.mpv_create();
            if (_ctx == IntPtr.Zero)
            {
                throw new InvalidOperationException("mpv_create() failed.");
            }

            SetOption("terminal", "no");
            SetOption("msg-level", "all=warn");
            SetOption("input-default-bindings", "no");
            SetOption("input-vo-keyboard", "no");
            SetOption("osc", "no");
            SetOption("audio-display", "no");
            SetOption("pause", "yes");
            SetOption("idle", "yes");
            SetOption("keep-open", "no");
            SetOption("force-window", "no");
            SetOption("vo", "null");
            SetOption("ao", "null");
            SetOption("cache", "yes");
            SetOption("demuxer-max-bytes", "50MiB");
            SetOption("demuxer-readahead-secs", "20");

            var initResult = NativeMpv.mpv_initialize(_ctx);
            if (initResult < 0)
            {
                var ctx = _ctx;
                _ctx = IntPtr.Zero;
                NativeMpv.mpv_terminate_destroy(ctx);
                throw new InvalidOperationException($"mpv_initialize failed with code {initResult}.");
            }

            NativeMpv.mpv_request_log_messages(_ctx, "warn");
        }

        private void SetOption(string name, string value)
        {
            NativeMpv.mpv_set_option_string(_ctx, name, value);
        }

        private void EventLoop()
        {
            try
            {
                while (!_disposed)
                {
                    var evPtr = NativeMpv.mpv_wait_event(_ctx, 0.25);
                    if (_disposed || evPtr == IntPtr.Zero)
                    {
                        continue;
                    }

                    var ev = NativeMpv.ReadEvent(evPtr);
                    if (ev.EventId == NativeMpv.MpvEventId.None)
                    {
                        continue;
                    }

                    if (ev.EventId == NativeMpv.MpvEventId.LogMessage)
                    {
                        HandleLogMessage(ev);
                        continue;
                    }

                    if (ev.EventId == NativeMpv.MpvEventId.FileLoaded)
                    {
                        StartRecording();
                        continue;
                    }

                    if (ev.EventId == NativeMpv.MpvEventId.EndFile)
                    {
                        Complete();
                        return;
                    }

                    if (ev.EventId == NativeMpv.MpvEventId.Shutdown)
                    {
                        Complete();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _completion.TrySetResult(new HeadlessMpvCaptureResult
                {
                    IsSuccess = false,
                    IsCanceled = _userCanceled,
                    Message = ex.Message
                });
            }
        }

        private void HandleLogMessage(NativeMpv.MpvEvent ev)
        {
            var message = NativeMpv.ReadEventLogMessage(ev.Data);
            var text = message.Text == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(message.Text) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
            {
                _lastWarning = text.Replace("\r", " ").Replace("\n", " ").Trim();
            }
        }

        private void StartRecording()
        {
            if (_ctx == IntPtr.Zero)
            {
                return;
            }

            NativeMpv.mpv_set_property_string(_ctx, "stream-record", _outputPath);
            NativeMpv.mpv_set_property_string(_ctx, "pause", "no");
        }

        private void RequestStop(bool userCanceled)
        {
            lock (_lifecycleLock)
            {
                if (_stopRequested)
                {
                    return;
                }

                _stopRequested = true;
                _userCanceled = userCanceled;
            }

            if (_ctx != IntPtr.Zero)
            {
                try
                {
                    NativeMpv.Command(_ctx, "stop");
                    NativeMpv.mpv_wakeup(_ctx);
                }
                catch
                {
                }
            }
        }

        private void Complete()
        {
            var hasFile = File.Exists(_outputPath);
            var fileSize = hasFile ? new FileInfo(_outputPath).Length : 0;
            var success = fileSize > 0 && (!_userCanceled || !_stopRequested);

            if (_stopRequested && !_userCanceled && fileSize > 0)
            {
                success = true;
            }

            var message = success
                ? string.Empty
                : !string.IsNullOrWhiteSpace(_lastWarning)
                    ? _lastWarning
                    : _userCanceled
                        ? "Capture canceled."
                        : "Capture ended before usable media was written.";

            _completion.TrySetResult(new HeadlessMpvCaptureResult
            {
                IsSuccess = success,
                IsCanceled = _userCanceled && !success,
                Message = message
            });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancellationRegistration.Dispose();

            if (_ctx != IntPtr.Zero)
            {
                try
                {
                    NativeMpv.Command(_ctx, "stop");
                    NativeMpv.mpv_wakeup(_ctx);
                }
                catch
                {
                }

                NativeMpv.mpv_terminate_destroy(_ctx);
                _ctx = IntPtr.Zero;
            }

            if (_eventThread != null && _eventThread.IsAlive)
            {
                _eventThread.Join(TimeSpan.FromSeconds(2));
            }
        }
    }
}
