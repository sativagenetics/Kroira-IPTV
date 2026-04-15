using System;

namespace Kroira.App.Models
{
    public class EpgProgram
    {
        public int Id { get; set; }
        public int ChannelId { get; set; }
        public DateTime StartTimeUtc { get; set; }
        public DateTime EndTimeUtc { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
