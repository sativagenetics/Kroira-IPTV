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
                    Title = "Item unavailable",
                    StatusText = "No playback context was available for inspection.",
                    Sections = Array.Empty<PlayableItemInspectionSection>(),
                    SafeReportText = $"KROIRA item inspection{Environment.NewLine}No playback context was available."
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
                        return LoadedItemDetails.Fallback("Channel", context);
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
                        return LoadedItemDetails.Fallback("Movie", context);
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
                        return LoadedItemDetails.Fallback("Episode", context);
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

            return LoadedItemDetails.Fallback("Item", context);
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
            AddField(fields, "Content type", context.ContentType.ToString());
            AddField(fields, "Source", details.SourceName);
            AddField(fields, "Source type", details.SourceType.ToString());
            AddField(fields, "Source profile id", details.SourceProfileId > 0 ? details.SourceProfileId.ToString() : string.Empty);
            AddField(fields, "Source endpoint", _redactionService.RedactUrl(credential?.Url));
            AddField(fields, "Logical identity", logicalKey);
            AddField(fields, "Acquisition profile", FirstNonEmpty(diagnostics?.AcquisitionProfileLabel, diagnostics?.AcquisitionProfileKey));
            AddField(fields, "Provider profile", BuildProviderProfileText(diagnostics, stalkerSnapshot));

            if (details.Channel != null)
            {
                AddField(fields, "Raw provider name", details.Channel.Name);
                AddField(fields, "Category", details.CategoryName);
                AddField(fields, "Normalized name", details.Channel.NormalizedName);
                AddField(fields, "Normalized identity", details.Channel.NormalizedIdentityKey);
                AddField(fields, "Alias keys", details.Channel.AliasKeys);
                AddField(fields, "Provider EPG id", details.Channel.ProviderEpgChannelId);
                AddField(fields, "Matched EPG id", details.Channel.EpgChannelId);
            }

            if (details.Movie != null)
            {
                AddField(fields, "Raw provider title", details.Movie.RawSourceTitle);
                AddField(fields, "Category", details.Movie.CategoryName);
                AddField(fields, "Provider item id", details.Movie.ExternalId);
                AddField(fields, "Canonical key", details.Movie.CanonicalTitleKey);
                AddField(fields, "Dedup fingerprint", details.Movie.DedupFingerprint);
                AddField(fields, "TMDb / IMDb", BuildJoined(" / ", details.Movie.TmdbId, details.Movie.ImdbId));
            }

            if (details.Episode != null)
            {
                AddField(fields, "Series", details.Series?.Title);
                AddField(fields, "Season / episode", details.SeasonNumber > 0 ? $"S{details.SeasonNumber:00}E{details.Episode.EpisodeNumber:00}" : $"Episode {details.Episode.EpisodeNumber}");
                AddField(fields, "Episode provider id", details.Episode.ExternalId);
                AddField(fields, "Series provider id", details.Series?.ExternalId);
                AddField(fields, "Series canonical key", details.Series?.CanonicalTitleKey);
                AddField(fields, "Series dedup fingerprint", details.Series?.DedupFingerprint);
            }

            if (stalkerSnapshot != null)
            {
                AddRedactedField(fields, "Portal", BuildJoined(" ", stalkerSnapshot.PortalName, stalkerSnapshot.PortalVersion));
                AddRedactedField(fields, "Portal profile", BuildJoined(" / ", stalkerSnapshot.ProfileName, stalkerSnapshot.ProfileId));
                AddField(fields, "Portal MAC", _redactionService.RedactMacAddress(stalkerSnapshot.MacAddress));
            }

            return new PlayableItemInspectionSection
            {
                Title = "Identity",
                Fields = fields
            };
        }

        private PlayableItemInspectionSection BuildStreamSection(
            LoadedItemDetails details,
            PlaybackLaunchContext context,
            SourceCredential? credential)
        {
            var fields = new List<PlayableItemInspectionField>();
            AddField(fields, "Catalog stream", _redactionService.RedactUrl(context.CatalogStreamUrl));
            AddField(fields, "Upstream stream", BuildUpstreamStreamText(context));
            AddField(fields, "Launch stream", ShouldShowResolvedUrl(context) ? _redactionService.RedactUrl(context.StreamUrl) : "Not resolved until launch");
            AddField(fields, "Live stream", context.ContentType == PlaybackContentType.Channel ? _redactionService.RedactUrl(context.LiveStreamUrl) : string.Empty);
            AddRedactedField(fields, "Launch path", BuildLaunchPathText(context));
            AddRedactedField(fields, "Provider resolution", FirstNonEmpty(context.ProviderSummary, InferProviderSummary(context.CatalogStreamUrl)));
            AddRedactedField(fields, "Routing", FirstNonEmpty(context.RoutingSummary, credential?.ProxyScope == SourceProxyScope.Disabled ? "Direct routing" : string.Empty));
            AddField(fields, "Source proxy policy", BuildProxyPolicyText(credential));
            AddField(fields, "Proxy endpoint", _redactionService.RedactUrl(credential?.ProxyUrl));
            AddField(fields, "Companion policy", BuildCompanionPolicyText(context, credential));
            AddField(fields, "Companion endpoint", _redactionService.RedactUrl(FirstNonEmpty(context.CompanionUrl, credential?.CompanionUrl)));
            AddRedactedField(fields, "Companion status", BuildCompanionStatusDisplayText(context, credential));
            AddRedactedField(fields, "Operational selection", context.OperationalSummary);
            AddField(fields, "Mirror candidates", context.MirrorCandidateCount > 0 ? context.MirrorCandidateCount.ToString() : string.Empty);
            AddField(fields, "Playback mode", context.PlaybackMode.ToString());

            if (context.PlaybackMode == CatchupPlaybackMode.Catchup || context.CatchupRequestKind != CatchupRequestKind.None)
            {
                AddField(fields, "Catchup request", context.CatchupRequestKind.ToString());
                AddRedactedField(fields, "Catchup status", BuildJoined(" - ", context.CatchupResolutionStatus.ToString(), context.CatchupStatusText));
                AddField(fields, "Catchup program", context.CatchupProgramTitle);
                AddField(fields, "Catchup window", FormatWindow(context.CatchupProgramStartTimeUtc, context.CatchupProgramEndTimeUtc));
            }

            if (details.Channel != null)
            {
                AddField(fields, "Catchup support", details.Channel.SupportsCatchup ? "Supported" : "Not advertised");
                AddField(fields, "Catchup window hours", details.Channel.CatchupWindowHours > 0 ? details.Channel.CatchupWindowHours.ToString() : string.Empty);
            }

            return new PlayableItemInspectionSection
            {
                Title = "Streams",
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
            AddRedactedField(fields, "Guide status", diagnostics?.EpgStatusText);
            AddRedactedField(fields, "Guide summary", FirstNonEmpty(diagnostics?.EpgStatusSummary, diagnostics?.EpgCoverageText));
            AddField(fields, "EPG match", BuildJoined(" / ", channel.EpgMatchSource.ToString(), channel.EpgMatchConfidence > 0 ? $"{channel.EpgMatchConfidence}%" : string.Empty));
            AddRedactedField(fields, "EPG match summary", channel.EpgMatchSummary);
            AddRedactedField(fields, "Catchup source", BuildJoined(" / ", channel.CatchupSource.ToString(), channel.ProviderCatchupSource));
            AddRedactedField(fields, "Catchup summary", channel.CatchupSummary);
            AddRedactedField(fields, "Latest catchup attempt", BuildCatchupAttemptText(catchupAttempt));
            AddRedactedField(fields, "Current playback catchup", context.PlaybackMode == CatchupPlaybackMode.Catchup ? context.CatchupStatusText : string.Empty);

            return new PlayableItemInspectionSection
            {
                Title = "Guide and catchup",
                Fields = fields
            };
        }

        private PlayableItemInspectionSection BuildOperationalSection(
            PlaybackLaunchContext context,
            SourceCredential? credential,
            LogicalOperationalState? operationalState)
        {
            var fields = new List<PlayableItemInspectionField>();
            AddField(fields, "Logical content key", context.LogicalContentKey);
            AddField(fields, "Preferred source profile", context.PreferredSourceProfileId > 0 ? context.PreferredSourceProfileId.ToString() : string.Empty);
            AddRedactedField(fields, "Routing summary", context.RoutingSummary);
            AddRedactedField(fields, "Provider summary", context.ProviderSummary);
            AddRedactedField(fields, "Operational summary", context.OperationalSummary);
            AddRedactedField(fields, "Recovery summary", operationalState?.RecoverySummary);
            AddField(fields, "Candidate count", operationalState?.CandidateCount > 0 ? operationalState.CandidateCount.ToString() : string.Empty);
            AddField(fields, "Last known good", operationalState != null && operationalState.LastKnownGoodSourceProfileId > 0
                ? $"{operationalState.LastKnownGoodSourceProfileId} at {operationalState.LastKnownGoodAtUtc?.ToLocalTime():g}"
                : string.Empty);
            AddRedactedField(fields, "Candidate ranking", BuildCandidateText(operationalState));
            AddField(fields, "Source proxy policy", credential?.ProxyScope.ToString());

            return new PlayableItemInspectionSection
            {
                Title = "Routing and fallback",
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
                    Title = "Source diagnostics",
                    Fields = fields
                };
            }

            AddRedactedField(fields, "Health", BuildJoined(" - ", diagnostics.HealthLabel, diagnostics.StatusSummary));
            AddRedactedField(fields, "Validation", diagnostics.ValidationResultText);
            AddRedactedField(fields, "Acquisition run", BuildJoined(" - ", diagnostics.AcquisitionRunStatusText, diagnostics.AcquisitionRunSummaryText));
            AddRedactedField(fields, "Acquisition stats", diagnostics.AcquisitionStatsText);
            AddRedactedField(fields, "Warning summary", FirstNonEmpty(diagnostics.WarningSummaryText, diagnostics.FailureSummaryText));
            AddRedactedField(fields, "Top issue", diagnostics.Issues.FirstOrDefault()?.Message);
            AddRedactedField(fields, "Probe summary", BuildProbeText(diagnostics.HealthProbes));
            AddRedactedField(fields, "Relevant evidence", BuildEvidenceText(diagnostics.AcquisitionEvidence));
            AddRedactedField(fields, "Portal status", BuildJoined(" - ", diagnostics.StalkerPortalSummaryText, diagnostics.StalkerPortalErrorText));
            AddRedactedField(fields, "Catchup diagnostics", FirstNonEmpty(diagnostics.CatchupStatusText, diagnostics.CatchupLatestAttemptText));

            return new PlayableItemInspectionSection
            {
                Title = "Source diagnostics",
                Fields = fields
            };
        }

        private PlayableItemInspectionSection BuildRuntimeSection(PlayableItemInspectionRuntimeState runtimeState)
        {
            var fields = new List<PlayableItemInspectionField>();
            AddField(fields, "Session state", runtimeState.SessionState);
            AddRedactedField(fields, "Session message", runtimeState.SessionMessage);
            AddField(fields, "Position / duration", BuildRuntimePositionText(runtimeState));
            AddField(fields, "Seekability", runtimeState.IsSeekable ? "Seekable" : "Not seekable");
            AddField(fields, "Resolution", runtimeState.Width > 0 && runtimeState.Height > 0 ? $"{runtimeState.Width}x{runtimeState.Height}" : string.Empty);
            AddField(fields, "FPS", runtimeState.FramesPerSecond > 0 ? runtimeState.FramesPerSecond.ToString("0.##") : string.Empty);
            AddField(fields, "Video codec", runtimeState.VideoCodec);
            AddField(fields, "Audio codec", runtimeState.AudioCodec);
            AddField(fields, "Container", runtimeState.ContainerFormat);
            AddField(fields, "Pixel format", runtimeState.PixelFormat);
            AddField(fields, "Hardware decode", runtimeState.IsHardwareDecodingActive ? "Active" : string.Empty);

            return new PlayableItemInspectionSection
            {
                Title = "Current playback",
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
                    return "Showing the active playback context, including the upstream provider stream and the local companion relay launch URL currently in use.";
                }

                return ShouldShowResolvedUrl(context)
                    ? "Showing the active playback context, including the resolved launch URL currently in use."
                    : "Showing the active playback context with source-aware routing details.";
            }

            if (context.PlaybackMode == CatchupPlaybackMode.Catchup || context.CatchupRequestKind != CatchupRequestKind.None)
            {
                if (context.CompanionStatus == CompanionRelayStatus.Applied)
                {
                    return "Showing the catalog, upstream replay stream, and companion relay launch context for this catchup request.";
                }

                return "Showing the catalog and catchup context for this item. The replay URL is resolved only when catchup is launched.";
            }

            if (context.CompanionStatus == CompanionRelayStatus.Applied)
            {
                return "Showing the catalog-level view of this item, including the upstream provider stream and the local companion relay launch path.";
            }

            return string.IsNullOrWhiteSpace(diagnostics?.StatusSummary)
                ? "Showing the catalog-level view of this item. Provider-specific URLs are resolved only at launch time."
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
            builder.AppendLine("KROIRA item inspection");
            builder.AppendLine(title);
            builder.AppendLine("Sensitive values are redacted.");

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
                ? "Stalker locator"
                : "Direct stream URL";
        }

        private static string BuildLaunchPathText(PlaybackLaunchContext context)
        {
            var parts = new List<string>();
            if (context.PlaybackMode == CatchupPlaybackMode.Catchup || context.CatchupRequestKind != CatchupRequestKind.None)
            {
                parts.Add("Catchup replay");
            }

            if (ShouldShowResolvedUrl(context))
            {
                parts.Add(!string.IsNullOrWhiteSpace(context.UpstreamStreamUrl) &&
                          !string.Equals(context.UpstreamStreamUrl, context.StreamUrl, StringComparison.Ordinal)
                    ? "Upstream resolved then relayed"
                    : "Resolved launch URL");
            }
            else
            {
                parts.Add("Catalog URL");
            }

            if (context.CompanionStatus == CompanionRelayStatus.Applied)
            {
                parts.Add("Companion relay");
            }
            else if (context.CompanionStatus == CompanionRelayStatus.FallbackDirect)
            {
                parts.Add("Companion fallback to direct");
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
                : "Not resolved until launch";
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
                return "Disabled";
            }

            var scopeText = scope == SourceCompanionScope.PlaybackAndProbing
                ? "Playback + probes"
                : "Playback only";
            var modeText = mode == SourceCompanionRelayMode.Buffered
                ? "buffered relay"
                : "pass-through relay";
            return $"{scopeText} - {modeText}";
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
                CompanionRelayStatus.Applied => "Applied",
                CompanionRelayStatus.FallbackDirect => "Fallback to direct",
                CompanionRelayStatus.Skipped => "Skipped",
                CompanionRelayStatus.Failed => "Failed",
                _ => "Pending"
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
                            ? "selected"
                            : candidate.IsLastKnownGood ? "last good" : $"rank {candidate.Rank}";
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
