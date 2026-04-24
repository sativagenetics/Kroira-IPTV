using System.ComponentModel.DataAnnotations.Schema;

namespace Kroira.App.Models
{
    /// <summary>
    /// Controls how much VOD / series content is imported from an M3U source.
    ///
    /// <see cref="LiveMoviesAndSeries"/> is the current default for all M3U
    /// sources — it imports live channels, VOD movies, AND high-confidence
    /// series. The confidence gate (in <c>M3uParserService.BuildSeriesList</c>)
    /// ensures only genuine episodic content becomes Series; everything else
    /// stays as standalone Movies. This mirrors Xtream's three-layer output
    /// (live / movies / series) as closely as M3U data allows.
    ///
    /// <see cref="LiveAndMovies"/> suppresses series grouping entirely; every
    /// episodic-looking item becomes a standalone Movie. Kept as an explicit
    /// escape hatch for noisy playlists where even the strict confidence gate
    /// produces too many false positives.
    ///
    /// <see cref="LiveOnly"/> is the most conservative option — it suppresses
    /// all VOD content (useful for pure live-TV playlists).
    ///
    /// This enum is stored as an <c>int</c> column on <see cref="SourceCredential"/>.
    /// New sources default to <see cref="LiveMoviesAndSeries"/> (value 2).
    /// Older DBs created before this change (with column default 1) are
    /// upgraded to 2 at startup by <c>DatabaseBootstrapper.EnsureRuntimeSchema</c>.
    /// </summary>
    public enum M3uImportMode
    {
        /// <summary>Live channels only. All VOD items are suppressed.</summary>
        LiveOnly = 0,

        /// <summary>
        /// Live channels + Movies / standalone VOD. Series grouping is
        /// disabled — every episodic-looking item is imported as a Movie.
        /// Explicit escape hatch for pathological playlists.
        /// </summary>
        LiveAndMovies = 1,

        /// <summary>
        /// Live channels + Movies + high-confidence Series.
        /// <b>Default for all M3U sources.</b> The series-confidence gate
        /// ensures only items with explicit episode markers, ≥2 distinct
        /// episodes, and a non-bucket base title ever form a Series.
        /// </summary>
        LiveMoviesAndSeries = 2,
    }

    public class SourceCredential
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        // Legacy column name retained for compatibility; this now stores the manual XMLTV override URL.
        public string EpgUrl { get; set; } = string.Empty;
        public string DetectedEpgUrl { get; set; } = string.Empty;
        public string FallbackEpgUrls { get; set; } = string.Empty;
        public EpgActiveMode EpgMode { get; set; } = EpgActiveMode.Detected;

        /// <summary>
        /// Controls which M3U content layers are imported.
        /// Ignored for Xtream sources.
        /// Defaults to <see cref="M3uImportMode.LiveMoviesAndSeries"/>.
        /// </summary>
        public M3uImportMode M3uImportMode { get; set; } = M3uImportMode.LiveMoviesAndSeries;
        public SourceProxyScope ProxyScope { get; set; } = SourceProxyScope.Disabled;
        public string ProxyUrl { get; set; } = string.Empty;
        public SourceCompanionScope CompanionScope { get; set; } = SourceCompanionScope.Disabled;
        public SourceCompanionRelayMode CompanionMode { get; set; } = SourceCompanionRelayMode.Buffered;
        public string CompanionUrl { get; set; } = string.Empty;
        public string StalkerMacAddress { get; set; } = string.Empty;
        public string StalkerDeviceId { get; set; } = string.Empty;
        public string StalkerSerialNumber { get; set; } = string.Empty;
        public string StalkerTimezone { get; set; } = string.Empty;
        public string StalkerLocale { get; set; } = string.Empty;
        public string StalkerApiUrl { get; set; } = string.Empty;

        [NotMapped]
        public string ManualEpgUrl
        {
            get => EpgUrl;
            set => EpgUrl = value ?? string.Empty;
        }
    }
}
