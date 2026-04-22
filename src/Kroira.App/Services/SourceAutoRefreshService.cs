#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Services
{
    public interface ISourceAutoRefreshService
    {
        Task<SourceAutoRefreshSettings> LoadSettingsAsync(AppDbContext db);
        Task SaveSettingsAsync(AppDbContext db, SourceAutoRefreshSettings settings);
        Task UpdateScheduleAsync(AppDbContext db, int sourceProfileId, SourceRefreshTrigger trigger, bool success, string summary);
        Task RefreshDueSourcesAsync(bool runOverdueOnly);
        void Start();
        void Stop();
    }

    public sealed class SourceAutoRefreshService : ISourceAutoRefreshService, IDisposable
    {
        private const string EnabledKey = "Sources.AutoRefresh.Enabled";
        private const string IntervalKey = "Sources.AutoRefresh.IntervalHours";
        private const string RunAfterLaunchKey = "Sources.AutoRefresh.RunAfterLaunch";
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(35);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

        private readonly IServiceProvider _serviceProvider;
        private readonly ISourceRefreshService _sourceRefreshService;
        private readonly SemaphoreSlim _pollLock = new(1, 1);
        private readonly HashSet<int> _runningSourceIds = new();
        private readonly object _gate = new();

        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;

        public SourceAutoRefreshService(IServiceProvider serviceProvider, ISourceRefreshService sourceRefreshService)
        {
            _serviceProvider = serviceProvider;
            _sourceRefreshService = sourceRefreshService;
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_backgroundTask != null)
                {
                    return;
                }

                _cts = new CancellationTokenSource();
                _backgroundTask = RunAsync(_cts.Token);
            }
        }

        public void Stop()
        {
            CancellationTokenSource? cts;
            Task? backgroundTask;
            lock (_gate)
            {
                cts = _cts;
                backgroundTask = _backgroundTask;
                _cts = null;
                _backgroundTask = null;
            }

            try
            {
                cts?.Cancel();
            }
            catch
            {
            }

            if (backgroundTask != null)
            {
                _ = backgroundTask.ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }

        public async Task<SourceAutoRefreshSettings> LoadSettingsAsync(AppDbContext db)
        {
            var settings = await db.AppSettings
                .Where(setting => setting.Key == EnabledKey ||
                                  setting.Key == IntervalKey ||
                                  setting.Key == RunAfterLaunchKey)
                .ToDictionaryAsync(setting => setting.Key, setting => setting.Value);

            return new SourceAutoRefreshSettings
            {
                IsEnabled = ParseBoolean(settings, EnabledKey, true),
                IntervalHours = ParseInt(settings, IntervalKey, 6, 1, 24),
                RunAfterLaunch = ParseBoolean(settings, RunAfterLaunchKey, true)
            };
        }

        public async Task SaveSettingsAsync(AppDbContext db, SourceAutoRefreshSettings settings)
        {
            await SaveSettingAsync(db, EnabledKey, settings.IsEnabled.ToString());
            await SaveSettingAsync(db, IntervalKey, Math.Clamp(settings.IntervalHours, 1, 24).ToString());
            await SaveSettingAsync(db, RunAfterLaunchKey, settings.RunAfterLaunch.ToString());

            var sourceIds = await db.SourceProfiles.AsNoTracking().Select(profile => profile.Id).ToListAsync();
            foreach (var sourceId in sourceIds)
            {
                await UpdateScheduleAsync(db, sourceId, SourceRefreshTrigger.Manual, success: true, string.Empty);
            }
        }

        public async Task UpdateScheduleAsync(AppDbContext db, int sourceProfileId, SourceRefreshTrigger trigger, bool success, string summary)
        {
            var settings = await LoadSettingsAsync(db);
            var profile = await db.SourceProfiles.FirstOrDefaultAsync(item => item.Id == sourceProfileId);
            if (profile == null)
            {
                return;
            }

            var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId);
            if (syncState == null)
            {
                syncState = new SourceSyncState
                {
                    SourceProfileId = sourceProfileId
                };
                db.SourceSyncStates.Add(syncState);
            }

            if (!settings.IsEnabled)
            {
                syncState.AutoRefreshState = SourceAutoRefreshState.Disabled;
                syncState.NextAutoRefreshDueAtUtc = null;
                syncState.AutoRefreshSummary = "Automatic refresh is turned off.";
                await db.SaveChangesAsync();
                return;
            }

            if (trigger == SourceRefreshTrigger.Auto)
            {
                syncState.LastAutoRefreshAttemptAtUtc ??= DateTime.UtcNow;
                if (success)
                {
                    syncState.LastAutoRefreshSuccessAtUtc = DateTime.UtcNow;
                    syncState.AutoRefreshFailureCount = 0;
                    syncState.AutoRefreshState = SourceAutoRefreshState.Succeeded;
                }
                else
                {
                    syncState.AutoRefreshFailureCount++;
                    syncState.AutoRefreshState = SourceAutoRefreshState.Failed;
                }
            }
            else if (success)
            {
                syncState.AutoRefreshState = SourceAutoRefreshState.Scheduled;
            }

            var nextDue = ComputeNextDue(syncState, profile.LastSync, settings, DateTime.UtcNow);
            syncState.NextAutoRefreshDueAtUtc = nextDue;
            syncState.AutoRefreshSummary = BuildSummary(settings, syncState, nextDue, success, summary);
            await db.SaveChangesAsync();
        }

        public async Task RefreshDueSourcesAsync(bool runOverdueOnly)
        {
            if (!await _pollLock.WaitAsync(0))
            {
                return;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var settings = await LoadSettingsAsync(db);
                var profiles = await db.SourceProfiles
                    .AsNoTracking()
                    .OrderBy(profile => profile.Name)
                    .ToListAsync();
                var syncStates = await db.SourceSyncStates
                    .Where(state => profiles.Select(profile => profile.Id).Contains(state.SourceProfileId))
                    .ToDictionaryAsync(state => state.SourceProfileId);

                if (!settings.IsEnabled)
                {
                    foreach (var profile in profiles)
                    {
                        if (!syncStates.TryGetValue(profile.Id, out var state))
                        {
                            state = new SourceSyncState { SourceProfileId = profile.Id };
                            db.SourceSyncStates.Add(state);
                            syncStates[profile.Id] = state;
                        }

                        state.AutoRefreshState = SourceAutoRefreshState.Disabled;
                        state.NextAutoRefreshDueAtUtc = null;
                        state.AutoRefreshSummary = "Automatic refresh is turned off.";
                    }

                    await db.SaveChangesAsync();
                    return;
                }

                var nowUtc = DateTime.UtcNow;
                var dueSourceIds = new List<int>();
                foreach (var profile in profiles)
                {
                    if (!syncStates.TryGetValue(profile.Id, out var state))
                    {
                        state = new SourceSyncState { SourceProfileId = profile.Id };
                        db.SourceSyncStates.Add(state);
                        syncStates[profile.Id] = state;
                    }

                    var nextDue = ComputeNextDue(state, profile.LastSync, settings, nowUtc);
                    state.NextAutoRefreshDueAtUtc = nextDue;
                    var hasNeverSucceeded = !state.LastAutoRefreshSuccessAtUtc.HasValue && !profile.LastSync.HasValue;
                    if (!_runningSourceIds.Contains(profile.Id) &&
                        nextDue.HasValue &&
                        (nextDue.Value <= nowUtc || !runOverdueOnly && hasNeverSucceeded))
                    {
                        dueSourceIds.Add(profile.Id);
                    }

                    if (!_runningSourceIds.Contains(profile.Id))
                    {
                        state.AutoRefreshState = SourceAutoRefreshState.Scheduled;
                        state.AutoRefreshSummary = BuildSummary(settings, state, nextDue, success: true, string.Empty);
                    }
                }

                await db.SaveChangesAsync();

                foreach (var sourceId in dueSourceIds.Distinct())
                {
                    lock (_runningSourceIds)
                    {
                        if (!_runningSourceIds.Add(sourceId))
                        {
                            continue;
                        }
                    }

                    try
                    {
                        await _sourceRefreshService.RefreshSourceAsync(sourceId, SourceRefreshTrigger.Auto, SourceRefreshScope.Full);
                    }
                    finally
                    {
                        lock (_runningSourceIds)
                        {
                            _runningSourceIds.Remove(sourceId);
                        }
                    }
                }
            }
            finally
            {
                _pollLock.Release();
            }
        }

        public void Dispose()
        {
            Stop();
            _pollLock.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(StartupDelay, cancellationToken);

                using (var scope = _serviceProvider.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var settings = await LoadSettingsAsync(db);
                    if (settings.RunAfterLaunch)
                    {
                        await RefreshDueSourcesAsync(runOverdueOnly: false);
                    }
                    else
                    {
                        await RefreshDueSourcesAsync(runOverdueOnly: true);
                    }
                }

                using var timer = new PeriodicTimer(PollInterval);
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    await RefreshDueSourcesAsync(runOverdueOnly: true);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static DateTime? ComputeNextDue(
            SourceSyncState syncState,
            DateTime? lastSuccessfulSync,
            SourceAutoRefreshSettings settings,
            DateTime nowUtc)
        {
            var baseUtc = syncState.LastAutoRefreshSuccessAtUtc
                          ?? (lastSuccessfulSync.HasValue ? NormalizeUtc(lastSuccessfulSync.Value) : (DateTime?)null);

            return baseUtc.HasValue
                ? baseUtc.Value.Add(settings.GetInterval())
                : nowUtc;
        }

        private static string BuildSummary(
            SourceAutoRefreshSettings settings,
            SourceSyncState syncState,
            DateTime? nextDue,
            bool success,
            string summary)
        {
            if (!settings.IsEnabled)
            {
                return "Automatic refresh is turned off.";
            }

            if (syncState.AutoRefreshState == SourceAutoRefreshState.Running)
            {
                return "Automatic refresh is running.";
            }

            if (!success && syncState.AutoRefreshState == SourceAutoRefreshState.Failed)
            {
                var shortSummary = string.IsNullOrWhiteSpace(summary) ? "The latest automatic refresh failed." : summary.Trim();
                return nextDue.HasValue
                    ? $"{shortSummary} Next attempt {nextDue.Value.ToLocalTime():g}."
                    : shortSummary;
            }

            return nextDue.HasValue
                ? $"Automatic refresh every {settings.IntervalHours}h. Next run {nextDue.Value.ToLocalTime():g}."
                : $"Automatic refresh every {settings.IntervalHours}h.";
        }

        private static bool ParseBoolean(IReadOnlyDictionary<string, string> values, string key, bool defaultValue)
        {
            return values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
                ? parsed
                : defaultValue;
        }

        private static int ParseInt(IReadOnlyDictionary<string, string> values, string key, int defaultValue, int minValue, int maxValue)
        {
            return values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
                ? Math.Clamp(parsed, minValue, maxValue)
                : defaultValue;
        }

        private static async Task SaveSettingAsync(AppDbContext db, string key, string value)
        {
            var setting = await db.AppSettings.FirstOrDefaultAsync(existing => existing.Key == key);
            if (setting == null)
            {
                db.AppSettings.Add(new AppSetting
                {
                    Key = key,
                    Value = value
                });
            }
            else
            {
                setting.Value = value;
            }

            await db.SaveChangesAsync();
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }
}
