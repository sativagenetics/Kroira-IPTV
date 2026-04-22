#nullable enable
using System;

namespace Kroira.App.Models
{
    public class StalkerPortalSnapshot
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public string PortalName { get; set; } = string.Empty;
        public string PortalVersion { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string Locale { get; set; } = string.Empty;
        public string Timezone { get; set; } = string.Empty;
        public string DiscoveredApiUrl { get; set; } = string.Empty;
        public bool SupportsLive { get; set; }
        public bool SupportsMovies { get; set; }
        public bool SupportsSeries { get; set; }
        public int LiveCategoryCount { get; set; }
        public int MovieCategoryCount { get; set; }
        public int SeriesCategoryCount { get; set; }
        public DateTime? LastHandshakeAtUtc { get; set; }
        public DateTime? LastProfileSyncAtUtc { get; set; }
        public string LastSummary { get; set; } = string.Empty;
        public string LastError { get; set; } = string.Empty;
    }
}
