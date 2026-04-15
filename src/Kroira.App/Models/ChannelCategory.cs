using System.Collections.Generic;

namespace Kroira.App.Models
{
    public class ChannelCategory
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int OrderIndex { get; set; }

        public ICollection<Channel>? Channels { get; set; }
    }
}
