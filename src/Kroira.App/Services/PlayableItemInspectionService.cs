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
                    Title = L("PlayableInspection.Title.ItemUnavailable"),
                    StatusText = L("PlayableInspection.Status.NoPlaybackContext"),
                    Sections = Array.Empty<PlayableItemInspectionSection>(),
                    SafeReportText = string.Join(
                        Environment.NewLine,
                        L("PlayableInspection.Report.Title"),
                        L("PlayableInspection.Report.NoPlaybackContext"))
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
                        return LoadedItemDetails.Fallback(L("PlayableInspection.Fallback.Channel"), context);
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
                        return LoadedItemDetails.Fallback(L("PlayableInspection.Fallback.Movie"), context);
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
                        return LoadedItemDetails.Fallback(L("PlayableInspection.Fallback.Episode"), context);
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

            return LoadedItemDetails.Fallback(L("PlayableInspection.Fallback.Item"), context);
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
            AddField(fields, L("PlayableInspection.Field.ContentType"), context.ContentType.ToString());
            AddField(fields, L("PlayableInspection.Field.Source"), details.SourceName);
            AddField(fields, L("PlayableInspection.Field.SourceType"), details.SourceType.ToString());
            AddField(fields, L("PlayableInspection.Field.SourceProfileId"), details.SourceProfileId > 0 ? details.SourceProfileId.ToString() : string.Empty);
            AddField(fields, L("PlayableInspection.Field.SourceEndpoint"), _redactionService.RedactUrl(credential?.Url));
            AddField(fields, L("PlayableInspection.Field.LogicalIdentity"), logicalKey);
            AddField(fields, L("PlayableInspection.Field.AcquisitionProfile"), FirstNonEmpty(diagnostics?.AcquisitionProfileLabel, diagnostics?.AcquisitionProfileKey));
            AddField(fields, L("PlayableInspection.Field.ProviderProfile"), BuildProviderProfileText(diagnostics, stalkerSnapshot));

            if (details.Channel != null)
            {
                AddField(fields, L("PlayableInspection.Field.RawProviderName"), details.Channel.Name);
                AddField(fields, L("PlayableInspection.Field.Category"), details.CategoryName);
                AddField(fields, L("PlayableInspection.Field.NormalizedName"), details.Channel.NormalizedName);
                AddField(fields, L("PlayableInspection.Field.NormalizedIdentity"), details.Channel.NormalizedIdentityKey);
                AddField(fields, L("PlayableInspection.Field.AliasKeys"), details.Channel.AliasKeys);
                AddField(fields, L("PlayableInspection.Field.ProviderEpgId"), details.Channel.ProviderEpgChannelId);
                AddField(fields, L("PlayableInspection.Field.MatchedEpgId"), details.Channel.EpgChannelId);
            }

            if (details.Movie != null)
            {
                AddField(fields, L("PlayableInspection.Field.RawProviderTitle"), details.Movie.RawSourceTitle);
                AddField(fields, L("PlayableInspection.Field.Category"), details.Movie.CategoryName);
                AddField(fields, L("PlayableInspection.Field.ProviderItemId"), details.Movie.ExternalId);
                AddField(fields, L("PlayableInspection.Field.CanonicalKey"), details.Movie.CanonicalTitleKey);
                AddField(fields, L("PlayableInspection.Field.DedupFingerprint"), details.Movie.DedupFingerprint);
                AddField(fields, "TMDb / IMDb", BuildJoined(" / ", details.Movie.TmdbId, details.Movie.ImdbId));
            }

            if (details.Episode != null)
            {
                AddField(fields, L("PlayableInspection.Field.Series"), details.Series?.Title);
                AddField(fields, L("PlayableInspection.Field.SeasonEpisode"), details.SeasonNumber > 0 ? $"S{details.SeasonNumber:00}E{details.Episode.EpisodeNumber:00}" : F("PlayableInspection.Value.EpisodeNumber", details.Episode.EpisodeNumber));
                AddField(fields, L("PlayableInspection.Field.EpisodeProviderId"), details.Episode.ExternalId);
                AddField(fields, L("PlayableInspection.Field.SeriesProviderId"), details.Series?.ExternalId);
                AddField(fields, L("PlayableInspection.Field.SeriesCanonicalKey"), details.Series?.CanonicalTitleKey);
                AddField(fields, L("PlayableInspection.Field.SeriesDedupFingerprint"), details.Series?.DedupFingerprint);
            }

            if (stalkerSnapshot != null)
            {
                AddRedactedField(fields, "Portal", BuildJoined(" ", stalkerSnapshot.PortalName, stalkerSnapshot.PortalVersion));
                AddRedactedField(fields, L("PlayableInspection.Field.PortalProfile"), BuildJoined(" / ", stalkerSnapshot.ProfileName, stalkerSnapshot.ProfileId));
                AddField(fields, "Portal MAC", _redactionService.RedactMacAddress(stalkerSnapshot.MacAddress));
            }

            return new PlayableItemInspectionSection
            {
                Title = L("PlayableInspection.Section.Identity"),
                Fields = fields
            };
        }

        private PlayableItemInspectionSection BuildStreamSection(
            LoadedItemDetails details,
            PlaybackLaunchContext context,
            SourceCredential? credential)
        {
            var fields = new List<PlayableItemInspectionField>();
            AddField(fields, L("PlayableInspection.Field.CatalogStream"), _redactionService.RedactUrl(context.CatalogStreamUrl));
            AddField(fields, L("PlayableInspection.Field.UpstreamStream"), BuildUpstreamStreamText(context));
            AddField(fields, L("PlayableInspection.Field.LaunchStream"), ShouldShowResolvedUrl(context) ? _redactionService.RedactUrl(context.StreamUrl) : L("PlayableInspection.Value.NotResolvedUntilLaunch"));
            AddField(fields, L("PlayableInspection.Field.LiveStream"), context.ContentType == PlaybackContentType.Channel ? _redactionService.RedactUrl(context.LiveStreamUrl) : string.Empty);
            AddRedactedField(fields, L("PlayableInspection.Field.LaunchPath"), BuildLaunchPathText(context));
            AddRedactedField(fields, L("PlayableInspection.Field.ProviderResolution"), FirstNonEmpty(context.ProviderSummary, InferProviderSummary(context.CatalogStreamUrl)));
            AddRedactedField(fields, L("PlayableInspection.Field.Routing"), FirstNonEmpty(context.RoutingSummary, credential?.ProxyScope == SourceProxyScope.Disabled ? L("PlayableInspection.Value.DirectRouting") : string.Empty));
            AddField(fields, L("PlayableInspection.Field.SourceProxyPolicy"), BuildProxyPolicyText(credential));
            AddField(fields, L("PlayableInspection.Field.ProxyEndpoint"), _redactionService.RedactUrl(credential?.ProxyUrl));
            AddField(fields, L("PlayableInspection.Field.CompanionPolicy"), BuildCompanionPolicyText(context, credential));
            AddField(fields, L("PlayableInspection.Field.CompanionEndpoint"), _redactionService.RedactUrl(FirstNonEmpty(context.CompanionUrl, credential?.CompanionUrl)));
            AddRedactedField(fields, L("PlayableInspection.Field.CompanionStatus"), BuildCompanionStatusDisplayText(context, credential));
            AddRedactedField(fields, L("PlayableInspection.Field.OperationalSelection"), context.OperationalSummary);
            AddField(fields, L("PlayableInspection.Field.MirrorCandidates"), context.MirrorCandidateCount > 0 ? context.MirrorCandidateCount.ToString() : string.Empty);
            AddField(fields, L("PlayableInspection.Field.PlaybackMode"), context.PlaybackMode.ToString());

            if (context.PlaybackMode == CatchupPlaybackMode.Catchup || context.CatchupRequestKind != CatchupRequestKind.None)
            {
                AddField(fields, L("PlayableInspection.Field.CatchupRequest"), context.CatchupRequestKind.ToString());
                AddRedactedField(fields, L("PlayableInspection.Field.CatchupStatus"), BuildJoined(" - ", context.CatchupResolutionStatus.ToString(), context.CatchupStatusText));
                AddField(fields, L("PlayableInspection.Field.CatchupProgram"), context.CatchupProgramTitle);
                AddField(fields, L("PlayableInspection.Field.CatchupWindow"), FormatWindow(context.CatchupProgramStartTimeUtc, context.CatchupProgramEndTimeUtc));
            }

            if (details.Channel != null)
            {
                AddField(fields, L("PlayableInspection.Field.CatchupSupport"), details.Channel.SupportsCatchup ? L("PlayableInspection.Value.Supported") : L("PlayableInspection.Value.NotAdvertised"));
                AddField(fields, L("PlayableInspection.Field.CatchupWindowHours"), details.Channel.CatchupWindowHours > 0 ? details.Channel.CatchupWindowHours.ToString() : string.Empty);
            }

            return new PlayableItemInspectionSection
            {
                Title = L("PlayableInspection.Section.Streams"),
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
            AddRedactedField(fields, L("PlayableInspection.Field.GuideStatus"), diagnostics?.EpgStatusText);
            AddRedactedField(fields, L("PlayableInspection.Field.GuideSummary"), FirstNonEmpty(diagnostics?.EpgStatusSummary, diagnostics?.EpgCoverageText));
            AddField(fields, "EPG match", BuildJoined(" / ", channel.EpgMatchSource.ToString(), channel.EpgMatchConfidence > 0 ? $"{channel.EpgMatchConfidence}%" : string.Empty));
            AddRedactedField(fields, L("PlayableInspection.Field.EpgMatchSummary"), channel.EpgMatchSummary);
            AddRedactedField(fields, L("PlayableInspection.Field.CatchupSource"), BuildJoined(" / ", channel.CatchupSource.ToString(), channel.ProviderCatchupSource));
            AddRedactedField(fields, L("PlayableInspection.Field.CatchupSummary"), channel.CatchupSummary);
            AddRedactedField(fields, L("PlayableInspection.Field.LatestCatchupAttempt"), BuildCatchupAttemptText(catchupAttempt));
            AddRedactedField(fields, L("PlayableInspection.Field.CurrentPlaybackCatchup"), context.PlaybackMode == CatchupPlaybackMode.Catchup ? context.CatchupStatusText : string.Empty);

            return new PlayableItemInspectionSection
            {
                Title = L("PlayableInspection.Section.GuideCatchup"),
                Fields = fields
            };
        }

        private PlayableItemInspectionSection BuildOperationalSection(
            PlaybackLaunchContext context,
            SourceCredential? credential,
            LogicalOperationalState? operationalState)
        {
            var fields = new List<PlayableItemInspectionField>();
            AddField(fields, L("PlayableInspection.Field.LogicalContentKey"), context.LogicalContentKey);
            AddField(fields, L("PlayableInspection.Field.PreferredSourceProfile"), context.PreferredSourceProfileId > 0 ? context.PreferredSourceProfileId.ToString() : string.Empty);
            AddRedactedField(fields, L("PlayableInspection.Field.RoutingSummary"), context.RoutingSummary);
            AddRedactedField(fields, L("PlayableInspection.Field.ProviderSummary"), context.ProviderSummary);
            AddRedactedField(fields, L("PlayableInspection.Field.OperationalSummary"), context.OperationalSummary);
            AddRedactedField(fields, L("PlayableInspection.Field.RecoverySummary"), operationalState?.RecoverySummary);
            AddField(fields, L("PlayableInspection.Field.CandidateCount"), operationalState?.CandidateCount > 0 ? operationalState.CandidateCount.ToString() : string.Empty);
            AddField(fields, L("PlayableInspection.Field.LastKnownGood"), operationalState != null && operationalState.LastKnownGoodSourceProfileId > 0
                ? $"{operationalState.LastKnownGoodSourceProfileId} at {operationalState.LastKnownGoodAtUtc?.ToLocalTime():g}"
                : string.Empty);
            AddRedactedField(fields, L("PlayableInspection.Field.CandidateRanking"), BuildCandidateText(operationalState));
            AddField(fields, L("PlayableInspection.Field.SourceProxyPolicy"), credential?.ProxyScope.ToString());

            return new PlayableItemInspectionSection
            {
                Title = L("PlayableInspection.Section.RoutingFallback"),
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
                    Title = L("PlayableInspection.Section.SourceDiagnostics"),
                    Fields = fields
                };
            }

            AddRedactedField(fields, L("PlayableInspection.Field.Health"), BuildJoined(" - ", diagnostics.HealthLabel, diagnostics.StatusSummary));
            AddRedactedField(fields, L("PlayableInspection.Field.Validation"), diagnostics.ValidationResultText);
            AddRedactedField(fields, L("PlayableInspection.Field.AcquisitionRun"), BuildJoined(" - ", diagnostics.AcquisitionRunStatusText, diagnostics.AcquisitionRunSummaryText));
            AddRedactedField(fields, L("PlayableInspection.Field.AcquisitionStats"), diagnostics.AcquisitionStatsText);
            AddRedactedField(fields, L("PlayableInspection.Field.WarningSummary"), FirstNonEmpty(diagnostics.WarningSummaryText, diagnostics.FailureSummaryText));
            AddRedactedField(fields, L("PlayableInspection.Field.TopIssue"), diagnostics.Issues.FirstOrDefault()?.Message);
            AddRedactedField(fields, L("PlayableInspection.Field.ProbeSummary"), BuildProbeText(diagnostics.HealthProbes));
            AddRedactedField(fields, L("PlayableInspection.Field.RelevantEvidence"), BuildEvidenceText(diagnostics.AcquisitionEvidence));
            AddRedactedField(fields, L("PlayableInspection.Field.PortalStatus"), BuildJoined(" - ", diagnostics.StalkerPortalSummaryText, diagnostics.StalkerPortalErrorText));
            AddRedactedField(fields, L("PlayableInspection.Field.CatchupDiagnostics"), FirstNonEmpty(diagnostics.CatchupStatusText, diagnostics.CatchupLatestAttemptText));

            return new PlayableItemInspectionSection
            {
                Title = L("PlayableInspection.Section.SourceDiagnostics"),
                Fields = fields
            };
        }

        private PlayableItemInspectionSection BuildRuntimeSection(PlayableItemInspectionRuntimeState runtimeState)
        {
            var fields = new List<PlayableItemInspectionField>();
            AddField(fields, L("PlayableInspection.Field.SessionState"), runtimeState.SessionState);
            AddRedactedField(fields, L("PlayableInspection.Field.SessionMessage"), runtimeState.SessionMessage);
            AddField(fields, L("PlayableInspection.Field.PositionDuration"), BuildRuntimePositionText(runtimeState));
            AddField(fields, L("PlayableInspection.Field.Seekability"), runtimeState.IsSeekable ? L("PlayableInspection.Value.Seekable") : L("PlayableInspection.Value.NotSeekable"));
            AddField(fields, L("PlayableInspection.Field.Resolution"), runtimeState.Width > 0 && runtimeState.Height > 0 ? $"{runtimeState.Width}x{runtimeState.Height}" : string.Empty);
            AddField(fields, "FPS", runtimeState.FramesPerSecond > 0 ? runtimeState.FramesPerSecond.ToString("0.##") : string.Empty);
            AddField(fields, L("PlayableInspection.Field.VideoCodec"), runtimeState.VideoCodec);
            AddField(fields, L("PlayableInspection.Field.AudioCodec"), runtimeState.AudioCodec);
            AddField(fields, L("PlayableInspection.Field.Container"), runtimeState.ContainerFormat);
            AddField(fields, L("PlayableInspection.Field.PixelFormat"), runtimeState.PixelFormat);
            AddField(fields, L("PlayableInspection.Field.HardwareDecode"), runtimeState.IsHardwareDecodingActive ? L("PlayableInspection.Value.Active") : string.Empty);

            return new PlayableItemInspectionSection
            {
                Title = L("PlayableInspection.Section.CurrentPlayback"),
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
                    return L("PlayableInspection.Status.ActiveCompanion");
                }

                return ShouldShowResolvedUrl(context)
                    ? L("PlayableInspection.Status.ActiveResolved")
                    : L("PlayableInspection.Status.ActiveRouting");
            }

            if (context.PlaybackMode == CatchupPlaybackMode.Catchup || context.CatchupRequestKind != CatchupRequestKind.None)
            {
                if (context.CompanionStatus == CompanionRelayStatus.Applied)
                {
                    return L("PlayableInspection.Status.CatchupCompanion");
                }

                return L("PlayableInspection.Status.CatchupCatalog");
            }

            if (context.CompanionStatus == CompanionRelayStatus.Applied)
            {
                return L("PlayableInspection.Status.CatalogCompanion");
            }

            return string.IsNullOrWhiteSpace(diagnostics?.StatusSummary)
                ? L("PlayableInspection.Status.CatalogDeferred")
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
            builder.AppendLine(L("PlayableInspection.Report.Title"));
            builder.AppendLine(title);
            builder.AppendLine(L("PlayableInspection.Report.RedactedNotice"));

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
                ? L("PlayableInspection.Value.StalkerLocator")
                : L("PlayableInspection.Value.DirectStreamUrl");
        }

        private static string BuildLaunchPathText(PlaybackLaunchContext context)
        {
            var parts = new List<string>();
            if (context.PlaybackMode == CatchupPlaybackMode.Catchup || context.CatchupRequestKind != CatchupRequestKind.None)
            {
                parts.Add(L("PlayableInspection.Value.CatchupReplay"));
            }

            if (ShouldShowResolvedUrl(context))
            {
                parts.Add(!string.IsNullOrWhiteSpace(context.UpstreamStreamUrl) &&
                          !string.Equals(context.UpstreamStreamUrl, context.StreamUrl, StringComparison.Ordinal)
                    ? L("PlayableInspection.Value.UpstreamResolvedRelayed")
                    : L("PlayableInspection.Value.ResolvedLaunchUrl"));
            }
            else
            {
                parts.Add(L("PlayableInspection.Value.CatalogUrl"));
            }

            if (context.CompanionStatus == CompanionRelayStatus.Applied)
            {
                parts.Add(L("PlayableInspection.Value.CompanionRelay"));
            }
            else if (context.CompanionStatus == CompanionRelayStatus.FallbackDirect)
            {
                parts.Add(L("PlayableInspection.Value.CompanionFallbackDirect"));
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
                : L("PlayableInspection.Value.NotResolvedUntilLaunch");
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
                return L("General.Disabled");
            }

            var scopeText = scope == SourceCompanionScope.PlaybackAndProbing
                ? L("PlayableInspection.Value.PlaybackProbes")
                : L("PlayableInspection.Value.PlaybackOnly");
            var modeText = mode == SourceCompanionRelayMode.Buffered
                ? L("PlayableInspection.Value.BufferedRelayLower")
                : L("PlayableInspection.Value.PassThroughRelayLower");
            return F("PlayableInspection.Value.CompanionPolicyFormat", scopeText, modeText);
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
                CompanionRelayStatus.Applied => L("PlayableInspection.Value.Applied"),
                CompanionRelayStatus.FallbackDirect => L("PlayableInspection.Value.FallbackToDirect"),
                CompanionRelayStatus.Skipped => L("PlayableInspection.Value.Skipped"),
                CompanionRelayStatus.Failed => L("PlayableInspection.Value.Failed"),
                _ => L("PlayableInspection.Value.Pending")
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
                            ? L("PlayableInspection.Value.Selected")
                            : candidate.IsLastKnownGood ? L("PlayableInspection.Value.LastGood") : F("PlayableInspection.Value.Rank", candidate.Rank);
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
