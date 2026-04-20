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
        public int ChannelCount { get; set; }
        public int MovieCount { get; set; }
        public int SeriesCount { get; set; }
        public string ImportResultText { get; set; } = string.Empty;
        public string EpgCoverageText { get; set; } = string.Empty;
        public string ParseWarningsText { get; set; } = string.Empty;
        public string NetworkFailureText { get; set; } = string.Empty;
        public string LastSuccessfulSyncText { get; set; } = string.Empty;
        public string GuideStatusText { get; set; } = string.Empty;
        public string GuideStatusSummaryText { get; set; } = string.Empty;
        public string GuideModeText { get; set; } = string.Empty;
        public string GuideUrlText { get; set; } = string.Empty;
        public string GuideMatchText { get; set; } = string.Empty;
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

        public Microsoft.UI.Xaml.Visibility ParseWarningsVisibility => string.IsNullOrWhiteSpace(ParseWarningsText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility NetworkFailureVisibility => string.IsNullOrWhiteSpace(NetworkFailureText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        partial void OnHealthLabelChanged(string value)
        {
            OnPropertyChanged(nameof(HealthyVisibility));
            OnPropertyChanged(nameof(DegradedVisibility));
            OnPropertyChanged(nameof(AttentionVisibility));
            OnPropertyChanged(nameof(WorkingVisibility));
            OnPropertyChanged(nameof(IdleVisibility));
        }
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
        private string _emptyStateMessage = "Add an M3U playlist or Xtream provider to populate live channels, VOD, and guide data.";

        [ObservableProperty]
        private int _sourceCount;

        [ObservableProperty]
        private int _m3uSourceCount;

        [ObservableProperty]
        private int _xtreamSourceCount;

        [ObservableProperty]
        private int _totalAssetCount;

        [ObservableProperty]
        private int _totalMatchedGuideChannelCount;

        [ObservableProperty]
        private string _healthStatusHeadline = "Idle";

        [ObservableProperty]
        private string _healthStatusCaption = "No sources loaded yet.";

        [ObservableProperty]
        private StatusPillKind _healthStatusKind = StatusPillKind.Neutral;

        [ObservableProperty]
        private string _guideCoverageHeadline = "0%";

        [ObservableProperty]
        private string _guideCoverageCaption = "No live channels available.";

        [ObservableProperty]
        private string _searchText = string.Empty;

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
                DetectedEpgUrl = credential.DetectedEpgUrl
            };
        }

        public async Task SaveGuideSettingsAsync(SourceGuideSettingsDraft draft, bool syncNow)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var credential = await db.SourceCredentials.FirstOrDefaultAsync(item => item.SourceProfileId == draft.SourceId);
            if (credential == null)
            {
                throw new Exception("Source credentials were not found.");
            }

            if (draft.ActiveMode == EpgActiveMode.Manual && string.IsNullOrWhiteSpace(draft.ManualEpgUrl))
            {
                throw new Exception("Manual XMLTV mode requires a manual XMLTV URL.");
            }

            var previousMode = credential.EpgMode;
            var previousManualUrl = credential.ManualEpgUrl ?? string.Empty;
            credential.EpgMode = draft.ActiveMode;
            credential.ManualEpgUrl = draft.ManualEpgUrl?.Trim() ?? string.Empty;
            await db.SaveChangesAsync();

            var settingsChanged = previousMode != draft.ActiveMode ||
                                  !string.Equals(previousManualUrl, credential.ManualEpgUrl, StringComparison.OrdinalIgnoreCase);

            if (syncNow || draft.ActiveMode == EpgActiveMode.None)
            {
                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXmltvParserService>();
                await parser.ParseAndImportEpgAsync(db, draft.SourceId);
            }
            else if (settingsChanged)
            {
                var channelIds = await db.ChannelCategories
                    .Where(category => category.SourceProfileId == draft.SourceId)
                    .Join(
                        db.Channels,
                        category => category.Id,
                        channel => channel.ChannelCategoryId,
                        (category, channel) => channel.Id)
                    .ToListAsync();

                if (channelIds.Count > 0)
                {
                    var existingPrograms = await db.EpgPrograms
                        .Where(program => channelIds.Contains(program.ChannelId))
                        .ToListAsync();
                    if (existingPrograms.Count > 0)
                    {
                        db.EpgPrograms.RemoveRange(existingPrograms);
                    }
                }

                var log = await db.EpgSyncLogs.FirstOrDefaultAsync(item => item.SourceProfileId == draft.SourceId);
                if (log == null)
                {
                    log = new EpgSyncLog { SourceProfileId = draft.SourceId };
                    db.EpgSyncLogs.Add(log);
                }

                log.SyncedAtUtc = DateTime.UtcNow;
                log.LastSuccessAtUtc = null;
                log.IsSuccess = false;
                log.Status = EpgStatus.Unknown;
                log.ResultCode = EpgSyncResultCode.None;
                log.FailureStage = EpgFailureStage.None;
                log.ActiveMode = draft.ActiveMode;
                log.ActiveXmltvUrl = string.Empty;
                log.MatchedChannelCount = 0;
                log.UnmatchedChannelCount = channelIds.Count;
                log.CurrentCoverageCount = 0;
                log.NextCoverageCount = 0;
                log.TotalLiveChannelCount = channelIds.Count;
                log.ProgrammeCount = 0;
                log.MatchBreakdown = string.Empty;
                log.FailureReason = "Guide settings updated. Sync pending.";

                await db.SaveChangesAsync();
            }

            await LoadSourcesAsync();
        }

        private async Task<string?> TryRefreshGuideAsync(int sourceId)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXmltvParserService>();
                await parser.ParseAndImportEpgAsync(db, sourceId);
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
                    EpgCoverageText = "Guide not synced.",
                    EpgStatusText = "Guide not synced",
                    EpgStatusSummary = "Guide has not synced yet.",
                    LastSuccessfulSyncText = $"Import {(profile.LastSync.HasValue ? profile.LastSync.Value.ToLocalTime().ToString("g") : "Never")} - Guide Never",
                    LastImportSuccessText = profile.LastSync?.ToLocalTime().ToString("g") ?? "Never",
                    LastEpgSuccessText = "Never",
                    ActiveEpgModeText = "Detected from provider",
                    EpgStatus = EpgStatus.Unknown,
                    EpgResultCode = EpgSyncResultCode.None
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
                    ChannelCount = snapshot.LiveChannelCount,
                    MovieCount = snapshot.MovieCount,
                    SeriesCount = snapshot.SeriesCount,
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
                    EpgCoverageText = snapshot.EpgCoverageText,
                    ParseWarningsText = snapshot.WarningSummaryText,
                    NetworkFailureText = snapshot.FailureSummaryText,
                    LastSuccessfulSyncText = snapshot.LastSuccessfulSyncText,
                    GuideStatusText = snapshot.EpgStatusText,
                    GuideStatusSummaryText = snapshot.EpgStatusSummary,
                    GuideModeText = snapshot.ActiveEpgModeText,
                    GuideUrlText = snapshot.EpgUrlSummaryText,
                    GuideMatchText = snapshot.MatchBreakdownText,
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
            TotalMatchedGuideChannelCount = loadedSources.Sum(source => source.EpgMatchedChannels);

            var totalLiveChannels = loadedSources.Sum(source => source.ChannelCount);
            var guideCoverage = totalLiveChannels > 0
                ? (double)TotalMatchedGuideChannelCount / totalLiveChannels
                : 0d;
            GuideCoverageHeadline = totalLiveChannels > 0
                ? $"{guideCoverage:P0}"
                : "0%";
            GuideCoverageCaption = totalLiveChannels > 0
                ? $"{TotalMatchedGuideChannelCount:N0} of {totalLiveChannels:N0} live channels mapped"
                : "No live channels available yet.";

            var healthySources = loadedSources.Count(source => source.HealthPillKind == StatusPillKind.Healthy);
            var failingSources = loadedSources.Count(source => source.HealthPillKind == StatusPillKind.Failed);
            var workingSources = loadedSources.Count(source => source.HealthPillKind == StatusPillKind.Syncing);

            if (workingSources > 0)
            {
                HealthStatusHeadline = "Syncing";
                HealthStatusCaption = $"{workingSources} source syncing right now.";
                HealthStatusKind = StatusPillKind.Syncing;
            }
            else if (failingSources > 0)
            {
                HealthStatusHeadline = "Attention";
                HealthStatusCaption = $"{failingSources} source requires intervention.";
                HealthStatusKind = StatusPillKind.Failed;
            }
            else if (healthySources == loadedSources.Count && loadedSources.Count > 0)
            {
                HealthStatusHeadline = "Optimal";
                HealthStatusCaption = "All configured sources look operational.";
                HealthStatusKind = StatusPillKind.Healthy;
            }
            else if (loadedSources.Count > 0)
            {
                HealthStatusHeadline = "Mixed";
                HealthStatusCaption = $"{healthySources} healthy, {loadedSources.Count - healthySources} need review.";
                HealthStatusKind = StatusPillKind.Warning;
            }
            else
            {
                HealthStatusHeadline = "Idle";
                HealthStatusCaption = "No configured sources yet.";
                HealthStatusKind = StatusPillKind.Neutral;
            }

            RecentActivities.Clear();
            foreach (var activity in BuildRecentActivities(profiles, diagnostics, epgLogs).Take(6))
            {
                RecentActivities.Add(activity);
            }
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
                ? "Add an M3U playlist or Xtream provider to populate live channels, VOD, and guide data."
                : "Try a different provider name, source type, or guide status filter.";
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
                    var importStatusText = snapshot.HealthLabel is "Failing" or "Attention"
                        ? "Review"
                        : "Complete";
                    var importKind = snapshot.HealthLabel is "Failing" or "Attention"
                        ? StatusPillKind.Warning
                        : StatusPillKind.Healthy;

                    activities.Add((profile.LastSync.Value.ToUniversalTime(), new SourceRecentActivityItemViewModel
                    {
                        TimestampText = profile.LastSync.Value.ToLocalTime().ToString("MMM d, HH:mm"),
                        SourceName = profile.Name,
                        ActionText = profile.Type == SourceType.Xtream ? "Source Sync" : "Playlist Import",
                        StatusText = importStatusText,
                        StatusKind = importKind,
                        PayloadText = $"{snapshot.LiveChannelCount:N0} live · {snapshot.MovieCount + snapshot.SeriesCount:N0} VOD/series"
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
                        PayloadText = $"{snapshot.MatchedLiveChannelCount:N0} matched · {snapshot.EpgProgramCount:N0} programmes"
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
                "Working" => StatusPillKind.Syncing,
                "Attention" or "Degraded" => StatusPillKind.Warning,
                "Failing" => StatusPillKind.Failed,
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
                item.Status = "Parsing...";
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IM3uParserService>();

                await parser.ParseAndImportM3uAsync(db, id);
                var guideError = await TryRefreshGuideAsync(id);
                await LoadSourcesAsync();
                if (!string.IsNullOrWhiteSpace(guideError))
                {
                    item = Sources.FirstOrDefault(source => source.Id == id);
                    if (item != null)
                    {
                        item.Status = $"Import completed, but guide sync failed: {(guideError.Length > 120 ? guideError.Substring(0, 120) + "..." : guideError)}";
                    }
                }
            }
            catch (Exception ex)
            {
                if (item != null)
                {
                    item.HealthLabel = "Failing";
                    item.Status = $"Parse failed: {ex.Message}";
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
            item.Status = "Syncing EPG...";

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXmltvParserService>();

                await parser.ParseAndImportEpgAsync(db, id);
                await LoadSourcesAsync();
            }
            catch (Exception ex)
            {
                item.IsEpgSyncing = false;
                item.HealthLabel = "Failing";
                item.Status = $"EPG failed: {(ex.Message.Length > 120 ? ex.Message.Substring(0, 120) + "..." : ex.Message)}";
            }
        }

        [RelayCommand]
        public async Task SyncXtreamAsync(int id)
        {
            var item = Sources.FirstOrDefault(source => source.Id == id);
            if (item != null)
            {
                item.HealthLabel = "Working";
                item.Status = "Syncing Xtream...";
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXtreamParserService>();

                await parser.ParseAndImportXtreamAsync(db, id);
                await parser.ParseAndImportXtreamVodAsync(db, id);
                var guideError = await TryRefreshGuideAsync(id);
                await LoadSourcesAsync();
                if (!string.IsNullOrWhiteSpace(guideError))
                {
                    item = Sources.FirstOrDefault(source => source.Id == id);
                    if (item != null)
                    {
                        item.Status = $"Xtream sync completed, but guide sync failed: {(guideError.Length > 120 ? guideError.Substring(0, 120) + "..." : guideError)}";
                    }
                }
            }
            catch (Exception ex)
            {
                if (item != null)
                {
                    item.HealthLabel = "Failing";
                    item.Status = $"Xtream sync failed: {ex.Message}";
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
                item.Status = "Syncing Xtream VOD...";
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var parser = scope.ServiceProvider.GetRequiredService<Kroira.App.Services.Parsing.IXtreamParserService>();

                await parser.ParseAndImportXtreamVodAsync(db, id);
                await LoadSourcesAsync();
            }
            catch (Exception ex)
            {
                if (item != null)
                {
                    item.HealthLabel = "Failing";
                    item.Status = $"Xtream VOD sync failed: {ex.Message}";
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
                uiItem.Status = "Deleting...";
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var profile = await db.SourceProfiles.FindAsync(id);
                if (profile == null)
                {
                    if (uiItem != null)
                    {
                        uiItem.HealthLabel = "Failing";
                        uiItem.Status = "Source not found.";
                    }
                    return;
                }

                using var transaction = await db.Database.BeginTransactionAsync();
                try
                {
                    var catIds = await db.ChannelCategories
                        .Where(category => category.SourceProfileId == id)
                        .Select(category => category.Id)
                        .ToListAsync();

                    if (catIds.Count > 0)
                    {
                        var channelIds = await db.Channels
                            .Where(channel => catIds.Contains(channel.ChannelCategoryId))
                            .Select(channel => channel.Id)
                            .ToListAsync();

                        if (channelIds.Count > 0)
                        {
                            var epgs = await db.EpgPrograms.Where(program => channelIds.Contains(program.ChannelId)).ToListAsync();
                            if (epgs.Count > 0) db.EpgPrograms.RemoveRange(epgs);

                            var favs = await db.Favorites
                                .Where(favorite => favorite.ContentType == FavoriteType.Channel && channelIds.Contains(favorite.ContentId))
                                .ToListAsync();
                            if (favs.Count > 0) db.Favorites.RemoveRange(favs);

                            var progress = await db.PlaybackProgresses
                                .Where(item => item.ContentType == PlaybackContentType.Channel && channelIds.Contains(item.ContentId))
                                .ToListAsync();
                            if (progress.Count > 0) db.PlaybackProgresses.RemoveRange(progress);

                            var channels = await db.Channels.Where(channel => channelIds.Contains(channel.Id)).ToListAsync();
                            db.Channels.RemoveRange(channels);
                        }

                        var cats = await db.ChannelCategories.Where(category => catIds.Contains(category.Id)).ToListAsync();
                        db.ChannelCategories.RemoveRange(cats);
                    }

                    var seriesIds = await db.Series.Where(series => series.SourceProfileId == id).Select(series => series.Id).ToListAsync();
                    if (seriesIds.Count > 0)
                    {
                        var seasonIds = await db.Seasons.Where(season => seriesIds.Contains(season.SeriesId)).Select(season => season.Id).ToListAsync();
                        if (seasonIds.Count > 0)
                        {
                            var episodeIds = await db.Episodes.Where(episode => seasonIds.Contains(episode.SeasonId)).Select(episode => episode.Id).ToListAsync();
                            var episodes = await db.Episodes.Where(episode => seasonIds.Contains(episode.SeasonId)).ToListAsync();
                            if (episodes.Count > 0) db.Episodes.RemoveRange(episodes);

                            if (episodeIds.Count > 0)
                            {
                                var episodeProgress = await db.PlaybackProgresses
                                    .Where(item => item.ContentType == PlaybackContentType.Episode && episodeIds.Contains(item.ContentId))
                                    .ToListAsync();
                                if (episodeProgress.Count > 0) db.PlaybackProgresses.RemoveRange(episodeProgress);
                            }

                            var seasons = await db.Seasons.Where(season => seasonIds.Contains(season.Id)).ToListAsync();
                            db.Seasons.RemoveRange(seasons);
                        }

                        var seriesFavorites = await db.Favorites
                            .Where(favorite => favorite.ContentType == FavoriteType.Series && seriesIds.Contains(favorite.ContentId))
                            .ToListAsync();
                        if (seriesFavorites.Count > 0) db.Favorites.RemoveRange(seriesFavorites);

                        var series = await db.Series.Where(series => seriesIds.Contains(series.Id)).ToListAsync();
                        db.Series.RemoveRange(series);
                    }

                    var movieIds = await db.Movies.Where(movie => movie.SourceProfileId == id).Select(movie => movie.Id).ToListAsync();
                    if (movieIds.Count > 0)
                    {
                        var movieFavorites = await db.Favorites
                            .Where(favorite => favorite.ContentType == FavoriteType.Movie && movieIds.Contains(favorite.ContentId))
                            .ToListAsync();
                        if (movieFavorites.Count > 0) db.Favorites.RemoveRange(movieFavorites);

                        var movieProgress = await db.PlaybackProgresses
                            .Where(item => item.ContentType == PlaybackContentType.Movie && movieIds.Contains(item.ContentId))
                            .ToListAsync();
                        if (movieProgress.Count > 0) db.PlaybackProgresses.RemoveRange(movieProgress);
                    }

                    var movies = await db.Movies.Where(movie => movie.SourceProfileId == id).ToListAsync();
                    if (movies.Count > 0) db.Movies.RemoveRange(movies);

                    var creds = await db.SourceCredentials.FirstOrDefaultAsync(credential => credential.SourceProfileId == id);
                    if (creds != null) db.SourceCredentials.Remove(creds);

                    var syncState = await db.SourceSyncStates.FirstOrDefaultAsync(state => state.SourceProfileId == id);
                    if (syncState != null) db.SourceSyncStates.Remove(syncState);

                    var epgLog = await db.EpgSyncLogs.FirstOrDefaultAsync(log => log.SourceProfileId == id);
                    if (epgLog != null) db.EpgSyncLogs.Remove(epgLog);

                    db.SourceProfiles.Remove(profile);

                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    await LoadSourcesAsync();
                }
                catch (Exception ex)
                {
                    try { await transaction.RollbackAsync(); } catch { }
                    if (uiItem != null)
                    {
                        uiItem.HealthLabel = "Failing";
                        uiItem.Status = $"Delete failed: {ex.Message}";
                    }
                }
            }
            catch (Exception ex)
            {
                if (uiItem != null)
                {
                    uiItem.HealthLabel = "Failing";
                    uiItem.Status = $"Delete error: {ex.Message}";
                }
            }
        }
    }
}
