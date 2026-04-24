#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Kroira.App.ViewModels
{
    public sealed class EpgSummaryCardViewModel
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
    }

    public sealed class EpgSourceStatusViewModel
    {
        public int SourceProfileId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public string GuideSourceName { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public string CoverageText { get; set; } = string.Empty;
        public string SyncText { get; set; } = string.Empty;
        public string SourceText { get; set; } = string.Empty;
        public string WarningText { get; set; } = string.Empty;
        public Visibility WarningVisibility => string.IsNullOrWhiteSpace(WarningText) ? Visibility.Collapsed : Visibility.Visible;
    }

    public sealed class EpgChannelCoverageViewModel
    {
        public int ChannelId { get; set; }
        public int SourceProfileId { get; set; }
        public string ChannelName { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string MatchedXmltvChannelId { get; set; } = string.Empty;
        public string MatchText { get; set; } = string.Empty;
        public string DetailText { get; set; } = string.Empty;
        public string ProposedGuideText { get; set; } = string.Empty;
        public string ReviewStatusText { get; set; } = string.Empty;
        public string WeakReasonText { get; set; } = string.Empty;
        public bool CanApprove { get; set; }
        public bool CanReject { get; set; }
        public bool CanClearDecision { get; set; }
        public Visibility ReviewActionsVisibility => CanApprove || CanReject || CanClearDecision ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ApproveVisibility => CanApprove ? Visibility.Visible : Visibility.Collapsed;
        public Visibility RejectVisibility => CanReject ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ClearDecisionVisibility => CanClearDecision ? Visibility.Visible : Visibility.Collapsed;
        public string SearchKey { get; set; } = string.Empty;
    }

    public sealed class EpgCenterSourceOption
    {
        public int SourceProfileId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public SourceType SourceType { get; set; }
        public string Label => $"{SourceName} ({SourceType})";
    }

    public enum EpgPublicGuideAddMode
    {
        SingleCountry = 0,
        AutoDetect = 1,
        AllCountries = 2
    }

    public sealed class EpgPublicGuideAddModeOption
    {
        public EpgPublicGuideAddModeOption(EpgPublicGuideAddMode mode, string label, string description)
        {
            Mode = mode;
            Label = label;
            Description = description;
        }

        public EpgPublicGuideAddMode Mode { get; }
        public string Label { get; }
        public string Description { get; }
    }

    public partial class EpgCenterViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;
        private List<EpgChannelCoverageViewModel> _allUnmatchedChannels = new();
        private List<EpgChannelCoverageViewModel> _allWeakMatches = new();

        public ObservableCollection<EpgSummaryCardViewModel> SummaryCards { get; } = new();
        public ObservableCollection<EpgSourceStatusViewModel> GuideSources { get; } = new();
        public ObservableCollection<EpgChannelCoverageViewModel> UnmatchedChannels { get; } = new();
        public ObservableCollection<EpgChannelCoverageViewModel> WeakMatches { get; } = new();
        public ObservableCollection<string> Warnings { get; } = new();
        public ObservableCollection<EpgCenterSourceOption> SourceOptions { get; } = new();
        public ObservableCollection<EpgPublicGuidePreset> PublicGuidePresets { get; } =
            new(EpgPublicGuideCatalog.IptvEpgOrgPresets);
        public ObservableCollection<EpgPublicGuideAddModeOption> PublicGuideAddModes { get; } =
            new(new[]
            {
                new EpgPublicGuideAddModeOption(
                    EpgPublicGuideAddMode.SingleCountry,
                    "Single country",
                    "Add one IPTV-EPG.org country guide to the selected source."),
                new EpgPublicGuideAddModeOption(
                    EpgPublicGuideAddMode.AutoDetect,
                    "Auto-detect from source (recommended)",
                    "Recommended: inspect source channel groups and names, then add only likely country guides."),
                new EpgPublicGuideAddModeOption(
                    EpgPublicGuideAddMode.AllCountries,
                    "Full global scan (slow)",
                    "Adds every known IPTV-EPG.org country guide. This can take several minutes and may increase weak-match noise.")
            });

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BusyText))]
        [NotifyPropertyChangedFor(nameof(IsRefreshEnabled))]
        [NotifyPropertyChangedFor(nameof(CanAddPublicGuidePresetMode))]
        [NotifyPropertyChangedFor(nameof(CanAddCustomPublicGuide))]
        [NotifyPropertyChangedFor(nameof(CanManagePublicGuidePresets))]
        [NotifyPropertyChangedFor(nameof(CanManageReviewDecisions))]
        private bool _isBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsRefreshEnabled))]
        [NotifyPropertyChangedFor(nameof(CanManagePublicGuidePresets))]
        private bool _hasSyncableSources;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAddPublicGuidePresetMode))]
        [NotifyPropertyChangedFor(nameof(CanAddCustomPublicGuide))]
        [NotifyPropertyChangedFor(nameof(CanManagePublicGuidePresets))]
        private bool _hasPublicGuideConfigSources;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAddPublicGuidePresetMode))]
        [NotifyPropertyChangedFor(nameof(CanAddCustomPublicGuide))]
        [NotifyPropertyChangedFor(nameof(CanManagePublicGuidePresets))]
        [NotifyPropertyChangedFor(nameof(CanManageReviewDecisions))]
        private EpgCenterSourceOption? _selectedSourceOption;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAddPublicGuidePresetMode))]
        private EpgPublicGuidePreset? _selectedPublicGuidePreset;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAddPublicGuidePresetMode))]
        [NotifyPropertyChangedFor(nameof(PublicGuideActionText))]
        [NotifyPropertyChangedFor(nameof(SingleCountryPresetVisibility))]
        private EpgPublicGuideAddModeOption? _selectedPublicGuideAddMode;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAddPublicGuidePresetMode))]
        private int _publicGuidePresetCandidateCount;

        [ObservableProperty]
        private string _publicGuideModeDescriptionText = string.Empty;

        [ObservableProperty]
        private string _publicGuidePresetPreviewText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAddCustomPublicGuide))]
        private string _customPublicGuideUrl = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AddPublicGuideStatusVisibility))]
        private string _addPublicGuideStatusText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RefreshProgressVisibility))]
        private string _refreshProgressText = string.Empty;

        [ObservableProperty]
        private string _headlineText = "No guide data yet";

        [ObservableProperty]
        private string _statusText = "Guide diagnostics will appear after sources are synced.";

        [ObservableProperty]
        private string _lastSyncText = "Last sync: Never";

        [ObservableProperty]
        private string _lastSuccessText = "Last success: Never";

        [ObservableProperty]
        private string _searchText = string.Empty;

        public string BusyText => IsBusy ? "Refreshing..." : "Refresh EPG";
        public bool IsRefreshEnabled => HasSyncableSources && !IsBusy;
        public bool CanAddPublicGuidePresetMode => HasPublicGuideConfigSources &&
                                                   SelectedSourceOption != null &&
                                                   SelectedPublicGuideAddMode != null &&
                                                   PublicGuidePresetCandidateCount > 0 &&
                                                   !IsBusy;
        public bool CanAddCustomPublicGuide => HasPublicGuideConfigSources && SelectedSourceOption != null && !string.IsNullOrWhiteSpace(CustomPublicGuideUrl) && !IsBusy;
        public bool CanManagePublicGuidePresets => HasPublicGuideConfigSources && SelectedSourceOption != null && !IsBusy;
        public bool CanManageReviewDecisions => SelectedSourceOption != null && !IsBusy;
        public string PublicGuideActionText => SelectedPublicGuideAddMode?.Mode switch
        {
            EpgPublicGuideAddMode.AutoDetect => "Add Auto-Detected",
            EpgPublicGuideAddMode.AllCountries => "Add All",
            _ => "Add Preset"
        };
        public Visibility SingleCountryPresetVisibility => SelectedPublicGuideAddMode?.Mode == EpgPublicGuideAddMode.SingleCountry
            ? Visibility.Visible
            : Visibility.Collapsed;
        public Visibility AddPublicGuideStatusVisibility => string.IsNullOrWhiteSpace(AddPublicGuideStatusText) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility RefreshProgressVisibility => string.IsNullOrWhiteSpace(RefreshProgressText) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility EmptySourcesVisibility => GuideSources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility EmptyUnmatchedVisibility => UnmatchedChannels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility EmptyWeakVisibility => WeakMatches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility WarningListVisibility => Warnings.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        public EpgCenterViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            SelectedPublicGuideAddMode = PublicGuideAddModes.FirstOrDefault(option => option.Mode == EpgPublicGuideAddMode.AutoDetect)
                ?? PublicGuideAddModes.FirstOrDefault();
            SelectedPublicGuidePreset = SelectDefaultPublicGuidePreset();
        }

        partial void OnSearchTextChanged(string value)
        {
            ApplyFilters();
        }

        partial void OnSelectedSourceOptionChanged(EpgCenterSourceOption? value)
        {
            _ = RefreshPublicGuidePresetPreviewAsync();
        }

        partial void OnSelectedPublicGuidePresetChanged(EpgPublicGuidePreset? value)
        {
            _ = RefreshPublicGuidePresetPreviewAsync();
        }

        partial void OnSelectedPublicGuideAddModeChanged(EpgPublicGuideAddModeOption? value)
        {
            PublicGuideModeDescriptionText = value?.Description ?? string.Empty;
            _ = RefreshPublicGuidePresetPreviewAsync();
        }

        [RelayCommand]
        public async Task LoadAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            try
            {
                await LoadReportAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (!IsRefreshEnabled)
            {
                return;
            }

            IsBusy = true;
            RefreshProgressText = string.Empty;
            StatusText = "Refreshing guide data across configured sources...";
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var reportService = scope.ServiceProvider.GetRequiredService<IEpgCoverageReportService>();
                var refreshService = scope.ServiceProvider.GetRequiredService<ISourceRefreshService>();
                var report = await reportService.BuildReportAsync(db);
                var sourceIds = report.Sources
                    .Where(source => source.CanSync)
                    .Select(source => source.SourceProfileId)
                    .ToList();

                var totalSources = sourceIds.Count;
                var completedSources = 0;
                foreach (var sourceId in sourceIds)
                {
                    completedSources++;
                    RefreshProgressText = $"Refreshing EPG source {completedSources:N0} / {totalSources:N0}...";
                    await Task.Yield();
                    try
                    {
                        await refreshService.RefreshSourceAsync(sourceId, SourceRefreshTrigger.Manual, SourceRefreshScope.EpgOnly);
                    }
                    catch (Exception ex)
                    {
                        RuntimeEventLogger.Log("EPG-CENTER", ex, $"source_id={sourceId} guide refresh skipped");
                    }
                }

                RefreshProgressText = "Refresh complete. Updating coverage report...";
                await LoadReportAsync();
            }
            finally
            {
                RefreshProgressText = string.Empty;
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task AddSelectedPublicGuideAsync()
        {
            if (SelectedSourceOption == null || SelectedPublicGuideAddMode == null)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var selection = await BuildPresetSelectionAsync();
                if (selection.PresetsToAdd.Count == 0)
                {
                    AddPublicGuideStatusText = string.IsNullOrWhiteSpace(selection.EmptyMessage)
                        ? "No new public guide presets to add."
                        : selection.EmptyMessage;
                    await RefreshPublicGuidePresetPreviewAsync();
                    return;
                }

                await AddPublicGuideUrlsAsync(
                    selection.PresetsToAdd
                        .Select(preset => (preset.Url, preset.Label))
                        .ToList(),
                    selection.SuccessSuffix);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task AddCustomPublicGuideAsync()
        {
            var url = CustomPublicGuideUrl?.Trim() ?? string.Empty;
            if (!CanAddCustomPublicGuide || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
            {
                AddPublicGuideStatusText = "Enter a full public http(s) XMLTV URL.";
                return;
            }

            IsBusy = true;
            try
            {
                await AddPublicGuideUrlsAsync(
                    new[]
                    {
                        (
                            url,
                            EpgPublicGuideCatalog.BuildGuideSourceLabel(url, EpgGuideSourceKind.Public, "Public XMLTV")
                        )
                    },
                    "Refresh EPG to fetch it.");
            }
            finally
            {
                IsBusy = false;
            }

            CustomPublicGuideUrl = string.Empty;
        }

        [RelayCommand]
        public async Task ClearPublicGuidePresetsAsync()
        {
            if (!CanManagePublicGuidePresets || SelectedSourceOption == null)
            {
                return;
            }

            IsBusy = true;
            try
            {
                await RewritePublicGuideUrlsAsync(
                    SelectedSourceOption.SourceProfileId,
                    existingUrls =>
                    {
                        var retained = existingUrls
                            .Where(url => !EpgPublicGuideCatalog.TryDescribeIptvEpgOrgUrl(url, out _))
                            .ToList();
                        var removed = existingUrls.Count - retained.Count;
                        return new PublicGuideRewriteResult(
                            retained,
                            removed == 0
                                ? "No IPTV-EPG.org public guide presets are enabled for this source."
                                : $"{removed:N0} IPTV-EPG.org public guide preset(s) removed. Custom XMLTV URLs were preserved.");
                    });
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ReplaceWithAutoDetectedPublicGuidesAsync()
        {
            if (!CanManagePublicGuidePresets || SelectedSourceOption == null)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var sourceId = SelectedSourceOption.SourceProfileId;
                var inferred = (await InferPresetsForSourceAsync(sourceId))
                    .GroupBy(preset => preset.Url, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(preset => preset.CountryName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (inferred.Count == 0)
                {
                    AddPublicGuideStatusText = $"No IPTV-EPG.org country presets could be inferred from {SelectedSourceOption.SourceName}. Existing guides were left unchanged.";
                    await RefreshPublicGuidePresetPreviewAsync();
                    return;
                }

                await RewritePublicGuideUrlsAsync(
                    sourceId,
                    existingUrls =>
                    {
                        var retainedCustom = existingUrls
                            .Where(url => !EpgPublicGuideCatalog.TryDescribeIptvEpgOrgUrl(url, out _))
                            .ToList();
                        var removed = existingUrls.Count - retainedCustom.Count;
                        var updated = retainedCustom
                            .Concat(inferred.Select(preset => preset.Url))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var labels = string.Join(", ", inferred.Take(6).Select(preset => preset.CountryName));
                        if (inferred.Count > 6)
                        {
                            labels += $", +{inferred.Count - 6:N0} more";
                        }

                        return new PublicGuideRewriteResult(
                            updated,
                            $"Replaced {removed:N0} IPTV-EPG.org preset(s) with {inferred.Count:N0} auto-detected preset(s): {labels}. Custom XMLTV URLs were preserved.");
                    });
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ApproveWeakMatchAsync(EpgChannelCoverageViewModel? item)
        {
            if (item == null || IsBusy)
            {
                return;
            }

            IsBusy = true;
            try
            {
                if (await SaveMappingDecisionAsync(item, EpgMappingDecisionState.Approved))
                {
                    AddPublicGuideStatusText = $"Approved EPG mapping for {item.ChannelName}. Refresh EPG to import programmes for the approved guide.";
                }
                await LoadReportAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task RejectWeakMatchAsync(EpgChannelCoverageViewModel? item)
        {
            if (item == null || IsBusy)
            {
                return;
            }

            IsBusy = true;
            try
            {
                if (await SaveMappingDecisionAsync(item, EpgMappingDecisionState.Rejected))
                {
                    AddPublicGuideStatusText = $"Rejected EPG suggestion for {item.ChannelName}. It will not be suggested again unless review decisions are cleared.";
                }
                await LoadReportAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ClearMappingDecisionAsync(EpgChannelCoverageViewModel? item)
        {
            if (item == null || IsBusy)
            {
                return;
            }

            IsBusy = true;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var xmltvChannelId = item.MatchedXmltvChannelId.Trim();
                var existing = await db.EpgMappingDecisions
                    .Where(decision => decision.SourceProfileId == item.SourceProfileId &&
                                       decision.ChannelId == item.ChannelId &&
                                       decision.XmltvChannelId == xmltvChannelId)
                    .ToListAsync();
                if (existing.Count > 0)
                {
                    db.EpgMappingDecisions.RemoveRange(existing);
                    await db.SaveChangesAsync();
                    AddPublicGuideStatusText = $"Cleared EPG review decision for {item.ChannelName}.";
                }
                else
                {
                    AddPublicGuideStatusText = $"No EPG review decision was stored for {item.ChannelName}.";
                }

                await LoadReportAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ClearReviewDecisionsAsync()
        {
            if (!CanManageReviewDecisions || SelectedSourceOption == null)
            {
                return;
            }

            IsBusy = true;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var sourceId = SelectedSourceOption.SourceProfileId;
                var decisions = await db.EpgMappingDecisions
                    .Where(decision => decision.SourceProfileId == sourceId)
                    .ToListAsync();
                if (decisions.Count == 0)
                {
                    AddPublicGuideStatusText = $"No EPG review decisions are stored for {SelectedSourceOption.SourceName}.";
                }
                else
                {
                    db.EpgMappingDecisions.RemoveRange(decisions);
                    await db.SaveChangesAsync();
                    AddPublicGuideStatusText = $"Cleared {decisions.Count:N0} EPG review decision(s) for {SelectedSourceOption.SourceName}. Refresh EPG to rebuild suggestions.";
                }

                await LoadReportAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoadReportAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reportService = scope.ServiceProvider.GetRequiredService<IEpgCoverageReportService>();
            var report = await reportService.BuildReportAsync(db);
            ApplyReport(report);
        }

        private async Task<bool> SaveMappingDecisionAsync(EpgChannelCoverageViewModel item, EpgMappingDecisionState decisionState)
        {
            var xmltvChannelId = item.MatchedXmltvChannelId.Trim();
            if (string.IsNullOrWhiteSpace(xmltvChannelId))
            {
                AddPublicGuideStatusText = "This suggestion does not have an XMLTV channel id to review.";
                return false;
            }

            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var channel = await db.Channels.FirstOrDefaultAsync(candidate => candidate.Id == item.ChannelId);
            if (channel == null)
            {
                AddPublicGuideStatusText = "The selected channel could not be loaded.";
                return false;
            }

            var category = await db.ChannelCategories.AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == channel.ChannelCategoryId && candidate.SourceProfileId == item.SourceProfileId);
            if (category == null)
            {
                AddPublicGuideStatusText = "The selected channel source could not be verified.";
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            var decision = await db.EpgMappingDecisions
                .FirstOrDefaultAsync(candidate => candidate.SourceProfileId == item.SourceProfileId &&
                                                  candidate.ChannelId == item.ChannelId &&
                                                  candidate.XmltvChannelId == xmltvChannelId);
            if (decision == null)
            {
                decision = new EpgMappingDecision
                {
                    SourceProfileId = item.SourceProfileId,
                    ChannelId = item.ChannelId,
                    XmltvChannelId = xmltvChannelId,
                    CreatedAtUtc = nowUtc
                };
                db.EpgMappingDecisions.Add(decision);
            }

            decision.ChannelIdentityKey = channel.NormalizedIdentityKey;
            decision.ChannelName = channel.Name;
            decision.CategoryName = category.Name;
            decision.ProviderEpgChannelId = channel.ProviderEpgChannelId;
            decision.StreamUrlHash = EpgMappingDecisionIdentity.ComputeStreamUrlHash(channel.StreamUrl);
            decision.XmltvDisplayName = CleanXmltvDisplayName(item.ProposedGuideText, xmltvChannelId);
            decision.Decision = decisionState;
            decision.SuggestedMatchSource = channel.EpgMatchSource;
            decision.SuggestedConfidence = channel.EpgMatchConfidence;
            decision.ReasonSummary = TrimForStorage(item.WeakReasonText, 500);
            decision.UpdatedAtUtc = nowUtc;

            if (decisionState == EpgMappingDecisionState.Rejected &&
                string.Equals(channel.EpgChannelId, xmltvChannelId, StringComparison.OrdinalIgnoreCase) &&
                channel.EpgMatchSource is ChannelEpgMatchSource.Previous or ChannelEpgMatchSource.Alias or ChannelEpgMatchSource.Regex or ChannelEpgMatchSource.Fuzzy)
            {
                RestoreProviderGuideDefault(channel);
            }

            await db.SaveChangesAsync();
            return true;
        }

        private static void RestoreProviderGuideDefault(Channel channel)
        {
            if (!string.IsNullOrWhiteSpace(channel.ProviderEpgChannelId))
            {
                channel.EpgChannelId = channel.ProviderEpgChannelId;
                channel.EpgMatchSource = ChannelEpgMatchSource.Provider;
                channel.EpgMatchConfidence = 70;
                channel.EpgMatchSummary = "Using provider guide metadata until XMLTV confirms a better match.";
                return;
            }

            channel.EpgChannelId = string.Empty;
            channel.EpgMatchSource = ChannelEpgMatchSource.None;
            channel.EpgMatchConfidence = 0;
            channel.EpgMatchSummary = string.Empty;
        }

        private static string CleanXmltvDisplayName(string value, string fallbackXmltvChannelId)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallbackXmltvChannelId;
            }

            var trimmed = value.Trim();
            return trimmed.StartsWith("XMLTV ", StringComparison.OrdinalIgnoreCase)
                ? trimmed[6..].Trim()
                : trimmed;
        }

        private static string TrimForStorage(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private void ApplyReport(EpgCoverageReport report)
        {
            SummaryCards.Clear();
            var coveragePercent = report.TotalLiveChannels == 0
                ? 0
                : Math.Round((double)report.ChannelsWithGuideProgrammes / report.TotalLiveChannels * 100d, 1);

            SummaryCards.Add(new EpgSummaryCardViewModel
            {
                Label = "Trusted Coverage",
                Value = $"{coveragePercent:0.#}%",
                Detail = $"{report.TrustedCoverageChannels:N0} / {report.TotalLiveChannels:N0} live channels active"
            });
            SummaryCards.Add(new EpgSummaryCardViewModel
            {
                Label = "Review Suggestions",
                Value = $"{report.ReviewSuggestionChannels:N0}",
                Detail = $"{report.WeakMatches:N0} weak total, {report.ApprovedMappingDecisions:N0} approved, {report.RejectedMappingDecisions:N0} rejected"
            });
            SummaryCards.Add(new EpgSummaryCardViewModel
            {
                Label = "Potential Coverage",
                Value = $"{report.PotentialCoverageChannels:N0}",
                Detail = $"If review suggestions are approved; {report.ProgrammeCount:N0} programmes imported"
            });
            SummaryCards.Add(new EpgSummaryCardViewModel
            {
                Label = "Trusted Matches",
                Value = $"{report.ExactMatches + report.NormalizedMatches + report.ApprovedMatches:N0}",
                Detail = $"{report.ExactMatches:N0} exact, {report.NormalizedMatches:N0} normalized, {report.ApprovedMatches:N0} approved"
            });
            SummaryCards.Add(new EpgSummaryCardViewModel
            {
                Label = "Unmatched",
                Value = $"{report.UnmatchedChannels:N0}",
                Detail = $"{report.XmltvChannelCount:N0} XMLTV channels indexed"
            });

            HeadlineText = report.TotalLiveChannels == 0
                ? "No live channels available"
                : $"{report.ChannelsWithGuideProgrammes:N0} of {report.TotalLiveChannels:N0} live channels have guide programmes";
            StatusText = report.Sources.Count == 0
                ? "Add a source to start tracking EPG health."
                : BuildStatusText(report);
            LastSyncText = $"Last sync: {FormatTimestamp(report.LastSyncAttemptUtc)}";
            LastSuccessText = $"Last success: {FormatTimestamp(report.LastSuccessUtc)}";
            HasSyncableSources = report.Sources.Any(source => source.CanSync);
            ApplySourceOptions(report);

            GuideSources.Clear();
            foreach (var source in report.Sources)
            {
                if (source.GuideSources.Count == 0)
                {
                    GuideSources.Add(BuildEmptyGuideSourceRow(source));
                    continue;
                }

                foreach (var guideSource in source.GuideSources)
                {
                    GuideSources.Add(BuildGuideSourceRow(source, guideSource));
                }
            }

            Warnings.Clear();
            foreach (var warning in report.Warnings)
            {
                Warnings.Add(warning);
            }

            _allUnmatchedChannels = report.TopUnmatchedChannels.Select(BuildChannelViewModel).ToList();
            _allWeakMatches = report.TopWeakMatches.Select(BuildChannelViewModel).ToList();
            ApplyFilters();
            RaiseVisibilityChanges();
        }

        private async Task AddPublicGuideUrlsAsync(
            IReadOnlyCollection<(string Url, string Label)> guideSources,
            string successSuffix)
        {
            if (SelectedSourceOption == null)
            {
                AddPublicGuideStatusText = "Choose a source before adding a guide URL.";
                return;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var sourceId = SelectedSourceOption.SourceProfileId;
                var source = await db.SourceProfiles.AsNoTracking()
                    .FirstOrDefaultAsync(profile => profile.Id == sourceId);
                var credential = await db.SourceCredentials.AsNoTracking()
                    .FirstOrDefaultAsync(item => item.SourceProfileId == sourceId);
                if (source == null || credential == null)
                {
                    AddPublicGuideStatusText = "The selected source could not be loaded.";
                    return;
                }

                var guideUrls = EpgPublicGuideCatalog.SplitGuideUrls(credential.FallbackEpgUrls).ToList();
                var existingUrls = guideUrls.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var newGuideSources = guideSources
                    .Where(item => !string.IsNullOrWhiteSpace(item.Url))
                    .GroupBy(item => item.Url.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Where(item => !existingUrls.Contains(item.Url.Trim()))
                    .ToList();

                if (newGuideSources.Count == 0)
                {
                    var label = guideSources.Count == 1
                        ? guideSources.First().Label
                        : "Selected public guide presets";
                    AddPublicGuideStatusText = $"{label} already enabled for {source.Name}.";
                    await LoadReportAsync();
                    await RefreshPublicGuidePresetPreviewAsync();
                    return;
                }

                guideUrls.AddRange(newGuideSources.Select(item => item.Url.Trim()));
                var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceLifecycleService>();
                await lifecycle.UpdateGuideSettingsAsync(
                    new SourceGuideSettingsUpdateRequest
                    {
                        SourceId = sourceId,
                        ActiveMode = credential.EpgMode == EpgActiveMode.None ? EpgActiveMode.Detected : credential.EpgMode,
                        ManualEpgUrl = credential.ManualEpgUrl,
                        FallbackEpgUrls = string.Join(Environment.NewLine, guideUrls),
                        ProxyScope = credential.ProxyScope,
                        ProxyUrl = credential.ProxyUrl,
                        CompanionScope = credential.CompanionScope,
                        CompanionMode = credential.CompanionMode,
                        CompanionUrl = credential.CompanionUrl
                    },
                    syncNow: false);

                var addedLabel = newGuideSources.Count == 1
                    ? newGuideSources[0].Label
                    : $"{newGuideSources.Count:N0} public guide presets";
                AddPublicGuideStatusText = $"{addedLabel} added to {source.Name}. {successSuffix}";
                await LoadReportAsync();
                await RefreshPublicGuidePresetPreviewAsync();
            }
            catch (Exception ex)
            {
                RuntimeEventLogger.Log("EPG-CENTER", ex, "public guide add failed");
                AddPublicGuideStatusText = ex.Message;
            }
        }

        private async Task RewritePublicGuideUrlsAsync(
            int sourceId,
            Func<IReadOnlyList<string>, PublicGuideRewriteResult> rewrite)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var source = await db.SourceProfiles.AsNoTracking()
                    .FirstOrDefaultAsync(profile => profile.Id == sourceId);
                var credential = await db.SourceCredentials.AsNoTracking()
                    .FirstOrDefaultAsync(item => item.SourceProfileId == sourceId);
                if (source == null || credential == null)
                {
                    AddPublicGuideStatusText = "The selected source could not be loaded.";
                    return;
                }

                var existingUrls = EpgPublicGuideCatalog.SplitGuideUrls(credential.FallbackEpgUrls);
                var result = rewrite(existingUrls);
                var normalized = result.UpdatedUrls
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Select(url => url.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var unchanged = existingUrls.SequenceEqual(normalized, StringComparer.OrdinalIgnoreCase);

                if (!unchanged)
                {
                    var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceLifecycleService>();
                    await lifecycle.UpdateGuideSettingsAsync(
                        new SourceGuideSettingsUpdateRequest
                        {
                            SourceId = sourceId,
                            ActiveMode = credential.EpgMode == EpgActiveMode.None ? EpgActiveMode.Detected : credential.EpgMode,
                            ManualEpgUrl = credential.ManualEpgUrl,
                            FallbackEpgUrls = string.Join(Environment.NewLine, normalized),
                            ProxyScope = credential.ProxyScope,
                            ProxyUrl = credential.ProxyUrl,
                            CompanionScope = credential.CompanionScope,
                            CompanionMode = credential.CompanionMode,
                            CompanionUrl = credential.CompanionUrl
                        },
                        syncNow: false);
                }

                AddPublicGuideStatusText = result.StatusText;
                await LoadReportAsync();
                await RefreshPublicGuidePresetPreviewAsync();
            }
            catch (Exception ex)
            {
                RuntimeEventLogger.Log("EPG-CENTER", ex, "public guide rewrite failed");
                AddPublicGuideStatusText = ex.Message;
            }
        }

        private async Task RefreshPublicGuidePresetPreviewAsync()
        {
            if (SelectedSourceOption == null || SelectedPublicGuideAddMode == null)
            {
                PublicGuidePresetCandidateCount = 0;
                PublicGuidePresetPreviewText = "Choose a source and preset mode.";
                return;
            }

            try
            {
                var selection = await BuildPresetSelectionAsync();
                PublicGuidePresetCandidateCount = selection.PresetsToAdd.Count;
                PublicGuidePresetPreviewText = selection.PreviewText;
            }
            catch (Exception ex)
            {
                RuntimeEventLogger.Log("EPG-CENTER", ex, "public guide preset preview failed");
                PublicGuidePresetCandidateCount = 0;
                PublicGuidePresetPreviewText = "Could not inspect public guide presets for this source.";
            }
        }

        private async Task<PublicGuidePresetSelection> BuildPresetSelectionAsync()
        {
            if (SelectedSourceOption == null || SelectedPublicGuideAddMode == null)
            {
                return PublicGuidePresetSelection.Empty("Choose a source and preset mode.");
            }

            var sourceState = await LoadPublicGuideSourceStateAsync(SelectedSourceOption.SourceProfileId);
            if (sourceState == null)
            {
                return PublicGuidePresetSelection.Empty("The selected source could not be loaded.");
            }

            var mode = SelectedPublicGuideAddMode.Mode;
            var existing = sourceState.ExistingFallbackUrls.ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<EpgPublicGuideInferenceResult> autoDetectEvidence = Array.Empty<EpgPublicGuideInferenceResult>();
            var candidates = mode switch
            {
                EpgPublicGuideAddMode.SingleCountry => SelectedPublicGuidePreset == null
                    ? Array.Empty<EpgPublicGuidePreset>()
                    : new[] { SelectedPublicGuidePreset },
                EpgPublicGuideAddMode.AutoDetect => (autoDetectEvidence = await InferPresetEvidenceForSourceAsync(sourceState.SourceProfileId))
                    .Select(result => result.Preset)
                    .ToList(),
                EpgPublicGuideAddMode.AllCountries => EpgPublicGuideCatalog.IptvEpgOrgPresets,
                _ => Array.Empty<EpgPublicGuidePreset>()
            };

            var distinctCandidates = candidates
                .GroupBy(preset => preset.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(preset => preset.CountryName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var presetsToAdd = distinctCandidates
                .Where(preset => !existing.Contains(preset.Url))
                .ToList();
            var duplicateCount = distinctCandidates.Count - presetsToAdd.Count;

            return mode switch
            {
                EpgPublicGuideAddMode.SingleCountry => BuildSingleCountrySelection(distinctCandidates, presetsToAdd, sourceState.SourceName),
                EpgPublicGuideAddMode.AutoDetect => BuildAutoDetectSelection(distinctCandidates, presetsToAdd, duplicateCount, sourceState.SourceName, autoDetectEvidence),
                EpgPublicGuideAddMode.AllCountries => BuildAllCountriesSelection(distinctCandidates, presetsToAdd, duplicateCount, sourceState.SourceName),
                _ => PublicGuidePresetSelection.Empty("Choose a preset mode.")
            };
        }

        private async Task<PublicGuideSourceState?> LoadPublicGuideSourceStateAsync(int sourceProfileId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var source = await db.SourceProfiles.AsNoTracking()
                .Where(profile => profile.Id == sourceProfileId)
                .Select(profile => new
                {
                    profile.Id,
                    profile.Name
                })
                .FirstOrDefaultAsync();
            var credential = await db.SourceCredentials.AsNoTracking()
                .FirstOrDefaultAsync(item => item.SourceProfileId == sourceProfileId);
            if (source == null || credential == null)
            {
                return null;
            }

            return new PublicGuideSourceState(
                source.Id,
                source.Name,
                EpgPublicGuideCatalog.SplitGuideUrls(credential.FallbackEpgUrls));
        }

        private async Task<IReadOnlyList<EpgPublicGuidePreset>> InferPresetsForSourceAsync(int sourceProfileId)
        {
            return (await InferPresetEvidenceForSourceAsync(sourceProfileId))
                .Select(result => result.Preset)
                .ToList();
        }

        private async Task<IReadOnlyList<EpgPublicGuideInferenceResult>> InferPresetEvidenceForSourceAsync(int sourceProfileId)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sourceName = await db.SourceProfiles.AsNoTracking()
                .Where(profile => profile.Id == sourceProfileId)
                .Select(profile => profile.Name)
                .FirstOrDefaultAsync();
            var categoryNames = await db.ChannelCategories.AsNoTracking()
                .Where(category => category.SourceProfileId == sourceProfileId)
                .Select(category => category.Name)
                .ToListAsync();
            var channelNames = await db.ChannelCategories.AsNoTracking()
                .Where(category => category.SourceProfileId == sourceProfileId)
                .Join(
                    db.Channels.AsNoTracking(),
                    category => category.Id,
                    channel => channel.ChannelCategoryId,
                    (category, channel) => channel.Name)
                .ToListAsync();

            return EpgPublicGuideCatalog.InferPresetEvidenceFromSourceText(
                categoryNames
                    .Concat(channelNames)
                    .Concat(new[] { sourceName ?? string.Empty }));
        }

        private static PublicGuidePresetSelection BuildSingleCountrySelection(
            IReadOnlyList<EpgPublicGuidePreset> candidates,
            IReadOnlyList<EpgPublicGuidePreset> presetsToAdd,
            string sourceName)
        {
            if (candidates.Count == 0)
            {
                return PublicGuidePresetSelection.Empty("Choose an IPTV-EPG.org country preset.");
            }

            var preset = candidates[0];
            if (presetsToAdd.Count == 0)
            {
                return PublicGuidePresetSelection.Empty($"{preset.Label} is already enabled for {sourceName}.");
            }

            return new PublicGuidePresetSelection(
                presetsToAdd,
                $"1 preset will be added: {preset.Label}.",
                $"{preset.Label} is already enabled for {sourceName}.",
                "Refresh EPG to fetch it.");
        }

        private static PublicGuidePresetSelection BuildAutoDetectSelection(
            IReadOnlyList<EpgPublicGuidePreset> candidates,
            IReadOnlyList<EpgPublicGuidePreset> presetsToAdd,
            int duplicateCount,
            string sourceName,
            IReadOnlyList<EpgPublicGuideInferenceResult> inference)
        {
            if (candidates.Count == 0)
            {
                return PublicGuidePresetSelection.Empty($"No IPTV-EPG.org country presets could be inferred from {sourceName}.");
            }

            var inferenceByCode = inference.ToDictionary(result => result.Preset.CountryCode, StringComparer.OrdinalIgnoreCase);
            var detectedSummary = string.Join(", ", candidates.Take(6).Select(preset =>
                inferenceByCode.TryGetValue(preset.CountryCode, out var result)
                    ? $"{preset.CountryName} ({result.EvidenceSummary})"
                    : preset.CountryName));
            if (candidates.Count > 6)
            {
                detectedSummary += $", +{candidates.Count - 6:N0} more";
            }

            if (presetsToAdd.Count == 0)
            {
                return PublicGuidePresetSelection.Empty($"Auto-detect found {candidates.Count:N0} ranked preset(s), but all are already enabled for {sourceName}: {detectedSummary}.");
            }

            var labels = string.Join(", ", presetsToAdd.Take(6).Select(preset =>
                inferenceByCode.TryGetValue(preset.CountryCode, out var result)
                    ? $"{preset.CountryName} ({result.EvidenceSummary})"
                    : preset.CountryName));
            if (presetsToAdd.Count > 6)
            {
                labels += $", +{presetsToAdd.Count - 6:N0} more";
            }

            var duplicateText = duplicateCount == 0 ? string.Empty : $" {duplicateCount:N0} already enabled.";
            return new PublicGuidePresetSelection(
                presetsToAdd,
                $"Auto-detect will add {presetsToAdd.Count:N0} ranked preset(s): {labels}.{duplicateText}",
                $"Auto-detect found {candidates.Count:N0} preset(s), but all are already enabled for {sourceName}.",
                "Refresh EPG to fetch the auto-detected public guides.");
        }

        private static PublicGuidePresetSelection BuildAllCountriesSelection(
            IReadOnlyList<EpgPublicGuidePreset> candidates,
            IReadOnlyList<EpgPublicGuidePreset> presetsToAdd,
            int duplicateCount,
            string sourceName)
        {
            if (presetsToAdd.Count == 0)
            {
                return PublicGuidePresetSelection.Empty($"All {candidates.Count:N0} IPTV-EPG.org country guides are already enabled for {sourceName}.");
            }

            var duplicateText = duplicateCount == 0 ? string.Empty : $" {duplicateCount:N0} already enabled.";
            return new PublicGuidePresetSelection(
                presetsToAdd,
                $"{presetsToAdd.Count:N0} of {candidates.Count:N0} IPTV-EPG.org country guides will be added.{duplicateText} Refresh can take longer and may increase weak-match noise.",
                $"All {candidates.Count:N0} IPTV-EPG.org country guides are already enabled for {sourceName}.",
                "Refresh EPG to fetch them. This can take longer and may increase weak-match noise.");
        }

        private void ApplySourceOptions(EpgCoverageReport report)
        {
            var previousSourceId = SelectedSourceOption?.SourceProfileId;
            SourceOptions.Clear();
            foreach (var source in report.Sources)
            {
                SourceOptions.Add(new EpgCenterSourceOption
                {
                    SourceProfileId = source.SourceProfileId,
                    SourceName = source.SourceName,
                    SourceType = source.SourceType
                });
            }

            HasPublicGuideConfigSources = SourceOptions.Count > 0;
            SelectedSourceOption = SourceOptions.FirstOrDefault(option => option.SourceProfileId == previousSourceId)
                ?? SourceOptions.FirstOrDefault();
            _ = RefreshPublicGuidePresetPreviewAsync();
        }

        private void ApplyFilters()
        {
            var query = SearchText?.Trim() ?? string.Empty;
            ApplyFilter(UnmatchedChannels, _allUnmatchedChannels, query);
            ApplyFilter(WeakMatches, _allWeakMatches, query);
            RaiseVisibilityChanges();
        }

        private static void ApplyFilter(
            ObservableCollection<EpgChannelCoverageViewModel> target,
            IEnumerable<EpgChannelCoverageViewModel> source,
            string query)
        {
            target.Clear();
            var rows = string.IsNullOrWhiteSpace(query)
                ? source
                : source.Where(item => item.SearchKey.Contains(query, StringComparison.OrdinalIgnoreCase));
            foreach (var row in rows.Take(100))
            {
                target.Add(row);
            }
        }

        private void RaiseVisibilityChanges()
        {
            OnPropertyChanged(nameof(EmptySourcesVisibility));
            OnPropertyChanged(nameof(EmptyUnmatchedVisibility));
            OnPropertyChanged(nameof(EmptyWeakVisibility));
            OnPropertyChanged(nameof(WarningListVisibility));
        }

        private static EpgChannelCoverageViewModel BuildChannelViewModel(EpgChannelCoverageReportItem item)
        {
            var guideId = string.IsNullOrWhiteSpace(item.ProviderEpgChannelId)
                ? "No provider tvg-id"
                : $"Provider tvg-id {item.ProviderEpgChannelId}";
            var matchedId = string.IsNullOrWhiteSpace(item.MatchedXmltvChannelId)
                ? "No XMLTV match"
                : $"XMLTV {item.MatchedXmltvChannelId}";

            return new EpgChannelCoverageViewModel
            {
                ChannelId = item.ChannelId,
                SourceProfileId = item.SourceProfileId,
                ChannelName = item.ChannelName,
                SourceName = item.SourceName,
                CategoryName = item.CategoryName,
                MatchedXmltvChannelId = item.MatchedXmltvChannelId,
                MatchText = $"{item.MatchType} - confidence {item.MatchConfidence}",
                DetailText = $"{item.CategoryName} - {guideId} - {matchedId} - {item.ProgrammeCount:N0} programmes",
                ProposedGuideText = matchedId,
                ReviewStatusText = string.IsNullOrWhiteSpace(item.ReviewStatus)
                    ? (item.IsActiveGuideAssignment ? "Active guide assignment" : "Review needed")
                    : item.ReviewStatus,
                WeakReasonText = string.IsNullOrWhiteSpace(item.MatchSummary)
                    ? "No match diagnostic was recorded."
                    : item.MatchSummary,
                CanApprove = item.CanApprove,
                CanReject = item.CanReject,
                CanClearDecision = item.CanClearDecision,
                SearchKey = $"{item.ChannelName} {item.SourceName} {item.CategoryName} {item.ProviderEpgChannelId} {item.MatchedXmltvChannelId} {item.MatchSummary}"
            };
        }

        private static string BuildStatusText(EpgCoverageReport report)
        {
            if (report.TotalLiveChannels == 0)
            {
                return "EPG coverage is waiting for imported live channels.";
            }

            return $"{report.ExactMatches:N0} exact matches, {report.NormalizedMatches:N0} normalized matches, {report.ApprovedMatches:N0} approved mappings, {report.ReviewSuggestionChannels:N0} review suggestions, {report.UnmatchedChannels:N0} unmatched channels.";
        }

        private static string BuildSourceStatusText(EpgSourceCoverageReportItem source)
        {
            return source.Status switch
            {
                EpgStatus.Ready => source.ResultCode == EpgSyncResultCode.PartialMatch ? "Guide partial" : "Guide ready",
                EpgStatus.ManualOverride => source.ResultCode == EpgSyncResultCode.PartialMatch ? "Manual guide partial" : "Manual guide ready",
                EpgStatus.Syncing => "Guide syncing",
                EpgStatus.Stale => "Guide stale",
                EpgStatus.FailedFetchOrParse => "Guide failed",
                EpgStatus.UnavailableNoXmltv => "No provider guide",
                _ => "Guide not synced"
            };
        }

        private static EpgSourceStatusViewModel BuildEmptyGuideSourceRow(EpgSourceCoverageReportItem source)
        {
            return new EpgSourceStatusViewModel
            {
                SourceProfileId = source.SourceProfileId,
                SourceName = source.SourceName,
                GuideSourceName = "No XMLTV source",
                StatusText = BuildSourceStatusText(source),
                CoverageText = $"{source.ChannelsWithGuideProgrammes:N0}/{source.TotalLiveChannels:N0} channels covered",
                SyncText = $"Attempt {FormatTimestamp(source.LastSyncAttemptUtc)} - Success {FormatTimestamp(source.LastSuccessUtc)}",
                SourceText = "No XMLTV source configured or detected.",
                WarningText = BuildWarningText(source)
            };
        }

        private static EpgSourceStatusViewModel BuildGuideSourceRow(
            EpgSourceCoverageReportItem source,
            EpgGuideSourceStatusSnapshot guideSource)
        {
            return new EpgSourceStatusViewModel
            {
                SourceProfileId = source.SourceProfileId,
                SourceName = source.SourceName,
                GuideSourceName = guideSource.Label,
                StatusText = $"{BuildGuideSourceKindText(guideSource.Kind)} - {guideSource.Status}",
                CoverageText = $"{guideSource.XmltvChannelCount:N0} XMLTV channels, {guideSource.ProgrammeCount:N0} programmes",
                SyncText = BuildGuideSourceSyncText(source, guideSource),
                SourceText = string.IsNullOrWhiteSpace(guideSource.Url) ? "No URL recorded." : guideSource.Url,
                WarningText = BuildGuideSourceWarningText(source, guideSource)
            };
        }

        private static string BuildGuideSourceSyncText(
            EpgSourceCoverageReportItem source,
            EpgGuideSourceStatusSnapshot guideSource)
        {
            var parts = new List<string>
            {
                $"Attempt {FormatTimestamp(guideSource.CheckedAtUtc ?? source.LastSyncAttemptUtc)}",
                $"Success {FormatTimestamp(guideSource.Status == EpgGuideSourceStatus.Ready ? source.LastSuccessUtc : null)}"
            };

            if (guideSource.FetchDurationMs > 0)
            {
                parts.Add($"Fetch {FormatDuration(guideSource.FetchDurationMs)}");
            }

            if (guideSource.WasContentUnchanged)
            {
                parts.Add("unchanged");
            }
            else if (guideSource.WasCacheHit)
            {
                parts.Add("cache");
            }

            return string.Join(" - ", parts);
        }

        private static string BuildGuideSourceKindText(EpgGuideSourceKind kind)
        {
            return kind switch
            {
                EpgGuideSourceKind.Provider => "Provider",
                EpgGuideSourceKind.Manual => "Manual",
                EpgGuideSourceKind.Public => "Public",
                EpgGuideSourceKind.Custom => "Custom",
                _ => "Guide"
            };
        }

        private static string BuildGuideSourceWarningText(EpgSourceCoverageReportItem source, EpgGuideSourceStatusSnapshot guideSource)
        {
            if (guideSource.Status is EpgGuideSourceStatus.Failed or EpgGuideSourceStatus.Skipped)
            {
                return string.IsNullOrWhiteSpace(guideSource.Message) ? source.FailureReason : guideSource.Message;
            }

            if (guideSource.Status == EpgGuideSourceStatus.Pending)
            {
                return guideSource.Message;
            }

            return string.Empty;
        }

        private static string BuildWarningText(EpgSourceCoverageReportItem source)
        {
            if (!string.IsNullOrWhiteSpace(source.WarningSummary))
            {
                return source.WarningSummary;
            }

            return source.Status is EpgStatus.FailedFetchOrParse or EpgStatus.Stale
                ? source.FailureReason
                : string.Empty;
        }

        private static string FormatTimestamp(DateTime? utc)
        {
            return utc.HasValue ? utc.Value.ToLocalTime().ToString("g") : "Never";
        }

        private static string FormatDuration(long durationMs)
        {
            return durationMs >= 1000
                ? $"{durationMs / 1000d:0.0}s"
                : $"{durationMs:N0}ms";
        }

        private static EpgPublicGuidePreset? SelectDefaultPublicGuidePreset()
        {
            try
            {
                var regionCode = RegionInfo.CurrentRegion.TwoLetterISORegionName;
                return EpgPublicGuideCatalog.IptvEpgOrgPresets
                    .FirstOrDefault(preset => string.Equals(preset.CountryCode, regionCode, StringComparison.OrdinalIgnoreCase))
                    ?? EpgPublicGuideCatalog.IptvEpgOrgPresets.FirstOrDefault(preset => preset.CountryCode == "US")
                    ?? EpgPublicGuideCatalog.IptvEpgOrgPresets.FirstOrDefault();
            }
            catch
            {
                return EpgPublicGuideCatalog.IptvEpgOrgPresets.FirstOrDefault();
            }
        }

        private sealed class PublicGuidePresetSelection
        {
            public PublicGuidePresetSelection(
                IReadOnlyList<EpgPublicGuidePreset> presetsToAdd,
                string previewText,
                string emptyMessage,
                string successSuffix)
            {
                PresetsToAdd = presetsToAdd;
                PreviewText = previewText;
                EmptyMessage = emptyMessage;
                SuccessSuffix = successSuffix;
            }

            public IReadOnlyList<EpgPublicGuidePreset> PresetsToAdd { get; }
            public string PreviewText { get; }
            public string EmptyMessage { get; }
            public string SuccessSuffix { get; }

            public static PublicGuidePresetSelection Empty(string message)
            {
                return new PublicGuidePresetSelection(
                    Array.Empty<EpgPublicGuidePreset>(),
                    message,
                    message,
                    string.Empty);
            }
        }

        private sealed record PublicGuideSourceState(
            int SourceProfileId,
            string SourceName,
            IReadOnlyList<string> ExistingFallbackUrls);

        private sealed record PublicGuideRewriteResult(
            IReadOnlyList<string> UpdatedUrls,
            string StatusText);
    }
}
