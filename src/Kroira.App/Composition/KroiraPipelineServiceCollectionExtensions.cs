using Kroira.App.Services;
using Kroira.App.Services.Parsing;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.Composition
{
    public static class KroiraPipelineServiceCollectionExtensions
    {
        public static IServiceCollection AddKroiraPipelineServices(this IServiceCollection services)
        {
            services.AddSingleton<IBrowsePreferencesService, BrowsePreferencesService>();
            services.AddSingleton<ILiveChannelIdentityService, LiveChannelIdentityService>();
            services.AddSingleton<IChannelCatchupService, ChannelCatchupService>();
            services.AddSingleton<ICatchupPlaybackService, CatchupPlaybackService>();
            services.AddSingleton<ILogicalCatalogStateService, LogicalCatalogStateService>();
            services.AddSingleton<ISourceRoutingService, SourceRoutingService>();
            services.AddSingleton<ICompanionRelayService, CompanionRelayService>();
            services.AddSingleton<ISensitiveDataRedactionService, SensitiveDataRedactionService>();
            services.AddSingleton<ISourceAcquisitionService, SourceAcquisitionService>();
            services.AddSingleton<IContentOperationalService, ContentOperationalService>();
            services.AddSingleton<IProviderStreamResolverService, ProviderStreamResolverService>();
            services.AddSingleton<IPlayableItemInspectionService, PlayableItemInspectionService>();
            services.AddSingleton<IExternalUriLauncher, SystemExternalUriLauncher>();
            services.AddSingleton<IExternalPlayerLaunchService, ExternalPlayerLaunchService>();
            services.AddSingleton<ISourceDiagnosticsService, SourceDiagnosticsService>();
            services.AddSingleton<ISourceEnrichmentService, SourceEnrichmentService>();
            services.AddSingleton<ISourceHealthService, SourceHealthService>();
            services.AddSingleton<ISourceProbeService, SourceProbeService>();
            services.AddSingleton<ISourceLifecycleService, SourceLifecycleService>();
            services.AddSingleton<ISourceRefreshService, SourceRefreshService>();
            services.AddSingleton<ISourceAutoRefreshService, SourceAutoRefreshService>();
            services.AddSingleton<IRuntimeMaintenanceService, RuntimeMaintenanceService>();
            services.AddSingleton<ICatalogNormalizationService, CatalogNormalizationService>();
            services.AddSingleton<IStalkerPortalClient, StalkerPortalClient>();
            services.AddSingleton<IM3uParserService, M3uParserService>();
            services.AddSingleton<IEpgSourceDiscoveryService, M3uEpgDiscoveryService>();
            services.AddSingleton<IEpgSourceDiscoveryService, XtreamEpgDiscoveryService>();
            services.AddSingleton<IEpgSourceDiscoveryService, StalkerEpgDiscoveryService>();
            services.AddSingleton<IXmltvParserService, XmltvParserService>();
            services.AddSingleton<IXtreamParserService, XtreamParserService>();
            services.AddSingleton<IStalkerParserService, StalkerParserService>();
            return services;
        }
    }
}
