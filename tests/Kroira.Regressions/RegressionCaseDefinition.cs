using System.Text.Json;
using System.Text.Json.Serialization;
using Kroira.App.Models;

namespace Kroira.Regressions;

internal sealed class RegressionCaseDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<RegressionSourceDefinition> Sources { get; set; } = [];
    public List<RegressionDiscoveryRequestDefinition> DiscoveryRequests { get; set; } = [];
    public List<RegressionPlaybackRequestDefinition> PlaybackRequests { get; set; } = [];
    public List<RegressionCatchupRequestDefinition> CatchupRequests { get; set; } = [];
    public List<RegressionInspectionRequestDefinition> InspectionRequests { get; set; } = [];
    public List<RegressionExternalLaunchRequestDefinition> ExternalLaunchRequests { get; set; } = [];
    public List<RegressionSetupValidationRequestDefinition> SetupValidationRequests { get; set; } = [];
    public List<RegressionSourceRepairRequestDefinition> RepairRequests { get; set; } = [];
    public List<RegressionSourceRepairActionRequestDefinition> RepairActionRequests { get; set; } = [];
    public List<RegressionSourceActivityRequestDefinition> ActivityRequests { get; set; } = [];
    public RegressionRemoteNavigationDefinition? RemoteNavigation { get; set; }
    public List<RegressionPlaybackRemoteCommandRequestDefinition> PlaybackRemoteCommands { get; set; } = [];
    public List<RegressionMutationDefinition> Mutations { get; set; } = [];
    public RegressionRuntimeMaintenanceDefinition? RuntimeMaintenance { get; set; }
    public RegressionSurfaceLoadDefinition? SurfaceLoads { get; set; }
}

internal sealed class RegressionPlaybackRequestDefinition
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public PlaybackContentType ContentType { get; set; } = PlaybackContentType.Channel;
    public string MatchName { get; set; } = string.Empty;
}

internal sealed class RegressionDiscoveryRequestDefinition
{
    public string Id { get; set; } = string.Empty;
    public CatalogDiscoveryDomain Domain { get; set; } = CatalogDiscoveryDomain.Live;
    public string NowUtc { get; set; } = string.Empty;
    public RegressionDiscoverySelectionDefinition Selection { get; set; } = new();
    public List<RegressionDiscoveryRecordDefinition> Records { get; set; } = [];
}

internal sealed class RegressionDiscoverySelectionDefinition
{
    public string SignalKey { get; set; } = string.Empty;
    public string SourceTypeKey { get; set; } = string.Empty;
    public string LanguageKey { get; set; } = string.Empty;
    public string TagKey { get; set; } = string.Empty;
}

internal sealed class RegressionDiscoveryRecordDefinition
{
    public string Key { get; set; } = string.Empty;
    public List<int> SourceProfileIds { get; set; } = [];
    public List<SourceType> SourceTypes { get; set; } = [];
    public string Language { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
    public bool HasGuide { get; set; }
    public bool HasCatchup { get; set; }
    public bool HasArtwork { get; set; }
    public bool HasPlayableChildren { get; set; }
    public CatalogDiscoveryHealthBucket HealthBucket { get; set; } = CatalogDiscoveryHealthBucket.Unknown;
    public string LastSyncUtc { get; set; } = string.Empty;
    public string LastInteractionUtc { get; set; } = string.Empty;
}

internal sealed class RegressionCatchupRequestDefinition
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string MatchName { get; set; } = string.Empty;
    public CatchupRequestKind RequestKind { get; set; } = CatchupRequestKind.None;
    public string ProgramTitle { get; set; } = string.Empty;
    public string RequestedAtUtc { get; set; } = string.Empty;
}

internal sealed class RegressionInspectionRequestDefinition
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public PlaybackContentType ContentType { get; set; } = PlaybackContentType.Channel;
    public string MatchName { get; set; } = string.Empty;
    public bool ResolveBeforeInspect { get; set; }
    public CatchupRequestKind CatchupRequestKind { get; set; } = CatchupRequestKind.None;
    public string ProgramTitle { get; set; } = string.Empty;
    public string RequestedAtUtc { get; set; } = string.Empty;
    public RegressionInspectionRuntimeStateDefinition? RuntimeState { get; set; }
}

