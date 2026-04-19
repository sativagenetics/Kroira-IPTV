using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Services.Playback
{
    internal sealed class PlaybackProgressCoordinator
    {
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
                var profileId = await ResolveProfileIdAsync(scope.ServiceProvider, db, context);
                var existing = await db.PlaybackProgresses.FirstOrDefaultAsync(
                    progress => progress.ProfileId == profileId &&
                                progress.ContentType == context.ContentType &&
                                progress.ContentId == context.ContentId &&
                                !progress.IsCompleted);

                if (existing != null && existing.PositionMs >= WatchStateRules.MinimumSavedPositionMs)
                {
                    var isWatched = WatchStateRules.IsWatched(existing);
                    var resumePositionMs = WatchStateRules.NormalizeResumePosition(existing.PositionMs, existing.DurationMs, isWatched);
                    if (resumePositionMs > 0)
                    {
                        context.ProfileId = profileId;
                        context.StartPositionMs = resumePositionMs;
                        _lastSavedPositionMs = existing.PositionMs;
                        _lastSavedDurationMs = existing.DurationMs;
                        _lastSavedCompleted = existing.IsCompleted;
                        return resumePositionMs;
                    }
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

            isCompleted = WatchStateRules.ComputeCompleted(normalizedPositionMs, normalizedDurationMs);
            if (context == null || context.ContentType == PlaybackContentType.Channel)
            {
                return false;
            }

            if (normalizedPositionMs < WatchStateRules.MinimumSavedPositionMs && !(force && isCompleted))
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
            var profileId = await ResolveProfileIdAsync(scope.ServiceProvider, db, context);
            var existing = await db.PlaybackProgresses.FirstOrDefaultAsync(
                progress => progress.ProfileId == profileId &&
                            progress.ContentType == context.ContentType &&
                            progress.ContentId == context.ContentId);

            ApplyProgress(db, existing, profileId, context, positionMs, durationMs, isCompleted);
            await db.SaveChangesAsync();
            UpdateLastSaved(positionMs, durationMs, isCompleted);
        }

        private void PersistCoreBlocking(PlaybackLaunchContext context, long positionMs, long durationMs, bool isCompleted)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profileId = ResolveProfileId(scope.ServiceProvider, db, context);
            var existing = db.PlaybackProgresses.FirstOrDefault(
                progress => progress.ProfileId == profileId &&
                            progress.ContentType == context.ContentType &&
                            progress.ContentId == context.ContentId);

            ApplyProgress(db, existing, profileId, context, positionMs, durationMs, isCompleted);
            db.SaveChanges();
            UpdateLastSaved(positionMs, durationMs, isCompleted);
        }

        private static void ApplyProgress(
            AppDbContext db,
            PlaybackProgress existing,
            int profileId,
            PlaybackLaunchContext context,
            long positionMs,
            long durationMs,
            bool isCompleted)
        {
            if (existing == null)
            {
                db.PlaybackProgresses.Add(new PlaybackProgress
                {
                    ProfileId = profileId,
                    ContentType = context.ContentType,
                    ContentId = context.ContentId,
                    PositionMs = positionMs,
                    DurationMs = durationMs,
                    IsCompleted = isCompleted,
                    WatchStateOverride = WatchStateOverride.None,
                    LastWatched = DateTime.UtcNow,
                    CompletedAtUtc = isCompleted ? DateTime.UtcNow : null,
                });
                return;
            }

            existing.PositionMs = positionMs;
            existing.DurationMs = durationMs;
            existing.IsCompleted = isCompleted;
            existing.WatchStateOverride = isCompleted ? WatchStateOverride.None : existing.WatchStateOverride == WatchStateOverride.Watched ? WatchStateOverride.None : existing.WatchStateOverride;
            existing.LastWatched = DateTime.UtcNow;
            existing.CompletedAtUtc = isCompleted ? DateTime.UtcNow : null;
        }

        private void UpdateLastSaved(long positionMs, long durationMs, bool isCompleted)
        {
            _lastSavedPositionMs = positionMs;
            _lastSavedDurationMs = durationMs;
            _lastSavedCompleted = isCompleted;
        }

        private static async Task<int> ResolveProfileIdAsync(IServiceProvider services, AppDbContext db, PlaybackLaunchContext context)
        {
            if (context.ProfileId > 0)
            {
                return context.ProfileId;
            }

            var profileService = services.GetRequiredService<IProfileStateService>();
            context.ProfileId = await profileService.GetActiveProfileIdAsync(db);
            return context.ProfileId;
        }

        private static int ResolveProfileId(IServiceProvider services, AppDbContext db, PlaybackLaunchContext context)
        {
            if (context.ProfileId > 0)
            {
                return context.ProfileId;
            }

            var profileService = services.GetRequiredService<IProfileStateService>();
            context.ProfileId = profileService.GetActiveProfileIdAsync(db).GetAwaiter().GetResult();
            return context.ProfileId;
        }
    }
}
