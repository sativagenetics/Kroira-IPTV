#nullable enable
using System;
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

                await logicalCatalogStateService.ReconcilePersistentStateAsync(db);
                await contentOperationalService.RefreshOperationalStateAsync(db);
                await autoRefreshService.UpdateScheduleAsync(db, sourceProfileId, trigger, success: true, guideSummary);

                var catalogSummary = scope switch
                {
                    SourceRefreshScope.EpgOnly => "Guide-only refresh completed.",
                    SourceRefreshScope.VodOnly => "VOD refresh completed.",
                    SourceRefreshScope.LiveOnly when profile.Type == SourceType.Xtream => "Xtream live refresh completed.",
                    SourceRefreshScope.LiveOnly => "Playlist import completed.",
                    _ when profile.Type == SourceType.Xtream => "Xtream catalog refresh completed.",
                    _ => "Playlist import completed."
                };

                var message = guideAttempted && !guideSucceeded && !string.IsNullOrWhiteSpace(guideSummary)
                    ? $"{catalogSummary} {guideSummary}"
                    : string.IsNullOrWhiteSpace(guideSummary)
                        ? catalogSummary
                        : $"{catalogSummary} {guideSummary}";

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
                try
                {
                    await logicalCatalogStateService.ReconcilePersistentStateAsync(db);
                    await contentOperationalService.RefreshOperationalStateAsync(db);
                }
                catch
                {
                }

                await autoRefreshService.UpdateScheduleAsync(db, sourceProfileId, trigger, success: false, ex.Message);

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
    }
}