internal sealed class RegressionExternalLaunchRequestDefinition
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public PlaybackContentType ContentType { get; set; } = PlaybackContentType.Channel;
    public string MatchName { get; set; } = string.Empty;
    public bool ResolveBeforeLaunch { get; set; }
    public bool PreferCurrentResolvedStream { get; set; }
    public CatchupRequestKind CatchupRequestKind { get; set; } = CatchupRequestKind.None;
    public string ProgramTitle { get; set; } = string.Empty;
    public string RequestedAtUtc { get; set; } = string.Empty;
}

internal sealed class RegressionInspectionRuntimeStateDefinition
{
    public bool IsCurrentPlayback { get; set; }
    public string SessionState { get; set; } = string.Empty;
    public string SessionMessage { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double FramesPerSecond { get; set; }
    public string VideoCodec { get; set; } = string.Empty;
    public string AudioCodec { get; set; } = string.Empty;
    public string ContainerFormat { get; set; } = string.Empty;
    public string PixelFormat { get; set; } = string.Empty;
    public bool IsHardwareDecodingActive { get; set; }
    public long PositionMs { get; set; }
    public long DurationMs { get; set; }
    public bool IsSeekable { get; set; }
}

internal sealed class RegressionSourceActivityRequestDefinition
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
}

internal sealed class RegressionSetupValidationRequestDefinition
{
    public string Id { get; set; } = string.Empty;
    public SourceType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ManualEpgUrl { get; set; } = string.Empty;
    public EpgActiveMode EpgMode { get; set; } = EpgActiveMode.Detected;
    public SourceProxyScope ProxyScope { get; set; } = SourceProxyScope.Disabled;
    public string ProxyUrl { get; set; } = string.Empty;
    public SourceCompanionScope CompanionScope { get; set; } = SourceCompanionScope.Disabled;
    public SourceCompanionRelayMode CompanionMode { get; set; } = SourceCompanionRelayMode.Buffered;
    public string CompanionUrl { get; set; } = string.Empty;
    public string StalkerMacAddress { get; set; } = string.Empty;
    public string StalkerDeviceId { get; set; } = string.Empty;
    public string StalkerSerialNumber { get; set; } = string.Empty;
    public string StalkerTimezone { get; set; } = string.Empty;
    public string StalkerLocale { get; set; } = string.Empty;
}

internal sealed class RegressionSourceRepairRequestDefinition
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
}

internal sealed class RegressionSourceRepairActionRequestDefinition
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public SourceRepairActionType ActionType { get; set; } = SourceRepairActionType.RetestSource;
}

internal sealed class RegressionRemoteNavigationDefinition
{
    public bool? UpdatedEnabled { get; set; }
}

internal sealed class RegressionPlaybackRemoteCommandRequestDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public bool ShiftDown { get; set; }
    public RegressionPlaybackRemoteContextDefinition Context { get; set; } = new();
}

internal sealed class RegressionPlaybackRemoteContextDefinition
{
    public bool IsTextInputFocused { get; set; }
    public bool IsMenuOpen { get; set; }
    public bool ReserveFocusedControlKeys { get; set; }
    public bool IsPictureInPicture { get; set; }
    public bool IsLivePlayback { get; set; }
    public bool IsChannelPlayback { get; set; }
    public bool CanSeek { get; set; }
    public bool HasLastChannel { get; set; }
    public bool CanRestartOrStartOver { get; set; }
    public bool CanGoLive { get; set; }
}

