using System.Text.Json.Serialization;

namespace Kroira.Regressions;

internal sealed class RegressionSnapshot
{
    public List<SourceSnapshot> Sources { get; set; } = [];
    public List<OperationalStateSnapshot> OperationalStates { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PlaybackResolutionSnapshot>? PlaybackResolutions { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CatchupResolutionSnapshot>? CatchupResolutions { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ItemInspectionSnapshot>? ItemInspections { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ExternalLaunchSnapshot>? ExternalLaunches { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RemoteNavigationSnapshot? RemoteNavigation { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PlaybackRemoteCommandSnapshot>? PlaybackRemoteCommands { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DiscoveryFacetSnapshot>? DiscoveryFacets { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SourceActivityRegressionSnapshot>? SourceActivities { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<IncrementalPatchRegressionSnapshot>? IncrementalPatches { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SourceSetupValidationRegressionSnapshot>? SetupValidations { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SourceRepairRegressionSnapshot>? SourceRepairs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SourceRepairActionRegressionSnapshot>? SourceRepairActions { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PlaybackProgressSnapshot>? PlaybackProgresses { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HomeSurfaceSnapshot? Home { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LiveTvSurfaceSnapshot? LiveTv { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MovieSurfaceSnapshot? Movies { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SeriesSurfaceSnapshot? Series { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContinueWatchingSurfaceSnapshot? ContinueWatching { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LiveBrowsePreferencesSnapshot? LiveBrowsePreferences { get; set; }
}

internal sealed class SourceSnapshot
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public SourceRefreshSnapshot Refresh { get; set; } = new();
    public SourceCredentialSnapshot Credential { get; set; } = new();
    public SourceSyncStateSnapshot SyncState { get; set; } = new();
    public SourceCountSnapshot Counts { get; set; } = new();
    public SourceAcquisitionProfileSnapshot AcquisitionProfile { get; set; } = new();
    public SourceAcquisitionRunSnapshot AcquisitionRun { get; set; } = new();
    public SourceHealthSnapshot Health { get; set; } = new();
    public SourceEpgSnapshot Epg { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StalkerPortalStateSnapshot? StalkerPortal { get; set; }

    public List<ChannelSnapshot> Channels { get; set; } = [];
    public List<MovieSnapshot> Movies { get; set; } = [];
    public List<SeriesSnapshot> Series { get; set; } = [];
    public List<EnrichmentRecordSnapshot> Enrichment { get; set; } = [];
    public List<SourceAcquisitionEvidenceSnapshot> AcquisitionEvidence { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CatchupAttemptSnapshot>? CatchupAttempts { get; set; }
}

internal sealed class SourceRefreshSnapshot
{
    public bool Success { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string CatalogSummary { get; set; } = string.Empty;
    public string GuideSummary { get; set; } = string.Empty;
    public bool GuideAttempted { get; set; }
    public bool GuideSucceeded { get; set; }
}

internal sealed class SourceCredentialSnapshot
{
    public string Url { get; set; } = string.Empty;
    public string DetectedEpgUrl { get; set; } = string.Empty;
    public string ManualEpgUrl { get; set; } = string.Empty;
    public string EpgMode { get; set; } = string.Empty;
    public string M3uImportMode { get; set; } = string.Empty;
    public string ProxyScope { get; set; } = string.Empty;
    public string ProxyUrl { get; set; } = string.Empty;
    public string CompanionScope { get; set; } = string.Empty;
    public string CompanionMode { get; set; } = string.Empty;
    public string CompanionUrl { get; set; } = string.Empty;
    public string StalkerMacAddress { get; set; } = string.Empty;
    public string StalkerDeviceId { get; set; } = string.Empty;
    public string StalkerSerialNumber { get; set; } = string.Empty;
    public string StalkerTimezone { get; set; } = string.Empty;
    public string StalkerLocale { get; set; } = string.Empty;
    public string StalkerApiUrl { get; set; } = string.Empty;
}

internal sealed class StalkerPortalStateSnapshot
{
    public string PortalName { get; set; } = string.Empty;
    public string PortalVersion { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string DiscoveredApiUrl { get; set; } = string.Empty;
    public bool SupportsLive { get; set; }
    public bool SupportsMovies { get; set; }
    public bool SupportsSeries { get; set; }
    public int LiveCategoryCount { get; set; }
    public int MovieCategoryCount { get; set; }
    public int SeriesCategoryCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

internal sealed class SourceSyncStateSnapshot
{
    public int HttpStatusCode { get; set; }
    public string ErrorLog { get; set; } = string.Empty;
    public string AutoRefreshState { get; set; } = string.Empty;
    public string AutoRefreshSummary { get; set; } = string.Empty;
    public int AutoRefreshFailureCount { get; set; }
}

internal sealed class SourceCountSnapshot
{
    public int Channels { get; set; }
    public int Movies { get; set; }
    public int Series { get; set; }
    public int Seasons { get; set; }
    public int Episodes { get; set; }
    public int EpgPrograms { get; set; }
}

internal sealed class SourceAcquisitionProfileSnapshot
{
    public string ProfileKey { get; set; } = string.Empty;
    public string ProfileLabel { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string NormalizationSummary { get; set; } = string.Empty;
    public string MatchingSummary { get; set; } = string.Empty;
    public string SuppressionSummary { get; set; } = string.Empty;
    public string ValidationSummary { get; set; } = string.Empty;
    public bool SupportsRegexMatching { get; set; }
    public bool PreferProxyDuringValidation { get; set; }
    public bool PreferLastKnownGoodRollback { get; set; }
}

internal sealed class SourceAcquisitionRunSnapshot
{
    public string Trigger { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ProfileKey { get; set; } = string.Empty;
    public string ProfileLabel { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string RoutingSummary { get; set; } = string.Empty;
    public string ValidationRoutingSummary { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string CatalogSummary { get; set; } = string.Empty;
    public string GuideSummary { get; set; } = string.Empty;
    public string ValidationSummary { get; set; } = string.Empty;
    public int RawItemCount { get; set; }
    public int AcceptedCount { get; set; }
    public int SuppressedCount { get; set; }
    public int DemotedCount { get; set; }
    public int MatchedCount { get; set; }
    public int UnmatchedCount { get; set; }
    public int LiveCount { get; set; }
    public int MovieCount { get; set; }
    public int SeriesCount { get; set; }
    public int EpisodeCount { get; set; }
    public int AliasMatchCount { get; set; }
    public int RegexMatchCount { get; set; }
    public int FuzzyMatchCount { get; set; }
    public int ProbeSuccessCount { get; set; }
    public int ProbeFailureCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
}

internal sealed class SourceHealthSnapshot
{
    public string State { get; set; } = string.Empty;
    public int Score { get; set; }
    public string StatusSummary { get; set; } = string.Empty;
    public string ImportResultSummary { get; set; } = string.Empty;
    public string ValidationSummary { get; set; } = string.Empty;
    public string TopIssueSummary { get; set; } = string.Empty;
    public int DuplicateCount { get; set; }
    public int InvalidStreamCount { get; set; }
    public int ChannelsWithEpgMatchCount { get; set; }
    public int ChannelsWithLogoCount { get; set; }
    public int SuspiciousEntryCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public List<SourceHealthComponentSnapshot> Components { get; set; } = [];
    public List<SourceHealthProbeSnapshot> Probes { get; set; } = [];
    public List<SourceHealthIssueSnapshot> Issues { get; set; } = [];
}

internal sealed class SourceHealthComponentSnapshot
{
    public string Type { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int Score { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int RelevantCount { get; set; }
    public int HealthyCount { get; set; }
    public int IssueCount { get; set; }
}

internal sealed class SourceHealthProbeSnapshot
{
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int CandidateCount { get; set; }
    public int SampleSize { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int TimeoutCount { get; set; }
    public int HttpErrorCount { get; set; }
    public int TransportErrorCount { get; set; }
    public string Summary { get; set; } = string.Empty;
}

internal sealed class SourceHealthIssueSnapshot
{
    public string Severity { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int AffectedCount { get; set; }
    public string SampleItems { get; set; } = string.Empty;
}

internal sealed class SourceEpgSnapshot
{
    public bool IsPresent { get; set; }
    public bool IsSuccess { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ResultCode { get; set; } = string.Empty;
    public string FailureStage { get; set; } = string.Empty;
    public string ActiveMode { get; set; } = string.Empty;
    public string ActiveXmltvUrl { get; set; } = string.Empty;
    public int MatchedChannelCount { get; set; }
    public int UnmatchedChannelCount { get; set; }
    public int CurrentCoverageCount { get; set; }
    public int NextCoverageCount { get; set; }
    public int TotalLiveChannelCount { get; set; }
    public int ProgrammeCount { get; set; }
    public string MatchBreakdown { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
}

internal sealed class ChannelSnapshot
{
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string ProviderEpgChannelId { get; set; } = string.Empty;
    public string EpgChannelId { get; set; } = string.Empty;
    public string ProviderLogoUrl { get; set; } = string.Empty;
    public string LogoUrl { get; set; } = string.Empty;
    public string NormalizedIdentityKey { get; set; } = string.Empty;
    public string AliasKeys { get; set; } = string.Empty;
    public string EpgMatchSource { get; set; } = string.Empty;
    public int EpgMatchConfidence { get; set; }
    public string EpgMatchSummary { get; set; } = string.Empty;
    public string LogoSource { get; set; } = string.Empty;
    public int LogoConfidence { get; set; }
    public string LogoSummary { get; set; } = string.Empty;
    public bool SupportsCatchup { get; set; }
    public int CatchupWindowHours { get; set; }
    public string CatchupSource { get; set; } = string.Empty;
    public int CatchupConfidence { get; set; }
    public string CatchupSummary { get; set; } = string.Empty;
}

internal sealed class MovieSnapshot
{
    public string Title { get; set; } = string.Empty;
    public string RawTitle { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string ContentKind { get; set; } = string.Empty;
    public string CanonicalTitleKey { get; set; } = string.Empty;
    public string DedupFingerprint { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
}

internal sealed class SeriesSnapshot
{
    public string Title { get; set; } = string.Empty;
    public string RawTitle { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string ContentKind { get; set; } = string.Empty;
    public string CanonicalTitleKey { get; set; } = string.Empty;
    public string DedupFingerprint { get; set; } = string.Empty;
    public List<SeasonSnapshot> Seasons { get; set; } = [];
}

internal sealed class SeasonSnapshot
{
    public int SeasonNumber { get; set; }
    public List<EpisodeSnapshot> Episodes { get; set; } = [];
}

internal sealed class EpisodeSnapshot
{
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int EpisodeNumber { get; set; }
    public string StreamUrl { get; set; } = string.Empty;
}

internal sealed class EnrichmentRecordSnapshot
{
    public string IdentityKey { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string AliasKeys { get; set; } = string.Empty;
    public string MatchedXmltvChannelId { get; set; } = string.Empty;
    public string MatchedXmltvDisplayName { get; set; } = string.Empty;
    public string ResolvedLogoUrl { get; set; } = string.Empty;
    public string EpgMatchSource { get; set; } = string.Empty;
    public int EpgMatchConfidence { get; set; }
    public string LogoSource { get; set; } = string.Empty;
    public int LogoConfidence { get; set; }
}

internal sealed class SourceAcquisitionEvidenceSnapshot
{
    public string Stage { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string ItemKind { get; set; } = string.Empty;
    public string RuleCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string RawName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string MatchedTarget { get; set; } = string.Empty;
    public int Confidence { get; set; }
}

internal sealed class CatchupResolutionSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string RequestKind { get; set; } = string.Empty;
    public string ProgramTitle { get; set; } = string.Empty;
    public string RequestedAtUtc { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string UpstreamStreamUrl { get; set; } = string.Empty;
    public string LiveStreamUrl { get; set; } = string.Empty;
    public string RoutingSummary { get; set; } = string.Empty;
}

internal sealed class ItemInspectionSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public bool IsCurrentPlayback { get; set; }
    public bool SupportsExternalLaunch { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string FailureText { get; set; } = string.Empty;
    public string SafetyText { get; set; } = string.Empty;
    public string SafeReportText { get; set; } = string.Empty;
    public List<ItemInspectionSectionSnapshot> Sections { get; set; } = [];
}

internal sealed class ItemInspectionSectionSnapshot
{
    public string Title { get; set; } = string.Empty;
    public List<ItemInspectionFieldSnapshot> Fields { get; set; } = [];
}

internal sealed class ItemInspectionFieldSnapshot
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal sealed class ExternalLaunchSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ProviderSummary { get; set; } = string.Empty;
    public string RoutingSummary { get; set; } = string.Empty;
    public string ResolvedUrlText { get; set; } = string.Empty;
    public string LaunchedUrl { get; set; } = string.Empty;
    public bool UsedApplicationPicker { get; set; }
}

internal sealed class RemoteNavigationSnapshot
{
    public bool InitialEnabled { get; set; }
    public bool UpdatedEnabled { get; set; }
    public bool ReloadedEnabled { get; set; }
    public int StateChangedCount { get; set; }
    public string SettingKey { get; set; } = string.Empty;
    public string StoredJson { get; set; } = string.Empty;
}

internal sealed class PlaybackRemoteCommandSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public bool ShiftDown { get; set; }
    public string Command { get; set; } = string.Empty;
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

internal sealed class DiscoveryFacetSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public int MatchingCount { get; set; }
    public int ProviderCount { get; set; }
    public bool HasActiveFacetFilters { get; set; }
    public string SummaryText { get; set; } = string.Empty;
    public DiscoverySelectionSnapshot EffectiveSelection { get; set; } = new();
    public List<DiscoveryFacetOptionSnapshot> SignalOptions { get; set; } = [];
    public List<DiscoveryFacetOptionSnapshot> SourceTypeOptions { get; set; } = [];
    public List<DiscoveryFacetOptionSnapshot> LanguageOptions { get; set; } = [];
    public List<DiscoveryFacetOptionSnapshot> TagOptions { get; set; } = [];
    public List<string> MatchingKeys { get; set; } = [];
}

internal sealed class DiscoverySelectionSnapshot
{
    public string SignalKey { get; set; } = string.Empty;
    public string SourceTypeKey { get; set; } = string.Empty;
    public string LanguageKey { get; set; } = string.Empty;
    public string TagKey { get; set; } = string.Empty;
}

internal sealed class DiscoveryFacetOptionSnapshot
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int ItemCount { get; set; }
}

internal sealed class SourceActivityRegressionSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public string Trend { get; set; } = string.Empty;
    public string CurrentState { get; set; } = string.Empty;
    public string LatestAttempt { get; set; } = string.Empty;
    public string LastSuccess { get; set; } = string.Empty;
    public string QuietState { get; set; } = string.Empty;
    public string SafeReport { get; set; } = string.Empty;
    public List<SourceActivityMetricRegressionSnapshot> Metrics { get; set; } = [];
    public List<SourceActivityTimelineRegressionSnapshot> Timeline { get; set; } = [];
}

internal sealed class SourceActivityMetricRegressionSnapshot
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Tone { get; set; } = string.Empty;
}

internal sealed class SourceActivityTimelineRegressionSnapshot
{
    public string TimestampUtc { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string BadgeText { get; set; } = string.Empty;
    public string Tone { get; set; } = string.Empty;
}

internal sealed class SourceSetupValidationRegressionSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string RequestedType { get; set; } = string.Empty;
    public string DetectedTypeHint { get; set; } = string.Empty;
    public bool CanSave { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Connection { get; set; } = string.Empty;
    public string TypeHint { get; set; } = string.Empty;
    public string CapabilitySummary { get; set; } = string.Empty;
    public string SafeReport { get; set; } = string.Empty;
    public List<SourceGuidanceCapabilityRegressionSnapshot> Capabilities { get; set; } = [];
    public List<SourceGuidanceIssueRegressionSnapshot> Issues { get; set; } = [];
}

internal sealed class SourceRepairRegressionSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public bool IsStable { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CapabilitySummary { get; set; } = string.Empty;
    public string SafeReport { get; set; } = string.Empty;
    public List<SourceGuidanceCapabilityRegressionSnapshot> Capabilities { get; set; } = [];
    public List<SourceGuidanceIssueRegressionSnapshot> Issues { get; set; } = [];
    public List<SourceRepairActionOptionRegressionSnapshot> Actions { get; set; } = [];
}

internal sealed class SourceRepairActionRegressionSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Change { get; set; } = string.Empty;
    public string SafeReport { get; set; } = string.Empty;
}

internal sealed class SourceGuidanceCapabilityRegressionSnapshot
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Tone { get; set; } = string.Empty;
}

internal sealed class SourceGuidanceIssueRegressionSnapshot
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Tone { get; set; } = string.Empty;
}

internal sealed class SourceRepairActionOptionRegressionSnapshot
{
    public string ActionType { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ButtonText { get; set; } = string.Empty;
    public string Tone { get; set; } = string.Empty;
}

internal sealed class PlaybackResolutionSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string CatalogStreamUrl { get; set; } = string.Empty;
    public string StreamUrl { get; set; } = string.Empty;
    public string UpstreamStreamUrl { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ProviderSummary { get; set; } = string.Empty;
    public string RoutingSummary { get; set; } = string.Empty;
    public string CompanionStatus { get; set; } = string.Empty;
    public string CompanionStatusText { get; set; } = string.Empty;
}

internal sealed class CatchupAttemptSnapshot
{
    public string ChannelName { get; set; } = string.Empty;
    public string LogicalContentKey { get; set; } = string.Empty;
    public string RequestKind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ProgramTitle { get; set; } = string.Empty;
    public int WindowHours { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RoutingSummary { get; set; } = string.Empty;
    public string ProviderMode { get; set; } = string.Empty;
    public string ProviderSource { get; set; } = string.Empty;
    public string ResolvedStreamUrl { get; set; } = string.Empty;
}

internal sealed class OperationalStateSnapshot
{
    public string ContentType { get; set; } = string.Empty;
    public string LogicalContentKey { get; set; } = string.Empty;
    public int CandidateCount { get; set; }
    public string PreferredSourceName { get; set; } = string.Empty;
    public string PreferredTitle { get; set; } = string.Empty;
    public int PreferredScore { get; set; }
    public string SelectionSummary { get; set; } = string.Empty;
    public string RecoveryAction { get; set; } = string.Empty;
    public string RecoverySummary { get; set; } = string.Empty;
    public List<OperationalCandidateSnapshot> Candidates { get; set; } = [];
}

internal sealed class OperationalCandidateSnapshot
{
    public int Rank { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Score { get; set; }
    public bool IsSelected { get; set; }
    public bool IsLastKnownGood { get; set; }
    public bool SupportsProxy { get; set; }
    public string Summary { get; set; } = string.Empty;
}

internal sealed class PlaybackProgressSnapshot
{
    public string ContentType { get; set; } = string.Empty;
    public int ContentId { get; set; }
    public string LogicalContentKey { get; set; } = string.Empty;
    public int PreferredSourceProfileId { get; set; }
    public bool IsCompleted { get; set; }
}

internal sealed class HomeSurfaceSnapshot
{
    public string State { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string LibraryStatusMessage { get; set; } = string.Empty;
    public string SourceStatusMessage { get; set; } = string.Empty;
    public int SummaryItemCount { get; set; }
    public int ContinueCount { get; set; }
    public int LiveCount { get; set; }
    public List<string> ContinueTitles { get; set; } = [];
    public List<string> LiveTitles { get; set; } = [];
}

internal sealed class LiveTvSurfaceSnapshot
{
    public string State { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int ChannelCount { get; set; }
    public int CategoryCount { get; set; }
    public int SelectedSourceId { get; set; }
    public string SelectedSourceLabel { get; set; } = string.Empty;
    public List<string> ChannelTitles { get; set; } = [];
}

internal sealed class MovieSurfaceSnapshot
{
    public string State { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int MovieCount { get; set; }
    public int DisplaySlotCount { get; set; }
    public string SelectedSourceLabel { get; set; } = string.Empty;
    public string DiscoverySummary { get; set; } = string.Empty;
    public List<string> Titles { get; set; } = [];
}

internal sealed class SeriesSurfaceSnapshot
{
    public string State { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int SeriesCount { get; set; }
    public int DisplaySlotCount { get; set; }
    public string SelectedSourceLabel { get; set; } = string.Empty;
    public string SelectedSeriesTitle { get; set; } = string.Empty;
    public string DiscoverySummary { get; set; } = string.Empty;
    public List<string> Titles { get; set; } = [];
}

internal sealed class ContinueWatchingSurfaceSnapshot
{
    public string State { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int LiveCount { get; set; }
    public int MovieCount { get; set; }
    public int SeriesCount { get; set; }
    public List<string> Titles { get; set; } = [];
}

internal sealed class IncrementalPatchRegressionSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Surface { get; set; } = string.Empty;
    public int InitialCount { get; set; }
    public int ReloadedCount { get; set; }
    public int ReusedItemCount { get; set; }
    public int ReusedSlotCount { get; set; }
    public int ReusedSourceOptionCount { get; set; }
    public int ReusedSourceVisibilityCount { get; set; }
    public string FirstTitle { get; set; } = string.Empty;
    public bool HealthExpandedPreserved { get; set; }
    public bool RepairExpandedPreserved { get; set; }
    public bool ActivityExpandedPreserved { get; set; }
}

internal sealed class LiveBrowsePreferencesSnapshot
{
    public int SelectedSourceId { get; set; }
    public int LastChannelId { get; set; }
    public string LastChannelLogicalKey { get; set; } = string.Empty;
    public int LastChannelPreferredSourceProfileId { get; set; }
    public List<int> HiddenSourceIds { get; set; } = [];
    public List<int> RecentChannelIds { get; set; } = [];
    public List<string> RecentLogicalKeys { get; set; } = [];
    public List<int> RecentPreferredSourceProfileIds { get; set; } = [];
}
