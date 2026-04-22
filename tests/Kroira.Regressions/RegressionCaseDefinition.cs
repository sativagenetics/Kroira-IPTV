using System.Text.Json;
using System.Text.Json.Serialization;
using Kroira.App.Models;

namespace Kroira.Regressions;

internal sealed class RegressionCaseDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<RegressionSourceDefinition> Sources { get; set; } = [];
    public List<RegressionMutationDefinition> Mutations { get; set; } = [];
    public RegressionRuntimeMaintenanceDefinition? RuntimeMaintenance { get; set; }
    public RegressionSurfaceLoadDefinition? SurfaceLoads { get; set; }
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
