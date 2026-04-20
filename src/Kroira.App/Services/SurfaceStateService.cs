#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;

namespace Kroira.App.Services
{
    public enum SurfaceViewState
    {
        NoSources,
        Loading,
        EmptyContent,
        Offline,
        ImportFailed,
        Ready
    }

    public sealed class SurfaceStatePresentation
    {
        public SurfaceStatePresentation(
            SurfaceViewState state,
            string glyph,
            string title,
            string message,
            string actionLabel)
        {
            State = state;
            Glyph = glyph;
            Title = title;
            Message = message;
            ActionLabel = actionLabel;
        }

        public SurfaceViewState State { get; }
        public string Glyph { get; }
        public string Title { get; }
        public string Message { get; }
        public string ActionLabel { get; }
        public Visibility StateVisibility => State == SurfaceViewState.Ready ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ContentVisibility => State == SurfaceViewState.Ready ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ActionVisibility =>
            State == SurfaceViewState.Loading || string.IsNullOrWhiteSpace(ActionLabel)
                ? Visibility.Collapsed
                : Visibility.Visible;
        public Visibility ProgressVisibility => State == SurfaceViewState.Loading ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IconVisibility => State == SurfaceViewState.Loading ? Visibility.Collapsed : Visibility.Visible;
    }

    public sealed record SurfaceStateCopy(
        string Glyph,
        string LoadingTitle,
        string LoadingMessage,
        string NoSourcesTitle,
        string NoSourcesMessage,
        string EmptyTitle,
        string EmptyMessage,
        string OfflineTitle,
        string OfflineMessage,
        string ImportFailedTitle,
        string ImportFailedMessage,
        string ActionLabel)
    {
        public SurfaceStatePresentation Create(SurfaceViewState state)
        {
            return state switch
            {
                SurfaceViewState.Loading => new SurfaceStatePresentation(
                    state,
                    Glyph,
                    LoadingTitle,
                    LoadingMessage,
                    string.Empty),
                SurfaceViewState.NoSources => new SurfaceStatePresentation(
                    state,
                    Glyph,
                    NoSourcesTitle,
                    NoSourcesMessage,
                    ActionLabel),
                SurfaceViewState.EmptyContent => new SurfaceStatePresentation(
                    state,
                    Glyph,
                    EmptyTitle,
                    EmptyMessage,
                    ActionLabel),
                SurfaceViewState.Offline => new SurfaceStatePresentation(
                    state,
                    Glyph,
                    OfflineTitle,
                    OfflineMessage,
                    ActionLabel),
                SurfaceViewState.ImportFailed => new SurfaceStatePresentation(
                    state,
                    Glyph,
                    ImportFailedTitle,
                    ImportFailedMessage,
                    ActionLabel),
                _ => new SurfaceStatePresentation(
                    SurfaceViewState.Ready,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty)
            };
        }
    }

    public static class SurfaceStateCopies
    {
        public static readonly SurfaceStateCopy Home = new(
            "\uE8F1",
            "Loading home",
            "Preparing your library surface.",
            "Add your first source",
            "KROIRA needs at least one source before it can build Home.",
            "Nothing imported yet",
            "Your configured sources have not returned usable home library items yet.",
            "Home library is offline",
            "KROIRA could not reach your configured sources. Check the network or source settings.",
            "Home library needs attention",
            "KROIRA could not import usable library data from the configured source.",
            "Open sources");

        public static readonly SurfaceStateCopy LiveTv = new(
            "\uE714",
            "Loading Live TV",
            "Checking your live channel library.",
            "Add a live source",
            "KROIRA needs a configured source before Live TV can be shown.",
            "No live channels imported",
            "Your configured sources did not return any usable live channels yet.",
            "Live TV is offline",
            "KROIRA could not reach the configured live source. Check the network or source settings.",
            "Live import needs attention",
            "KROIRA could not import usable live channel data from the configured source.",
            "Open sources");

        public static readonly SurfaceStateCopy Movies = new(
            "\uE8B2",
            "Loading movies",
            "Checking your movie library.",
            "Add a movie source",
            "KROIRA needs a configured source before Movies can be shown.",
            "No movies imported",
            "Your configured sources did not return any usable movies yet.",
            "Movies are offline",
            "KROIRA could not reach the configured source for movies. Check the network or source settings.",
            "Movie import needs attention",
            "KROIRA could not import usable movie data from the configured source.",
            "Open sources");

        public static readonly SurfaceStateCopy Series = new(
            "\uE8A9",
            "Loading series",
            "Checking your series library.",
            "Add a series source",
            "KROIRA needs a configured source before Series can be shown.",
            "No series imported",
            "Your configured sources did not return any usable series yet.",
            "Series are offline",
            "KROIRA could not reach the configured source for series. Check the network or source settings.",
            "Series import needs attention",
            "KROIRA could not import usable series data from the configured source.",
            "Open sources");

        public static readonly SurfaceStateCopy Favorites = new(
            "\uE734",
            "Loading favorites",
            "Preparing your saved items.",
            "No sources configured",
            "There are no configured sources in KROIRA yet.",
            "No favorites yet",
            "Save a channel, movie, or series and it will appear here.",
            "Favorites are unavailable",
            "KROIRA could not load Favorites right now.",
            "Favorites are unavailable",
            "KROIRA could not load Favorites right now.",
            string.Empty);

