#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        Task<SourceRefreshResult> RefreshSourceAsync(
            int sourceProfileId,
            SourceRefreshTrigger trigger,
            SourceRefreshScope scope,
            CancellationToken cancellationToken = default);
    }

    public sealed class SourceRefreshService : ISourceRefreshService
    {
        private readonly IServiceProvider _serviceProvider;

        public SourceRefreshService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task<SourceRefreshResult> RefreshSourceAsync(
            int sourceProfileId,
            SourceRefreshTrigger trigger,
            SourceRefreshScope scope,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ResolveRefreshTimeout(scope));
            var operationToken = timeoutCts.Token;

            using var scopeServices = _serviceProvider.CreateScope();
            var db = scopeServices.ServiceProvider.GetRequiredService<AppDbContext>();
            var parserM3u = scopeServices.ServiceProvider.GetRequiredService<IM3uParserService>();
            var parserXtream = scopeServices.ServiceProvider.GetRequiredService<IXtreamParserService>();
            var parserStalker = scopeServices.ServiceProvider.GetRequiredService<IStalkerParserService>();
            var parserXmltv = scopeServices.ServiceProvider.GetRequiredService<IXmltvParserService>();
            var acquisitionService = scopeServices.ServiceProvider.GetRequiredService<ISourceAcquisitionService>();
            var browsePreferencesService = scopeServices.ServiceProvider.GetRequiredService<IBrowsePreferencesService>();
            var logicalCatalogStateService = scopeServices.ServiceProvider.GetRequiredService<ILogicalCatalogStateService>();
            var contentOperationalService = scopeServices.ServiceProvider.GetRequiredService<IContentOperationalService>();
            var autoRefreshService = scopeServices.ServiceProvider.GetRequiredService<ISourceAutoRefreshService>();
            var sourceHealthService = scopeServices.ServiceProvider.GetRequiredService<ISourceHealthService>();
            var credentialStore = scopeServices.ServiceProvider.GetRequiredService<ISourceCredentialStore>();

            var profile = await db.SourceProfiles.FirstOrDefaultAsync(item => item.Id == sourceProfileId, operationToken);
            if (profile == null)
            {
                throw new InvalidOperationException("Source not found.");
            }

            var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId, operationToken);
            if (syncState == null)
            {
                syncState = new SourceSyncState
                {
                    SourceProfileId = sourceProfileId,
                    LastAttempt = DateTime.UtcNow
                };
                db.SourceSyncStates.Add(syncState);
                await db.SaveChangesAsync(operationToken);
            }

            if (trigger == SourceRefreshTrigger.Auto)
            {
                syncState.LastAutoRefreshAttemptAtUtc = DateTime.UtcNow;
                syncState.AutoRefreshState = SourceAutoRefreshState.Running;
                syncState.AutoRefreshSummary = "Automatic refresh is running.";
                await db.SaveChangesAsync(operationToken);
            }

            var credential = await credentialStore.GetCredentialAsync(db, sourceProfileId);
            var acquisitionSession = await acquisitionService.BeginSessionAsync(db, profile, credential, trigger, scope);
            var shouldAttemptGuideSync = ShouldAttemptGuideSync(profile, credential);

            var guideAttempted = false;
            var guideSucceeded = false;
            var guideSummary = string.Empty;
            var runtimeRepairSummary = string.Empty;

            try
            {
                switch (scope)
                {
                    case SourceRefreshScope.EpgOnly:
                        if (!shouldAttemptGuideSync)
                        {
                            guideSummary = "Guide sync is not applicable for this source configuration.";
                            break;
                        }

                        guideAttempted = true;
                        await parserXmltv.ParseAndImportEpgAsync(db, sourceProfileId, acquisitionSession, refreshHealth: false, operationToken);
                        guideSucceeded = true;
                        guideSummary = "Guide sync completed.";
                        break;

                    case SourceRefreshScope.VodOnly:
                        if (profile.Type == SourceType.Xtream)
                        {
                            await parserXtream.ParseAndImportXtreamVodAsync(db, sourceProfileId, acquisitionSession, refreshHealth: false, operationToken);
                            break;
                        }

                        if (profile.Type == SourceType.Stalker)
                        {
                            await parserStalker.ParseAndImportStalkerVodAsync(db, sourceProfileId, acquisitionSession, refreshHealth: false, operationToken);
                            break;
                        }

                        if (profile.Type != SourceType.Xtream)
                        {
                            throw new InvalidOperationException("VOD-only refresh is only supported for Xtream or Stalker sources.");
                        }
                        break;

                    case SourceRefreshScope.LiveOnly:
                        if (profile.Type == SourceType.M3U)
                        {
                            await parserM3u.ParseAndImportM3uAsync(db, sourceProfileId, acquisitionSession, refreshHealth: false, operationToken);
                        }
                        else if (profile.Type == SourceType.Stalker)
                        {
                            await parserStalker.ParseAndImportStalkerAsync(db, sourceProfileId, acquisitionSession, refreshHealth: false, operationToken);
                        }
                        else
                        {
                            await parserXtream.ParseAndImportXtreamAsync(db, sourceProfileId, acquisitionSession, refreshHealth: false, operationToken);
                        }

                        if (shouldAttemptGuideSync)
                        {
                            guideAttempted = true;
                            try
                            {
                                await parserXmltv.ParseAndImportEpgAsync(db, sourceProfileId, acquisitionSession, refreshHealth: false, operationToken);
                                guideSucceeded = true;
                                guideSummary = "Guide sync completed.";
                            }
                            catch (Exception ex)
                            {
                                guideSummary = $"Guide sync failed: {ex.Message}";
                            }
                        }
                        break;

                    default:
                        if (profile.Type == SourceType.M3U)
                        {
                            await parserM3u.ParseAndImportM3uAsync(db, sourceProfileId, acquisitionSession, refreshHealth: false, operationToken);
                        }
                        else if (profile.Type == SourceType.Xtream)
                        {
                            await parserXtream.ParseAndImportXtreamAsync(db, sourceProfileId, acquisitionSession, refreshHealth: false, operationToken);
                            await parserXtream.ParseAndImportXtreamVodAsync(db, sourceProfileId, acquisitionSession, refreshHealth: false, operationToken);
                        }
                        else
                        {
                            await parserStalker.ParseAndImportStalkerAsync(db, sourceProfileId, acquisitionSession, refreshHealth: false, operationToken);
                        }

                        if (shouldAttemptGuideSync)
                        {
                            guideAttempted = true;
                            try
                            {
                                await parserXmltv.ParseAndImportEpgAsync(db, sourceProfileId, acquisitionSession, refreshHealth: false, operationToken);
                                guideSucceeded = true;
                                guideSummary = "Guide sync completed.";
                            }
                            catch (Exception ex)
                            {
                                guideSummary = $"Guide sync failed: {ex.Message}";
                            }
                        }
                        break;
                }

                await sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                runtimeRepairSummary = await TryFinalizeRuntimeStateAsync(
                    db,
                    sourceProfileId,
                    browsePreferencesService,
                    logicalCatalogStateService,
                    contentOperationalService,
                    acquisitionSession);
                var scheduleSummary = CombineSummaries(guideSummary, runtimeRepairSummary);
                await autoRefreshService.UpdateScheduleAsync(db, sourceProfileId, trigger, success: true, scheduleSummary);

                var catalogSummary = scope switch
                {
                    SourceRefreshScope.EpgOnly => "Guide-only refresh completed.",
                    SourceRefreshScope.VodOnly => "VOD refresh completed.",
                    SourceRefreshScope.LiveOnly when profile.Type == SourceType.Stalker => "Stalker portal refresh completed.",
                    SourceRefreshScope.LiveOnly when profile.Type == SourceType.Xtream => "Xtream live refresh completed.",
                    SourceRefreshScope.LiveOnly => "Playlist import completed.",
                    _ when profile.Type == SourceType.Stalker => "Stalker catalog refresh completed.",
                    _ when profile.Type == SourceType.Xtream => "Xtream catalog refresh completed.",
                    _ => "Playlist import completed."
                };

                var message = CombineSummaries(catalogSummary, guideSummary, runtimeRepairSummary);
                var runStatus = guideAttempted && !guideSucceeded
                    ? SourceAcquisitionRunStatus.Partial
                    : SourceAcquisitionRunStatus.Succeeded;
                await acquisitionService.CompleteSessionAsync(
                    db,
                    acquisitionSession,
                    runStatus,
                    message,
                    catalogSummary,
                    guideSummary,
                    acquisitionSession.ValidationSummary);

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
                var userMessage = MapRefreshFailureMessage(ex, profile.Type, scope, cancellationToken);
                acquisitionSession.RecordFailure(
                    scope == SourceRefreshScope.EpgOnly ? SourceAcquisitionStage.GuideMatch : SourceAcquisitionStage.Acquire,
                    SourceAcquisitionItemKind.Source,
                    "source.refresh.failed",
                    userMessage);

                try
                {
                    await sourceHealthService.RefreshSourceHealthAsync(db, sourceProfileId, acquisitionSession);
                }
                catch (Exception healthEx)
                {
                    RuntimeEventLogger.Log("SOURCE-REFRESH", healthEx, $"source_id={sourceProfileId} health refresh skipped after failure");
                    acquisitionSession.RecordRuntimeRepairWarning(
                        "runtime.health_refresh_deferred",
                        "Validation refresh was deferred after the source refresh failure.");
                }

                runtimeRepairSummary = await TryFinalizeRuntimeStateAsync(
                    db,
                    sourceProfileId,
                    browsePreferencesService,
                    logicalCatalogStateService,
                    contentOperationalService,
                    acquisitionSession);

                await autoRefreshService.UpdateScheduleAsync(
                    db,
                    sourceProfileId,
                    trigger,
                    success: false,
                    CombineSummaries(userMessage, runtimeRepairSummary));
                RuntimeEventLogger.Log("SOURCE-REFRESH", ex, $"source_id={sourceProfileId} refresh failed message={userMessage}");
                await acquisitionService.CompleteSessionAsync(
                    db,
                    acquisitionSession,
                    SourceAcquisitionRunStatus.Failed,
                    userMessage,
                    string.Empty,
                    guideSummary,
                    acquisitionSession.ValidationSummary);

                return new SourceRefreshResult
                {
                    SourceProfileId = sourceProfileId,
                    Trigger = trigger,
                    Scope = scope,
                    Success = false,
                    Message = userMessage,
                    CatalogSummary = string.Empty,
                    GuideSummary = guideSummary,
                    GuideAttempted = guideAttempted,
                    GuideSucceeded = false
                };
            }
        }

        private static TimeSpan ResolveRefreshTimeout(SourceRefreshScope scope)
        {
            return scope switch
            {
                SourceRefreshScope.EpgOnly => TimeSpan.FromMinutes(2),
                SourceRefreshScope.LiveOnly => TimeSpan.FromMinutes(3),
                SourceRefreshScope.VodOnly => TimeSpan.FromMinutes(3),
                _ => TimeSpan.FromMinutes(4)
            };
        }

        private static string MapRefreshFailureMessage(
            Exception ex,
            SourceType sourceType,
            SourceRefreshScope scope,
            CancellationToken callerCancellationToken)
        {
            if (ex is OperationCanceledException)
            {
                return callerCancellationToken.IsCancellationRequested
                    ? "Source import was canceled."
                    : "Request timed out.";
            }

            if (scope == SourceRefreshScope.EpgOnly ||
                ex.Message.Contains("XMLTV", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("guide", StringComparison.OrdinalIgnoreCase))
            {
                return "EPG could not be loaded.";
            }

            if (sourceType == SourceType.Xtream && ex is UnauthorizedAccessException)
            {
                return "Xtream login failed.";
            }

            if (sourceType == SourceType.M3U &&
                ex.Message.Contains("M3U", StringComparison.OrdinalIgnoreCase) &&
                ex.Message.Contains("format", StringComparison.OrdinalIgnoreCase))
            {
                return "Invalid M3U format.";
            }

            if (ex.Message.Contains("No playable", StringComparison.OrdinalIgnoreCase))
            {
                return "No playable channels found.";
            }

            if (ex is System.Net.Http.HttpRequestException)
            {
                return sourceType == SourceType.Xtream
                    ? "Xtream login failed."
                    : "Playlist could not be reached.";
            }

            return ex.Message;
        }

        private static async Task<string> TryFinalizeRuntimeStateAsync(
            AppDbContext db,
            int sourceProfileId,
            IBrowsePreferencesService browsePreferencesService,
            ILogicalCatalogStateService logicalCatalogStateService,
            IContentOperationalService contentOperationalService,
            SourceAcquisitionSession? acquisitionSession)
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
                acquisitionSession?.RecordRuntimeRepairWarning(
                    "runtime.browse_repair_deferred",
                    "Browse state repairs were deferred after the source sync.");
            }

            try
            {
                await logicalCatalogStateService.ReconcilePersistentStateAsync(db);
            }
            catch (Exception ex)
            {
                RuntimeEventLogger.Log("SOURCE-REFRESH", ex, $"source_id={sourceProfileId} state reconciliation skipped");
                warnings.Add("Some saved state repairs were deferred.");
                acquisitionSession?.RecordRuntimeRepairWarning(
                    "runtime.state_reconciliation_deferred",
                    "Saved state reconciliation was deferred after the source sync.");
            }

            try
            {
                await contentOperationalService.RefreshOperationalStateAsync(db);
            }
            catch (Exception ex)
            {
                RuntimeEventLogger.Log("SOURCE-REFRESH", ex, $"source_id={sourceProfileId} operational rebuild skipped");
                warnings.Add("Operational mirror refresh was deferred.");
                acquisitionSession?.RecordRuntimeRepairWarning(
                    "runtime.operational_refresh_deferred",
                    "Operational candidate refresh was deferred after the source sync.");
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

        private static bool ShouldAttemptGuideSync(SourceProfile profile, SourceCredential? credential)
        {
            if (credential == null || credential.EpgMode == EpgActiveMode.None)
            {
                return false;
            }

            if (profile.Type == SourceType.Stalker)
            {
                return !string.IsNullOrWhiteSpace(credential.ManualEpgUrl) ||
                       !string.IsNullOrWhiteSpace(credential.DetectedEpgUrl) ||
                       !string.IsNullOrWhiteSpace(credential.FallbackEpgUrls);
            }

            return true;
        }
    }
}
