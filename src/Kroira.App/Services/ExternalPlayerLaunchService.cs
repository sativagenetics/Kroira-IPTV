#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
using Microsoft.Extensions.DependencyInjection;
using Windows.System;

namespace Kroira.App.Services
{
    public interface IExternalUriLauncher
    {
        Task<bool> LaunchAsync(Uri uri, bool showApplicationPicker);
    }

    public interface IExternalPlayerLaunchService
    {
        Task<ExternalPlayerLaunchResult> LaunchAsync(
            PlaybackLaunchContext context,
            bool preferCurrentResolvedStream = false,
            CancellationToken cancellationToken = default);
    }

    public sealed class SystemExternalUriLauncher : IExternalUriLauncher
    {
        public async Task<bool> LaunchAsync(Uri uri, bool showApplicationPicker)
        {
            var options = new LauncherOptions
            {
                DisplayApplicationPicker = showApplicationPicker
            };

            return await Launcher.LaunchUriAsync(uri, options);
        }
    }

    public sealed class ExternalPlayerLaunchService : IExternalPlayerLaunchService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IExternalUriLauncher _externalUriLauncher;
        private readonly ISensitiveDataRedactionService _redactionService;

        public ExternalPlayerLaunchService(
            IServiceScopeFactory scopeFactory,
            IExternalUriLauncher externalUriLauncher,
            ISensitiveDataRedactionService redactionService)
        {
            _scopeFactory = scopeFactory;
            _externalUriLauncher = externalUriLauncher;
            _redactionService = redactionService;
        }

        public async Task<ExternalPlayerLaunchResult> LaunchAsync(
            PlaybackLaunchContext context,
            bool preferCurrentResolvedStream = false,
            CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                return Failed("Playback context is missing.");
            }

            var workingContext = context.Clone();
            if (string.IsNullOrWhiteSpace(workingContext.CatalogStreamUrl) &&
                !string.IsNullOrWhiteSpace(workingContext.StreamUrl))
            {
                workingContext.CatalogStreamUrl = workingContext.StreamUrl;
            }

            if (preferCurrentResolvedStream &&
                CanUseCurrentResolvedStream(workingContext) &&
                TryCreateLaunchUri(workingContext.StreamUrl, out var currentResolvedUri))
            {
                return await LaunchResolvedUriAsync(
                    currentResolvedUri,
                    workingContext.ProviderSummary,
                    workingContext.RoutingSummary,
                    workingContext.StreamUrl);
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var contentOperationalService = scope.ServiceProvider.GetRequiredService<IContentOperationalService>();
            var providerStreamResolverService = scope.ServiceProvider.GetRequiredService<IProviderStreamResolverService>();
            var catchupPlaybackService = scope.ServiceProvider.GetRequiredService<ICatchupPlaybackService>();

            if (IsCatchupRequested(workingContext))
            {
                var catchupResolution = await catchupPlaybackService.ResolveForContextAsync(db, workingContext, cancellationToken);
                if (!catchupResolution.Success)
                {
                    return Failed(catchupResolution.Message);
                }

                if (!TryCreateLaunchUri(catchupResolution.StreamUrl, out var catchupUri))
                {
                    return Failed("Catchup resolved, but the replay stream is not launchable in an external player.");
                }

                return await LaunchResolvedUriAsync(
                    catchupUri,
                    "Catchup replay URL",
                    catchupResolution.RoutingSummary,
                    catchupResolution.StreamUrl);
            }

            await contentOperationalService.ResolvePlaybackContextAsync(db, workingContext);
            var providerResolution = await providerStreamResolverService.ResolvePlaybackContextAsync(
                db,
                workingContext,
                SourceNetworkPurpose.Playback,
                cancellationToken);
            if (!providerResolution.Success || string.IsNullOrWhiteSpace(workingContext.StreamUrl))
            {
                return Failed(providerResolution.Message);
            }

            if (!TryCreateLaunchUri(workingContext.StreamUrl, out var resolvedUri))
            {
                return Failed("The resolved stream is not launchable in an external player.");
            }

            return await LaunchResolvedUriAsync(
                resolvedUri,
                providerResolution.ProviderSummary,
                workingContext.RoutingSummary,
                workingContext.StreamUrl);
        }

        private async Task<ExternalPlayerLaunchResult> LaunchResolvedUriAsync(
            Uri uri,
            string providerSummary,
            string routingSummary,
            string resolvedStreamUrl)
        {
            var launched = await _externalUriLauncher.LaunchAsync(uri, showApplicationPicker: true);
            if (!launched)
            {
                return Failed("Windows did not hand this stream to an external player. Choose a handler or install one that can open stream URLs.");
            }

            return new ExternalPlayerLaunchResult
            {
                Success = true,
                Message = _redactionService.RedactLooseText("Opened the resolved stream in an external player picker."),
                ProviderSummary = _redactionService.RedactLooseText(providerSummary),
                RoutingSummary = _redactionService.RedactLooseText(routingSummary),
                ResolvedUrlText = _redactionService.RedactUrl(resolvedStreamUrl),
                UsedApplicationPicker = true
            };
        }

        private static bool CanUseCurrentResolvedStream(PlaybackLaunchContext context)
        {
            if (string.IsNullOrWhiteSpace(context.StreamUrl) ||
                StalkerLocatorCodec.TryParse(context.StreamUrl, out _))
            {
                return false;
            }

            if (context.PlaybackMode == CatchupPlaybackMode.Catchup)
            {
                return context.CatchupResolutionStatus == CatchupResolutionStatus.Resolved;
            }

            return true;
        }

        private static bool TryCreateLaunchUri(string? value, out Uri uri)
        {
            return Uri.TryCreate(value?.Trim(), UriKind.Absolute, out uri!);
        }

        private static bool IsCatchupRequested(PlaybackLaunchContext context)
        {
            return context.ContentType == PlaybackContentType.Channel &&
                   context.CatchupRequestKind != CatchupRequestKind.None &&
                   context.CatchupProgramStartTimeUtc.HasValue &&
                   context.CatchupProgramEndTimeUtc.HasValue &&
                   context.CatchupResolutionStatus != CatchupResolutionStatus.Resolved;
        }

        private ExternalPlayerLaunchResult Failed(string? message)
        {
            return new ExternalPlayerLaunchResult
            {
                Success = false,
                Message = _redactionService.RedactLooseText(
                    string.IsNullOrWhiteSpace(message)
                        ? "External player launch failed."
                        : message.Trim())
            };
        }
    }
}
