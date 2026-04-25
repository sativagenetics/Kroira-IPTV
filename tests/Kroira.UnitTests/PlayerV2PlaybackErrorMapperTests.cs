using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using Kroira.App.Services.Playback;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kroira.UnitTests;

[TestClass]
public sealed class PlayerV2PlaybackErrorMapperTests
{
    [TestMethod]
    public void EmptyOrRelativeUrl_MapsToInvalidUrl()
    {
        AssertError(
            PlayerV2PlaybackErrorCode.InvalidUrl,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput { StreamUrl = "" }));
        AssertError(
            PlayerV2PlaybackErrorCode.InvalidUrl,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput { StreamUrl = "live/channel.m3u8" }));
    }

    [TestMethod]
    public void TimeoutSignals_MapToTimeout()
    {
        AssertError(
            PlayerV2PlaybackErrorCode.Timeout,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput
            {
                StreamUrl = "https://example.invalid/live.m3u8",
                WasTimeout = true
            }));

        AssertError(
            PlayerV2PlaybackErrorCode.Timeout,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput
            {
                StreamUrl = "https://example.invalid/live.m3u8",
                Exception = new TimeoutException()
            }));
    }

    [TestMethod]
    public void CancelledSignals_MapToCancelled()
    {
        AssertError(
            PlayerV2PlaybackErrorCode.Cancelled,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput
            {
                StreamUrl = "https://example.invalid/live.m3u8",
                WasCancelled = true
            }));

        AssertError(
            PlayerV2PlaybackErrorCode.Cancelled,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput
            {
                StreamUrl = "https://example.invalid/live.m3u8",
                Exception = new OperationCanceledException("cancelled", new CancellationToken(true))
            }));
    }

    [TestMethod]
    public void NetworkFailures_MapToUnreachable()
    {
        AssertError(
            PlayerV2PlaybackErrorCode.Unreachable,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput
            {
                StreamUrl = "https://example.invalid/live.m3u8",
                Exception = new HttpRequestException("host unreachable")
            }));

        AssertError(
            PlayerV2PlaybackErrorCode.Unreachable,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput
            {
                StreamUrl = "https://example.invalid/live.m3u8",
                PlayerMessage = "DNS lookup failed"
            }));
    }

    [TestMethod]
    public void ForbiddenOrAuthFailures_MapToForbiddenAuthFailure()
    {
        AssertError(
            PlayerV2PlaybackErrorCode.ForbiddenAuthFailure,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput
            {
                StreamUrl = "https://example.invalid/live.m3u8",
                HttpStatusCode = HttpStatusCode.Forbidden
            }));

        AssertError(
            PlayerV2PlaybackErrorCode.ForbiddenAuthFailure,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput
            {
                StreamUrl = "https://example.invalid/live.m3u8",
                PlayerMessage = "401 unauthorized"
            }));
    }

    [TestMethod]
    public void UnsupportedMediaSignals_MapToUnsupportedMedia()
    {
        AssertError(
            PlayerV2PlaybackErrorCode.UnsupportedMedia,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput
            {
                StreamUrl = "https://example.invalid/live.bin",
                Exception = new NotSupportedException()
            }));

        AssertError(
            PlayerV2PlaybackErrorCode.UnsupportedMedia,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput
            {
                StreamUrl = "https://example.invalid/live.bin",
                PlayerMessage = "unsupported codec"
            }));
    }

    [TestMethod]
    public void EndedSignals_MapToStreamEnded()
    {
        AssertError(
            PlayerV2PlaybackErrorCode.StreamEnded,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput
            {
                StreamUrl = "https://example.invalid/live.m3u8",
                StreamEnded = true
            }));

        AssertError(
            PlayerV2PlaybackErrorCode.StreamEnded,
            PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput
            {
                StreamUrl = "https://example.invalid/live.m3u8",
                PlayerMessage = "EOF"
            }));
    }

    [TestMethod]
    public void UnclassifiedFailure_MapsToUnknown()
    {
        var mapped = PlayerV2PlaybackErrorMapper.Map(new PlayerV2PlaybackErrorInput
        {
            StreamUrl = "https://example.invalid/live.m3u8",
            PlayerMessage = "mpv returned an unrecognized failure"
        });

        AssertError(PlayerV2PlaybackErrorCode.Unknown, mapped);
        Assert.IsTrue(mapped.IsRetryable);
    }

    private static void AssertError(PlayerV2PlaybackErrorCode expected, PlayerV2PlaybackError actual)
    {
        Assert.AreEqual(expected, actual.Code);
        Assert.IsFalse(string.IsNullOrWhiteSpace(actual.Title));
        Assert.IsFalse(string.IsNullOrWhiteSpace(actual.Message));
    }
}