internal sealed class RegressionSourceDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SourceType Type { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public EpgActiveMode EpgMode { get; set; } = EpgActiveMode.Detected;
    public string ManualEpgUrl { get; set; } = string.Empty;
    public M3uImportMode M3uImportMode { get; set; } = M3uImportMode.LiveMoviesAndSeries;
    public SourceProxyScope ProxyScope { get; set; } = SourceProxyScope.Disabled;
    public string ProxyUrl { get; set; } = string.Empty;
    public SourceCompanionScope CompanionScope { get; set; } = SourceCompanionScope.Disabled;
    public SourceCompanionRelayMode CompanionMode { get; set; } = SourceCompanionRelayMode.Buffered;
    public string CompanionUrl { get; set; } = string.Empty;
    public string StalkerMacAddress { get; set; } = string.Empty;
    public string StalkerDeviceId { get; set; } = string.Empty;
    public string StalkerSerialNumber { get; set; } = string.Empty;
    public string StalkerTimezone { get; set; } = string.Empty;
    public string StalkerLocale { get; set; } = string.Empty;
    public SourceRefreshTrigger RefreshTrigger { get; set; } = SourceRefreshTrigger.InitialImport;
    public SourceRefreshScope RefreshScope { get; set; } = SourceRefreshScope.Full;
}

internal sealed class RegressionMutationDefinition
{
    public string Kind { get; set; } = string.Empty;
    public PlaybackContentType ContentType { get; set; } = PlaybackContentType.Channel;
    public string SourceKey { get; set; } = string.Empty;
    public string MatchName { get; set; } = string.Empty;
    public string LogicalContentKey { get; set; } = string.Empty;
    public string PreferredSourceKey { get; set; } = string.Empty;
    public EpgActiveMode ActiveMode { get; set; } = EpgActiveMode.Detected;
    public string ManualEpgUrl { get; set; } = string.Empty;
    public SourceProxyScope ProxyScope { get; set; } = SourceProxyScope.Disabled;
    public string ProxyUrl { get; set; } = string.Empty;
    public SourceCompanionScope CompanionScope { get; set; } = SourceCompanionScope.Disabled;
    public SourceCompanionRelayMode CompanionMode { get; set; } = SourceCompanionRelayMode.Buffered;
    public string CompanionUrl { get; set; } = string.Empty;
    public bool SyncNow { get; set; }
    public int ContentIdOverride { get; set; }
    public int PreferredSourceProfileIdOverride { get; set; }
    public long PositionMs { get; set; }
    public long DurationMs { get; set; }
    public bool IsCompleted { get; set; }
    public WatchStateOverride WatchStateOverride { get; set; } = WatchStateOverride.None;
    public string LastWatchedUtc { get; set; } = string.Empty;
}

internal sealed class RegressionSurfaceLoadDefinition
{
    public bool Home { get; set; }
    public bool LiveTv { get; set; }
    public bool ContinueWatching { get; set; }
}

internal sealed class RegressionRuntimeMaintenanceDefinition
{
    public bool Startup { get; set; }
    public bool Deferred { get; set; }
}

internal sealed class FixtureServerManifest
{
    public List<FixtureServerRouteDefinition> Routes { get; set; } = [];
}

internal sealed class FixtureServerRouteDefinition
{
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> Query { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int StatusCode { get; set; } = 200;
    public int DelayMs { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string BodyFile { get; set; } = string.Empty;
}

internal sealed class RegressionRunnerOptions
{
    public string CaseId { get; init; } = string.Empty;
    public bool UpdateBaselines { get; init; }
    public bool ListOnly { get; init; }
    public bool ShowHelp { get; init; }

    public static RegressionRunnerOptions Parse(string[] args)
    {
        var caseId = string.Empty;
        var update = false;
        var list = false;
        var help = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--case" when index + 1 < args.Length:
                    caseId = args[++index].Trim();
                    break;
                case "--update":
                    update = true;
                    break;
                case "--list":
                    list = true;
                    break;
                case "--help":
                case "-h":
                case "/?":
                    help = true;
                    break;
            }
        }

        return new RegressionRunnerOptions
        {
            CaseId = caseId,
            UpdateBaselines = update,
            ListOnly = list,
            ShowHelp = help
        };
    }
}

internal static class RegressionJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
