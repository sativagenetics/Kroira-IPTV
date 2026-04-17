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
}
