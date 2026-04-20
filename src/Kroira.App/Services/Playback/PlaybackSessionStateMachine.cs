using System;

namespace Kroira.App.Services.Playback
{
    public enum PlaybackSessionState
    {
        Idle,
        Opening,
        Buffering,
        Playing,
        Paused,
        Error,
        Ended
    }

    public sealed class PlaybackSessionStateSnapshot
    {
        public PlaybackSessionState State { get; init; }
        public string Message { get; init; } = string.Empty;
        public int RetryAttempt { get; init; }
    }

    internal sealed class PlaybackSessionStateMachine
    {
        public PlaybackSessionState State { get; private set; } = PlaybackSessionState.Idle;
        public string Message { get; private set; } = string.Empty;
        public int RetryAttempt { get; private set; }

        public event Action<PlaybackSessionStateSnapshot> StateChanged;

        public void Reset()
        {
            TransitionTo(PlaybackSessionState.Idle, string.Empty, 0);
        }

        public void BeginLoad()
        {
            TransitionTo(PlaybackSessionState.Opening, "Loading stream...", 0);
        }

        public void SetBuffering()
        {
            TransitionTo(PlaybackSessionState.Buffering, "Buffering stream...", 0);
        }

        public void SetPlaying()
        {
            TransitionTo(PlaybackSessionState.Playing, string.Empty, 0);
        }

        public void SetPaused()
        {
            TransitionTo(PlaybackSessionState.Paused, "Paused", 0);
        }

        public void BeginReconnect(int retryAttempt, int maxRetries, string reason)
        {
            var details = string.IsNullOrWhiteSpace(reason) ? "Trying to recover stream." : reason.Trim();
            TransitionTo(
                PlaybackSessionState.Opening,
                $"Reconnecting ({retryAttempt}/{maxRetries})... {details}",
                retryAttempt);
        }

        public void SetError(string message)
        {
            TransitionTo(
                PlaybackSessionState.Error,
                string.IsNullOrWhiteSpace(message) ? "Playback failed." : message.Trim(),
                RetryAttempt);
        }

        public void SetEnded()
        {
            TransitionTo(PlaybackSessionState.Ended, string.Empty, 0);
        }

        private void TransitionTo(PlaybackSessionState nextState, string message, int retryAttempt)
        {
            State = nextState;
            Message = message;
            RetryAttempt = retryAttempt;
            StateChanged?.Invoke(new PlaybackSessionStateSnapshot
            {
                State = nextState,
                Message = message,
                RetryAttempt = retryAttempt
            });
        }
    }
}
