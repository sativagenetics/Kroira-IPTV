namespace Kroira.App.Models
{
    public enum ChannelsNavigationMode
    {
        Default = 0,
        Sports = 1
    }

    public sealed class ChannelsNavigationContext
    {
        public ChannelsNavigationMode Mode { get; init; } = ChannelsNavigationMode.Default;

        public static ChannelsNavigationContext Sports()
        {
            return new ChannelsNavigationContext
            {
                Mode = ChannelsNavigationMode.Sports
            };
        }
    }
}
