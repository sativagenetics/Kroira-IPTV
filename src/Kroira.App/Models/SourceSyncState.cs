using System;

namespace Kroira.App.Models
{
    public class SourceSyncState
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public DateTime LastAttempt { get; set; }
        public int HttpStatusCode { get; set; }
        public string ErrorLog { get; set; } = string.Empty;
    }
}
