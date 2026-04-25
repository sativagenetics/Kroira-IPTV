#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Kroira.App.Services.Playback
{
    public enum PlayerV2PlaybackErrorCode
    {
        InvalidUrl = 0,
        Timeout,
        Cancelled,
        Unreachable,
        ForbiddenAuthFailure,
        UnsupportedMedia,
        StreamEnded,
        Unknown
    }

    public sealed record PlayerV2PlaybackError(
        PlayerV2PlaybackErrorCode Code,
        string Title,
        string Message,
        bool IsRetryable);

    public sealed class PlayerV2PlaybackErrorInput
    {
        public string? StreamUrl { get; init; }

        public string? PlayerMessage { get; init; }

        public Exception? Exception { get; init; }

        public HttpStatusCode? HttpStatusCode { get; init; }

        public bool WasCancelled { get; init; }

        public bool WasTimeout { get; init; }

        public bool StreamEnded { get; init; }
    }

    public static class PlayerV2PlaybackErrorMapper
    {
        public static PlayerV2PlaybackError Map(PlayerV2PlaybackErrorInput? input)
        {
            input ??= new PlayerV2PlaybackErrorInput();

            if (IsInvalidUrl(input.StreamUrl))
            {
                return Create(
                    PlayerV2PlaybackErrorCode.InvalidUrl,
                    "Invalid stream URL",
                    "The stream URL is empty or is not an absolute URL.",
                    isRetryable: false);
            }

            if (input.WasTimeout || input.Exception is TimeoutException || ContainsAny(input.PlayerMessage, "timeout", "timed out"))
            {
                return Create(
                    PlayerV2PlaybackErrorCode.Timeout,
                    "Stream timed out",
                    "The stream did not respond before the playback timeout elapsed.",
                    isRetryable: true);
            }

            if (input.WasCancelled || input.Exception is OperationCanceledException)
            {
                return Create(
                    PlayerV2PlaybackErrorCode.Cancelled,
                    "Playback cancelled",
                    "Playback was cancelled before the stream opened.",
                    isRetryable: false);
            }

            if (IsForbiddenOrAuthFailure(input))
            {
                return Create(
                    PlayerV2PlaybackErrorCode.ForbiddenAuthFailure,
                    "Stream authorization failed",
                    "The provider rejected the stream request or the source credentials are no longer accepted.",
                    isRetryable: false);
            }

            if (IsUnsupportedMedia(input))
            {
                return Create(
                    PlayerV2PlaybackErrorCode.UnsupportedMedia,
                    "Unsupported media",
                    "The stream format or codec is not supported by the current player runtime.",
                    isRetryable: false);
            }

            if (input.StreamEnded || ContainsAny(input.PlayerMessage, "stream ended", "end of file", "eof"))
            {
                return Create(
                    PlayerV2PlaybackErrorCode.StreamEnded,
                    "Stream ended",
                    "The provider ended the stream.",
                    isRetryable: false);
            }

            if (IsUnreachable(input))
            {
                return Create(
                    PlayerV2PlaybackErrorCode.Unreachable,
                    "Stream unreachable",
                    "The stream host could not be reached from this device.",
                    isRetryable: true);
            }

            return Create(
                PlayerV2PlaybackErrorCode.Unknown,
                "Playback failed",
                "The player could not classify the playback failure.",
                isRetryable: true);
        }

        private static PlayerV2PlaybackError Create(
            PlayerV2PlaybackErrorCode code,
            string title,
            string message,
            bool isRetryable)
        {
            return new PlayerV2PlaybackError(code, title, message, isRetryable);
        }

        private static bool IsInvalidUrl(string? streamUrl)
        {
            return string.IsNullOrWhiteSpace(streamUrl) ||
                   !Uri.TryCreate(streamUrl.Trim(), UriKind.Absolute, out var uri) ||
                   string.IsNullOrWhiteSpace(uri.Scheme);
        }

        private static bool IsForbiddenOrAuthFailure(PlayerV2PlaybackErrorInput input)
        {
            if (input.HttpStatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return true;
            }

            if (input.Exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden })
            {
                return true;
            }

            return ContainsAny(
                input.PlayerMessage,
                "401",
                "403",
                "forbidden",
                "unauthorized",
                "authentication failed",
                "authorization failed",
                "auth failed");
        }

        private static bool IsUnsupportedMedia(PlayerV2PlaybackErrorInput input)
        {
            return input.Exception is NotSupportedException ||
                   ContainsAny(
                       input.PlayerMessage,
                       "unsupported",
                       "unsupported media",
                       "unsupported format",
                       "unsupported codec",
                       "no demuxer",
                       "codec not found",
                       "invalid data found");
        }

        private static bool IsUnreachable(PlayerV2PlaybackErrorInput input)
        {
            if (input.Exception is SocketException or HttpRequestException)
            {
                return true;
            }

            return ContainsAny(
                input.PlayerMessage,
                "unreachable",
                "network is unreachable",
                "host unreachable",
                "could not resolve",
                "dns",
                "connection refused",
                "connection reset",
                "no route to host",
                "name or service not known");
        }

        private static bool ContainsAny(string? value, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (var needle in needles)
            {
                if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
