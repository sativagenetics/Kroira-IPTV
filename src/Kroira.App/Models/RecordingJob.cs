using System;

namespace Kroira.App.Models
{
    public class RecordingJob
    {
        public int Id { get; set; }
        public int ChannelId { get; set; }
        public DateTime StartTimeUtc { get; set; }
        public DateTime EndTimeUtc { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
    }
}
