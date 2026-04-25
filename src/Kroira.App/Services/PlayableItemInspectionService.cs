#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Services
{
    public interface IPlayableItemInspectionService
    {
        Task<PlayableItemInspectionSnapshot> BuildAsync(
            PlaybackLaunchContext context,
            PlayableItemInspectionRuntimeState? runtimeState = null,
            CancellationToken cancellationToken = default);
    }

    public sealed class PlayableItemInspectionService : IPlayableItemInspectionService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ISensitiveDataRedactionService _redactionService;

        public PlayableItemInspectionService(
            IServiceScopeFactory scopeFactory,
            ISensitiveDataRedactionService redactionService)
        {
            _scopeFactory = scopeFactory;
            _redactionService = redactionService;
        }

        public async Task<PlayableItemInspectionSnapshot> BuildAsync(
            PlaybackLaunchContext context,
            PlayableItemInspectionRuntimeState? runtimeState = null,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                return new PlayableItemInspectionSnapshot
                {
                    Title = L("PlayableInspection_Title_ItemUnavailable"),
                    StatusText = L("PlayableInspection_Status_NoPlaybackContext"),
                    Sections = Array.Empty<PlayableItemInspectionSection>(),
                    SafeReportText = string.Join(
                        Environment.NewLine,
                        L("PlayableInspection_Report_Title"),
                        L("PlayableInspection_Report_NoPlaybackContext"))
                };
            }

            var inspectionContext = context.Clone();
            if (string.IsNullOrWhiteSpace(inspectionContext.CatalogStreamUrl) &&
                !string.IsNullOrWhiteSpace(inspectionContext.StreamUrl))
            {
                inspectionContext.CatalogStreamUrl = inspectionContext.StreamUrl;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contentOperationalService = scope.ServiceProvider.GetRequiredService<IContentOperationalService>();
            var sourceDiagnosticsService = scope.ServiceProvider.GetRequiredService<ISourceDiagnosticsService>();

            if (runtimeState?.IsCurrentPlayback != true &&
                (inspectionContext.ContentId > 0 || !string.IsNullOrWhiteSpace(inspectionContext.LogicalContentKey)))
            {
                await contentOperationalService.ResolvePlaybackContextAsync(db, inspectionContext);
            }

            var details = await LoadDetailsAsync(db, context, cancellationToken);
            var logicalKey = FirstNonEmpty(
                context.LogicalContentKey,
                inspectionContext.LogicalContentKey,
                details.LogicalContentKey);
            var sourceProfileId = details.SourceProfileId > 0
                ? details.SourceProfileId
                : inspectionContext.PreferredSourceProfileId;
            var credential = sourceProfileId > 0
                ? await db.SourceCredentials.AsNoTracking().FirstOrDefaultAsync(
                    item => item.SourceProfileId == sourceProfileId,
                    cancellationToken)
                : null;
            var diagnostics = sourceProfileId > 0
                ? (await sourceDiagnosticsService.GetSnapshotsAsync(db, new[] { sourceProfileId }))
                    .GetValueOrDefault(sourceProfileId)
                : null;
            var stalkerSnapshot = sourceProfileId > 0
                ? await db.StalkerPortalSnapshots
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId, cancellationToken)
                : null;
            var catchupAttempt = context.ContentType == PlaybackContentType.Channel && context.ContentId > 0
                ? await db.CatchupPlaybackAttempts
                    .AsNoTracking()
                    .Where(item => item.ChannelId == context.ContentId)
                    .OrderByDescending(item => item.RequestedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken)
                : null;
            var operationalState = !string.IsNullOrWhiteSpace(logicalKey)
                ? await db.LogicalOperationalStates
                    .AsNoTracking()
                    .Include(item => item.Candidates)
                    .FirstOrDefaultAsync(item => item.LogicalContentKey == logicalKey, cancellationToken)
                : null;

            var sections = new List<PlayableItemInspectionSection>();
            sections.Add(BuildIdentitySection(details, inspectionContext, credential, diagnostics, stalkerSnapshot, logicalKey));
            sections.Add(BuildStreamSection(details, inspectionContext, credential));
            if (details.Channel != null)
            {
                sections.Add(BuildGuideSection(details.Channel, inspectionContext, diagnostics, catchupAttempt));
            }

            sections.Add(BuildOperationalSection(inspectionContext, credential, operationalState));
            sections.Add(BuildSourceDiagnosticsSection(diagnostics));
            if (runtimeState != null && runtimeState.IsCurrentPlayback)
            {
                sections.Add(BuildRuntimeSection(runtimeState));
            }

            sections = sections.Where(section => section.Fields.Count > 0).ToList();
            var snapshot = new PlayableItemInspectionSnapshot
            {
                IsCurrentPlayback = runtimeState?.IsCurrentPlayback == true,
                SupportsExternalLaunch = SupportsExternalLaunch(context),
                Title = details.Title,
                Subtitle = BuildSubtitle(details, diagnostics),
                StatusText = _redactionService.RedactLooseText(BuildStatusText(inspectionContext, runtimeState, diagnostics)),
                FailureText = _redactionService.RedactLooseText(BuildFailureText(inspectionContext, runtimeState)),
                Sections = sections,
                SafeReportText = BuildSafeReport(details.Title, sections)
            };

            return snapshot;
        }

        private async Task<LoadedItemDetails> LoadDetailsAsync(
            AppDbContext db,
            PlaybackLaunchContext context,
            CancellationToken cancellationToken)
        {
            switch (context.ContentType)
            {
                case PlaybackContentType.Channel:
                {
                    var row = await db.Channels
                        .AsNoTracking()
                        .Where(channel => channel.Id == context.ContentId)
                        .Join(
                            db.ChannelCategories.AsNoTracking(),
                            channel => channel.ChannelCategoryId,
                            category => category.Id,
                            (channel, category) => new { Channel = channel, Category = category })
                        .Join(
                            db.SourceProfiles.AsNoTracking(),
                            item => item.Category.SourceProfileId,
                            profile => profile.Id,
                            (item, profile) => new { item.Channel, item.Category, Profile = profile })
                        .FirstOrDefaultAsync(cancellationToken);
                    if (row == null)
                    {
                        return LoadedItemDetails.Fallback(L("PlayableInspection_Fallback_Channel"), context);
                    }

                    return new LoadedItemDetails
                    {
                        Title = row.Channel.Name,
                        SourceProfileId = row.Profile.Id,
                        SourceName = row.Profile.Name,
                        SourceType = row.Profile.Type,
                        CategoryName = row.Category.Name,
                        LogicalContentKey = row.Channel.NormalizedIdentityKey,
                        Channel = row.Channel
                    };
                }

                case PlaybackContentType.Movie:
                {
                    var row = await db.Movies
                        .AsNoTracking()
                        .Where(movie => movie.Id == context.ContentId)
                        .Join(
                            db.SourceProfiles.AsNoTracking(),
                            movie => movie.SourceProfileId,
                            profile => profile.Id,
                            (movie, profile) => new { Movie = movie, Profile = profile })
                        .FirstOrDefaultAsync(cancellationToken);
                    if (row == null)
                    {
                        return LoadedItemDetails.Fallback(L("PlayableInspection_Fallback_Movie"), context);
                    }

                    return new LoadedItemDetails
                    {
                        Title = row.Movie.Title,
                        SourceProfileId = row.Profile.Id,
                        SourceName = row.Profile.Name,
                        SourceType = row.Profile.Type,
                        CategoryName = row.Movie.CategoryName,
                        LogicalContentKey = BuildMovieLogicalKey(row.Movie),
                        Movie = row.Movie
                    };
                }

                case PlaybackContentType.Episode:
                {
                    var row = await db.Episodes
                        .AsNoTracking()
                        .Where(episode => episode.Id == context.ContentId)
                        .Join(
                            db.Seasons.AsNoTracking(),
                            episode => episode.SeasonId,
                            season => season.Id,
                            (episode, season) => new { Episode = episode, Season = season })
                        .Join(
                            db.Series.AsNoTracking(),
                            item => item.Season.SeriesId,
                            series => series.Id,
                            (item, series) => new { item.Episode, item.Season, Series = series })
                        .Join(
                            db.SourceProfiles.AsNoTracking(),
                            item => item.Series.SourceProfileId,
                            profile => profile.Id,
                            (item, profile) => new { item.Episode, item.Season, item.Series, Profile = profile })
                        .FirstOrDefaultAsync(cancellationToken);
                    if (row == null)
                    {
                        return LoadedItemDetails.Fallback(L("PlayableInspection_Fallback_Episode"), context);
                    }

                    return new LoadedItemDetails
                    {
                        Title = row.Episode.Title,
                        SourceProfileId = row.Profile.Id,
                        SourceName = row.Profile.Name,
                        SourceType = row.Profile.Type,
                        CategoryName = row.Series.CategoryName,
                        LogicalContentKey = BuildEpisodeLogicalKey(row.Episode, row.Series),
                        Episode = row.Episode,
                        SeasonNumber = row.Season.SeasonNumber,
                        Series = row.Series
                    };
                }
            }

            return LoadedItemDetails.Fallback(L("PlayableInspection_Fallback_Item"), context);
        }

        private PlayableItemInspectionSection BuildIdentitySection(
            LoadedItemDetails details,
            PlaybackLaunchContext context,
            SourceCredential? credential,
            SourceDiagnosticsSnapshot? diagnostics,
            StalkerPortalSnapshot? stalkerSnapshot,
            string logicalKey)
        {
            var fields = new List<PlayableItemInspectionField>();
            AddField(fields, L("PlayableInspection_Field_ContentType"), context.ContentType.ToString());
            AddField(fields, L("PlayableInspection_Field_Source"), details.SourceName);
            AddField(fields, L("PlayableInspection_Field_SourceType"), details.SourceType.ToString());
            AddField(fields, L("PlayableInspection_Field_SourceProfileId"), details.SourceProfileId > 0 ? details.SourceProfileId.ToString() : string.Empty);
            AddField(fields, L("PlayableInspection_Field_SourceEndpoint"), _redactionService.RedactUrl(credential?.Url));
            AddField(fields, L("PlayableInspection_Field_LogicalIdentity"), logicalKey);
            AddField(fields, L("PlayableInspection_Field_AcquisitionProfile"), FirstNonEmpty(diagnostics?.AcquisitionProfileLabel, diagnostics?.AcquisitionProfileKey));
            AddField(fields, L("PlayableInspection_Field_ProviderProfile"), BuildProviderProfileText(diagnostics, stalkerSnapshot));

            if (details.Channel != null)
            {
                AddField(fields, L("PlayableInspection_Field_RawProviderName"), details.Channel.Name);
                AddField(fields, L("PlayableInspection_Field_Category"), details.CategoryName);
                AddField(fields, L("PlayableInspection_Field_NormalizedName"), details.Channel.NormalizedName);
                AddField(fields, L("PlayableInspection_Field_NormalizedIdentity"), details.Channel.NormalizedIdentityKey);
                AddField(fields, L("PlayableInspection_Field_AliasKeys"), details.Channel.AliasKeys);
                AddField(fields, L("PlayableInspection_Field_ProviderEpgId"), details.Channel.ProviderEpgChannelId);
                AddField(fields, L("PlayableInspection_Field_MatchedEpgId"), details.Channel.EpgChannelId);
            }

            if (details.Movie != null)
            {
                AddField(fields, L("PlayableInspection_Field_RawProviderTitle"), details.Movie.RawSourceTitle);
                AddField(fields, L("PlayableInspection_Field_Category"), details.Movie.CategoryName);
                AddField(fields, L("PlayableInspection_Field_ProviderItemId"), details.Movie.ExternalId);
                AddField(fields, L("PlayableInspection_Field_CanonicalKey"), details.Movie.CanonicalTitleKey);
                AddField(fields, L("PlayableInspection_Field_DedupFingerprint"), details.Movie.DedupFingerprint);
                AddField(fields, "TMDb / IMDb", BuildJoined(" / ", details.Movie.TmdbId, details.Movie.ImdbId));
            }

            if (details.Episode != null)
            {
                AddField(fields, L("PlayableInspection_Field_Series"), details.Series?.Title);
                AddField(fields, L("PlayableInspection_Field_SeasonEpisode"), details.SeasonNumber > 0 ? $"S{details.SeasonNumber:00}E{details.Episode.EpisodeNumber:00}" : F("PlayableInspection_Value_EpisodeNumber", details.Episode.EpisodeNumber));
                AddField(fields, L("PlayableInspection_Field_EpisodeProviderId"), details.Episode.ExternalId);
                AddField(fields, L("PlayableInspection_Field_SeriesProviderId"), details.Series?.ExternalId);
                AddField(fields, L("PlayableInspection_Field_SeriesCanonicalKey"), details.Series?.CanonicalTitleKey);
                AddField(fields, L("PlayableInspection_Field_SeriesDedupFingerprint"), details.Series?.DedupFingerprint);
            }

            if (stalkerSnapshot != null)
            {
                AddRedactedField(fields, "Portal", BuildJoined(" ", stalkerSnapshot.PortalName, stalkerSnapshot.PortalVersion));
                AddRedactedField(fields, L("PlayableInspection_Field_PortalProfile"), BuildJoined(" / ", stalkerSnapshot.ProfileName, stalkerSnapshot.ProfileId));
                AddField(fields, "Portal MAC", _redactionService.RedactMacAddress(stalkerSnapshot.MacAddress));
            }

            return new PlayableItemInspectionSection
            {
                Title = L("PlayableInspection_Section_Identity"),
                Fields = fields
            };
        }

        private PlayableItemInspectionSection BuildStreamSection(
            LoadedItemDetails details,
            PlaybackLaunchContext context,
            SourceCredential? credential)
        {
            var fields = new List<PlayableItemInspectionField>();
            AddField(fields, L("PlayableInspection_Field_CatalogStream"), _redactionService.RedactUrl(context.CatalogStreamUrl));
            AddField(fields, L("PlayableInspection_Field_UpstreamStream"), BuildUpstreamStreamText(context));
            AddField(fields, L("PlayableInspection_Field_LaunchStream"), ShouldShowResolvedUrl(context) ? _redactionService.RedactUrl(context.StreamUrl) : L("PlayableInspection_Value_NotResolvedUntilLaunch"));
            AddField(fields, L("PlayableInspection_Field_LiveStream"), context.ContentType == PlaybackContentType.Channel ? _redactionService.RedactUrl(context.LiveStreamUrl) : string.Empty);
            AddRedactedField(fields, L("PlayableInspection_Field_LaunchPath"), BuildLaunchPathText(context));
            AddRedactedField(fields, L("PlayableInspection_Field_ProviderResolution"), FirstNonEmpty(context.ProviderSummary, InferProviderSummary(context.CatalogStreamUrl)));
            AddRedactedField(fields, L("PlayableInspection_Field_Routing"), FirstNonEmpty(context.RoutingSummary, credential?.ProxyScope == SourceProxyScope.Disabled ? L("PlayableInspection_Value_DirectRouting") : string.Empty));
            AddField(fields, L("PlayableInspection_Field_SourceProxyPolicy"), BuildProxyPolicyText(credential));
            AddField(fields, L("PlayableInspection_Field_ProxyEndpoint"), _redactionService.RedactUrl(credential?.ProxyUrl));
            AddField(fields, L("PlayableInspection_Field_CompanionPolicy"), BuildCompanionPolicyText(context, credential));
            AddField(fields, L("PlayableInspection_Field_CompanionEndpoint"), _redactionService.RedactUrl(FirstNonEmpty(context.CompanionUrl, credential?.CompanionUrl)));
            AddRedactedField(fields, L("PlayableInspection_Field_CompanionStatus"), BuildCompanionStatusDisplayText(context, credential));
            AddRedactedField(fields, L("PlayableInspection_Field_OperationalSelection"), context.OperationalSummary);
            AddField(fields, L("PlayableInspection_Field_MirrorCandidates"), context.MirrorCandidateCount > 0 ? context.MirrorCandidateCount.ToString() : string.Empty);
            AddField(fields, L("PlayableInspection_Field_PlaybackMode"), context.PlaybackMode.ToString());

            if (context.PlaybackMode == CatchupPlaybackMode.Catchup || context.CatchupRequestKind != CatchupRequestKind.None)
            {
                AddField(fields, L("PlayableInspection_Field_CatchupRequest"), context.CatchupRequestKind.ToString());
                AddRedactedField(fields, L("PlayableInspection_Field_CatchupStatus"), BuildJoined(" - ", context.CatchupResolutionStatus.ToString(), context.CatchupStatusText));
                AddField(fields, L("PlayableInspection_Field_CatchupProgram"), context.CatchupProgramTitle);
                AddField(fields, L("PlayableInspection_Field_CatchupWindow"), FormatWindow(context.CatchupProgramStartTimeUtc, context.CatchupProgramEndTimeUtc));
            }

            if (details.Channel != null)
            {
                AddField(fields, L("PlayableInspection_Field_CatchupSupport"), details.Channel.SupportsCatchup ? L("PlayableInspection_Value_Supported") : L("PlayableInspection_Value_NotAdvertised"));
                AddField(fields, L("PlayableInspection_Field_CatchupWindowHours"), details.Channel.CatchupWindowHours > 0 ? details.Channel.CatchupWindowHours.ToString() : string.Empty);
            }

            return new PlayableItemInspectionSection
            {
                Title = L("PlayableInspection_Section_Streams"),
                Fields = fields
            };
        }

        private PlayableItemInspectionSection BuildGuideSection(
            Channel channel,
            PlaybackLaunchContext context,
            SourceDiagnosticsSnapshot? diagnostics,
            CatchupPlaybackAttempt? catchupAttempt)
        {
            var fields = new List<PlayableItemInspectionField>();
            AddRedactedField(fields, L("PlayableInspection_Field_GuideStatus"), diagnostics?.EpgStatusText);
            AddRedactedField(fields, L("PlayableInspection_Field_GuideSummary"), FirstNonEmpty(diagnostics?.EpgStatusSummary, diagnostics?.EpgCoverageText));
            AddField(fields, "EPG match", BuildJoined(" / ", channel.EpgMatchSource.ToString(), channel.EpgMatchConfidence > 0 ? $"{channel.EpgMatchConfidence}%" : string.Empty));
            AddRedactedField(fields, L("PlayableInspection_Field_EpgMatchSummary"), channel.EpgMatchSummary);
            AddRedactedField(fields, L("PlayableInspection_Field_CatchupSource"), BuildJoined(" / ", channel.CatchupSource.ToString(), channel.ProviderCatchupSource));
            AddRedactedField(fields, L("PlayableInspection_Field_CatchupSummary"), channel.CatchupSummary);
            AddRedactedField(fields, L("PlayableInspection_Field_LatestCatchupAttempt"), BuildCatchupAttemptText(catchupAttempt));
            AddRedactedField(fields, L("PlayableInspection_Field_CurrentPlaybackCatchup"), context.PlaybackMode == CatchupPlaybackMode.Catchup ? context.CatchupStatusText : string.Empty);

            return new PlayableItemInspectionSection
            {
                Title = L("PlayableInspection_Section_GuideCatchup"),
                Fields = fields
            };
        }

        private PlayableItemInspectionSection BuildOperationalSection(
            PlaybackLaunchContext context,
            SourceCredential? credential,
            LogicalOperationalState? operationalState)
        {
            var fields = new List<PlayableItemInspectionField>();
            AddField(fields, L("PlayableInspection_Field_LogicalContentKey"), context.LogicalContentKey);
            AddField(fields, L("PlayableInspection_Field_PreferredSourceProfile"), context.PreferredSourceProfileId > 0 ? context.PreferredSourceProfileId.ToString() : string.Empty);
            AddRedactedField(fields, L("PlayableInspection_Field_RoutingSummary"), context.RoutingSummary);
            AddRedactedField(fields, L("PlayableInspection_Field_ProviderSummary"), context.ProviderSummary);
            AddRedactedField(fields, L("PlayableInspection_Field_OperationalSummary"), context.OperationalSummary);
            AddRedactedField(fields, L("PlayableInspection_Field_RecoverySummary"), operationalState?.RecoverySummary);
            AddField(fields, L("PlayableInspection_Field_CandidateCount"), operationalState?.CandidateCount > 0 ? operationalState.CandidateCount.ToString() : string.Empty);
            AddField(fields, L("PlayableInspection_Field_LastKnownGood"), operationalState != null && operationalState.LastKnownGoodSourceProfileId > 0
                ? $"{operationalState.LastKnownGoodSourceProfileId} at {operationalState.LastKnownGoodAtUtc?.ToLocalTime():g}"
                : string.Empty);
            AddRedactedField(fields, L("PlayableInspection_Field_CandidateRanking"), BuildCandidateText(operationalState));
            AddField(fields, L("PlayableInspection_Field_SourceProxyPolicy"), credential?.ProxyScope.ToString());

            return new PlayableItemInspectionSection
            {
                Title = L("PlayableInspection_Section_RoutingFallback"),
                Fields = fields
            };
        }

        private PlayableItemInspectionSection BuildSourceDiagnosticsSection(SourceDiagnosticsSnapshot? diagnostics)
        {
            var fields = new List<PlayableItemInspectionField>();
            if (diagnostics == null)
            {
                return new PlayableItemInspectionSection
                {
                    Title = L("PlayableInspection_Section_SourceDiagnostics"),
                    Fields = fields
                };
            }

            AddRedactedField(fields, L("PlayableInspection_Field_Health"), BuildJoined(" - ", diagnostics.HealthLabel, diagnostics.StatusSummary));
            AddRedactedField(fields, L("PlayableInspection_Field_Validation"), diagnostics.ValidationResultText);
            AddRedactedField(fields, L("PlayableInspection_Field_AcquisitionRun"), BuildJoined(" - ", diagnostics.AcquisitionRunStatusText, diagnostics.AcquisitionRunSummaryText));
            AddRedactedField(fields, L("PlayableInspection_Field_AcquisitionStats"), diagnostics.AcquisitionStatsText);
            AddRedactedField(fields, L("PlayableInspection_Field_WarningSummary"), FirstNonEmpty(diagnostics.WarningSummaryText, diagnostics.FailureSummaryText));
            AddRedactedField(fields, L("PlayableInspection_Field_TopIssue"), diagnostics.Issues.FirstOrDefault()?.Message);
            AddRedactedField(fields, L("PlayableInspection_Field_ProbeSummary"), BuildProbeText(diagnostics.HealthProbes));
            AddRedactedField(fields, L("PlayableInspection_Field_RelevantEvidence"), BuildEvidenceText(diagnostics.AcquisitionEvidence));
            AddRedactedField(fields, L("PlayableInspection_Field_PortalStatus"), BuildJoined(" - ", diagnostics.StalkerPortalSummaryText, diagnostics.StalkerPortalErrorText));
            AddRedactedField(fields, L("PlayableInspection_Field_CatchupDiagnostics"), FirstNonEmpty(diagnostics.CatchupStatusText, diagnostics.CatchupLatestAttemptText));

            return new PlayableItemInspectionSection
            {
                Title = L("PlayableInspection_Section_SourceDiagnostics"),
                Fields = fields
            };
        }

        private PlayableItemInspectionSection BuildRuntimeSection(PlayableItemInspectionRuntimeState runtimeState)
        {
            var fields = new List<PlayableItemInspectionField>();
            AddField(fields, L("PlayableInspection_Field_SessionState"), runtimeState.SessionState);
            AddRedactedField(fields, L("PlayableInspection_Field_SessionMessage"), runtimeState.SessionMessage);
            AddField(fields, L("PlayableInspection_Field_PositionDuration"), BuildRuntimePositionText(runtimeState));
            AddField(fields, L("PlayableInspection_Field_Seekability"), runtimeState.IsSeekable ? L("PlayableInspection_Value_Seekable") : L("PlayableInspection_Value_NotSeekable"));
            AddField(fields, L("PlayableInspection_Field_Resolution"), runtimeState.Width > 0 && runtimeState.Height > 0 ? $"{runtimeState.Width}x{runtimeState.Height}" : string.Empty);
            AddField(fields, "FPS", runtimeState.FramesPerSecond > 0 ? runtimeState.FramesPerSecond.ToString("0.##") : string.Empty);
            AddField(fields, L("PlayableInspection_Field_VideoCodec"), runtimeState.VideoCodec);
            AddField(fields, L("PlayableInspection_Field_AudioCodec"), runtimeState.AudioCodec);
            AddField(fields, L("PlayableInspection_Field_Container"), runtimeState.ContainerFormat);
            AddField(fields, L("PlayableInspection_Field_PixelFormat"), runtimeState.PixelFormat);
            AddField(fields, L("PlayableInspection_Field_HardwareDecode"), runtimeState.IsHardwareDecodingActive ? L("PlayableInspection_Value_Active") : string.Empty);

            return new PlayableItemInspectionSection
            {
                Title = L("PlayableInspection_Section_CurrentPlayback"),
                Fields = fields
            };
        }

        private string BuildStatusText(
            PlaybackLaunchContext context,
            PlayableItemInspectionRuntimeState? runtimeState,
            SourceDiagnosticsSnapshot? diagnostics)
        {
            if (runtimeState?.IsCurrentPlayback == true)
            {
                if (context.CompanionStatus == CompanionRelayStatus.Applied)
                {
                    return L("PlayableInspection_Status_ActiveCompanion");
                }

                return ShouldShowResolvedUrl(context)
                    ? L("PlayableInspection_Status_ActiveResolved")
                    : L("PlayableInspection_Status_ActiveRouting");
            }

            if (context.PlaybackMode == CatchupPlaybackMode.Catchup || context.CatchupRequestKind != CatchupRequestKind.None)
            {
                if (context.CompanionStatus == CompanionRelayStatus.Applied)
                {
                    return L("PlayableInspection_Status_CatchupCompanion");
                }

                return L("PlayableInspection_Status_CatchupCatalog");
            }

            if (context.CompanionStatus == CompanionRelayStatus.Applied)
            {
                return L("PlayableInspection_Status_CatalogCompanion");
            }

            return string.IsNullOrWhiteSpace(diagnostics?.StatusSummary)
                ? L("PlayableInspection_Status_CatalogDeferred")
                : diagnostics.StatusSummary;
        }

        private static string BuildFailureText(
            PlaybackLaunchContext context,
            PlayableItemInspectionRuntimeState? runtimeState)
        {
            if (runtimeState?.IsCurrentPlayback == true &&
                string.Equals(runtimeState.SessionState, "Error", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(runtimeState.SessionMessage))
            {
                return runtimeState.SessionMessage;
            }

            if (context.PlaybackMode == CatchupPlaybackMode.Catchup &&
                context.CatchupResolutionStatus != CatchupResolutionStatus.None &&
                context.CatchupResolutionStatus != CatchupResolutionStatus.Resolved)
            {
                return context.CatchupStatusText;
            }

            if (context.CompanionStatus == CompanionRelayStatus.FallbackDirect &&
                !string.IsNullOrWhiteSpace(context.CompanionStatusText))
            {
                return context.CompanionStatusText;
            }

            return string.Empty;
        }

        private string BuildSafeReport(string title, IReadOnlyList<PlayableItemInspectionSection> sections)
        {
            var builder = new StringBuilder();
            builder.AppendLine(L("PlayableInspection_Report_Title"));
            builder.AppendLine(title);
            builder.AppendLine(L("PlayableInspection_Report_RedactedNotice"));

            foreach (var section in sections)
            {
                if (section.Fields.Count == 0)
                {
                    continue;
                }

                builder.AppendLine();
                builder.AppendLine(section.Title);
                foreach (var field in section.Fields)
                {
                    builder.AppendLine($"{field.Label}: {field.Value}");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static bool SupportsExternalLaunch(PlaybackLaunchContext context)
        {
            return context.ContentType is PlaybackContentType.Channel or PlaybackContentType.Movie or PlaybackContentType.Episode &&
                   (!string.IsNullOrWhiteSpace(context.StreamUrl) ||
                    !string.IsNullOrWhiteSpace(context.CatalogStreamUrl) ||
                    context.ContentId > 0);
        }

        private static bool ShouldShowResolvedUrl(PlaybackLaunchContext context)
        {
            return !string.IsNullOrWhiteSpace(context.StreamUrl) &&
                   (!string.Equals(context.StreamUrl, context.CatalogStreamUrl, StringComparison.Ordinal) ||
                    context.PlaybackMode == CatchupPlaybackMode.Catchup ||
                    string.IsNullOrWhiteSpace(context.CatalogStreamUrl) ||
                    !string.IsNullOrWhiteSpace(context.ProviderSummary));
        }

        private static string BuildSubtitle(LoadedItemDetails details, SourceDiagnosticsSnapshot? diagnostics)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(details.SourceName))
            {
                parts.Add(details.SourceName);
            }

            parts.Add(details.SourceType.ToString());

            if (!string.IsNullOrWhiteSpace(details.CategoryName))
            {
                parts.Add(details.CategoryName);
            }

            if (!string.IsNullOrWhiteSpace(diagnostics?.AcquisitionProviderKey))
            {
                parts.Add(diagnostics.AcquisitionProviderKey);
            }

            return string.Join("  -  ", parts.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string InferProviderSummary(string? catalogStreamUrl)
        {
            return StalkerLocatorCodec.TryParse(catalogStreamUrl, out _)
                ? L("PlayableInspection_Value_StalkerLocator")
                : L("PlayableInspection_Value_DirectStreamUrl");
        }

        private static string BuildLaunchPathText(PlaybackLaunchContext context)
        {
            var parts = new List<string>();
            if (context.PlaybackMode == CatchupPlaybackMode.Catchup || context.CatchupRequestKind != CatchupRequestKind.None)
            {
                parts.Add(L("PlayableInspection_Value_CatchupReplay"));
            }

            if (ShouldShowResolvedUrl(context))
            {
                parts.Add(!string.IsNullOrWhiteSpace(context.UpstreamStreamUrl) &&
                          !string.Equals(context.UpstreamStreamUrl, context.StreamUrl, StringComparison.Ordinal)
                    ? L("PlayableInspection_Value_UpstreamResolvedRelayed")
                    : L("PlayableInspection_Value_ResolvedLaunchUrl"));
            }
            else
            {
                parts.Add(L("PlayableInspection_Value_CatalogUrl"));
            }

            if (context.CompanionStatus == CompanionRelayStatus.Applied)
            {
                parts.Add(L("PlayableInspection_Value_CompanionRelay"));
            }
            else if (context.CompanionStatus == CompanionRelayStatus.FallbackDirect)
            {
                parts.Add(L("PlayableInspection_Value_CompanionFallbackDirect"));
            }

            if (!string.IsNullOrWhiteSpace(context.ProviderSummary))
            {
                parts.Add(context.ProviderSummary);
            }

            if (!string.IsNullOrWhiteSpace(context.RoutingSummary))
            {
                parts.Add(context.RoutingSummary);
            }

            if (!string.IsNullOrWhiteSpace(context.OperationalSummary))
            {
                parts.Add(context.OperationalSummary);
            }

            return string.Join("  -  ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private string BuildUpstreamStreamText(PlaybackLaunchContext context)
        {
            if (!string.IsNullOrWhiteSpace(context.UpstreamStreamUrl))
            {
                return _redactionService.RedactUrl(context.UpstreamStreamUrl);
            }

            return ShouldShowResolvedUrl(context)
                ? _redactionService.RedactUrl(context.StreamUrl)
                : L("PlayableInspection_Value_NotResolvedUntilLaunch");
        }

        private static string BuildProxyPolicyText(SourceCredential? credential)
        {
            return credential?.ProxyScope.ToString() ?? string.Empty;
        }

        private static string BuildCompanionPolicyText(PlaybackLaunchContext context, SourceCredential? credential)
        {
            var scope = context.CompanionScope != SourceCompanionScope.Disabled
                ? context.CompanionScope
                : credential?.CompanionScope ?? SourceCompanionScope.Disabled;
            var mode = context.CompanionScope != SourceCompanionScope.Disabled || context.CompanionStatus != CompanionRelayStatus.None
                ? context.CompanionMode
                : credential?.CompanionMode ?? SourceCompanionRelayMode.Buffered;

            if (scope == SourceCompanionScope.Disabled)
            {
                return L("General_Disabled");
            }

            var scopeText = scope == SourceCompanionScope.PlaybackAndProbing
                ? L("PlayableInspection_Value_PlaybackProbes")
                : L("PlayableInspection_Value_PlaybackOnly");
            var modeText = mode == SourceCompanionRelayMode.Buffered
                ? L("PlayableInspection_Value_BufferedRelayLower")
                : L("PlayableInspection_Value_PassThroughRelayLower");
            return F("PlayableInspection_Value_CompanionPolicyFormat", scopeText, modeText);
        }

        private static string BuildCompanionStatusDisplayText(PlaybackLaunchContext context, SourceCredential? credential)
        {
            var scope = context.CompanionScope != SourceCompanionScope.Disabled
                ? context.CompanionScope
                : credential?.CompanionScope ?? SourceCompanionScope.Disabled;
            if (scope == SourceCompanionScope.Disabled && context.CompanionStatus == CompanionRelayStatus.None)
            {
                return string.Empty;
            }

            var status = context.CompanionStatus switch
            {
                CompanionRelayStatus.Applied => L("PlayableInspection_Value_Applied"),
                CompanionRelayStatus.FallbackDirect => L("PlayableInspection_Value_FallbackToDirect"),
                CompanionRelayStatus.Skipped => L("PlayableInspection_Value_Skipped"),
                CompanionRelayStatus.Failed => L("PlayableInspection_Value_Failed"),
                _ => L("PlayableInspection_Value_Pending")
            };

            return BuildJoined(" - ", status, context.CompanionStatusText);
        }

        private static string BuildProviderProfileText(SourceDiagnosticsSnapshot? diagnostics, StalkerPortalSnapshot? stalkerSnapshot)
        {
            if (stalkerSnapshot != null)
            {
                return BuildJoined(" - ", stalkerSnapshot.PortalName, stalkerSnapshot.ProfileName);
            }

            return BuildJoined(" - ", diagnostics?.AcquisitionProviderKey, diagnostics?.AcquisitionProfileLabel);
        }

        private static string BuildProbeText(IReadOnlyList<SourceDiagnosticsProbeSnapshot> probes)
        {
            return string.Join(" | ",
                probes
                    .Take(2)
                    .Select(probe => $"{probe.ProbeType}: {probe.Summary}")
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private string BuildEvidenceText(IReadOnlyList<SourceDiagnosticsEvidenceSnapshot> evidence)
        {
            return string.Join(" | ",
                evidence
                    .Take(3)
                    .Select(item => _redactionService.RedactLooseText($"{item.Stage}: {FirstNonEmpty(item.Reason, item.MatchedTarget, item.RawName)}"))
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string BuildCandidateText(LogicalOperationalState? state)
        {
            if (state == null || state.Candidates.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(" | ",
                state.Candidates
                    .OrderBy(candidate => candidate.Rank)
                    .Take(3)
                    .Select(candidate =>
                    {
                        var suffix = candidate.IsSelected
                            ? L("PlayableInspection_Value_Selected")
                            : candidate.IsLastKnownGood ? L("PlayableInspection_Value_LastGood") : F("PlayableInspection_Value_Rank", candidate.Rank);
                        return $"{candidate.SourceName} ({suffix})";
                    }));
        }

        private static string BuildCatchupAttemptText(CatchupPlaybackAttempt? attempt)
        {
            if (attempt == null)
            {
                return string.Empty;
            }

            var parts = new List<string>
            {
                attempt.Status.ToString()
            };

            if (!string.IsNullOrWhiteSpace(attempt.ProgramTitle))
            {
                parts.Add(attempt.ProgramTitle);
            }

            if (!string.IsNullOrWhiteSpace(attempt.Message))
            {
                parts.Add(attempt.Message);
            }

            return string.Join(" - ", parts);
        }

        private static string BuildRuntimePositionText(PlayableItemInspectionRuntimeState runtimeState)
        {
            if (runtimeState.DurationMs <= 0 && runtimeState.PositionMs <= 0)
            {
                return string.Empty;
            }

            var position = TimeSpan.FromMilliseconds(Math.Max(0, runtimeState.PositionMs));
            var duration = TimeSpan.FromMilliseconds(Math.Max(0, runtimeState.DurationMs));
            return runtimeState.DurationMs > 0
                ? $"{position:hh\\:mm\\:ss} / {duration:hh\\:mm\\:ss}"
                : position.ToString(@"hh\:mm\:ss");
        }

        private static string BuildMovieLogicalKey(Movie movie)
        {
            if (!string.IsNullOrWhiteSpace(movie.ExternalId))
            {
                return $"movie:external:{movie.SourceProfileId}:{movie.ExternalId.Trim()}";
            }

            return string.Empty;
        }

        private static string BuildEpisodeLogicalKey(Episode episode, Series? series)
        {
            if (!string.IsNullOrWhiteSpace(episode.ExternalId))
            {
                return $"episode:external:{episode.ExternalId.Trim()}";
            }

            return series == null || string.IsNullOrWhiteSpace(series.ExternalId)
                ? string.Empty
                : $"series:external:{series.ExternalId.Trim()}";
        }

        private static string FormatWindow(DateTime? startUtc, DateTime? endUtc)
        {
            if (!startUtc.HasValue || !endUtc.HasValue)
            {
                return string.Empty;
            }

            return $"{startUtc.Value.ToLocalTime():g} - {endUtc.Value.ToLocalTime():g}";
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
        }

        private static string BuildJoined(string separator, params string?[] values)
        {
            return string.Join(separator, values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
        }

        private static void AddField(ICollection<PlayableItemInspectionField> fields, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            fields.Add(new PlayableItemInspectionField
            {
                Label = label,
                Value = value.Trim()
            });
        }

        private void AddRedactedField(ICollection<PlayableItemInspectionField> fields, string label, string? value)
        {
            AddField(fields, label, _redactionService.RedactLooseText(value));
        }

        private static string L(string key)
        {
            return LocalizedStrings.Get(key);
        }

        private static string F(string key, params object?[] args)
        {
            return LocalizedStrings.Format(key, args);
        }

        private sealed class LoadedItemDetails
        {
            public string Title { get; set; } = string.Empty;
            public int SourceProfileId { get; set; }
            public string SourceName { get; set; } = string.Empty;
            public SourceType SourceType { get; set; }
            public string CategoryName { get; set; } = string.Empty;
            public string LogicalContentKey { get; set; } = string.Empty;
            public Channel? Channel { get; set; }
            public Movie? Movie { get; set; }
            public Episode? Episode { get; set; }
            public Series? Series { get; set; }
            public int SeasonNumber { get; set; }

            public static LoadedItemDetails Fallback(string title, PlaybackLaunchContext context)
            {
                return new LoadedItemDetails
                {
                    Title = title,
                    SourceProfileId = context.PreferredSourceProfileId,
                    LogicalContentKey = context.LogicalContentKey
                };
            }
        }
    }
}
