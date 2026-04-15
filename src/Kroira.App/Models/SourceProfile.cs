using System;

namespace Kroira.App.Models
{
    public enum SourceType { M3U, Xtream }

    public class SourceProfile
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public SourceType Type { get; set; }
        public DateTime? LastSync { get; set; }
    }
}
