using System;
using System.Collections.Generic;
using System.Linq;
using LibVLCSharp.Shared;
using Microsoft.UI.Dispatching;

namespace Kroira.App.Services.Playback
{
    public class LibVlcPlaybackEngine : IPlaybackEngine
    {
        private readonly LibVLC _libVLC;
        private readonly MediaPlayer _mediaPlayer;
        private readonly DispatcherQueue _dispatcherQueue;

        public PlaybackState State { get; private set; } = PlaybackState.Idle;

        public event EventHandler<PlaybackState> StateChanged;
        public event EventHandler<string> ErrorOccurred;

        public object MediaPlayerInstance => _mediaPlayer;

        public long PositionMs => _mediaPlayer?.Time ?? 0;
        public long LengthMs => _mediaPlayer?.Length ?? 0;
        public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;
        public bool IsSeekable => _mediaPlayer?.IsSeekable ?? false;
        public float PlaybackRate => _mediaPlayer?.Rate ?? 1f;
        public int CurrentAudioTrackId => _mediaPlayer?.AudioTrack ?? -1;
        public int CurrentSubtitleTrackId => _mediaPlayer?.Spu ?? -1;

        public LibVlcPlaybackEngine()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            Core.Initialize();

            _libVLC = new LibVLC(
                "--avcodec-hw=any",
                "--embedded-video",
                "--no-video-deco",
                "--no-video-title-show",
                "--network-caching=1500",
                "--file-caching=800",
                "--live-caching=1500",
                "--drop-late-frames",
                "--skip-frames");
            _mediaPlayer = new MediaPlayer(_libVLC);

            _mediaPlayer.EncounteredError += OnError;
            _mediaPlayer.Playing += (s, e) => UpdateState(PlaybackState.Playing);
            _mediaPlayer.Paused += (s, e) => UpdateState(PlaybackState.Paused);
            _mediaPlayer.Stopped += (s, e) => UpdateState(PlaybackState.Stopped);
            _mediaPlayer.EndReached += (s, e) => UpdateState(PlaybackState.Stopped);
        }

        private void UpdateState(PlaybackState newState)
        {
            EnsureUIThread(() =>
            {
                State = newState;
                StateChanged?.Invoke(this, newState);
            });
        }

        private void OnError(object sender, EventArgs e)
        {
            EnsureUIThread(() =>
            {
                State = PlaybackState.Error;
                StateChanged?.Invoke(this, State);
                ErrorOccurred?.Invoke(this, "LibVLC encountered an error during playback.");
            });
        }

        private void EnsureUIThread(Action action)
        {
            if (_dispatcherQueue.HasThreadAccess)
            {
                action();
            }
            else
            {
                _dispatcherQueue.TryEnqueue(() => action());
            }
        }

        public void Play(string sourceUrl) => Play(sourceUrl, 0);

        public void Play(string sourceUrl, long startPositionMs)
        {
            if (string.IsNullOrWhiteSpace(sourceUrl))
            {
                ErrorOccurred?.Invoke(this, "Playback source is empty.");
                return;
            }

            EnsureUIThread(() => UpdateState(PlaybackState.Loading));

            var media = new Media(_libVLC, new Uri(sourceUrl));
            media.AddOption(":network-caching=1500");
            media.AddOption(":file-caching=800");
            media.AddOption(":live-caching=1500");
            media.AddOption(":input-fast-seek");

            if (startPositionMs > 0)
            {
                media.AddOption($":start-time={startPositionMs / 1000f}");
            }

            _mediaPlayer.Play(media);
        }

        public void Resume() => _mediaPlayer.SetPause(false);

        public void Pause() => _mediaPlayer.Pause();

        public void Stop() => _mediaPlayer.Stop();

        public void SeekTo(long positionMs)
        {
            if (_mediaPlayer == null || !_mediaPlayer.IsSeekable)
            {
                return;
            }

            var safePosition = Math.Max(0, positionMs);
            if (_mediaPlayer.Length > 0)
            {
                safePosition = Math.Min(safePosition, _mediaPlayer.Length);
            }

            _mediaPlayer.SeekTo(TimeSpan.FromMilliseconds(safePosition));

            if (!_mediaPlayer.IsPlaying && State != PlaybackState.Paused)
            {
                _mediaPlayer.SetPause(false);
            }
        }

        public void SeekBy(long deltaMs)
        {
            SeekTo(PositionMs + deltaMs);
        }

        public bool SetPlaybackRate(float rate)
        {
            if (_mediaPlayer == null)
            {
                return false;
            }

            var normalizedRate = Math.Clamp(rate, 0.25f, 4f);
            return _mediaPlayer.SetRate(normalizedRate) == 0;
        }

        public IReadOnlyList<PlaybackTrack> GetAudioTracks()
        {
            return BuildTracks(_mediaPlayer?.AudioTrackDescription);
        }

        public IReadOnlyList<PlaybackTrack> GetSubtitleTracks()
        {
            return BuildTracks(_mediaPlayer?.SpuDescription);
        }

        public bool SetAudioTrack(int trackId)
        {
            return _mediaPlayer != null && _mediaPlayer.SetAudioTrack(trackId);
        }

        public bool SetSubtitleTrack(int trackId)
        {
            return _mediaPlayer != null && _mediaPlayer.SetSpu(trackId);
        }

        public bool AddSubtitleFile(string filePath)
        {
            if (_mediaPlayer == null || string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            return _mediaPlayer.AddSlave(MediaSlaveType.Subtitle, filePath, true);
        }

        public void SetVideoHostHandle(IntPtr hwnd)
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Hwnd = hwnd;
            }
        }

        private static IReadOnlyList<PlaybackTrack> BuildTracks(IEnumerable<LibVLCSharp.Shared.Structures.TrackDescription> descriptions)
        {
            if (descriptions == null)
            {
                return Array.Empty<PlaybackTrack>();
            }

            return descriptions
                .Select(track => new PlaybackTrack(track.Id, track.Name))
                .ToList();
        }

        public void Dispose()
        {
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
        }
    }
}
