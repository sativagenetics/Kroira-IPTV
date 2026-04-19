using System;

namespace Kroira.App.Models
{
    public class DownloadJob
    {
        public int Id { get; set; }
        public int ProfileId { get; set; }
        public PlaybackContentType ContentType { get; set; }
        public int ContentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public DateTime RequestedAtUtc { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public DateTime? NextRetryAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public string TempOutputPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetryCount { get; set; } = 2;
        public string LastError { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
    }
}
