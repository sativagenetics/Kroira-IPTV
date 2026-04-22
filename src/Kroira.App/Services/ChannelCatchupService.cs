#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Kroira.App.Models;
using Kroira.App.Services.Parsing;

namespace Kroira.App.Services
{
    public interface IChannelCatchupService
    {
        void ApplyM3uCatchup(Channel channel, IReadOnlyDictionary<string, string> attributes);
        void ApplyXtreamCatchup(Channel channel, JsonElement payload);
    }

    public sealed class ChannelCatchupService : IChannelCatchupService
    {
        public void ApplyM3uCatchup(Channel channel, IReadOnlyDictionary<string, string> attributes)
        {
            if (channel == null)
            {
                return;
            }

            var catchupMode = M3uMetadataParser.GetFirstAttributeValue(
                attributes,
                "catchup",
                "timeshift",
                "tvg-rec");
            var catchupSource = M3uMetadataParser.GetFirstAttributeValue(
                attributes,
                "catchup-source",
                "timeshift-source");
            var catchupDays = M3uMetadataParser.GetFirstAttributeValue(
                attributes,
                "catchup-days",
                "timeshift-days");

            var snapshot = BuildSnapshot(
                explicitSupport: IsTruthy(catchupMode) ||
                                 !string.IsNullOrWhiteSpace(catchupSource) ||
                                 TryParseWindowHours(catchupDays, isDaysValue: true, out _),
                explicitWindowHours: TryParseWindowHours(catchupDays, isDaysValue: true, out var windowHours) ? windowHours : null,
                providerMode: catchupMode,
                providerSource: catchupSource,
                streamUrl: channel.StreamUrl,
                explicitSource: !string.IsNullOrWhiteSpace(catchupSource) || TryContainsCatchupHint(catchupSource)
                    ? ChannelCatchupSource.M3uAttributes
                    : ChannelCatchupSource.None);

            Apply(channel, snapshot);
        }

        public void ApplyXtreamCatchup(Channel channel, JsonElement payload)
        {
            if (channel == null)
            {
                return;
            }

            var archiveMode = GetString(payload, "tv_archive", "catchup");
            var archiveSource = GetString(payload, "tv_archive_server", "catchup_source");
            var durationText = GetString(payload, "tv_archive_duration", "archive_duration", "catchup_days");
            var windowHours = TryParseWindowHours(durationText, isDaysValue: string.Equals(GetMatchedPropertyName(payload, "catchup_days"), "catchup_days", StringComparison.OrdinalIgnoreCase), out var parsedWindowHours)
                ? parsedWindowHours
                : null;

            var snapshot = BuildSnapshot(
                explicitSupport: IsTruthy(archiveMode) || windowHours.HasValue,
                explicitWindowHours: windowHours,
                providerMode: archiveMode,
                providerSource: archiveSource,
                streamUrl: channel.StreamUrl,
                explicitSource: IsTruthy(archiveMode) || windowHours.HasValue
                    ? ChannelCatchupSource.XtreamArchive
                    : ChannelCatchupSource.None);

            Apply(channel, snapshot);
        }

        private static CatchupSnapshot BuildSnapshot(
            bool explicitSupport,
            int? explicitWindowHours,
            string providerMode,
            string providerSource,
            string streamUrl,
            ChannelCatchupSource explicitSource)
        {
            var detectedSource = explicitSource;
            var supportsCatchup = explicitSupport;
            var confidence = 0;

            if (supportsCatchup)
            {
                confidence = explicitSource == ChannelCatchupSource.XtreamArchive ? 92 : 84;
            }

            if (!supportsCatchup && TryContainsCatchupHint(providerSource))
            {
                supportsCatchup = true;
                detectedSource = ChannelCatchupSource.M3uAttributes;
                confidence = 72;
            }

            if (!supportsCatchup && TryContainsCatchupHint(streamUrl))
            {
                supportsCatchup = true;
                detectedSource = ChannelCatchupSource.UrlPattern;
                confidence = 58;
            }

            if (supportsCatchup && detectedSource == ChannelCatchupSource.None)
            {
                detectedSource = ChannelCatchupSource.M3uAttributes;
            }

            var summary = supportsCatchup
                ? explicitWindowHours.HasValue && explicitWindowHours.Value > 0
                    ? $"Catchup available for about {FormatWindow(explicitWindowHours.Value)}."
                    : "Catchup appears to be available, but the provider did not expose a reliable window."
                : "No clear provider catchup signal was detected.";

            return new CatchupSnapshot(
                supportsCatchup,
                explicitWindowHours,
                detectedSource,
                confidence,
                providerMode,
                providerSource,
                summary);
        }

        private static void Apply(Channel channel, CatchupSnapshot snapshot)
        {
            channel.ProviderCatchupMode = snapshot.ProviderMode;
            channel.ProviderCatchupSource = snapshot.ProviderSource;
            channel.SupportsCatchup = snapshot.SupportsCatchup;
            channel.CatchupWindowHours = snapshot.WindowHours ?? 0;
            channel.CatchupSource = snapshot.Source;
            channel.CatchupConfidence = snapshot.Confidence;
            channel.CatchupSummary = snapshot.Summary;
            channel.CatchupDetectedAtUtc = DateTime.UtcNow;
        }

        private static string GetMatchedPropertyName(JsonElement payload, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (payload.TryGetProperty(propertyName, out _))
                {
                    return propertyName;
                }
            }

            return string.Empty;
        }

        private static string GetString(JsonElement payload, params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                if (!payload.TryGetProperty(propertyName, out var property))
                {
                    continue;
                }

                switch (property.ValueKind)
                {
                    case JsonValueKind.String:
                        return property.GetString()?.Trim() ?? string.Empty;
                    case JsonValueKind.Number:
                        return property.GetRawText().Trim();
                    case JsonValueKind.True:
                        return "1";
                    case JsonValueKind.False:
                        return "0";
                }
            }

            return string.Empty;
        }

        private static bool TryParseWindowHours(string rawValue, bool isDaysValue, out int? hours)
        {
            hours = null;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return false;
            }

            var trimmed = rawValue.Trim().Trim('"', '\'');
            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
            {
                return false;
            }

            var computedHours = isDaysValue ? (int)Math.Round(value * 24d) : (int)Math.Round(value);
            hours = computedHours <= 0 ? null : computedHours;
            return hours.HasValue;
        }

        private static bool IsTruthy(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "1" or "true" or "yes" or "y" or "on" or "append" or "shift" or "default" => true,
                _ => false
            };
        }

        private static bool TryContainsCatchupHint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim().ToLowerInvariant();
            return normalized.Contains("catchup", StringComparison.Ordinal) ||
                   normalized.Contains("archive", StringComparison.Ordinal) ||
                   normalized.Contains("timeshift", StringComparison.Ordinal) ||
                   normalized.Contains("/timeshift/", StringComparison.Ordinal) ||
                   normalized.Contains("{utc", StringComparison.Ordinal) ||
                   normalized.Contains("${start", StringComparison.Ordinal);
        }

        private static string FormatWindow(int hours)
        {
            if (hours >= 48 && hours % 24 == 0)
            {
                return $"{hours / 24} days";
            }

            return $"{hours} hours";
        }

        private sealed record CatchupSnapshot(
            bool SupportsCatchup,
            int? WindowHours,
            ChannelCatchupSource Source,
            int Confidence,
            string ProviderMode,
            string ProviderSource,
            string Summary);
    }
}
