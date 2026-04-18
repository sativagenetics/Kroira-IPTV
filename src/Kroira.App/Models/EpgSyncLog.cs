using System;

namespace Kroira.App.Models
{
    public class EpgSyncLog
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public DateTime SyncedAtUtc { get; set; }
        public bool IsSuccess { get; set; }
        public int MatchedChannelCount { get; set; }
        public int ProgrammeCount { get; set; }
        public string FailureReason { get; set; } = string.Empty;
    }
}
