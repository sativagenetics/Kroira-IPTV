using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Controls;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kroira.App.ViewModels
{
    public partial class SourceItemViewModel : ObservableObject
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string SourceKindText { get; set; } = string.Empty;
        public string HealthBadgeText { get; set; } = string.Empty;
        public string GuideBadgeText { get; set; } = string.Empty;
        public string PrimarySyncText { get; set; } = "Sync Now";
        public string SourcePanelSummaryText { get; set; } = string.Empty;
        public string ConnectionLabelText { get; set; } = string.Empty;
        public StatusPillKind HealthPillKind { get; set; } = StatusPillKind.Neutral;
        public StatusPillKind GuidePillKind { get; set; } = StatusPillKind.Neutral;
        public string LastSyncText { get; set; } = "Never";
        public string LastAttemptText { get; set; } = "Never";
        public int ChannelCount { get; set; }
        public int MovieCount { get; set; }
        public int SeriesCount { get; set; }
        public int DuplicateCount { get; set; }
        public int InvalidStreamCount { get; set; }
        public int ChannelsWithLogoCount { get; set; }
        public int SuspiciousEntryCount { get; set; }
        public int HealthScore { get; set; }
        public string ImportResultText { get; set; } = string.Empty;
        public string ValidationResultText { get; set; } = string.Empty;
        public string EpgCoverageText { get; set; } = string.Empty;
        public string ParseWarningsText { get; set; } = string.Empty;
        public string NetworkFailureText { get; set; } = string.Empty;
        public string LastSuccessfulSyncText { get; set; } = string.Empty;
        public string GuideStatusText { get; set; } = string.Empty;
        public string GuideStatusSummaryText { get; set; } = string.Empty;
        public string GuideModeText { get; set; } = string.Empty;
        public string GuideUrlText { get; set; } = string.Empty;
        public string GuideMatchText { get; set; } = string.Empty;
        public string AutoRefreshStatusText { get; set; } = string.Empty;
        public string AutoRefreshSummaryText { get; set; } = string.Empty;
        public string NextAutoRefreshText { get; set; } = string.Empty;
        public string LastAutoRefreshText { get; set; } = string.Empty;
        public string OperationalStatusText { get; set; } = string.Empty;
        public string ProxyStatusText { get; set; } = "Direct routing";
        public IReadOnlyList<SourceHealthComponentItemViewModel> HealthComponents { get; set; } = Array.Empty<SourceHealthComponentItemViewModel>();
        public IReadOnlyList<SourceIssueItemViewModel> HealthIssues { get; set; } = Array.Empty<SourceIssueItemViewModel>();
        public int ImportWarningCount { get; set; }
        public int GuideWarningCount { get; set; }

        public bool HasEpgUrl { get; set; }
        public bool CanSyncEpg { get; set; }
        public bool HasEpgData { get; set; }
        public string EpgLastSyncText { get; set; } = string.Empty;
        public int EpgMatchedChannels { get; set; }
        public int EpgProgramCount { get; set; }
        public string EpgSummaryText { get; set; } = string.Empty;
        public bool EpgSyncSuccess { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EpgSyncButtonText))]
        [NotifyPropertyChangedFor(nameof(IsEpgSyncEnabled))]
        private bool _isEpgSyncing;

        public string EpgSyncButtonText => IsEpgSyncing ? "Syncing..." : "EPG";
        public bool IsEpgSyncEnabled => CanSyncEpg && !IsEpgSyncing;
        public bool CanRunXtreamVodOnly => Type == "Xtream";

        [ObservableProperty]
        private string _status = string.Empty;

        [ObservableProperty]
        private string _healthLabel = "Saved";

        public Microsoft.UI.Xaml.Visibility ParseVisibility => Type == "M3U"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility SyncEpgVisibility => CanSyncEpg
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility SyncXtreamVisibility => Type == "Xtream"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility BrowseVisibility => (Type == "M3U" || Type == "Xtream")
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility PrimarySyncVisibility => (Type == "M3U" || Type == "Xtream")
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility XtreamVodOnlyVisibility => Type == "Xtream"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility HealthyVisibility => HealthLabel is "Healthy" or "Ready"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility DegradedVisibility => HealthLabel == "Degraded"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility AttentionVisibility => HealthLabel is "Attention" or "Failing"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility WorkingVisibility => HealthLabel == "Working"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility IdleVisibility => HealthLabel is "Saved" or "Not synced"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility EpgHealthVisibility => CanSyncEpg
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility EpgConfiguredVisibility => HasEpgUrl && !HasEpgData
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility GuideUrlVisibility => string.IsNullOrWhiteSpace(GuideUrlText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility GuideMatchVisibility => string.IsNullOrWhiteSpace(GuideMatchText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility GuideStatusSummaryVisibility => string.IsNullOrWhiteSpace(GuideStatusSummaryText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility AutoRefreshSummaryVisibility => string.IsNullOrWhiteSpace(AutoRefreshSummaryText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility OperationalStatusVisibility => string.IsNullOrWhiteSpace(OperationalStatusText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility ProxyStatusVisibility => string.IsNullOrWhiteSpace(ProxyStatusText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility ParseWarningsVisibility => string.IsNullOrWhiteSpace(ParseWarningsText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility NetworkFailureVisibility => string.IsNullOrWhiteSpace(NetworkFailureText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public int CatalogAssetCount => MovieCount + SeriesCount;

        public double GuideCoveragePercent => ChannelCount > 0
            ? Math.Min(100d, Math.Round((double)EpgMatchedChannels / ChannelCount * 100d, 1))
            : 0d;

        public string GuideCoverageRatioText => ChannelCount > 0
            ? $"{EpgMatchedChannels:N0} / {ChannelCount:N0} mapped"
            : "No live channels";

        public string GuideCoverageSecondaryText => ChannelCount > 0
            ? $"{Math.Max(ChannelCount - EpgMatchedChannels, 0):N0} unmatched"
            : GuideStatusText;

        public string OperationalSummaryText => string.IsNullOrWhiteSpace(SourcePanelSummaryText)
            ? Status
            : SourcePanelSummaryText;

        public string QualitySnapshotText => $"Duplicates {DuplicateCount:N0}, invalid {InvalidStreamCount:N0}, suspicious {SuspiciousEntryCount:N0}";

        public string LogoCoverageText => ChannelCount > 0
            ? $"{ChannelsWithLogoCount:N0} / {ChannelCount:N0} live channels with logos"
            : "No live channels available";

        public string ValidationScoreText => HealthScore > 0
            ? $"{HealthScore}/100 confidence"
            : LastSyncText == "Never"
                ? "Validation pending"
                : "0/100 confidence";

        public Microsoft.UI.Xaml.Visibility GuideCoverageVisibility => ChannelCount > 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility ValidationVisibility => string.IsNullOrWhiteSpace(ValidationResultText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility HealthComponentsVisibility => HealthComponents.Count > 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility HealthIssuesVisibility => HealthIssues.Count > 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        partial void OnHealthLabelChanged(string value)
        {
            OnPropertyChanged(nameof(HealthyVisibility));
            OnPropertyChanged(nameof(DegradedVisibility));
            OnPropertyChanged(nameof(AttentionVisibility));
            OnPropertyChanged(nameof(WorkingVisibility));
            OnPropertyChanged(nameof(IdleVisibility));
        }
    }

    public sealed class SourceIssueItemViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public StatusPillKind SeverityKind { get; set; } = StatusPillKind.Info;
    }

    public sealed class SourceHealthComponentItemViewModel
    {
        public string Label { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string PillLabel { get; set; } = string.Empty;
        public StatusPillKind PillKind { get; set; } = StatusPillKind.Standby;
    }

    public sealed class SourceRecentActivityItemViewModel
    {
        public string TimestampText { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public string PayloadText { get; set; } = string.Empty;
        public StatusPillKind StatusKind { get; set; } = StatusPillKind.Neutral;
    }

    public sealed class SourceGuideSettingsDraft
    {
        public int SourceId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public SourceType SourceType { get; set; }
        public EpgActiveMode ActiveMode { get; set; } = EpgActiveMode.Detected;
        public string ManualEpgUrl { get; set; } = string.Empty;
        public string DetectedEpgUrl { get; set; } = string.Empty;
        public SourceProxyScope ProxyScope { get; set; } = SourceProxyScope.Disabled;
        public string ProxyUrl { get; set; } = string.Empty;
    }

    public partial class SourceListViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private List<SourceItemViewModel> _allSources = new();

        public ObservableCollection<SourceItemViewModel> Sources { get; } = new();
        public ObservableCollection<SourceRecentActivityItemViewModel> RecentActivities { get; } = new();

        [ObservableProperty]
        private bool _isEmpty;

        [ObservableProperty]
        private string _emptyStateTitle = "No sources configured";

        [ObservableProperty]
        private string _emptyStateMessage = "Add an M3U playlist or Xtream provider to start importing live channels, movies, series, and guide data.";

        [ObservableProperty]
        private int _sourceCount;

        [ObservableProperty]
        private int _m3uSourceCount;

        [ObservableProperty]
        private int _xtreamSourceCount;

        [ObservableProperty]
        private int _totalAssetCount;

        [ObservableProperty]
        private int _totalLiveChannelCount;

        [ObservableProperty]
        private int _totalLibraryAssetCount;

        [ObservableProperty]
        private int _totalMatchedGuideChannelCount;

        [ObservableProperty]
        private string _healthStatusHeadline = "Idle";

        [ObservableProperty]
        private string _healthStatusCaption = "Add a source to get started.";

        [ObservableProperty]
        private StatusPillKind _healthStatusKind = StatusPillKind.Neutral;

        [ObservableProperty]
        private string _guideCoverageHeadline = "0%";

        [ObservableProperty]
        private string _guideCoverageCaption = "Add a live source to see guide coverage.";

        [ObservableProperty]
        private double _guideCoveragePercent;

        [ObservableProperty]
        private string _searchText = string.Empty;

        public string M3uSourceCountLabel => $"{M3uSourceCount:N0} M3U";
        public string XtreamSourceCountLabel => $"{XtreamSourceCount:N0} Xtream";
        public string ConfiguredSourceCountText => $"{SourceCount:N0} configured";
        public string RecentActivityCountText => $"{RecentActivities.Count:N0} events";

        public SourceListViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        partial void OnSearchTextChanged(string value)
        {
            ApplySourceFilter();
        }

        public async Task<SourceGuideSettingsDraft?> GetGuideSettingsAsync(int sourceId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = await db.SourceProfiles
                .AsNoTracking()
                .Where(profile => profile.Id == sourceId)
                .Select(profile => new
                {
                    profile.Id,
                    profile.Name,
                    profile.Type
                })
                .FirstOrDefaultAsync();
            if (source == null)
            {
                return null;
            }

            var credential = await db.SourceCredentials
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.SourceProfileId == sourceId);
            if (credential == null)
            {
                return null;
            }

            return new SourceGuideSettingsDraft
            {
                SourceId = source.Id,
                SourceName = source.Name,
                SourceType = source.Type,
                ActiveMode = credential.EpgMode,
                ManualEpgUrl = credential.ManualEpgUrl,
                DetectedEpgUrl = credential.DetectedEpgUrl,
                ProxyScope = credential.ProxyScope,
                ProxyUrl = credential.ProxyUrl
            };
        }

        public async Task SaveGuideSettingsAsync(SourceGuideSettingsDraft draft, bool syncNow)
        {
            using var scope = _serviceProvider.CreateScope();
            var lifecycleService = scope.ServiceProvider.GetRequiredService<ISourceLifecycleService>();
            await lifecycleService.UpdateGuideSettingsAsync(
                new SourceGuideSettingsUpdateRequest
                {
                    SourceId = draft.SourceId,
                    ActiveMode = draft.ActiveMode,
                    ManualEpgUrl = draft.ManualEpgUrl,
                    ProxyScope = draft.ProxyScope,
                    ProxyUrl = draft.ProxyUrl
                },
                syncNow);

            await LoadSourcesAsync();
        }

        private async Task<string?> TryRefreshGuideAsync(int sourceId)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var refreshService = scope.ServiceProvider.GetRequiredService<ISourceRefreshService>();
                var result = await refreshService.RefreshSourceAsync(sourceId, SourceRefreshTrigger.Manual, SourceRefreshScope.EpgOnly);
                if (!result.Success)
                {
                    return result.Message;
                }

                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        [RelayCommand]
        public async Task LoadSourcesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var diagnosticsService = scope.ServiceProvider.GetRequiredService<ISourceDiagnosticsService>();

            var profiles = await db.SourceProfiles
                .AsNoTracking()
                .OrderBy(profile => profile.Name)
                .ToListAsync();

            var sourceIds = profiles.Select(profile => profile.Id).ToList();
            var diagnostics = await diagnosticsService.GetSnapshotsAsync(db, sourceIds);
            var epgLogs = await db.EpgSyncLogs
                .AsNoTracking()
                .Where(log => sourceIds.Contains(log.SourceProfileId))
                .ToDictionaryAsync(log => log.SourceProfileId);
            var loadedSources = new List<SourceItemViewModel>(profiles.Count);

            foreach (var profile in profiles)
            {
                diagnostics.TryGetValue(profile.Id, out var snapshot);
                snapshot ??= new SourceDiagnosticsSnapshot
                {
                    SourceProfileId = profile.Id,
                    SourceType = profile.Type,
                    HealthLabel = profile.LastSync.HasValue ? "Healthy" : "Not synced",
                    StatusSummary = profile.LastSync.HasValue
                        ? $"Last import completed {profile.LastSync.Value.ToLocalTime():g}."
                        : "Saved source. No successful import recorded yet.",
                    ImportResultText = profile.LastSync.HasValue
                        ? $"Imported at {profile.LastSync.Value.ToLocalTime():g}"
                        : "No successful import recorded.",
                    ValidationResultText = "Validation will appear after the first completed sync.",
                    EpgCoverageText = "Guide not synced.",
                    EpgStatusText = "Guide not synced",
                    EpgStatusSummary = "Guide has not synced yet.",
                    LastSuccessfulSyncText = $"Import {(profile.LastSync.HasValue ? profile.LastSync.Value.ToLocalTime().ToString("g") : "Never")} - Guide Never",
                    LastSyncAttemptText = "Never",
                    LastImportSuccessText = profile.LastSync?.ToLocalTime().ToString("g") ?? "Never",
                    LastEpgSuccessText = "Never",
                    ActiveEpgModeText = "Detected from provider",
                    EpgStatus = EpgStatus.Unknown,
                    EpgResultCode = EpgSyncResultCode.None,
                    AutoRefreshStatusText = "Auto standby",
                    AutoRefreshSummaryText = "Automatic refresh has not run yet.",
                    NextAutoRefreshText = "Never",
                    LastAutoRefreshText = "Never",
                    HealthComponents = Array.Empty<SourceDiagnosticsComponentSnapshot>(),
                    HealthProbes = Array.Empty<SourceDiagnosticsProbeSnapshot>()
                };

                loadedSources.Add(new SourceItemViewModel
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    Type = profile.Type.ToString(),
                    SourceKindText = profile.Type == SourceType.Xtream ? "XTREAM API" : "M3U PLAYLIST",
                    HealthBadgeText = (snapshot.HealthLabel ?? "Saved").ToUpperInvariant(),
                    GuideBadgeText = BuildGuideBadgeText(snapshot),
                    PrimarySyncText = profile.Type == SourceType.Xtream ? "Sync Now" : "Import Now",
                    SourcePanelSummaryText = BuildSourcePanelSummary(snapshot),
                    ConnectionLabelText = BuildConnectionLabel(snapshot),
                    HealthPillKind = MapHealthPillKind(snapshot.HealthLabel),
                    GuidePillKind = MapGuidePillKind(snapshot),
                    LastSyncText = snapshot.LastImportSuccessText,
                    LastAttemptText = snapshot.LastSyncAttemptText,
                    ChannelCount = snapshot.LiveChannelCount,
                    MovieCount = snapshot.MovieCount,
                    SeriesCount = snapshot.SeriesCount,
                    DuplicateCount = snapshot.DuplicateCount,
                    InvalidStreamCount = snapshot.InvalidStreamCount,
                    ChannelsWithLogoCount = snapshot.ChannelsWithLogoCount,
                    SuspiciousEntryCount = snapshot.SuspiciousEntryCount,
                    HealthScore = snapshot.HealthScore,
                    HealthLabel = snapshot.HealthLabel,
                    Status = snapshot.StatusSummary,
                    HasEpgUrl = snapshot.HasEpgUrl,
                    CanSyncEpg = profile.Type is SourceType.M3U or SourceType.Xtream,
                    HasEpgData = snapshot.HasPersistedGuideData,
                    EpgLastSyncText = snapshot.LastEpgSuccessText,
                    EpgMatchedChannels = snapshot.MatchedLiveChannelCount,
                    EpgProgramCount = snapshot.EpgProgramCount,
                    EpgSummaryText = snapshot.EpgStatusText,
                    EpgSyncSuccess = snapshot.EpgSyncSuccess,
                    ImportResultText = snapshot.ImportResultText,
                    ValidationResultText = snapshot.ValidationResultText,
                    EpgCoverageText = snapshot.EpgCoverageText,
                    ParseWarningsText = snapshot.WarningSummaryText,
                    NetworkFailureText = snapshot.FailureSummaryText,
                    LastSuccessfulSyncText = snapshot.LastSuccessfulSyncText,
                    GuideStatusText = snapshot.EpgStatusText,
                    GuideStatusSummaryText = snapshot.EpgStatusSummary,
                    GuideModeText = snapshot.ActiveEpgModeText,
                    GuideUrlText = snapshot.EpgUrlSummaryText,
                      GuideMatchText = snapshot.MatchBreakdownText,
                      AutoRefreshStatusText = snapshot.AutoRefreshStatusText,
                      AutoRefreshSummaryText = snapshot.AutoRefreshSummaryText,
                      NextAutoRefreshText = snapshot.NextAutoRefreshText,
                      LastAutoRefreshText = snapshot.LastAutoRefreshText,
                      OperationalStatusText = snapshot.OperationalStatusText,
                      ProxyStatusText = snapshot.ProxyStatusText,
                      HealthComponents = snapshot.HealthComponents
                          .Select(component => new SourceHealthComponentItemViewModel
                          {
                            Label = SourceHealthDisplay.GetComponentLabel(component.ComponentType),
                            Summary = component.Summary,
                            PillLabel = BuildComponentPillLabel(component, snapshot.HealthProbes),
                            PillKind = MapComponentPillKind(component.State)
                        })
                        .ToList(),
                    HealthIssues = snapshot.Issues
                        .Take(3)
                        .Select(issue => new SourceIssueItemViewModel
                        {
                            Title = issue.Title,
                            Message = issue.Message,
                            SeverityKind = MapIssuePillKind(issue.Severity)
                        })
                        .ToList(),
                    ImportWarningCount = snapshot.ImportWarningCount,
                    GuideWarningCount = snapshot.GuideWarningCount
                });
            }

            _allSources = loadedSources;
            ApplySourceFilter();

            SourceCount = loadedSources.Count;
            M3uSourceCount = loadedSources.Count(source => source.Type == "M3U");
            XtreamSourceCount = loadedSources.Count(source => source.Type == "Xtream");
            TotalAssetCount = loadedSources.Sum(source => source.ChannelCount + source.MovieCount + source.SeriesCount);
            TotalLiveChannelCount = loadedSources.Sum(source => source.ChannelCount);
            TotalLibraryAssetCount = loadedSources.Sum(source => source.MovieCount + source.SeriesCount);
            TotalMatchedGuideChannelCount = loadedSources.Sum(source => source.EpgMatchedChannels);
            OnPropertyChanged(nameof(M3uSourceCountLabel));
            OnPropertyChanged(nameof(XtreamSourceCountLabel));
            OnPropertyChanged(nameof(ConfiguredSourceCountText));

            var totalLiveChannels = TotalLiveChannelCount;
            var guideCoverage = totalLiveChannels > 0
                ? (double)TotalMatchedGuideChannelCount / totalLiveChannels
                : 0d;
            GuideCoveragePercent = guideCoverage * 100d;
            GuideCoverageHeadline = totalLiveChannels > 0
                ? $"{guideCoverage:P0}"
                : "0%";
            GuideCoverageCaption = totalLiveChannels > 0
                ? $"{TotalMatchedGuideChannelCount:N0} of {totalLiveChannels:N0} live channels matched to guide data"
                : "Add a live source to see guide coverage.";

            var healthySources = loadedSources.Count(source => source.HealthPillKind == StatusPillKind.Healthy);
            var failingSources = loadedSources.Count(source => source.HealthPillKind == StatusPillKind.Failed);
            var workingSources = loadedSources.Count(source => source.HealthPillKind == StatusPillKind.Syncing);

            if (workingSources > 0)
            {
                HealthStatusHeadline = "Syncing";
                HealthStatusCaption = $"{workingSources} source{(workingSources == 1 ? string.Empty : "s")} syncing now.";
                HealthStatusKind = StatusPillKind.Syncing;
            }
            else if (failingSources > 0)
            {
                HealthStatusHeadline = "Attention";
                HealthStatusCaption = $"{failingSources} source{(failingSources == 1 ? string.Empty : "s")} need review.";
                HealthStatusKind = StatusPillKind.Failed;
            }
            else if (healthySources == loadedSources.Count && loadedSources.Count > 0)
            {
                HealthStatusHeadline = "Optimal";
                HealthStatusCaption = "All configured sources are ready.";
                HealthStatusKind = StatusPillKind.Healthy;
            }
            else if (loadedSources.Count > 0)
            {
                HealthStatusHeadline = "Mixed";
                HealthStatusCaption = $"{healthySources} source{(healthySources == 1 ? string.Empty : "s")} ready, {loadedSources.Count - healthySources} to review.";
                HealthStatusKind = StatusPillKind.Warning;
            }
            else
            {
                HealthStatusHeadline = "Idle";
                HealthStatusCaption = "Add a source to get started.";
                HealthStatusKind = StatusPillKind.Neutral;
            }

            RecentActivities.Clear();
            foreach (var activity in BuildRecentActivities(profiles, diagnostics, epgLogs).Take(6))
            {
                RecentActivities.Add(activity);
            }

            OnPropertyChanged(nameof(RecentActivityCountText));
        }

        private void ApplySourceFilter()
        {
            Sources.Clear();

            IEnumerable<SourceItemViewModel> filtered = _allSources;
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var search = SearchText.Trim();
                filtered = filtered.Where(source =>
                    ContainsSearch(source.Name, search) ||
                    ContainsSearch(source.Type, search) ||
                    ContainsSearch(source.SourceKindText, search) ||
                    ContainsSearch(source.HealthLabel, search) ||
                    ContainsSearch(source.GuideStatusText, search) ||
                    ContainsSearch(source.Status, search) ||
                    ContainsSearch(source.ValidationResultText, search) ||
                    ContainsSearch(source.OperationalStatusText, search) ||
                    ContainsSearch(source.ProxyStatusText, search) ||
                    source.HealthComponents.Any(component => ContainsSearch(component.Label, search) || ContainsSearch(component.Summary, search)) ||
                    ContainsSearch(source.GuideModeText, search) ||
                    ContainsSearch(source.GuideUrlText, search));
            }

            foreach (var source in filtered)
            {
                Sources.Add(source);
            }

            var noConfiguredSources = _allSources.Count == 0;
            IsEmpty = Sources.Count == 0;
            EmptyStateTitle = noConfiguredSources
                ? "No sources configured"
                : "No matching sources";
            EmptyStateMessage = noConfiguredSources
                ? "Add an M3U playlist or Xtream provider to start importing live channels, movies, series, and guide data."
                : "Try a different source name, type, or guide status.";
        }

        private static IEnumerable<SourceRecentActivityItemViewModel> BuildRecentActivities(
            IReadOnlyCollection<SourceProfile> profiles,
            IReadOnlyDictionary<int, SourceDiagnosticsSnapshot> diagnostics,
            IReadOnlyDictionary<int, EpgSyncLog> epgLogs)
        {
            var activities = new List<(DateTime TimestampUtc, SourceRecentActivityItemViewModel Item)>();

            foreach (var profile in profiles)
            {
                diagnostics.TryGetValue(profile.Id, out var snapshot);
                snapshot ??= new SourceDiagnosticsSnapshot();
                epgLogs.TryGetValue(profile.Id, out var epgLog);

                if (profile.LastSync.HasValue)
                {
                    var importStatusText = snapshot.HealthLabel is "Failing" or "Attention" or "Weak" or "Incomplete" or "Outdated" or "Problematic"
                        ? "Review"
                        : "Complete";
                    var importKind = snapshot.HealthLabel is "Failing" or "Problematic"
                        ? StatusPillKind.Failed
                        : snapshot.HealthLabel is "Attention"
                            ? StatusPillKind.Warning
                        : snapshot.HealthLabel is "Weak" or "Incomplete" or "Outdated"
                            ? StatusPillKind.Warning
                            : StatusPillKind.Healthy;

                    activities.Add((profile.LastSync.Value.ToUniversalTime(), new SourceRecentActivityItemViewModel
                    {
                        TimestampText = profile.LastSync.Value.ToLocalTime().ToString("MMM d, HH:mm"),
                        SourceName = profile.Name,
                        ActionText = profile.Type == SourceType.Xtream ? "Source Sync" : "Playlist Import",
                        StatusText = importStatusText,
                        StatusKind = importKind,
                        PayloadText = $"{snapshot.LiveChannelCount:N0} live, {snapshot.MovieCount + snapshot.SeriesCount:N0} VOD/series"
                    }));
                }

                if (epgLog != null && epgLog.SyncedAtUtc > DateTime.MinValue)
                {
                    activities.Add((epgLog.SyncedAtUtc, new SourceRecentActivityItemViewModel
                    {
                        TimestampText = epgLog.SyncedAtUtc.ToLocalTime().ToString("MMM d, HH:mm"),
                        SourceName = profile.Name,
                        ActionText = "Guide Sync",
                        StatusText = snapshot.EpgStatusText,
                        StatusKind = MapGuidePillKind(snapshot),
                        PayloadText = $"{snapshot.MatchedLiveChannelCount:N0} matched, {snapshot.EpgProgramCount:N0} programmes"
                    }));
                }
            }

            return activities
                .OrderByDescending(activity => activity.TimestampUtc)
                .Select(activity => activity.Item)
                .ToList();
        }

        private static bool ContainsSearch(string value, string search)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildGuideBadgeText(SourceDiagnosticsSnapshot snapshot)
        {
            var text = snapshot.EpgStatusText;
            if (string.IsNullOrWhiteSpace(text))
            {
                return "GUIDE";
            }

            return text.ToUpperInvariant();
        }

        private static string BuildSourcePanelSummary(SourceDiagnosticsSnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.FailureSummaryText))
            {
                return snapshot.FailureSummaryText;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.WarningSummaryText))
            {
                return snapshot.WarningSummaryText;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.ValidationResultText))
            {
                return snapshot.ValidationResultText;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.EpgCoverageText))
            {
                return snapshot.EpgCoverageText;
            }

            return snapshot.StatusSummary;
        }

        private static string BuildConnectionLabel(SourceDiagnosticsSnapshot snapshot)
        {
            return snapshot.EpgStatus switch
            {
                EpgStatus.Syncing => "Connection syncing",
                EpgStatus.Ready => "Connection active",
                EpgStatus.ManualOverride => "Manual guide active",
                EpgStatus.Stale => "Guide is stale",
                EpgStatus.FailedFetchOrParse => "Connection failed",
                EpgStatus.UnavailableNoXmltv => "Guide unavailable",
                _ => "Standby"
            };
        }

        private static StatusPillKind MapHealthPillKind(string healthLabel)
        {
            return healthLabel switch
            {
                "Healthy" or "Ready" => StatusPillKind.Healthy,
                "Weak" or "Incomplete" or "Outdated" => StatusPillKind.Warning,
                "Working" => StatusPillKind.Syncing,
                "Attention" or "Degraded" => StatusPillKind.Warning,
                "Failing" or "Problematic" => StatusPillKind.Failed,
                _ => StatusPillKind.Standby
            };
        }

        private static StatusPillKind MapIssuePillKind(SourceHealthIssueSeverity severity)
        {
            return severity switch
            {
                SourceHealthIssueSeverity.Error => StatusPillKind.Failed,
                SourceHealthIssueSeverity.Warning => StatusPillKind.Warning,
                _ => StatusPillKind.Info
            };
        }

        private static string BuildComponentPillLabel(
            SourceDiagnosticsComponentSnapshot component,
            IReadOnlyList<SourceDiagnosticsProbeSnapshot> probes)
        {
            var baseLabel = SourceHealthDisplay.GetComponentShortLabel(component.ComponentType).ToUpperInvariant();
            if (component.ComponentType is not SourceHealthComponentType.Live and not SourceHealthComponentType.Vod)
            {
                return $"{baseLabel} {SourceHealthDisplay.GetComponentBadgeText(component.State)}";
            }

            var probeType = component.ComponentType == SourceHealthComponentType.Live
                ? SourceHealthProbeType.Live
                : SourceHealthProbeType.Vod;
            var probe = probes.FirstOrDefault(item => item.ProbeType == probeType);
            return probe != null && probe.Status == SourceHealthProbeStatus.Completed && probe.SampleSize > 0
                ? $"{baseLabel} {probe.SuccessCount}/{probe.SampleSize}"
                : $"{baseLabel} {SourceHealthDisplay.GetComponentBadgeText(component.State)}";
        }

        private static StatusPillKind MapComponentPillKind(SourceHealthComponentState state)
        {
            return state switch
            {
                SourceHealthComponentState.Healthy => StatusPillKind.Healthy,
                SourceHealthComponentState.Weak or SourceHealthComponentState.Incomplete or SourceHealthComponentState.Outdated => StatusPillKind.Warning,
                SourceHealthComponentState.Problematic => StatusPillKind.Failed,
                SourceHealthComponentState.NotApplicable => StatusPillKind.Info,
                _ => StatusPillKind.Standby
            };
        }

        private static StatusPillKind MapGuidePillKind(SourceDiagnosticsSnapshot snapshot)
        {
            return snapshot.EpgStatus switch
            {
                EpgStatus.Ready or EpgStatus.ManualOverride => snapshot.EpgResultCode == EpgSyncResultCode.PartialMatch
                    ? StatusPillKind.Warning
                    : StatusPillKind.Healthy,
                EpgStatus.Syncing => StatusPillKind.Syncing,
                EpgStatus.Stale => StatusPillKind.Warning,
                EpgStatus.FailedFetchOrParse => StatusPillKind.Failed,
                EpgStatus.UnavailableNoXmltv => StatusPillKind.Info,
                _ => StatusPillKind.Standby
            };
        }

        [RelayCommand]
        public async Task ParseSourceAsync(int id)
        {
            var item = Sources.FirstOrDefault(source => source.Id == id);
            if (item != null)
            {
                item.HealthLabel = "Working";
                item.Status = "Refreshing live catalog...";
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var refreshService = scope.ServiceProvider.GetRequiredService<ISourceRefreshService>();
                var result = await refreshService.RefreshSourceAsync(id, SourceRefreshTrigger.Manual, SourceRefreshScope.LiveOnly);
                await LoadSourcesAsync();
                if (!result.Success)
                {
                    item = Sources.FirstOrDefault(source => source.Id == id);
                    if (item != null)
                    {
                        item.Status = BuildStatusMessage("Live refresh needs review", result.Message);
                    }
                }
                else if (!result.GuideSucceeded && result.GuideAttempted && !string.IsNullOrWhiteSpace(result.GuideSummary))
                {
                    item = Sources.FirstOrDefault(source => source.Id == id);
                    if (item != null)
                    {
                        item.Status = result.Message;
                    }
                }
            }
            catch (Exception ex)
            {
                if (item != null)
                {
                    item.HealthLabel = "Failing";
                    item.Status = BuildStatusMessage("Live refresh did not finish", ex.Message);
                }
            }
        }

        [RelayCommand]
        public async Task SyncEpgAsync(int id)
        {
            var item = Sources.FirstOrDefault(source => source.Id == id);
            if (item == null) return;

            item.IsEpgSyncing = true;
            item.HealthLabel = "Working";
            item.Status = "Refreshing guide data...";

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var refreshService = scope.ServiceProvider.GetRequiredService<ISourceRefreshService>();
                await refreshService.RefreshSourceAsync(id, SourceRefreshTrigger.Manual, SourceRefreshScope.EpgOnly);
                await LoadSourcesAsync();
            }
            catch (Exception ex)
            {
                item.IsEpgSyncing = false;
                item.HealthLabel = "Failing";
                item.Status = BuildStatusMessage("Guide refresh needs review", ex.Message);
            }
        }

        [RelayCommand]
        public async Task SyncXtreamAsync(int id)
        {
            var item = Sources.FirstOrDefault(source => source.Id == id);
            if (item != null)
            {
                item.HealthLabel = "Working";
                item.Status = "Refreshing source...";
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var refreshService = scope.ServiceProvider.GetRequiredService<ISourceRefreshService>();
                var result = await refreshService.RefreshSourceAsync(id, SourceRefreshTrigger.Manual, SourceRefreshScope.Full);
                await LoadSourcesAsync();
                if (!result.Success)
                {
                    item = Sources.FirstOrDefault(source => source.Id == id);
                    if (item != null)
                    {
                        item.Status = BuildStatusMessage("Source refresh needs review", result.Message);
                    }
                }
                else if (!result.GuideSucceeded && result.GuideAttempted && !string.IsNullOrWhiteSpace(result.GuideSummary))
                {
                    item = Sources.FirstOrDefault(source => source.Id == id);
                    if (item != null)
                    {
                        item.Status = result.Message;
                    }
                }
            }
            catch (Exception ex)
            {
                if (item != null)
                {
                    item.HealthLabel = "Failing";
                    item.Status = BuildStatusMessage("Source refresh did not finish", ex.Message);
                }
            }
        }

        [RelayCommand]
        public async Task SyncXtreamVodAsync(int id)
        {
            var item = Sources.FirstOrDefault(source => source.Id == id);
            if (item != null)
            {
                item.HealthLabel = "Working";
                item.Status = "Refreshing movie and series catalog...";
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var refreshService = scope.ServiceProvider.GetRequiredService<ISourceRefreshService>();
                await refreshService.RefreshSourceAsync(id, SourceRefreshTrigger.Manual, SourceRefreshScope.VodOnly);
                await LoadSourcesAsync();
            }
            catch (Exception ex)
            {
                if (item != null)
                {
                    item.HealthLabel = "Failing";
                    item.Status = BuildStatusMessage("Library refresh did not finish", ex.Message);
                }
            }
        }

        [RelayCommand]
        public async Task DeleteSourceAsync(int id)
        {
            var uiItem = Sources.FirstOrDefault(source => source.Id == id);
            if (uiItem != null)
            {
                uiItem.HealthLabel = "Working";
                uiItem.Status = "Removing source...";
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var lifecycleService = scope.ServiceProvider.GetRequiredService<ISourceLifecycleService>();
                var result = await lifecycleService.DeleteSourceAsync(id);
                if (!result.Success)
                {
                    if (uiItem != null)
                    {
                        uiItem.HealthLabel = "Failing";
                        uiItem.Status = result.Message;
                    }

                    return;
                }

                await LoadSourcesAsync();
            }
            catch (Exception ex)
            {
                if (uiItem != null)
                {
                    uiItem.HealthLabel = "Failing";
                    uiItem.Status = BuildStatusMessage("Could not remove this source", ex.Message);
                }
            }
        }

        private static string BuildStatusMessage(string prefix, string? detail)
        {
            var trimmed = TrimStatusDetail(detail);
            return string.IsNullOrWhiteSpace(trimmed)
                ? prefix
                : $"{prefix}: {trimmed}";
        }

        private static string TrimStatusDetail(string? detail, int maxLength = 120)
        {
            if (string.IsNullOrWhiteSpace(detail))
            {
                return string.Empty;
            }

            var normalized = detail.Trim();
            return normalized.Length > maxLength
                ? normalized[..maxLength] + "..."
                : normalized;
        }
    }
}