        public static readonly SurfaceStateCopy ContinueWatching = new(
            "\uE768",
            "Loading Continue Watching",
            "Preparing your saved playback queue.",
            "No sources configured",
            "There are no configured sources in KROIRA yet.",
            "Nothing to resume yet",
            "Start watching a channel, movie, or episode and it will appear here.",
            "Continue Watching is unavailable",
            "KROIRA could not load saved playback right now.",
            "Continue Watching is unavailable",
            "KROIRA could not load saved playback right now.",
            string.Empty);
    }

    public sealed record SourceAvailabilitySnapshot(
        int SourceCount,
        bool HasOfflineFailure,
        bool HasImportFailure);

    public interface ISurfaceStateService
    {
        Task<SourceAvailabilitySnapshot> GetSourceAvailabilityAsync(AppDbContext db);
        SurfaceStatePresentation ResolveSourceBackedState(
            SourceAvailabilitySnapshot sourceAvailability,
            int contentCount,
            SurfaceStateCopy copy);
        SurfaceStatePresentation ResolveLocalState(int contentCount, SurfaceStateCopy copy);
        SurfaceStatePresentation CreateFailureState(SurfaceStateCopy copy, Exception exception);
    }

    public sealed class SurfaceStateService : ISurfaceStateService
    {
        public async Task<SourceAvailabilitySnapshot> GetSourceAvailabilityAsync(AppDbContext db)
        {
            var sourceIds = await db.SourceProfiles
                .AsNoTracking()
                .Select(profile => profile.Id)
                .ToListAsync();

            if (sourceIds.Count == 0)
            {
                return new SourceAvailabilitySnapshot(0, false, false);
            }

            var syncStates = await db.SourceSyncStates
                .AsNoTracking()
                .Where(state => sourceIds.Contains(state.SourceProfileId))
                .ToListAsync();

            var hasOfflineFailure = syncStates.Any(state => IsOfflineFailure(state.HttpStatusCode, state.ErrorLog));
            var hasImportFailure = syncStates.Any(state => IsImportFailure(state.HttpStatusCode, state.ErrorLog));

            return new SourceAvailabilitySnapshot(sourceIds.Count, hasOfflineFailure, hasImportFailure);
        }

        public SurfaceStatePresentation ResolveSourceBackedState(
            SourceAvailabilitySnapshot sourceAvailability,
            int contentCount,
            SurfaceStateCopy copy)
        {
            if (sourceAvailability.SourceCount <= 0)
            {
                return copy.Create(SurfaceViewState.NoSources);
            }

            if (contentCount > 0)
            {
                return copy.Create(SurfaceViewState.Ready);
            }

            if (sourceAvailability.HasOfflineFailure)
            {
                return copy.Create(SurfaceViewState.Offline);
            }

            if (sourceAvailability.HasImportFailure)
            {
                return copy.Create(SurfaceViewState.ImportFailed);
            }

            return copy.Create(SurfaceViewState.EmptyContent);
        }

        public SurfaceStatePresentation ResolveLocalState(int contentCount, SurfaceStateCopy copy)
        {
            return contentCount > 0
                ? copy.Create(SurfaceViewState.Ready)
                : copy.Create(SurfaceViewState.EmptyContent);
        }

        public SurfaceStatePresentation CreateFailureState(SurfaceStateCopy copy, Exception exception)
        {
            return IsOfflineFailure(500, exception.ToString())
                ? copy.Create(SurfaceViewState.Offline)
                : copy.Create(SurfaceViewState.ImportFailed);
        }

        private static bool IsImportFailure(int httpStatusCode, string? details)
        {
            if (string.IsNullOrWhiteSpace(details) && httpStatusCode < 400)
            {
                return false;
            }

            if (details != null &&
                details.StartsWith("EPG", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IsOfflineFailure(httpStatusCode, details))
            {
                return false;
            }

            return httpStatusCode >= 400 ||
                   ContainsAny(details, "unauthorized", "forbidden", "not found", "invalid", "username", "password", "credentials");
        }

        private static bool IsOfflineFailure(int httpStatusCode, string? details)
        {
            if (string.IsNullOrWhiteSpace(details))
            {
                return httpStatusCode is 408 or 502 or 503 or 504;
            }

            if (details.StartsWith("EPG", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return httpStatusCode is 408 or 502 or 503 or 504 ||
                   ContainsAny(
                       details,
                       "timed out",
                       "timeout",
                       "network",
                       "unable to connect",
                       "connection refused",
                       "connection reset",
                       "actively refused",
                       "no such host",
                       "name or service not known",
                       "temporary failure",
                       "temporarily unavailable",
                       "socket",
                       "dns",
                       "ssl",
                       "response status code does not indicate success: 502",
                       "response status code does not indicate success: 503",
                       "response status code does not indicate success: 504");
        }

        private static bool ContainsAny(string? details, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(details))
            {
                return false;
            }

            return tokens.Any(token => details.Contains(token, StringComparison.OrdinalIgnoreCase));
        }
    }
}
