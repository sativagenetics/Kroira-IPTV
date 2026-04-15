using System;

namespace Kroira.App.Services.Playback
{
    public enum PlaybackState { Idle, Loading, Playing, Paused, Stopped, Error }

    public interface IPlaybackEngine : IDisposable
    {
        PlaybackState State { get; }
        event EventHandler<PlaybackState> StateChanged;
        event EventHandler<string> ErrorOccurred;

        object MediaPlayerInstance { get; }

        void Play(string sourceUrl);
        void Pause();
        void Stop();
    }
}
