#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Services
{
    public sealed class SourceRefreshResult
    {
        public int SourceProfileId { get; init; }
        public SourceRefreshTrigger Trigger { get; init; }
        public SourceRefreshScope Scope { get; init; }
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public string CatalogSummary { get; init; } = string.Empty;
        public string GuideSummary { get; init; } = string.Empty;
        public bool GuideAttempted { get; init; }
        public bool GuideSucceeded { get; init; }
    }

    public interface ISourceRefreshService
    {
        Task<SourceRefreshResult> RefreshSourceAsync(int sourceProfileId, SourceRefreshTrigger trigger, SourceRefreshScope scope);
    }

    public sealed class SourceRefreshService : ISourceRefreshService
    {
        private readonly IServiceProvider _serviceProvider;

        public SourceRefreshService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task<SourceRefreshResult> RefreshSourceAsync(int sourceProfileId, SourceRefreshTrigger trigger, SourceRefreshScope scope)
        {
            using var scopeServices = _serviceProvider.CreateScope();
            var db = scopeServices.ServiceProvider.GetRequiredService<AppDbContext>();
            var parserM3u = scopeServices.ServiceProvider.GetRequiredService<IM3uParserService>();
            var parserXtream = scopeServices.ServiceProvider.GetRequiredService<IXtreamParserService>();
            var parserXmltv = scopeServices.ServiceProvider.GetRequiredService<IXmltvParserService>();
            var browsePreferencesService = scopeServices.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
            var logicalCatalogStateService = scopeServices.ServiceProvider.GetRequiredService<ILogicalCatalogStateService>();
            var contentOperationalService = scopeServices.ServiceProvider.GetRequiredService<IContentOperationalService>();
            var autoRefreshService = scopeServices.ServiceProvider.GetRequiredService<ISourceAutoRefreshService>();

            var profile = await db.SourceProfiles.FirstOrDefaultAsync(item => item.Id == sourceProfileId);
            if (profile == null)
            {
                throw new InvalidOperationException("Source not found.");
            }

            var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId);
            if (syncState == null)
            {
                syncState = new SourceSyncState
                {
                    SourceProfileId = sourceProfileId,
                    LastAttempt = DateTime.UtcNow
                };
                db.SourceSyncStates.Add(syncState);
                await db.SaveChangesAsync();
            }

            if (trigger == SourceRefreshTrigger.Auto)
            {
                syncState.LastAutoRefreshAttemptAtUtc = DateTime.UtcNow;
                syncState.AutoRefreshState = SourceAutoRefreshState.Running;
                syncState.AutoRefreshSummary = "Automatic refresh is running.";
                await db.SaveChangesAsync();
            }

            var guideAttempted = false;
            var guideSucceeded = false;
            var guideSummary = string.Empty;
            var runtimeRepairSummary = string.Empty;

            try
            {
                switch (scope)
                {
                    case SourceRefreshScope.EpgOnly:
                        guideAttempted = true;
                        await parserXmltv.ParseAndImportEpgAsync(db, sourceProfileId);
                        guideSucceeded = true;
                        guideSummary = "Guide sync completed.";
                        break;

                    case SourceRefreshScope.VodOnly:
                        if (profile.Type != SourceType.Xtream)
                        {
                            throw new InvalidOperationException("VOD-only refresh is only supported for Xtream sources.");
                        }

                        await parserXtream.ParseAndImportXtreamVodAsync(db, sourceProfileId);
                        break;

                    case SourceRefreshScope.LiveOnly:
                        if (profile.Type == SourceType.M3U)
                        {
                            await parserM3u.ParseAndImportM3uAsync(db, sourceProfileId);
                        }
                        else
                        {
                            await parserXtream.ParseAndImportXtreamAsync(db, sourceProfileId);
                        }

                        guideAttempted = true;
                        try
                        {
                            await parserXmltv.ParseAndImportEpgAsync(db, sourceProfileId);
                            guideSucceeded = true;
                            guideSummary = "Guide sync completed.";
                        }
                        catch (Exception ex)
                        {
                            guideSummary = $"Guide sync failed: {ex.Message}";
                        }
                        break;

                    default:
                        if (profile.Type == SourceType.M3U)
                        {
                            await parserM3u.ParseAndImportM3uAsync(db, sourceProfileId);
                        }
                        else
                        {
                            await parserXtream.ParseAndImportXtreamAsync(db, sourceProfileId);
                            await parserXtream.ParseAndImportXtreamVodAsync(db, sourceProfileId);
                        }

                        guideAttempted = true;
                        try
                        {
                            await parserXmltv.ParseAndImportEpgAsync(db, sourceProfileId);
                            guideSucceeded = true;
                            guideSummary = "Guide sync completed.";
                        }
                        catch (Exception ex)
                        {
                            guideSummary = $"Guide sync failed: {ex.Message}";
                        }
                        break;
                }

                runtimeRepairSummary = await TryFinalizeRuntimeStateAsync(
                    db,
                    sourceProfileId,
                    browsePreferencesService,
                    logicalCatalogStateService,
                    contentOperationalService);
                var scheduleSummary = CombineSummaries(guideSummary, runtimeRepairSummary);
                await autoRefreshService.UpdateScheduleAsync(db, sourceProfileId, trigger, success: true, scheduleSummary);

                var catalogSummary = scope switch
                {
                    SourceRefreshScope.EpgOnly => "Guide-only refresh completed.",
                    SourceRefreshScope.VodOnly => "VOD refresh completed.",
                    SourceRefreshScope.LiveOnly when profile.Type == SourceType.Xtream => "Xtream live refresh completed.",
                    SourceRefreshScope.LiveOnly => "Playlist import completed.",
                    _ when profile.Type == SourceType.Xtream => "Xtream catalog refresh completed.",
                    _ => "Playlist import completed."
                };

                var message = CombineSummaries(catalogSummary, guideSummary, runtimeRepairSummary);

                return new SourceRefreshResult
                {
                    SourceProfileId = sourceProfileId,
                    Trigger = trigger,
                    Scope = scope,
                    Success = true,
                    Message = message,
                    CatalogSummary = catalogSummary,
                    GuideSummary = guideSummary,
                    GuideAttempted = guideAttempted,
                    GuideSucceeded = guideSucceeded
                };
            }
            catch (Exception ex)
            {
                runtimeRepairSummary = await TryFinalizeRuntimeStateAsync(
                    db,
                    sourceProfileId,
                    browsePreferencesService,
                    logicalCatalogStateService,
                    contentOperationalService);

                await autoRefreshService.UpdateScheduleAsync(
                    db,
                    sourceProfileId,
                    trigger,
                    success: false,
                    CombineSummaries(ex.Message, runtimeRepairSummary));
                RuntimeEventLogger.Log("SOURCE-REFRESH", ex, $"source_id={sourceProfileId} refresh failed");

                return new SourceRefreshResult
                {
                    SourceProfileId = sourceProfileId,
                    Trigger = trigger,
                    Scope = scope,
                    Success = false,
                    Message = ex.Message,
                    CatalogSummary = string.Empty,
                    GuideSummary = guideSummary,
                    GuideAttempted = guideAttempted,
                    GuideSucceeded = false
                };
            }
        }

        private static async Task<string> TryFinalizeRuntimeStateAsync(
            AppDbContext db,
            int sourceProfileId,
            IBrowsePreferencesService browsePreferencesService,
            ILogicalCatalogStateService logicalCatalogStateService,
            IContentOperationalService contentOperationalService)
        {
            var warnings = new List<string>();

            try
            {
                await browsePreferencesService.RepairSourceReferencesAsync(db, sourceProfileId);
            }
            catch (Exception ex)
            {
                RuntimeEventLogger.Log("SOURCE-REFRESH", ex, $"source_id={sourceProfileId} browse repair skipped");
                warnings.Add("Some browse state repairs were deferred.");
            }

            try
            {
                await logicalCatalogStateService.ReconcilePersistentStateAsync(db);
            }
            catch (Exception ex)
            {
                RuntimeEventLogger.Log("SOURCE-REFRESH", ex, $"source_id={sourceProfileId} state reconciliation skipped");
                warnings.Add("Some saved state repairs were deferred.");
            }

            try
            {
                await contentOperationalService.RefreshOperationalStateAsync(db);
            }
            catch (Exception ex)
            {
                RuntimeEventLogger.Log("SOURCE-REFRESH", ex, $"source_id={sourceProfileId} operational rebuild skipped");
                warnings.Add("Operational mirror refresh was deferred.");
            }

            return CombineSummaries(warnings.ToArray());
        }

        private static string CombineSummaries(params string[] segments)
        {
            return string.Join(
                " ",
                segments
                    .Where(segment => !string.IsNullOrWhiteSpace(segment))
                    .Select(segment => segment.Trim()));
        }
    }
}
