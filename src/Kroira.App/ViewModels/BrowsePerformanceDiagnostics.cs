#nullable enable

namespace Kroira.App.ViewModels
{
    internal static class BrowsePerformanceDiagnostics
    {
        internal const int FrameNavigateWarningMs = 250;
        internal const int PageVisibleWarningMs = 250;
        internal const int FirstUsefulContentWarningMs = 1000;
        internal const int MoviePreviewWarningMs = 700;
        internal const int SmartCategoryIndexWarningMs = 1000;
        internal const int UiThreadCollectionApplyWarningMs = 250;
        internal const int SkeletonVisibleWarningMs = 1500;

        internal static void WarnIfFrameNavigateSlow(string pageName, long elapsedMs)
        {
            var media = ResolveBrowseMedia(pageName);
            if (media == null)
            {
                return;
            }

            WarnIfSlow("NAV", "frame_navigate", media, elapsedMs, FrameNavigateWarningMs, $"page={pageName}");
        }

        internal static void WarnIfPageVisibleSlow(string area, string media, long elapsedMs, string detail = "")
        {
            WarnIfSlow(area, "page_visible", media, elapsedMs, PageVisibleWarningMs, detail);
        }

        internal static void WarnIfFirstUsefulContentSlow(string area, string media, long elapsedMs, string detail = "")
        {
            WarnIfSlow(area, "first_useful_content", media, elapsedMs, FirstUsefulContentWarningMs, detail);
        }

        internal static void WarnIfMoviePreviewSlow(string area, long elapsedMs, string detail = "")
        {
            WarnIfSlow(area, "movie_preview", "movies", elapsedMs, MoviePreviewWarningMs, detail);
        }

        internal static void WarnIfSmartCategoryIndexSlow(string area, string media, long elapsedMs, string detail = "")
        {
            WarnIfSlow(area, "smart_category_index", media, elapsedMs, SmartCategoryIndexWarningMs, detail);
        }

        internal static void WarnIfUiThreadCollectionApplySlow(string area, string media, long elapsedMs, string detail = "")
        {
            WarnIfSlow(area, "ui_collection_apply", media, elapsedMs, UiThreadCollectionApplyWarningMs, detail);
        }

        internal static void WarnIfSkeletonVisibleSlow(string area, string media, long elapsedMs, string detail = "")
        {
            WarnIfSlow(area, "skeleton_visible", media, elapsedMs, SkeletonVisibleWarningMs, detail);
        }

        private static void WarnIfSlow(string area, string metric, string media, long elapsedMs, int thresholdMs, string detail)
        {
            if (elapsedMs <= thresholdMs)
            {
                return;
            }

            BrowseRuntimeLogger.Log(
                area,
                $"PERF WARN metric={metric} media={media} ms={elapsedMs} thresholdMs={thresholdMs}{FormatDetail(detail)}");
        }

        private static string? ResolveBrowseMedia(string pageName)
        {
            return pageName switch
            {
                "ChannelsPage" => "live",
                "MoviesPage" => "movies",
                "SeriesPage" => "series",
                _ => null
            };
        }

        private static string FormatDetail(string detail)
        {
            return string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail.Trim()}";
        }
    }
}
