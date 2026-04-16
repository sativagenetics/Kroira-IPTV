using System;
using System.Collections.Generic;

namespace Kroira.App.Services.Playback
{
    public enum PlaybackState { Idle, Loading, Playing, Paused, Stopped, Error }

    public sealed class PlaybackTrack
    {
        public PlaybackTrack(int id, string name)
        {
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? $"Track {id}" : name;
        }

        public int Id { get; }
        public string Name { get; }
    }

    public interface IPlaybackEngine : IDisposable
    {
        PlaybackState State { get; }
        event EventHandler<PlaybackState> StateChanged;
        event EventHandler<string> ErrorOccurred;

        object MediaPlayerInstance { get; }

        long PositionMs { get; }
        long LengthMs { get; }
        bool IsPlaying { get; }
        bool IsSeekable { get; }
        float PlaybackRate { get; }
        int CurrentAudioTrackId { get; }
        int CurrentSubtitleTrackId { get; }

        void Play(string sourceUrl);
        void Play(string sourceUrl, long startPositionMs);
        void Resume();
        void Pause();
        void Stop();
        void SeekTo(long positionMs);
        void SeekBy(long deltaMs);
        bool SetPlaybackRate(float rate);
        IReadOnlyList<PlaybackTrack> GetAudioTracks();
        IReadOnlyList<PlaybackTrack> GetSubtitleTracks();
        bool SetAudioTrack(int trackId);
        bool SetSubtitleTrack(int trackId);
        bool AddSubtitleFile(string filePath);
    }
}
