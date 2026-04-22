using System;

namespace Kroira.App.Models
{
    public enum SourceAutoRefreshState
    {
        Idle = 0,
        Scheduled = 1,
        Running = 2,
        Succeeded = 3,
        Failed = 4,
        Disabled = 5
    }

    public class SourceSyncState
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public DateTime LastAttempt { get; set; }
        public int HttpStatusCode { get; set; }
        public string ErrorLog { get; set; } = string.Empty;
        public DateTime? LastAutoRefreshAttemptAtUtc { get; set; }
        public DateTime? LastAutoRefreshSuccessAtUtc { get; set; }
        public DateTime? NextAutoRefreshDueAtUtc { get; set; }
        public SourceAutoRefreshState AutoRefreshState { get; set; }
        public string AutoRefreshSummary { get; set; } = string.Empty;
        public int AutoRefreshFailureCount { get; set; }
    }
}
