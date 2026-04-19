#nullable enable
using System.Collections.Generic;

namespace Kroira.App.Models
{
    public class Channel
    {
        public int Id { get; set; }
        public int ChannelCategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
        public string EpgChannelId { get; set; } = string.Empty;

        public ICollection<EpgProgram>? EpgPrograms { get; set; }
    }
}
