using System;
using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kroira.App.Converters
{
    public sealed class UrlToImageSourceConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, DateTime> FailedUrlCooldowns = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan FailedUrlTtl = TimeSpan.FromHours(6);
        private const int DefaultDecodePixelWidth = 160;
        private const int MaxFailedUrlCooldowns = 512;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not string url || string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var trimmed = url.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                MarkFailed(trimmed);
                return null;
            }

            if (!IsSupportedImageUri(uri))
            {
                MarkFailed(trimmed);
                return null;
            }

            var cacheKey = uri.AbsoluteUri;
            if (IsTemporarilySuppressed(cacheKey))
            {
                return null;
            }

            try
            {
                var image = new BitmapImage
                {
                    DecodePixelWidth = ResolveDecodePixelWidth(parameter)
                };
                image.ImageOpened += (_, _) => FailedUrlCooldowns.TryRemove(cacheKey, out _);
                image.ImageFailed += (_, _) => MarkFailed(cacheKey);
                image.UriSource = uri;
                return image;
            }
            catch
            {
                MarkFailed(cacheKey);
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();

        private static bool IsTemporarilySuppressed(string key)
        {
            if (!FailedUrlCooldowns.TryGetValue(key, out var retryAfterUtc))
            {
                return false;
            }

            if (retryAfterUtc > DateTime.UtcNow)
            {
                return true;
            }

            FailedUrlCooldowns.TryRemove(key, out _);
            return false;
        }

        private static bool IsSupportedImageUri(Uri uri)
        {
            return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                   uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                   uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) ||
                   uri.Scheme.Equals("ms-appx", StringComparison.OrdinalIgnoreCase) ||
                   uri.Scheme.Equals("ms-appdata", StringComparison.OrdinalIgnoreCase);
        }

        private static int ResolveDecodePixelWidth(object parameter)
        {
            if (parameter is int intValue && intValue > 0)
            {
                return intValue;
            }

            if (parameter is string text &&
                int.TryParse(text, out var parsed) &&
                parsed > 0)
            {
                return parsed;
            }

            return DefaultDecodePixelWidth;
        }

        private static void MarkFailed(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            TrimFailedUrlCooldowns();
            FailedUrlCooldowns[key] = DateTime.UtcNow + FailedUrlTtl;
        }

        private static void TrimFailedUrlCooldowns()
        {
            if (FailedUrlCooldowns.Count < MaxFailedUrlCooldowns)
            {
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var pair in FailedUrlCooldowns)
            {
                if (pair.Value <= now)
                {
                    FailedUrlCooldowns.TryRemove(pair.Key, out _);
                }
            }

            if (FailedUrlCooldowns.Count < MaxFailedUrlCooldowns)
            {
                return;
            }

            foreach (var pair in FailedUrlCooldowns)
            {
                FailedUrlCooldowns.TryRemove(pair.Key, out _);
                if (FailedUrlCooldowns.Count < MaxFailedUrlCooldowns)
                {
                    break;
                }
            }
        }
    }
}
