#nullable enable
using System;
using System.Collections.Generic;

namespace Kroira.App.Models
{
    public sealed class EpgGuideTimelineRequest
    {
        public int? SourceProfileId { get; set; }
        public int? CategoryId { get; set; }
        public string SearchText { get; set; } = string.Empty;
        public DateTime RangeStartUtc { get; set; }
        public TimeSpan RangeDuration { get; set; } = TimeSpan.FromHours(4);
        public TimeSpan SlotDuration { get; set; } = TimeSpan.FromMinutes(30);
        public DateTime NowUtc { get; set; }
        public int MaxChannels { get; set; } = 120;
    }

    public sealed class EpgGuideTimelineResult
    {
        public DateTime RangeStartUtc { get; set; }
        public DateTime RangeEndUtc { get; set; }
        public DateTime NowUtc { get; set; }
        public IReadOnlyList<EpgGuideTimelineSlot> Slots { get; set; } = Array.Empty<EpgGuideTimelineSlot>();
        public IReadOnlyList<EpgGuideTimelineChannel> Channels { get; set; } = Array.Empty<EpgGuideTimelineChannel>();
        public IReadOnlyList<EpgGuideTimelineSourceOption> SourceOptions { get; set; } = Array.Empty<EpgGuideTimelineSourceOption>();
        public IReadOnlyList<EpgGuideTimelineCategoryOption> CategoryOptions { get; set; } = Array.Empty<EpgGuideTimelineCategoryOption>();
        public bool HasGuideData { get; set; }
        public bool IsStale { get; set; }
        public string StaleWarningText { get; set; } = string.Empty;
    }

    public sealed record EpgGuideTimelineSlot(
        DateTime StartUtc,
        DateTime EndUtc,
        string Label);

    public sealed class EpgGuideTimelineSourceOption
    {
        public int? SourceProfileId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public SourceType? SourceType { get; set; }
        public string Label { get; set; } = string.Empty;
    }

    public sealed class EpgGuideTimelineCategoryOption
    {
        public int? CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    public sealed class EpgGuideTimelineChannel
    {
        public int ChannelId { get; set; }
        public int SourceProfileId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public SourceType SourceType { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string EpgChannelId { get; set; } = string.Empty;
        public ChannelEpgMatchSource MatchSource { get; set; }
        public int MatchConfidence { get; set; }
        public EpgGuideTimelineProgram? CurrentProgram { get; set; }
        public EpgGuideTimelineProgram? NextProgram { get; set; }
        public IReadOnlyList<EpgGuideTimelineProgram> Programs { get; set; } = Array.Empty<EpgGuideTimelineProgram>();
    }

    public sealed class EpgGuideTimelineProgram
    {
        public int ProgramId { get; set; }
        public int ChannelId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? Category { get; set; }
        public DateTime StartTimeUtc { get; set; }
        public DateTime EndTimeUtc { get; set; }
        public bool IsCurrent { get; set; }
        public double OffsetPercent { get; set; }
        public double WidthPercent { get; set; }
    }

    public sealed class EpgManualMatchChannel
    {
        public int ChannelId { get; set; }
        public int SourceProfileId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string ChannelName { get; set; } = string.Empty;
        public string ProviderEpgChannelId { get; set; } = string.Empty;
        public string CurrentXmltvChannelId { get; set; } = string.Empty;
        public ChannelEpgMatchSource MatchSource { get; set; }
        public int MatchConfidence { get; set; }
        public string MatchSummary { get; set; } = string.Empty;
    }

    public sealed class EpgManualMatchCandidate
    {
        public string XmltvChannelId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public ChannelEpgMatchSource SuggestedMatchSource { get; set; }
        public int Confidence { get; set; }
        public string DetailText { get; set; } = string.Empty;
        public bool IsCustomCandidate { get; set; }
    }
}
