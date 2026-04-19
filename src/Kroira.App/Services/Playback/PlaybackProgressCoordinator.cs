using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Services.Playback
{
    internal sealed class PlaybackProgressCoordinator
    {
        private const long MinimumSavedPositionMs = 5_000;
        private readonly IServiceProvider _services;
        private readonly SemaphoreSlim _saveLock = new(1, 1);

        private long _lastSavedPositionMs;
        private long _lastSavedDurationMs;
        private bool _lastSavedCompleted;

        public PlaybackProgressCoordinator(IServiceProvider services)
        {
            _services = services;
        }

        public async Task<long> ResolveResumePositionAsync(PlaybackLaunchContext context)
        {
            if (context == null || context.ContentType == PlaybackContentType.Channel || context.StartPositionMs > 0)
            {
                return context?.StartPositionMs ?? 0;
            }

            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var existing = await db.PlaybackProgresses.FirstOrDefaultAsync(
                    progress => progress.ContentType == context.ContentType &&
                                progress.ContentId == context.ContentId &&
                                !progress.IsCompleted);

                if (existing != null && existing.PositionMs >= MinimumSavedPositionMs)
                {
                    context.StartPositionMs = existing.PositionMs;
                    _lastSavedPositionMs = existing.PositionMs;
                    _lastSavedDurationMs = 0;
                    _lastSavedCompleted = existing.IsCompleted;
                    return existing.PositionMs;
                }
            }
            catch
            {
            }

            return context.StartPositionMs;
        }

        public async Task PersistAsync(PlaybackLaunchContext context, long positionMs, long durationMs, bool force)
        {
            if (!ShouldPersist(context, positionMs, durationMs, force, out var normalizedPositionMs, out var normalizedDurationMs, out var isCompleted))
            {
                return;
            }

            await _saveLock.WaitAsync();
            try
            {
                await PersistCoreAsync(context, normalizedPositionMs, normalizedDurationMs, isCompleted);
            }
            catch
            {
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public void PersistBlocking(PlaybackLaunchContext context, long positionMs, long durationMs, bool force)
        {
            if (!ShouldPersist(context, positionMs, durationMs, force, out var normalizedPositionMs, out var normalizedDurationMs, out var isCompleted))
            {
                return;
            }

            _saveLock.Wait();
            try
            {
                PersistCoreBlocking(context, normalizedPositionMs, normalizedDurationMs, isCompleted);
            }
            catch
            {
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private bool ShouldPersist(
            PlaybackLaunchContext context,
            long positionMs,
            long durationMs,
            bool force,
            out long normalizedPositionMs,
            out long normalizedDurationMs,
            out bool isCompleted)
        {
            normalizedDurationMs = Math.Max(durationMs, 0);
            normalizedPositionMs = Math.Max(positionMs, 0);

            if (normalizedDurationMs > 0 && normalizedPositionMs > normalizedDurationMs)
            {
                normalizedPositionMs = normalizedDurationMs;
            }

            isCompleted = normalizedDurationMs > 0 && normalizedPositionMs >= normalizedDurationMs * 0.95;
            if (context == null || context.ContentType == PlaybackContentType.Channel)
            {
                return false;
            }

            if (normalizedPositionMs < MinimumSavedPositionMs && !(force && isCompleted))
            {
                return false;
            }

            if (!force &&
                Math.Abs(normalizedPositionMs - _lastSavedPositionMs) < 4_000 &&
                Math.Abs(normalizedDurationMs - _lastSavedDurationMs) < 4_000 &&
                _lastSavedCompleted == isCompleted)
            {
                return false;
            }

            return true;
        }

        private async Task PersistCoreAsync(PlaybackLaunchContext context, long positionMs, long durationMs, bool isCompleted)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existing = await db.PlaybackProgresses.FirstOrDefaultAsync(
                progress => progress.ContentType == context.ContentType &&
                            progress.ContentId == context.ContentId);

            ApplyProgress(db, existing, context, positionMs, isCompleted);
            await db.SaveChangesAsync();
            UpdateLastSaved(positionMs, durationMs, isCompleted);
        }

        private void PersistCoreBlocking(PlaybackLaunchContext context, long positionMs, long durationMs, bool isCompleted)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existing = db.PlaybackProgresses.FirstOrDefault(
                progress => progress.ContentType == context.ContentType &&
                            progress.ContentId == context.ContentId);

            ApplyProgress(db, existing, context, positionMs, isCompleted);
            db.SaveChanges();
            UpdateLastSaved(positionMs, durationMs, isCompleted);
        }

        private static void ApplyProgress(
            AppDbContext db,
            PlaybackProgress existing,
            PlaybackLaunchContext context,
            long positionMs,
            bool isCompleted)
        {
            if (existing == null)
            {
                db.PlaybackProgresses.Add(new PlaybackProgress
                {
                    ContentType = context.ContentType,
                    ContentId = context.ContentId,
                    PositionMs = positionMs,
                    IsCompleted = isCompleted,
                    LastWatched = DateTime.UtcNow,
                });
                return;
            }

            existing.PositionMs = positionMs;
            existing.IsCompleted = isCompleted;
            existing.LastWatched = DateTime.UtcNow;
        }

        private void UpdateLastSaved(long positionMs, long durationMs, bool isCompleted)
        {
            _lastSavedPositionMs = positionMs;
            _lastSavedDurationMs = durationMs;
            _lastSavedCompleted = isCompleted;
        }
    }
}
