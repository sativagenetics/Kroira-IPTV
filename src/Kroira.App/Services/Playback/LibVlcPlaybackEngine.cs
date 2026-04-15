using LibVLCSharp.Shared;
using Microsoft.UI.Dispatching;
using System;

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

        public LibVlcPlaybackEngine()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread(); 
            Core.Initialize();
            
            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);

            _mediaPlayer.EncounteredError += OnError;
            _mediaPlayer.Playing += (s,e) => UpdateState(PlaybackState.Playing);
            _mediaPlayer.Paused += (s,e) => UpdateState(PlaybackState.Paused);
            _mediaPlayer.Stopped += (s,e) => UpdateState(PlaybackState.Stopped);
            _mediaPlayer.EndReached += (s,e) => UpdateState(PlaybackState.Stopped);
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

        public void Play(string sourceUrl)
        {
            EnsureUIThread(() => UpdateState(PlaybackState.Loading));
            using var media = new Media(_libVLC, new Uri(sourceUrl));
            _mediaPlayer.Play(media);
        }

        public void Pause() => _mediaPlayer.Pause();

        public void Stop() => _mediaPlayer.Stop();

        public void Dispose()
        {
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
        }
    }
}
