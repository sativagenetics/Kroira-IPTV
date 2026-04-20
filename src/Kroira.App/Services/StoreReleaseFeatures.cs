namespace Kroira.App.Services
{
    public static class StoreReleaseFeatures
    {
        public static bool IsStoreV1Lite => true;

        public static bool ShowRestore => !IsStoreV1Lite;

        public static bool ShowDownloadActions => !IsStoreV1Lite;

        public static bool ShowRecordingActions => !IsStoreV1Lite;

        public static bool ShowMediaLibrary => !IsStoreV1Lite;
    }
}
