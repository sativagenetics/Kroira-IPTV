#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kroira.App.Data;
using Kroira.App.Models;
using Kroira.App.Services;
using Kroira.App.Services.Playback;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Kroira.App.ViewModels
{
    public sealed class GuideTimelineSlotViewModel
    {
        public string Label { get; set; } = string.Empty;
        public double Width { get; set; }
    }

    public sealed class GuideTimelineProgramViewModel
    {
        public int ChannelId { get; set; }
        public int SourceProfileId { get; set; }
        public string ChannelName { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string DetailText { get; set; } = string.Empty;
        public DateTime StartTimeUtc { get; set; }
        public DateTime EndTimeUtc { get; set; }
        public double Left { get; set; }
        public double Width { get; set; }
        public string TimeText => $"{StartTimeUtc.ToLocalTime():HH:mm} - {EndTimeUtc.ToLocalTime():HH:mm}";
        public Visibility TitleVisibility => Width >= 34 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility TimeVisibility => Width >= 74 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DetailVisibility => Width >= 126 && !string.IsNullOrWhiteSpace(DetailText) ? Visibility.Visible : Visibility.Collapsed;
        public int TitleMaxLines => Width >= 112 ? 2 : 1;
    }

    public sealed class GuideTimelineChannelViewModel
    {
        public int ChannelId { get; set; }
        public int SourceProfileId { get; set; }
        public string ChannelName { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string MatchText { get; set; } = string.Empty;
        public string CurrentText { get; set; } = string.Empty;
        public string NextText { get; set; } = string.Empty;
        public double TimelineWidth { get; set; }
        public double NowMarkerLeft { get; set; }
        public Visibility NowMarkerVisibility { get; set; } = Visibility.Collapsed;
        public Visibility EmptyGuideVisibility => Programs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public ObservableCollection<GuideTimelineProgramViewModel> Programs { get; } = new();
    }

    public sealed class ManualMatchChannelViewModel
    {
        public int ChannelId { get; set; }
        public int SourceProfileId { get; set; }
        public string ChannelName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string CurrentXmltvChannelId { get; set; } = string.Empty;
        public string MatchText { get; set; } = string.Empty;
        public string DisplayName => $"{ChannelName} - {CategoryName}";
    }

    public sealed class ManualMatchCandidateViewModel
    {
        public string XmltvChannelId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string MatchText { get; set; } = string.Empty;
        public string DetailText { get; set; } = string.Empty;
    }

    public partial class EpgCenterViewModel
    {
        private const double TimelineSlotWidth = 120d;
        private CancellationTokenSource? _guideTimelineLoadCts;
        private CancellationTokenSource? _manualMatchLoadCts;
        private CancellationTokenSource? _manualMatchSearchCts;
        private DateTime _guideRangeStartUtc = EpgGuideTimelineService.AlignToSlot(DateTime.UtcNow, TimeSpan.FromMinutes(30));
        private bool _suppressGuideTimelineFilterReload;
        private bool _suppressManualMatchReload;

        public ObservableCollection<EpgGuideTimelineSourceOption> GuideSourceFilterOptions { get; } = new();
        public ObservableCollection<EpgGuideTimelineCategoryOption> GuideCategoryOptions { get; } = new();
        public ObservableCollection<GuideTimelineSlotViewModel> GuideTimelineSlots { get; } = new();
        public ObservableCollection<GuideTimelineChannelViewModel> GuideTimelineRows { get; } = new();
        public ObservableCollection<ManualMatchChannelViewModel> ManualMatchChannels { get; } = new();
        public ObservableCollection<ManualMatchCandidateViewModel> ManualMatchCandidates { get; } = new();

        [ObservableProperty]
        private EpgGuideTimelineSourceOption? _selectedGuideSourceFilter;

        [ObservableProperty]
        private EpgGuideTimelineCategoryOption? _selectedGuideCategoryOption;

        [ObservableProperty]
        private string _guideSearchText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(GuideTimelineBusyVisibility))]
        private bool _isGuideTimelineBusy;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(GuideTimelineEmptyVisibility))]
        private bool _hasGuideTimelineData;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(GuideStaleWarningVisibility))]
        private string _guideStaleWarningText = string.Empty;

        [ObservableProperty]
        private string _guideRangeText = string.Empty;

        [ObservableProperty]
        private double _guideTimelineWidth = 960d;

        [ObservableProperty]
        private double _guideNowMarkerLeft;

        [ObservableProperty]
        private Visibility _guideNowMarkerVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private ManualMatchChannelViewModel? _selectedManualMatchChannel;

        [ObservableProperty]
        private string _manualMatchSearchText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ManualMatchStatusVisibility))]
        private string _manualMatchStatusText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ManualMatchBusyVisibility))]
        private bool _isManualMatchBusy;

        public Visibility GuideTimelineBusyVisibility => IsGuideTimelineBusy ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GuideTimelineEmptyVisibility => HasGuideTimelineData ? Visibility.Collapsed : Visibility.Visible;
        public Visibility GuideStaleWarningVisibility => string.IsNullOrWhiteSpace(GuideStaleWarningText) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ManualMatchStatusVisibility => string.IsNullOrWhiteSpace(ManualMatchStatusText) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ManualMatchBusyVisibility => IsManualMatchBusy ? Visibility.Visible : Visibility.Collapsed;

        partial void OnSelectedGuideSourceFilterChanged(EpgGuideTimelineSourceOption? value)
        {
            if (_suppressGuideTimelineFilterReload)
            {
                return;
            }

            SelectedGuideCategoryOption = null;
            _ = LoadGuideTimelineAsync();
        }

        partial void OnSelectedGuideCategoryOptionChanged(EpgGuideTimelineCategoryOption? value)
        {
            if (!_suppressGuideTimelineFilterReload)
            {
                _ = LoadGuideTimelineAsync();
            }
        }

        partial void OnGuideSearchTextChanged(string value)
        {
            _ = LoadGuideTimelineAsync();
        }

        partial void OnSelectedManualMatchChannelChanged(ManualMatchChannelViewModel? value)
        {
            RefreshManualMatchStatus();
            if (!_suppressManualMatchReload)
            {
                _ = SearchManualMatchesAsync();
            }
        }

        partial void OnManualMatchSearchTextChanged(string value)
        {
            _ = SearchManualMatchesAsync();
        }

        [RelayCommand]
        public Task RefreshGuideTimelineAsync()
        {
            return LoadGuideTimelineAsync();
        }

        [RelayCommand]
        public Task JumpGuideToNowAsync()
        {
            _guideRangeStartUtc = EpgGuideTimelineService.AlignToSlot(DateTime.UtcNow, TimeSpan.FromMinutes(30));
            return LoadGuideTimelineAsync();
        }

        [RelayCommand]
        public async Task SetManualOverrideAsync(ManualMatchCandidateViewModel? candidate)
        {
            if (candidate == null || SelectedManualMatchChannel == null || SelectedSourceOption == null)
            {
                return;
            }

            IsManualMatchBusy = true;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var service = scope.ServiceProvider.GetRequiredService<IEpgManualMatchService>();
                await service.SetOverrideAsync(
                    db,
                    SelectedSourceOption.SourceProfileId,
                    SelectedManualMatchChannel.ChannelId,
                    candidate.XmltvChannelId,
                    candidate.DisplayName);
                ManualMatchStatusText = $"Manual override set: {SelectedManualMatchChannel.ChannelName} -> {candidate.XmltvChannelId}.";
                await LoadManualMatchingAsync();
                await LoadGuideTimelineAsync();
            }
            finally
            {
                IsManualMatchBusy = false;
            }
        }

        [RelayCommand]
        public async Task ClearManualOverrideAsync()
        {
            if (SelectedManualMatchChannel == null || SelectedSourceOption == null)
            {
                return;
            }

            IsManualMatchBusy = true;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var service = scope.ServiceProvider.GetRequiredService<IEpgManualMatchService>();
                await service.ClearOverrideAsync(
                    db,
                    SelectedSourceOption.SourceProfileId,
                    SelectedManualMatchChannel.ChannelId);
                ManualMatchStatusText = $"Manual override cleared for {SelectedManualMatchChannel.ChannelName}.";
                await LoadManualMatchingAsync();
                await LoadGuideTimelineAsync();
            }
            finally
            {
                IsManualMatchBusy = false;
            }
        }

        public async Task ReloadGuideSurfacesAsync()
        {
            await LoadGuideTimelineAsync();
            await LoadManualMatchingAsync();
        }

        public PlaybackLaunchContext? CreatePlaybackContext(GuideTimelineProgramViewModel? program)
        {
            if (program == null || string.IsNullOrWhiteSpace(program.StreamUrl))
            {
                return null;
            }

            return new PlaybackLaunchContext
            {
                ContentId = program.ChannelId,
                ContentType = PlaybackContentType.Channel,
                PreferredSourceProfileId = program.SourceProfileId,
                CatalogStreamUrl = program.StreamUrl,
                StreamUrl = program.StreamUrl,
                LiveStreamUrl = program.StreamUrl,
                StartPositionMs = 0
            };
        }

        public PlaybackLaunchContext? CreatePlaybackContext(GuideTimelineChannelViewModel? channel)
        {
            if (channel == null || string.IsNullOrWhiteSpace(channel.StreamUrl))
            {
                return null;
            }

            return new PlaybackLaunchContext
            {
                ContentId = channel.ChannelId,
                ContentType = PlaybackContentType.Channel,
                PreferredSourceProfileId = channel.SourceProfileId,
                CatalogStreamUrl = channel.StreamUrl,
                StreamUrl = channel.StreamUrl,
                LiveStreamUrl = channel.StreamUrl,
                StartPositionMs = 0
            };
        }

        private async Task LoadGuideTimelineAsync()
        {
            _guideTimelineLoadCts?.Cancel();
            _guideTimelineLoadCts = new CancellationTokenSource();
            var cancellationToken = _guideTimelineLoadCts.Token;
            IsGuideTimelineBusy = true;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var service = scope.ServiceProvider.GetRequiredService<IEpgGuideTimelineService>();
                var result = await service.BuildTimelineAsync(
                    db,
                    new EpgGuideTimelineRequest
                    {
                        SourceProfileId = SelectedGuideSourceFilter?.SourceProfileId,
                        CategoryId = SelectedGuideCategoryOption?.CategoryId,
                        SearchText = GuideSearchText,
                        RangeStartUtc = _guideRangeStartUtc,
                        RangeDuration = TimeSpan.FromHours(4),
                        SlotDuration = TimeSpan.FromMinutes(30),
                        NowUtc = DateTime.UtcNow,
                        MaxChannels = 120
                    },
                    cancellationToken);

                ApplyGuideTimeline(result);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    IsGuideTimelineBusy = false;
                }
            }
        }

        private async Task LoadManualMatchingAsync()
        {
            _manualMatchSearchCts?.Cancel();
            _manualMatchSearchCts = new CancellationTokenSource();
            var cancellationToken = _manualMatchSearchCts.Token;
            ManualMatchChannels.Clear();
            ManualMatchCandidates.Clear();

            if (SelectedSourceOption == null)
            {
                ManualMatchStatusText = "Choose a source to review manual guide matches.";
                return;
            }

            IsManualMatchBusy = true;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var service = scope.ServiceProvider.GetRequiredService<IEpgManualMatchService>();
                var channels = await service.GetChannelsAsync(db, SelectedSourceOption.SourceProfileId, cancellationToken);
                var previousChannelId = SelectedManualMatchChannel?.ChannelId;
                foreach (var channel in channels.Select(BuildManualChannelViewModel))
                {
                    ManualMatchChannels.Add(channel);
                }

                _suppressManualMatchReload = true;
                try
                {
                    SelectedManualMatchChannel = ManualMatchChannels.FirstOrDefault(channel => channel.ChannelId == previousChannelId)
                        ?? ManualMatchChannels.FirstOrDefault();
                }
                finally
                {
                    _suppressManualMatchReload = false;
                }

                RefreshManualMatchStatus();
                await SearchManualMatchesAsync();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    IsManualMatchBusy = false;
                }
            }
        }

        private async Task SearchManualMatchesAsync()
        {
            if (SelectedSourceOption == null || SelectedManualMatchChannel == null)
            {
                ManualMatchCandidates.Clear();
                return;
            }

            _manualMatchLoadCts?.Cancel();
            _manualMatchLoadCts = new CancellationTokenSource();
            var cancellationToken = _manualMatchLoadCts.Token;
            IsManualMatchBusy = true;

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var service = scope.ServiceProvider.GetRequiredService<IEpgManualMatchService>();
                var query = string.IsNullOrWhiteSpace(ManualMatchSearchText)
                    ? SelectedManualMatchChannel.ChannelName
                    : ManualMatchSearchText;
                var candidates = await service.SearchXmltvChannelsAsync(
                    db,
                    SelectedSourceOption.SourceProfileId,
                    query,
                    50,
                    cancellationToken);

                ManualMatchCandidates.Clear();
                foreach (var candidate in candidates.Select(BuildManualCandidateViewModel))
                {
                    ManualMatchCandidates.Add(candidate);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    IsManualMatchBusy = false;
                }
            }
        }

        private void ApplyGuideTimeline(EpgGuideTimelineResult result)
        {
            _guideRangeStartUtc = result.RangeStartUtc;
            GuideTimelineWidth = Math.Max(TimelineSlotWidth, result.Slots.Count * TimelineSlotWidth);
            GuideRangeText = $"{result.RangeStartUtc.ToLocalTime():ddd, MMM d HH:mm} - {result.RangeEndUtc.ToLocalTime():HH:mm}";
            GuideStaleWarningText = result.StaleWarningText;
            HasGuideTimelineData = result.HasGuideData && result.Channels.Count > 0;

            _suppressGuideTimelineFilterReload = true;
            try
            {
                var previousSourceId = SelectedGuideSourceFilter?.SourceProfileId;
                GuideSourceFilterOptions.Clear();
                foreach (var option in result.SourceOptions)
                {
                    GuideSourceFilterOptions.Add(option);
                }

                SelectedGuideSourceFilter = GuideSourceFilterOptions.FirstOrDefault(option => option.SourceProfileId == previousSourceId)
                    ?? GuideSourceFilterOptions.FirstOrDefault();

                var previousCategoryId = SelectedGuideCategoryOption?.CategoryId;
                GuideCategoryOptions.Clear();
                foreach (var option in result.CategoryOptions)
                {
                    GuideCategoryOptions.Add(option);
                }

                SelectedGuideCategoryOption = GuideCategoryOptions.FirstOrDefault(option => option.CategoryId == previousCategoryId)
                    ?? GuideCategoryOptions.FirstOrDefault();
            }
            finally
            {
                _suppressGuideTimelineFilterReload = false;
            }

            GuideTimelineSlots.Clear();
            foreach (var slot in result.Slots)
            {
                GuideTimelineSlots.Add(new GuideTimelineSlotViewModel
                {
                    Label = slot.Label,
                    Width = TimelineSlotWidth
                });
            }

            GuideNowMarkerLeft = CalculateNowMarkerLeft(result);
            GuideNowMarkerVisibility = GuideNowMarkerLeft >= 0 && GuideNowMarkerLeft <= GuideTimelineWidth
                ? Visibility.Visible
                : Visibility.Collapsed;

            GuideTimelineRows.Clear();
            foreach (var row in result.Channels)
            {
                GuideTimelineRows.Add(BuildTimelineRow(row, result));
            }

            OnPropertyChanged(nameof(GuideTimelineEmptyVisibility));
            OnPropertyChanged(nameof(GuideStaleWarningVisibility));
        }

        private GuideTimelineChannelViewModel BuildTimelineRow(EpgGuideTimelineChannel row, EpgGuideTimelineResult result)
        {
            var viewModel = new GuideTimelineChannelViewModel
            {
                ChannelId = row.ChannelId,
                SourceProfileId = row.SourceProfileId,
                ChannelName = row.ChannelName,
                SourceName = row.SourceName,
                CategoryName = row.CategoryName,
                LogoUrl = row.LogoUrl,
                StreamUrl = row.StreamUrl,
                MatchText = $"{row.MatchSource} {row.MatchConfidence}",
                CurrentText = row.CurrentProgram == null ? "No current programme" : $"Now: {row.CurrentProgram.Title}",
                NextText = row.NextProgram == null ? "No next programme" : $"Next: {row.NextProgram.Title}",
                TimelineWidth = GuideTimelineWidth,
                NowMarkerLeft = GuideNowMarkerLeft,
                NowMarkerVisibility = GuideNowMarkerVisibility
            };

            foreach (var program in row.Programs)
            {
                viewModel.Programs.Add(BuildTimelineProgramViewModel(program, row));
            }

            return viewModel;
        }

        private GuideTimelineProgramViewModel BuildTimelineProgramViewModel(
            EpgGuideTimelineProgram program,
            EpgGuideTimelineChannel row)
        {
            return new GuideTimelineProgramViewModel
            {
                ChannelId = row.ChannelId,
                SourceProfileId = row.SourceProfileId,
                ChannelName = row.ChannelName,
                StreamUrl = row.StreamUrl,
                Title = string.IsNullOrWhiteSpace(program.Title) ? "Unknown programme" : program.Title,
                DetailText = string.Join(" - ", new[] { program.Subtitle, program.Category }.Where(value => !string.IsNullOrWhiteSpace(value))),
                StartTimeUtc = program.StartTimeUtc,
                EndTimeUtc = program.EndTimeUtc,
                Left = program.OffsetPercent * GuideTimelineWidth / 100d,
                Width = Math.Max(2, program.WidthPercent * GuideTimelineWidth / 100d)
            };
        }

        private static ManualMatchChannelViewModel BuildManualChannelViewModel(EpgManualMatchChannel channel)
        {
            return new ManualMatchChannelViewModel
            {
                ChannelId = channel.ChannelId,
                SourceProfileId = channel.SourceProfileId,
                ChannelName = channel.ChannelName,
                CategoryName = channel.CategoryName,
                CurrentXmltvChannelId = channel.CurrentXmltvChannelId,
                MatchText = $"{channel.MatchSource} - confidence {channel.MatchConfidence}"
            };
        }

        private static ManualMatchCandidateViewModel BuildManualCandidateViewModel(EpgManualMatchCandidate candidate)
        {
            return new ManualMatchCandidateViewModel
            {
                XmltvChannelId = candidate.XmltvChannelId,
                DisplayName = candidate.DisplayName,
                MatchText = $"{candidate.SuggestedMatchSource} - confidence {candidate.Confidence}",
                DetailText = candidate.DetailText
            };
        }

        private void RefreshManualMatchStatus()
        {
            if (SelectedManualMatchChannel == null)
            {
                ManualMatchStatusText = "Choose a channel to inspect or set a manual XMLTV match.";
                return;
            }

            var match = string.IsNullOrWhiteSpace(SelectedManualMatchChannel.CurrentXmltvChannelId)
                ? "No XMLTV match"
                : SelectedManualMatchChannel.CurrentXmltvChannelId;
            ManualMatchStatusText = $"{SelectedManualMatchChannel.ChannelName}: {match} ({SelectedManualMatchChannel.MatchText}).";
        }

        private double CalculateNowMarkerLeft(EpgGuideTimelineResult result)
        {
            if (result.NowUtc < result.RangeStartUtc || result.NowUtc > result.RangeEndUtc)
            {
                return -1;
            }

            var rangeTicks = Math.Max(1, (result.RangeEndUtc - result.RangeStartUtc).Ticks);
            return (result.NowUtc - result.RangeStartUtc).Ticks * GuideTimelineWidth / rangeTicks;
        }
    }
}
