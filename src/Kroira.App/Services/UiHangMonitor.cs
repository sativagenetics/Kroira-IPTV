#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace Kroira.App.Services
{
    internal sealed class UiHangMonitor : IDisposable
    {
        private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan BlockWarningThreshold = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan HangThreshold = TimeSpan.FromSeconds(5);

        private readonly DispatcherQueue _dispatcherQueue;
        private readonly CancellationTokenSource _cancellation = new();
        private long _lastHeartbeatTicks;
        private bool _warningLogged;
        private bool _hangLogged;
        private Task? _monitorTask;
        private int _disposed;

        public UiHangMonitor(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
            Volatile.Write(ref _lastHeartbeatTicks, DateTime.UtcNow.Ticks);
        }

        public void Start()
        {
            if (_monitorTask != null)
            {
                return;
            }

            _monitorTask = Task.Run(MonitorSafelyAsync);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                _cancellation.Cancel();
            }
            catch
            {
            }

            var monitorTask = _monitorTask;
            if (monitorTask == null || monitorTask.IsCompleted)
            {
                _cancellation.Dispose();
                return;
            }

            _ = monitorTask.ContinueWith(
                _ => _cancellation.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task MonitorSafelyAsync()
        {
            try
            {
                await MonitorAsync(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                RuntimeEventLogger.LogEvent("ui_thread_block_warning", ex, "reason=monitor_failed");
            }
        }

        private async Task MonitorAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var enqueued = _dispatcherQueue.TryEnqueue(() =>
                {
                    Volatile.Write(ref _lastHeartbeatTicks, DateTime.UtcNow.Ticks);
                    if (_warningLogged || _hangLogged)
                    {
                        RuntimeEventLogger.LogEvent("ui_thread_block_recovered");
                    }

                    _warningLogged = false;
                    _hangLogged = false;
                });

                if (!enqueued && !_warningLogged)
                {
                    RuntimeEventLogger.LogEvent("ui_thread_block_warning", "reason=dispatcher_enqueue_failed");
                    _warningLogged = true;
                }

                try
                {
                    await Task.Delay(ProbeInterval, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var lastHeartbeat = new DateTime(Volatile.Read(ref _lastHeartbeatTicks), DateTimeKind.Utc);
                var blockedFor = DateTime.UtcNow - lastHeartbeat;
                if (blockedFor >= HangThreshold)
                {
                    if (!_hangLogged)
                    {
                        RuntimeEventLogger.LogEvent("app_hang_detected", $"blocked_ms={(long)blockedFor.TotalMilliseconds}");
                        _hangLogged = true;
                    }
                }
                else if (blockedFor >= BlockWarningThreshold && !_warningLogged)
                {
                    RuntimeEventLogger.LogEvent("ui_thread_block_warning", $"blocked_ms={(long)blockedFor.TotalMilliseconds}");
                    _warningLogged = true;
                }
            }
        }
    }
}
