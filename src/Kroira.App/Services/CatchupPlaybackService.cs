#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroira.App.Services
{
    public interface ICatchupPlaybackService
    {
        CatchupProgramAvailability EvaluateProgramAvailability(
            Channel channel,
            SourceType sourceType,
            ChannelGuideProgram program,
            DateTime nowUtc);

        Task<CatchupPlaybackResolution> ResolveAsync(
            AppDbContext db,
            CatchupPlaybackRequest request,
            CancellationToken cancellationToken = default);

        Task<CatchupPlaybackResolution> ResolveForContextAsync(
            AppDbContext db,
            PlaybackLaunchContext context,
            CancellationToken cancellationToken = default);
    }

    public sealed class CatchupPlaybackService : ICatchupPlaybackService
    {
        private const int MaxAttemptsPerSource = 24;
        private readonly IContentOperationalService _contentOperationalService;
        private readonly ICompanionRelayService _companionRelayService;

        public CatchupPlaybackService(
            IContentOperationalService contentOperationalService,
            ICompanionRelayService companionRelayService)
        {
            _contentOperationalService = contentOperationalService;
            _companionRelayService = companionRelayService;
        }

        public CatchupProgramAvailability EvaluateProgramAvailability(
            Channel channel,
            SourceType sourceType,
            ChannelGuideProgram program,
            DateTime nowUtc)
        {
            if (channel == null)
            {
                return Unavailable(CatchupAvailabilityState.Unsupported, "Catchup metadata is unavailable.");
            }

            var startUtc = NormalizeUtc(program.StartTimeUtc);
            var endUtc = NormalizeUtc(program.EndTimeUtc);
            var normalizedNowUtc = NormalizeUtc(nowUtc);

            if (!channel.SupportsCatchup)
            {
                return Unavailable(
                    CatchupAvailabilityState.Unsupported,
                    string.IsNullOrWhiteSpace(channel.CatchupSummary)
                        ? "This channel does not advertise catchup support."
                        : channel.CatchupSummary);
            }

            if (!HasReplayTemplate(channel, sourceType))
            {
                return Unavailable(
                    CatchupAvailabilityState.MissingTemplate,
                    "Catchup is advertised, but the provider did not expose a replay template.");
            }

            if (channel.CatchupWindowHours <= 0)
            {
                return Unavailable(
                    CatchupAvailabilityState.MissingWindow,
                    "Catchup is advertised, but the provider did not expose an archive window.");
            }

            if (startUtc > normalizedNowUtc)
            {
                return Unavailable(
                    CatchupAvailabilityState.Future,
                    "This programme has not started yet.");
            }

            var archiveWindowStartUtc = normalizedNowUtc.AddHours(-channel.CatchupWindowHours);
            if (startUtc < archiveWindowStartUtc)
            {
                return Unavailable(
                    CatchupAvailabilityState.Expired,
                    $"This programme is outside the {FormatWindow(channel.CatchupWindowHours)} catchup window.");
            }

            var isCurrent = startUtc <= normalizedNowUtc && endUtc > normalizedNowUtc;
            return new CatchupProgramAvailability
            {
                State = CatchupAvailabilityState.Available,
                Message = isCurrent
                    ? "Start over is available for the current programme."
                    : "Replay is available for this programme.",
                ActionLabel = isCurrent ? "Start over" : "Watch from start",
                RequestKind = isCurrent ? CatchupRequestKind.StartOver : CatchupRequestKind.ReplayProgram
            };
        }

        public async Task<CatchupPlaybackResolution> ResolveAsync(
            AppDbContext db,
            CatchupPlaybackRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                return new CatchupPlaybackResolution
                {
                    Status = CatchupResolutionStatus.Failed,
                    Message = "Catchup request is missing."
                };
            }

            var context = new PlaybackLaunchContext
            {
                ProfileId = request.ProfileId,
                ContentId = request.ChannelId,
                ContentType = PlaybackContentType.Channel,
                PlaybackMode = CatchupPlaybackMode.Catchup,
                LogicalContentKey = request.LogicalContentKey,
                PreferredSourceProfileId = request.PreferredSourceProfileId,
                CatchupRequestKind = request.RequestKind,
                CatchupProgramTitle = request.ProgramTitle,
                CatchupProgramStartTimeUtc = request.ProgramStartTimeUtc,
                CatchupProgramEndTimeUtc = request.ProgramEndTimeUtc,
                CatchupRequestedAtUtc = request.RequestedAtUtc
            };

            await _contentOperationalService.ResolvePlaybackContextAsync(db, context);
            return await ResolveForContextAsync(db, context, cancellationToken);
        }

        public async Task<CatchupPlaybackResolution> ResolveForContextAsync(
            AppDbContext db,
            PlaybackLaunchContext context,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                return new CatchupPlaybackResolution
                {
                    Status = CatchupResolutionStatus.Failed,
                    Message = "Catchup context is missing."
                };
            }

            var resolution = new CatchupPlaybackResolution
            {
                ChannelId = context.ContentId,
                LogicalContentKey = context.LogicalContentKey,
                RequestKind = context.CatchupRequestKind,
                ProgramTitle = context.CatchupProgramTitle,
                ProgramStartTimeUtc = NormalizeNullableUtc(context.CatchupProgramStartTimeUtc),
                ProgramEndTimeUtc = NormalizeNullableUtc(context.CatchupProgramEndTimeUtc),
                RoutingSummary = context.RoutingSummary,
                LiveStreamUrl = context.LiveStreamUrl
            };
            var requestedAtUtc = NormalizeNullableUtc(context.CatchupRequestedAtUtc) ?? DateTime.UtcNow;
            resolution.RequestedAtUtc = requestedAtUtc;

            if (context.ContentType != PlaybackContentType.Channel)
            {
                return Fail(context, resolution, CatchupResolutionStatus.Unsupported, "Catchup is only available for live channels.");
            }

            if (context.CatchupRequestKind == CatchupRequestKind.None)
            {
                return Fail(context, resolution, CatchupResolutionStatus.ProgramMissing, "No catchup action was requested.");
            }

            if (!resolution.ProgramStartTimeUtc.HasValue || !resolution.ProgramEndTimeUtc.HasValue)
            {
                return Fail(context, resolution, CatchupResolutionStatus.ProgramMissing, "Guide timing is required to resolve catchup playback.");
            }

            if (context.ContentId <= 0)
            {
                return Fail(context, resolution, CatchupResolutionStatus.Failed, "The target channel could not be resolved.");
            }

            if (string.IsNullOrWhiteSpace(context.StreamUrl))
            {
                await _contentOperationalService.ResolvePlaybackContextAsync(db, context);
                resolution.LiveStreamUrl = context.LiveStreamUrl;
                resolution.RoutingSummary = context.RoutingSummary;
            }

            if (string.IsNullOrWhiteSpace(context.StreamUrl))
            {
                return Fail(context, resolution, CatchupResolutionStatus.InvalidStream, "The live stream URL could not be resolved for catchup playback.");
            }

            var channelContext = await db.Channels
                .AsNoTracking()
                .Where(channel => channel.Id == context.ContentId)
                .Join(
                    db.ChannelCategories.AsNoTracking(),
                    channel => channel.ChannelCategoryId,
                    category => category.Id,
                    (channel, category) => new
                    {
                        Channel = channel,
                        category.SourceProfileId
                    })
                .Join(
                    db.SourceProfiles.AsNoTracking(),
                    item => item.SourceProfileId,
                    profile => profile.Id,
                    (item, profile) => new ResolvedChannelContext(
                        item.Channel,
                        item.SourceProfileId,
                        profile.Type))
                .FirstOrDefaultAsync(cancellationToken);

            if (channelContext == null)
            {
                return Fail(context, resolution, CatchupResolutionStatus.Failed, "The requested channel could not be loaded.");
            }

            resolution.SourceProfileId = channelContext.SourceProfileId;
            resolution.ChannelId = channelContext.Channel.Id;
            resolution.LogicalContentKey = string.IsNullOrWhiteSpace(context.LogicalContentKey)
                ? channelContext.Channel.NormalizedIdentityKey
                : context.LogicalContentKey;
            resolution.LiveStreamUrl = string.IsNullOrWhiteSpace(context.LiveStreamUrl)
                ? context.StreamUrl
                : context.LiveStreamUrl;

            var program = new ChannelGuideProgram(
                channelContext.Channel.Id,
                resolution.ProgramTitle,
                string.Empty,
                null,
                null,
                resolution.ProgramStartTimeUtc.Value,
                resolution.ProgramEndTimeUtc.Value,
                false,
                CatchupAvailabilityState.None,
                string.Empty,
                string.Empty,
                CatchupRequestKind.None);
            var availability = EvaluateProgramAvailability(channelContext.Channel, channelContext.SourceType, program, requestedAtUtc);
            if (!availability.CanPlay)
            {
                return await FailAndRecordAsync(
                    db,
                    context,
                    resolution,
                    channelContext,
                    MapAvailabilityToResolutionStatus(availability.State),
                    availability.Message,
                    requestedAtUtc,
                    cancellationToken);
            }

            var credentials = await db.SourceCredentials
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.SourceProfileId == channelContext.SourceProfileId, cancellationToken);

            var resolveResult = channelContext.SourceType == SourceType.Xtream
                ? BuildXtreamCatchupUrl(credentials, channelContext.Channel, resolution, requestedAtUtc)
                : BuildM3uCatchupUrl(channelContext.Channel, resolution, requestedAtUtc);

            if (resolveResult.Status != CatchupResolutionStatus.Resolved)
            {
                return await FailAndRecordAsync(
                    db,
                    context,
                    resolution,
                    channelContext,
                    resolveResult.Status,
                    resolveResult.Message,
                    requestedAtUtc,
                    cancellationToken);
            }

            resolution.Status = CatchupResolutionStatus.Resolved;
            resolution.Message = resolveResult.Message;
            resolution.UpstreamStreamUrl = resolveResult.StreamUrl;
            resolution.StreamUrl = resolveResult.StreamUrl;

            var companion = await _companionRelayService.ApplyAsync(
                credentials,
                new CompanionRelayRequest
                {
                    SourceProfileId = channelContext.SourceProfileId,
                    SourceType = channelContext.SourceType,
                    Purpose = SourceNetworkPurpose.Playback,
                    PlaybackMode = CatchupPlaybackMode.Catchup,
                    UpstreamUrl = resolveResult.StreamUrl,
                    ProviderSummary = resolveResult.Message
                },
                cancellationToken);
            if (companion.UseCompanion)
            {
                resolution.StreamUrl = companion.RelayUrl;
                resolution.RoutingSummary = companion.Summary;
            }
            else if (companion.Status == CompanionRelayStatus.FallbackDirect)
            {
                resolution.RoutingSummary = companion.Summary;
            }

            ApplySuccessfulResolution(context, resolution);
            context.CompanionScope = companion.Scope;
            context.CompanionMode = companion.Mode;
            context.CompanionUrl = companion.CompanionUrl;
            context.CompanionStatus = companion.Status;
            context.CompanionStatusText = companion.StatusText;

            await RecordAttemptAsync(
                db,
                channelContext,
                resolution,
                channelContext.Channel.CatchupWindowHours,
                channelContext.Channel.ProviderCatchupMode,
                channelContext.Channel.ProviderCatchupSource,
                requestedAtUtc,
                cancellationToken);
            return resolution;
        }

        private static void ApplySuccessfulResolution(
            PlaybackLaunchContext context,
            CatchupPlaybackResolution resolution)
        {
            context.PlaybackMode = CatchupPlaybackMode.Catchup;
            context.CatchupResolutionStatus = resolution.Status;
            context.CatchupStatusText = resolution.Message;
            context.CatchupProgramTitle = resolution.ProgramTitle;
            context.CatchupProgramStartTimeUtc = resolution.ProgramStartTimeUtc;
            context.CatchupProgramEndTimeUtc = resolution.ProgramEndTimeUtc;
            context.CatchupRequestedAtUtc = resolution.RequestedAtUtc;
            context.LiveStreamUrl = string.IsNullOrWhiteSpace(context.LiveStreamUrl)
                ? resolution.LiveStreamUrl
                : context.LiveStreamUrl;
            context.UpstreamStreamUrl = resolution.UpstreamStreamUrl;
            context.StreamUrl = resolution.StreamUrl;
            context.RoutingSummary = resolution.RoutingSummary;
            context.StartPositionMs = 0;
        }

        private CatchupPlaybackResolution Fail(
            PlaybackLaunchContext context,
            CatchupPlaybackResolution resolution,
            CatchupResolutionStatus status,
            string message)
        {
            resolution.Status = status;
            resolution.Message = message;
            context.CatchupResolutionStatus = status;
            context.CatchupStatusText = message;
            context.CatchupRequestedAtUtc = resolution.RequestedAtUtc;
            return resolution;
        }

        private async Task<CatchupPlaybackResolution> FailAndRecordAsync(
            AppDbContext db,
            PlaybackLaunchContext context,
            CatchupPlaybackResolution resolution,
            ResolvedChannelContext channelContext,
            CatchupResolutionStatus status,
            string message,
            DateTime requestedAtUtc,
            CancellationToken cancellationToken)
        {
            Fail(context, resolution, status, message);
            await RecordAttemptAsync(
                db,
                channelContext,
                resolution,
                channelContext.Channel.CatchupWindowHours,
                channelContext.Channel.ProviderCatchupMode,
                channelContext.Channel.ProviderCatchupSource,
                requestedAtUtc,
                cancellationToken);
            return resolution;
        }

        private async Task RecordAttemptAsync(
            AppDbContext db,
            ResolvedChannelContext channelContext,
            CatchupPlaybackResolution resolution,
            int windowHours,
            string providerMode,
            string providerSource,
            DateTime requestedAtUtc,
            CancellationToken cancellationToken)
        {
            db.CatchupPlaybackAttempts.Add(new CatchupPlaybackAttempt
            {
                SourceProfileId = channelContext.SourceProfileId,
                ChannelId = channelContext.Channel.Id,
                LogicalContentKey = string.IsNullOrWhiteSpace(resolution.LogicalContentKey)
                    ? channelContext.Channel.NormalizedIdentityKey
                    : resolution.LogicalContentKey,
                RequestKind = resolution.RequestKind,
                Status = resolution.Status,
                RequestedAtUtc = requestedAtUtc,
                ProgramTitle = Trim(resolution.ProgramTitle, 220),
                ProgramStartTimeUtc = resolution.ProgramStartTimeUtc,
                ProgramEndTimeUtc = resolution.ProgramEndTimeUtc,
                WindowHours = windowHours,
                Message = Trim(resolution.Message, 320),
                RoutingSummary = Trim(resolution.RoutingSummary, 240),
                ResolvedStreamUrl = Trim(resolution.StreamUrl, 1200),
                ProviderMode = Trim(providerMode, 64),
                ProviderSource = Trim(providerSource, 600)
            });

            await db.SaveChangesAsync(cancellationToken);
            await PruneAttemptsAsync(db, channelContext.SourceProfileId, cancellationToken);
        }

        private async Task PruneAttemptsAsync(
            AppDbContext db,
            int sourceProfileId,
            CancellationToken cancellationToken)
        {
            var staleAttempts = await db.CatchupPlaybackAttempts
                .Where(item => item.SourceProfileId == sourceProfileId)
                .OrderByDescending(item => item.RequestedAtUtc)
                .ThenByDescending(item => item.Id)
                .Skip(MaxAttemptsPerSource)
                .ToListAsync(cancellationToken);
            if (staleAttempts.Count == 0)
            {
                return;
            }

            db.CatchupPlaybackAttempts.RemoveRange(staleAttempts);
            await db.SaveChangesAsync(cancellationToken);
        }

        private static CatchupResolutionStatus MapAvailabilityToResolutionStatus(CatchupAvailabilityState state) => state switch
        {
            CatchupAvailabilityState.Unsupported => CatchupResolutionStatus.Unsupported,
            CatchupAvailabilityState.MissingWindow => CatchupResolutionStatus.MissingWindow,
            CatchupAvailabilityState.Expired => CatchupResolutionStatus.Expired,
            CatchupAvailabilityState.MissingTemplate => CatchupResolutionStatus.MissingTemplate,
            CatchupAvailabilityState.Future => CatchupResolutionStatus.ProgramMissing,
            _ => CatchupResolutionStatus.Failed
        };

        private static (CatchupResolutionStatus Status, string Message, string StreamUrl) BuildXtreamCatchupUrl(
            SourceCredential? credential,
            Channel channel,
            CatchupPlaybackResolution resolution,
            DateTime nowUtc)
        {
            if (credential == null ||
                string.IsNullOrWhiteSpace(credential.Url) ||
                string.IsNullOrWhiteSpace(credential.Username) ||
                string.IsNullOrWhiteSpace(credential.Password))
            {
                return (CatchupResolutionStatus.MissingCredential, "Xtream catchup requires source credentials.", string.Empty);
            }

            var streamId = ExtractXtreamStreamId(resolution.LiveStreamUrl);
            if (string.IsNullOrWhiteSpace(streamId))
            {
                return (CatchupResolutionStatus.InvalidStream, "The Xtream catchup stream id could not be derived from the live URL.", string.Empty);
            }

            var archiveBaseUrl = ResolveXtreamArchiveBaseUrl(credential.Url, channel.ProviderCatchupSource);
            if (string.IsNullOrWhiteSpace(archiveBaseUrl))
            {
                return (CatchupResolutionStatus.MissingCredential, "The Xtream archive host could not be resolved.", string.Empty);
            }

            var startUtc = resolution.ProgramStartTimeUtc!.Value;
            var endUtc = ResolveEffectiveEndUtc(resolution.ProgramStartTimeUtc.Value, resolution.ProgramEndTimeUtc!.Value, nowUtc);
            var durationMinutes = Math.Max(1, (int)Math.Ceiling((endUtc - startUtc).TotalMinutes));
            var startText = startUtc.ToString("yyyy-MM-dd:HH-mm", CultureInfo.InvariantCulture);
            var url = $"{archiveBaseUrl}/timeshift/{Uri.EscapeDataString(credential.Username)}/{Uri.EscapeDataString(credential.Password)}/{durationMinutes}/{startText}/{streamId}";
            var message = resolution.RequestKind == CatchupRequestKind.StartOver
                ? "Start over resolved through the provider archive."
                : "Catchup playback resolved through the provider archive.";
            return (CatchupResolutionStatus.Resolved, message, url);
        }

        private static (CatchupResolutionStatus Status, string Message, string StreamUrl) BuildM3uCatchupUrl(
            Channel channel,
            CatchupPlaybackResolution resolution,
            DateTime nowUtc)
        {
            var liveStreamUrl = resolution.LiveStreamUrl;
            if (string.IsNullOrWhiteSpace(liveStreamUrl))
            {
                return (CatchupResolutionStatus.InvalidStream, "The base live stream URL is missing.", string.Empty);
            }

            var template = !string.IsNullOrWhiteSpace(channel.ProviderCatchupSource)
                ? channel.ProviderCatchupSource.Trim()
                : liveStreamUrl.Trim();
            if (string.IsNullOrWhiteSpace(template))
            {
                return (CatchupResolutionStatus.MissingTemplate, "The playlist did not expose a catchup template.", string.Empty);
            }

            var startUtc = resolution.ProgramStartTimeUtc!.Value;
            var endUtc = ResolveEffectiveEndUtc(resolution.ProgramStartTimeUtc.Value, resolution.ProgramEndTimeUtc!.Value, nowUtc);
            var duration = endUtc - startUtc;
            if (duration <= TimeSpan.Zero)
            {
                duration = TimeSpan.FromMinutes(1);
            }

            var replaced = ReplaceTokens(template, startUtc, endUtc, duration);
            if (ContainsUnresolvedToken(replaced))
            {
                return (CatchupResolutionStatus.InvalidTemplate, "The catchup template still contains unresolved placeholders.", string.Empty);
            }

            var resolvedUrl = ResolveTemplateUrl(channel.ProviderCatchupMode, liveStreamUrl, replaced);
            if (!IsValidPlaybackUrl(resolvedUrl))
            {
                return (CatchupResolutionStatus.InvalidTemplate, "The provider catchup template produced an invalid playback URL.", string.Empty);
            }

            var message = resolution.RequestKind == CatchupRequestKind.StartOver
                ? "Start over resolved through the provider catchup template."
                : "Catchup playback resolved through the provider catchup template.";
            return (CatchupResolutionStatus.Resolved, message, resolvedUrl);
        }

        private static string ResolveXtreamArchiveBaseUrl(string sourceUrl, string providerSource)
        {
            if (!string.IsNullOrWhiteSpace(providerSource))
            {
                if (Uri.TryCreate(providerSource.Trim(), UriKind.Absolute, out var absolute))
                {
                    return absolute.ToString().TrimEnd('/');
                }

                if (providerSource.StartsWith("/", StringComparison.Ordinal) &&
                    Uri.TryCreate(sourceUrl, UriKind.Absolute, out var baseUri))
                {
                    return $"{baseUri.Scheme}://{baseUri.Authority}{providerSource.TrimEnd('/')}";
                }
            }

            return string.IsNullOrWhiteSpace(sourceUrl)
                ? string.Empty
                : sourceUrl.Trim().TrimEnd('/');
        }

        private static string ResolveTemplateUrl(string providerMode, string liveStreamUrl, string template)
        {
            if (Uri.TryCreate(template, UriKind.Absolute, out var absolute))
            {
                return absolute.ToString();
            }

            if (template.StartsWith("?", StringComparison.Ordinal))
            {
                return AppendQuery(liveStreamUrl, template[1..]);
            }

            if (template.StartsWith("&", StringComparison.Ordinal))
            {
                return AppendQuery(liveStreamUrl, template.TrimStart('&'));
            }

            if (template.StartsWith("/", StringComparison.Ordinal) &&
                Uri.TryCreate(liveStreamUrl, UriKind.Absolute, out var liveRootUri))
            {
                return $"{liveRootUri.Scheme}://{liveRootUri.Authority}{template}";
            }

            if (string.Equals(providerMode, "append", StringComparison.OrdinalIgnoreCase))
            {
                if (template.StartsWith("/", StringComparison.Ordinal) ||
                    template.StartsWith("?", StringComparison.Ordinal) ||
                    template.StartsWith("&", StringComparison.Ordinal))
                {
                    return $"{liveStreamUrl}{template}";
                }

                return $"{liveStreamUrl.TrimEnd('/')}/{template.TrimStart('/')}";
            }

            if (Uri.TryCreate(liveStreamUrl, UriKind.Absolute, out var liveUri))
            {
                return new Uri(liveUri, template).ToString();
            }

            return $"{liveStreamUrl}{template}";
        }

        private static string AppendQuery(string liveStreamUrl, string queryText)
        {
            if (string.IsNullOrWhiteSpace(queryText))
            {
                return liveStreamUrl;
            }

            var separator = liveStreamUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return $"{liveStreamUrl}{separator}{queryText.TrimStart('?', '&')}";
        }

        private static string ReplaceTokens(string template, DateTime startUtc, DateTime endUtc, TimeSpan duration)
        {
            var startUnix = new DateTimeOffset(startUtc).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var endUnix = new DateTimeOffset(endUtc).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var durationMinutes = Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes)).ToString(CultureInfo.InvariantCulture);
            var durationSeconds = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            var startText = startUtc.ToString("yyyy-MM-dd:HH-mm", CultureInfo.InvariantCulture);
            var endText = endUtc.ToString("yyyy-MM-dd:HH-mm", CultureInfo.InvariantCulture);
            var localStartText = startUtc.ToLocalTime().ToString("yyyy-MM-dd:HH-mm", CultureInfo.InvariantCulture);
            var localEndText = endUtc.ToLocalTime().ToString("yyyy-MM-dd:HH-mm", CultureInfo.InvariantCulture);

            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["{utc}"] = startText,
                ["{utcend}"] = endText,
                ["{lutc}"] = localStartText,
                ["{lutcend}"] = localEndText,
                ["{start}"] = startUnix,
                ["{timestamp}"] = startUnix,
                ["{end}"] = endUnix,
                ["{duration}"] = durationMinutes,
                ["{duration_minutes}"] = durationMinutes,
                ["{duration_seconds}"] = durationSeconds,
                ["${start}"] = startUnix,
                ["${end}"] = endUnix,
                ["${duration}"] = durationMinutes
            };

            var resolved = template;
            foreach (var replacement in replacements)
            {
                resolved = resolved.Replace(replacement.Key, replacement.Value, StringComparison.OrdinalIgnoreCase);
            }

            return resolved;
        }

        private static DateTime ResolveEffectiveEndUtc(DateTime startUtc, DateTime endUtc, DateTime nowUtc)
        {
            var normalizedStartUtc = NormalizeUtc(startUtc);
            var normalizedEndUtc = NormalizeUtc(endUtc);
            var normalizedNowUtc = NormalizeUtc(nowUtc);
            if (normalizedEndUtc > normalizedNowUtc && normalizedStartUtc <= normalizedNowUtc)
            {
                return normalizedNowUtc;
            }

            return normalizedEndUtc;
        }

        private static string ExtractXtreamStreamId(string liveStreamUrl)
        {
            if (string.IsNullOrWhiteSpace(liveStreamUrl))
            {
                return string.Empty;
            }

            var candidate = liveStreamUrl;
            if (Uri.TryCreate(liveStreamUrl, UriKind.Absolute, out var uri))
            {
                candidate = uri.AbsolutePath;
            }

            var lastSegment = candidate
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(lastSegment))
            {
                return string.Empty;
            }

            var dotIndex = lastSegment.IndexOf('.');
            return dotIndex > 0 ? lastSegment[..dotIndex] : lastSegment;
        }

        private static bool HasReplayTemplate(Channel channel, SourceType sourceType)
        {
            if (!channel.SupportsCatchup)
            {
                return false;
            }

            if (sourceType == SourceType.Xtream)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(channel.ProviderCatchupSource) ||
                   ContainsCatchupHint(channel.ProviderCatchupSource) ||
                   ContainsCatchupHint(channel.StreamUrl);
        }

        private static bool ContainsCatchupHint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim().ToLowerInvariant();
            return normalized.Contains("catchup", StringComparison.Ordinal) ||
                   normalized.Contains("archive", StringComparison.Ordinal) ||
                   normalized.Contains("timeshift", StringComparison.Ordinal) ||
                   normalized.Contains("{utc", StringComparison.Ordinal) ||
                   normalized.Contains("${start", StringComparison.Ordinal) ||
                   normalized.Contains("?utc=", StringComparison.Ordinal);
        }

        private static bool ContainsUnresolvedToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Contains('{', StringComparison.Ordinal) || value.Contains('}', StringComparison.Ordinal);
        }

        private static bool IsValidPlaybackUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                   (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }

        private static CatchupProgramAvailability Unavailable(CatchupAvailabilityState state, string message)
        {
            return new CatchupProgramAvailability
            {
                State = state,
                Message = message,
                ActionLabel = string.Empty,
                RequestKind = CatchupRequestKind.None
            };
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static DateTime? NormalizeNullableUtc(DateTime? value)
        {
            return value.HasValue ? NormalizeUtc(value.Value) : null;
        }

        private static string FormatWindow(int hours)
        {
            if (hours >= 48 && hours % 24 == 0)
            {
                return $"{hours / 24} day{(hours == 48 ? string.Empty : "s")}";
            }

            return $"{hours} hour{(hours == 1 ? string.Empty : "s")}";
        }

        private static string Trim(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..(maxLength - 3)] + "...";
        }

        private sealed record ResolvedChannelContext(
            Channel Channel,
            int SourceProfileId,
            SourceType SourceType);
    }
}
