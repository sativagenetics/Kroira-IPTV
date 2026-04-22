#nullable enable
using System;
using System.Collections.Generic;

namespace Kroira.App.Models
{
    public enum OperationalContentType
    {
        Channel = 0,
        Movie = 1
    }

    public enum OperationalRecoveryAction
    {
        None = 0,
        PreservedLastKnownGood = 1,
        SwitchedMirror = 2,
        RolledForward = 3,
        Degraded = 4
    }

    public enum SourceProxyScope
    {
        Disabled = 0,
        PlaybackOnly = 1,
        PlaybackAndProbing = 2,
        AllRequests = 3
    }

    public enum SourceNetworkPurpose
    {
        Playback = 0,
        Probe = 1,
        Import = 2,
        Guide = 3
    }

    public enum SourceCompanionScope
    {
        Disabled = 0,
        PlaybackOnly = 1,
        PlaybackAndProbing = 2
    }

    public enum SourceCompanionRelayMode
    {
        Relay = 0,
        Buffered = 1
    }

    public enum CompanionRelayStatus
    {
        None = 0,
        Skipped = 1,
        Applied = 2,
        FallbackDirect = 3,
        Failed = 4
    }

    public class LogicalOperationalState
    {
        public int Id { get; set; }
        public OperationalContentType ContentType { get; set; }
        public string LogicalContentKey { get; set; } = string.Empty;
        public int CandidateCount { get; set; }
        public int PreferredContentId { get; set; }
        public int PreferredSourceProfileId { get; set; }
        public int PreferredScore { get; set; }
        public string SelectionSummary { get; set; } = string.Empty;
        public int LastKnownGoodContentId { get; set; }
        public int LastKnownGoodSourceProfileId { get; set; }
        public int LastKnownGoodScore { get; set; }
        public DateTime? LastKnownGoodAtUtc { get; set; }
        public DateTime? LastPlaybackSuccessAtUtc { get; set; }
        public DateTime? LastPlaybackFailureAtUtc { get; set; }
        public int ConsecutivePlaybackFailures { get; set; }
        public OperationalRecoveryAction RecoveryAction { get; set; }
        public string RecoverySummary { get; set; } = string.Empty;
        public DateTime SnapshotEvaluatedAtUtc { get; set; }
        public DateTime? PreferredUpdatedAtUtc { get; set; }

        public ICollection<LogicalOperationalCandidate> Candidates { get; set; } = new List<LogicalOperationalCandidate>();
    }

    public class LogicalOperationalCandidate
    {
        public int Id { get; set; }
        public int LogicalOperationalStateId { get; set; }
        public int ContentId { get; set; }
        public int SourceProfileId { get; set; }
        public int Rank { get; set; }
        public int Score { get; set; }
        public bool IsSelected { get; set; }
        public bool IsLastKnownGood { get; set; }
        public bool SupportsProxy { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public DateTime LastSeenAtUtc { get; set; }

        public LogicalOperationalState? State { get; set; }
    }

    public sealed class SourceRoutingDecision
    {
        public SourceProxyScope Scope { get; init; }
        public string ProxyUrl { get; init; } = string.Empty;
        public bool UseProxy { get; init; }
        public string Summary { get; init; } = "Direct routing";
    }

    public sealed class CompanionRelayDecision
    {
        public SourceCompanionScope Scope { get; init; }
        public SourceCompanionRelayMode Mode { get; init; }
        public CompanionRelayStatus Status { get; init; }
        public string CompanionUrl { get; init; } = string.Empty;
        public string UpstreamUrl { get; init; } = string.Empty;
        public string RelayUrl { get; init; } = string.Empty;
        public string Summary { get; init; } = "Direct provider path";
        public string StatusText { get; init; } = string.Empty;
        public bool UseCompanion => Status == CompanionRelayStatus.Applied && !string.IsNullOrWhiteSpace(RelayUrl);

        public static CompanionRelayDecision Skipped(
            SourceCompanionScope scope,
            SourceCompanionRelayMode mode,
            string upstreamUrl,
            string summary,
            string statusText = "")
        {
            return new CompanionRelayDecision
            {
                Scope = scope,
                Mode = mode,
                Status = CompanionRelayStatus.Skipped,
                UpstreamUrl = upstreamUrl,
                Summary = string.IsNullOrWhiteSpace(summary) ? "Direct provider path" : summary.Trim(),
                StatusText = statusText?.Trim() ?? string.Empty
            };
        }

        public static CompanionRelayDecision Applied(
            SourceCompanionScope scope,
            SourceCompanionRelayMode mode,
            string companionUrl,
            string upstreamUrl,
            string relayUrl,
            string summary,
            string statusText)
        {
            return new CompanionRelayDecision
            {
                Scope = scope,
                Mode = mode,
                Status = CompanionRelayStatus.Applied,
                CompanionUrl = companionUrl,
                UpstreamUrl = upstreamUrl,
                RelayUrl = relayUrl,
                Summary = string.IsNullOrWhiteSpace(summary) ? "Companion relay" : summary.Trim(),
                StatusText = statusText?.Trim() ?? string.Empty
            };
        }

        public static CompanionRelayDecision FallbackDirect(
            SourceCompanionScope scope,
            SourceCompanionRelayMode mode,
            string companionUrl,
            string upstreamUrl,
            string summary,
            string statusText)
        {
            return new CompanionRelayDecision
            {
                Scope = scope,
                Mode = mode,
                Status = CompanionRelayStatus.FallbackDirect,
                CompanionUrl = companionUrl,
                UpstreamUrl = upstreamUrl,
                Summary = string.IsNullOrWhiteSpace(summary) ? "Direct provider path" : summary.Trim(),
                StatusText = statusText?.Trim() ?? string.Empty
            };
        }
    }

    public sealed class OperationalPlaybackResolution
    {
        public OperationalContentType ContentType { get; init; }
        public int ContentId { get; init; }
        public int SourceProfileId { get; init; }
        public string LogicalContentKey { get; init; } = string.Empty;
        public string CatalogStreamUrl { get; init; } = string.Empty;
        public string StreamUrl { get; init; } = string.Empty;
        public string SourceName { get; init; } = string.Empty;
        public int CandidateCount { get; init; }
        public int Score { get; init; }
        public string SelectionSummary { get; init; } = string.Empty;
        public string RecoverySummary { get; init; } = string.Empty;
        public bool UsedLastKnownGood { get; init; }
        public SourceRoutingDecision Routing { get; init; } = new();
    }
}
