#nullable enable
using System;

namespace Kroira.App.Models
{
    public enum SourceRefreshTrigger
    {
        Manual = 0,
        InitialImport = 1,
        Auto = 2
    }

    public enum SourceRefreshScope
    {
        Full = 0,
        LiveOnly = 1,
        VodOnly = 2,
        EpgOnly = 3
    }

    public sealed class SourceAutoRefreshSettings
    {
        public bool IsEnabled { get; set; } = true;
        public int IntervalHours { get; set; } = 6;
        public bool RunAfterLaunch { get; set; } = true;

        public TimeSpan GetInterval()
        {
            var normalizedHours = Math.Clamp(IntervalHours, 1, 24);
            return TimeSpan.FromHours(normalizedHours);
        }
    }
}
