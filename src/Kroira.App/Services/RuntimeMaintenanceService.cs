#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Services
{
    public interface IRuntimeMaintenanceService
    {
        Task RunStartupRepairAsync(CancellationToken cancellationToken = default);
        Task RunDeferredRepairAsync(CancellationToken cancellationToken = default);
    }

    public sealed class RuntimeMaintenanceService : IRuntimeMaintenanceService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly SemaphoreSlim _repairLock = new(1, 1);

        public RuntimeMaintenanceService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task RunStartupRepairAsync(CancellationToken cancellationToken = default)
        {
            await RunPhaseAsync("RUNTIME-MAINT", "startup repair", cancellationToken, async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                var profileStateService = services.GetRequiredService<IProfileStateService>();
                var browsePreferencesService = services.GetRequiredService<IBrowsePreferencesService>();
                var logicalCatalogStateService = services.GetRequiredService<ILogicalCatalogStateService>();
                var contentOperationalService = services.GetRequiredService<IContentOperationalService>();
                var autoRefreshService = services.GetRequiredService<ISourceAutoRefreshService>();

                await profileStateService.GetActiveProfileAsync(db);
                await CleanupOrphanedRowsAsync(db, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                await autoRefreshService.RepairRuntimeStateAsync(db);
                await browsePreferencesService.RepairSourceReferencesAsync(db);
                await logicalCatalogStateService.ReconcilePersistentStateAsync(db);

                if (await NeedsOperationalRepairAsync(db, cancellationToken))
                {
                    RuntimeEventLogger.Log("RUNTIME-MAINT", "rebuilding operational state during startup repair");
                    await contentOperationalService.RefreshOperationalStateAsync(db);
                }
            });
        }

        public async Task RunDeferredRepairAsync(CancellationToken cancellationToken = default)
        {
            await RunPhaseAsync("RUNTIME-MAINT", "deferred repair", cancellationToken, async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                var profileStateService = services.GetRequiredService<IProfileStateService>();
                var browsePreferencesService = services.GetRequiredService<IBrowsePreferencesService>();
                var contentOperationalService = services.GetRequiredService<IContentOperationalService>();
                var sourceHealthService = services.GetRequiredService<ISourceHealthService>();

                await profileStateService.GetActiveProfileAsync(db);
                await CleanupOrphanedRowsAsync(db, cancellationToken);
                await browsePreferencesService.RepairSourceReferencesAsync(db);

                var sourcesToRefresh = await FindSourcesNeedingHealthRepairAsync(db, cancellationToken);
                foreach (var sourceId in sourcesToRefresh)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        RuntimeEventLogger.Log("RUNTIME-MAINT", $"repairing source health for source_id={sourceId}");
                        await sourceHealthService.RefreshSourceHealthAsync(db, sourceId);
                    }
                    catch (Exception ex)
                    {
                        RuntimeEventLogger.Log("RUNTIME-MAINT", ex, $"source health repair skipped for source_id={sourceId}");
                    }
                }

                if (await NeedsOperationalRepairAsync(db, cancellationToken))
                {
                    RuntimeEventLogger.Log("RUNTIME-MAINT", "rebuilding operational state during deferred repair");
                    await contentOperationalService.RefreshOperationalStateAsync(db);
                }
            });
        }

        public void Dispose()
        {
            _repairLock.Dispose();
        }

        private async Task RunPhaseAsync(
            string area,
            string phase,
            CancellationToken cancellationToken,
            Func<IServiceProvider, Task> action)
        {
            await _repairLock.WaitAsync(cancellationToken);
            try
            {
                RuntimeEventLogger.Log(area, $"{phase} started");
                using var scope = _serviceProvider.CreateScope();
                await action(scope.ServiceProvider);
                RuntimeEventLogger.Log(area, $"{phase} completed");
            }
            finally
            {
                _repairLock.Release();
            }
        }

        private static async Task<bool> NeedsOperationalRepairAsync(AppDbContext db, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hasPlayableCatalog = await db.Channels.AnyAsync(cancellationToken) ||
                                     await db.Movies.AnyAsync(cancellationToken);
            if (!hasPlayableCatalog)
            {
                return false;
            }

            var hasOperationalStates = await db.LogicalOperationalStates.AnyAsync(cancellationToken);
            if (!hasOperationalStates)
            {
                return true;
            }

            var stateIds = await db.LogicalOperationalStates
                .AsNoTracking()
                .Select(state => state.Id)
                .ToListAsync(cancellationToken);
            var candidateStateIds = await db.LogicalOperationalCandidates
                .AsNoTracking()
                .Select(candidate => candidate.LogicalOperationalStateId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var hasCandidateFreeState = stateIds.Except(candidateStateIds).Any();
            if (hasCandidateFreeState)
            {
                return true;
            }

            return candidateStateIds.Except(stateIds).Any();
        }

        private static async Task<List<int>> FindSourcesNeedingHealthRepairAsync(AppDbContext db, CancellationToken cancellationToken)
        {
            var sourceIds = await db.SourceProfiles
                .AsNoTracking()
                .OrderBy(profile => profile.Id)
                .Select(profile => profile.Id)
                .ToListAsync(cancellationToken);
            if (sourceIds.Count == 0)
            {
                return new List<int>();
            }

            var reportIds = await db.SourceHealthReports
                .AsNoTracking()
                .Where(report => sourceIds.Contains(report.SourceProfileId))
                .Select(report => new { report.Id, report.SourceProfileId })
                .ToListAsync(cancellationToken);
            var reportIdBySource = reportIds.ToDictionary(item => item.SourceProfileId, item => item.Id);

            var componentCounts = await db.SourceHealthComponents
                .AsNoTracking()
                .Join(
                    db.SourceHealthReports.AsNoTracking(),
                    component => component.SourceHealthReportId,
                    report => report.Id,
                    (component, report) => report.SourceProfileId)
                .Where(sourceId => sourceIds.Contains(sourceId))
                .GroupBy(sourceId => sourceId)
                .Select(group => new { SourceId = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);
            var probeCounts = await db.SourceHealthProbes
                .AsNoTracking()
                .Join(
                    db.SourceHealthReports.AsNoTracking(),
                    probe => probe.SourceHealthReportId,
                    report => report.Id,
                    (probe, report) => report.SourceProfileId)
                .Where(sourceId => sourceIds.Contains(sourceId))
                .GroupBy(sourceId => sourceId)
                .Select(group => new { SourceId = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);
            var sourcesWithCatalog = await db.SourceProfiles
                .AsNoTracking()
                .Where(profile => sourceIds.Contains(profile.Id))
                .Select(profile => new
                {
                    profile.Id,
                    profile.LastSync,
                    LiveCount = db.ChannelCategories
                        .Where(category => category.SourceProfileId == profile.Id)
                        .Join(db.Channels, category => category.Id, channel => channel.ChannelCategoryId, (category, channel) => channel.Id)
                        .Count(),
                    MovieCount = db.Movies.Count(movie => movie.SourceProfileId == profile.Id),
                    SeriesCount = db.Series.Count(series => series.SourceProfileId == profile.Id)
                })
                .ToListAsync(cancellationToken);

            var componentCountBySource = componentCounts.ToDictionary(item => item.SourceId, item => item.Count);
            var probeCountBySource = probeCounts.ToDictionary(item => item.SourceId, item => item.Count);
            var sourcesToRefresh = new List<int>();

            foreach (var source in sourcesWithCatalog)
            {
                var hasCatalog = source.LiveCount > 0 || source.MovieCount > 0 || source.SeriesCount > 0 || source.LastSync.HasValue;
                if (!hasCatalog)
                {
                    continue;
                }

                if (!reportIdBySource.ContainsKey(source.Id))
                {
                    sourcesToRefresh.Add(source.Id);
                    continue;
                }

                if (!componentCountBySource.TryGetValue(source.Id, out var componentCount) || componentCount == 0)
                {
                    sourcesToRefresh.Add(source.Id);
                    continue;
                }

                if (!probeCountBySource.TryGetValue(source.Id, out var probeCount) || probeCount == 0)
                {
                    sourcesToRefresh.Add(source.Id);
                }
            }

            return sourcesToRefresh.Distinct().ToList();
        }

        private static async Task CleanupOrphanedRowsAsync(AppDbContext db, CancellationToken cancellationToken)
        {
            var profileIds = await db.AppProfiles
                .AsNoTracking()
                .Select(profile => profile.Id)
                .ToListAsync(cancellationToken);
            var sourceIds = await db.SourceProfiles
                .AsNoTracking()
                .Select(profile => profile.Id)
                .ToListAsync(cancellationToken);

            var orphanFavorites = await db.Favorites
                .Where(favorite => !profileIds.Contains(favorite.ProfileId))
                .ToListAsync(cancellationToken);
            var orphanProgress = await db.PlaybackProgresses
                .Where(progress => !profileIds.Contains(progress.ProfileId))
                .ToListAsync(cancellationToken);
            var orphanControls = await db.ParentalControlSettings
                .Where(setting => !profileIds.Contains(setting.ProfileId))
                .ToListAsync(cancellationToken);
            var orphanSyncStates = await db.SourceSyncStates
                .Where(state => !sourceIds.Contains(state.SourceProfileId))
                .ToListAsync(cancellationToken);
            var orphanCredentials = await db.SourceCredentials
                .Where(credential => !sourceIds.Contains(credential.SourceProfileId))
                .ToListAsync(cancellationToken);
            var orphanEpgLogs = await db.EpgSyncLogs
                .Where(log => !sourceIds.Contains(log.SourceProfileId))
                .ToListAsync(cancellationToken);
            var orphanHealthReports = await db.SourceHealthReports
                .Where(report => !sourceIds.Contains(report.SourceProfileId))
                .ToListAsync(cancellationToken);
            var healthReportIds = await db.SourceHealthReports
                .AsNoTracking()
                .Select(report => report.Id)
                .ToListAsync(cancellationToken);
            var orphanHealthComponents = await db.SourceHealthComponents
                .Where(component => !healthReportIds.Contains(component.SourceHealthReportId))
                .ToListAsync(cancellationToken);
            var orphanHealthProbes = await db.SourceHealthProbes
                .Where(probe => !healthReportIds.Contains(probe.SourceHealthReportId))
                .ToListAsync(cancellationToken);
            var orphanHealthIssues = await db.SourceHealthIssues
                .Where(issue => !healthReportIds.Contains(issue.SourceHealthReportId))
                .ToListAsync(cancellationToken);
            var orphanEnrichment = await db.SourceChannelEnrichmentRecords
                .Where(record => !sourceIds.Contains(record.SourceProfileId))
                .ToListAsync(cancellationToken);
            var operationalStateIds = await db.LogicalOperationalStates
                .AsNoTracking()
                .Select(state => state.Id)
                .ToListAsync(cancellationToken);
            var orphanOperationalCandidates = await db.LogicalOperationalCandidates
                .Where(candidate => !operationalStateIds.Contains(candidate.LogicalOperationalStateId))
                .ToListAsync(cancellationToken);
            var candidateStateIds = await db.LogicalOperationalCandidates
                .AsNoTracking()
                .Select(candidate => candidate.LogicalOperationalStateId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var orphanOperationalStates = await db.LogicalOperationalStates
                .Where(state => !candidateStateIds.Contains(state.Id))
                .ToListAsync(cancellationToken);

            if (orphanFavorites.Count == 0 &&
                orphanProgress.Count == 0 &&
                orphanControls.Count == 0 &&
                orphanSyncStates.Count == 0 &&
                orphanCredentials.Count == 0 &&
                orphanEpgLogs.Count == 0 &&
                orphanHealthReports.Count == 0 &&
                orphanHealthComponents.Count == 0 &&
                orphanHealthProbes.Count == 0 &&
                orphanHealthIssues.Count == 0 &&
                orphanEnrichment.Count == 0 &&
                orphanOperationalCandidates.Count == 0 &&
                orphanOperationalStates.Count == 0)
            {
                return;
            }

            db.Favorites.RemoveRange(orphanFavorites);
            db.PlaybackProgresses.RemoveRange(orphanProgress);
            db.ParentalControlSettings.RemoveRange(orphanControls);
            db.SourceSyncStates.RemoveRange(orphanSyncStates);
            db.SourceCredentials.RemoveRange(orphanCredentials);
            db.EpgSyncLogs.RemoveRange(orphanEpgLogs);
            db.SourceHealthComponents.RemoveRange(orphanHealthComponents);
            db.SourceHealthProbes.RemoveRange(orphanHealthProbes);
            db.SourceHealthIssues.RemoveRange(orphanHealthIssues);
            db.SourceHealthReports.RemoveRange(orphanHealthReports);
            db.SourceChannelEnrichmentRecords.RemoveRange(orphanEnrichment);
            db.LogicalOperationalCandidates.RemoveRange(orphanOperationalCandidates);
            db.LogicalOperationalStates.RemoveRange(orphanOperationalStates);

            await db.SaveChangesAsync(cancellationToken);
            RuntimeEventLogger.Log(
                "RUNTIME-MAINT",
                $"removed orphaned rows: favorites={orphanFavorites.Count}, progress={orphanProgress.Count}, sync={orphanSyncStates.Count}, operational={orphanOperationalCandidates.Count + orphanOperationalStates.Count}");
        }
    }
}
