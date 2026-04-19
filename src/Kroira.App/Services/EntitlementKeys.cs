namespace Kroira.App.Services
{
    public static class EntitlementFeatureKeys
    {
        public const string SourcesAdd = "sources.add";
        public const string LibraryBackupRestore = "library.backup_restore";
        public const string ProfilesMultiple = "profiles.multiple";
        public const string ProfilesParentalControls = "profiles.parental_controls";
        public const string PlaybackFullscreen = "playback.fullscreen";
        public const string PlaybackPictureInPicture = "playback.picture_in_picture";
        public const string PlaybackAudioTrackSelection = "playback.audio_track_selection";
        public const string PlaybackSubtitleTrackSelection = "playback.subtitle_track_selection";
        public const string PlaybackAspectControls = "playback.aspect_controls";
        public const string AppearanceThemes = "appearance.themes";
        public const string AppearanceAccentPacks = "appearance.accent_packs";
    }

    public static class EntitlementLimitKeys
    {
        public const string SourcesMaxCount = "sources.max_count";
        public const string ProfilesMaxCount = "profiles.max_count";
        public const string RecordingConcurrentJobs = "recording.concurrent_jobs";
        public const string DownloadConcurrentJobs = "download.concurrent_jobs";
    }
}
