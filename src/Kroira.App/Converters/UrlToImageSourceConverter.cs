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

            var cacheKey = uri.AbsoluteUri;
            if (IsTemporarilySuppressed(cacheKey))
            {
                return null;
            }

            try
            {
                var image = new BitmapImage();
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

        private static void MarkFailed(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            FailedUrlCooldowns[key] = DateTime.UtcNow + FailedUrlTtl;
        }
    }
}
