#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroira.App.Data;
using Kroira.App.Models;
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
        string NoSourcesActionLabel,
        string EmptyActionLabel,
        string OfflineActionLabel,
        string ImportFailedActionLabel)
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
                    NoSourcesActionLabel),
                SurfaceViewState.EmptyContent => new SurfaceStatePresentation(
                    state,
                    Glyph,
                    EmptyTitle,
                    EmptyMessage,
                    EmptyActionLabel),
                SurfaceViewState.Offline => new SurfaceStatePresentation(
                    state,
                    Glyph,
                    OfflineTitle,
                    OfflineMessage,
                    OfflineActionLabel),
                SurfaceViewState.ImportFailed => new SurfaceStatePresentation(
                    state,
                    Glyph,
                    ImportFailedTitle,
                    ImportFailedMessage,
                    ImportFailedActionLabel),
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
            "Preparing your library.",
            "Add your first source",
            "Connect an M3U playlist or Xtream provider to start building Home.",
            "Home is still getting ready",
            "Your sources are saved, but Home does not have enough imported items yet.",
            "Sources are temporarily offline",
            "KROIRA could not reach your sources just now. Check the connection or try again shortly.",
            "Home needs a refresh",
            "The latest refresh did not return enough library data for Home. Review the affected source, then try again.",
            "Open Sources",
            "Open Sources",
            "Open Sources",
            "Open Sources");

        public static readonly SurfaceStateCopy LiveTv = new(
            "\uE714",
            "Loading Live TV",
            "Checking your live channels.",
            "Add a live source",
            "Connect a source with live channels to start watching Live TV.",
            "No live channels imported",
            "Your current sources have not returned any usable live channels yet.",
            "Live TV is temporarily offline",
            "KROIRA could not reach your live source just now. Check the connection or try again shortly.",
            "Live TV needs a refresh",
            "The latest refresh did not return enough live channel data. Review the affected source, then try again.",
            "Open Sources",
            "Open Sources",
            "Open Sources",
            "Open Sources");

        public static readonly SurfaceStateCopy Movies = new(
            "\uE8B2",
            "Loading movies",
            "Checking your movie library.",
            "Add a movie source",
            "Connect a source with movie catalog data to start browsing Movies.",
            "No movies imported",
            "Your current sources have not returned any usable movies yet.",
            "Movies are temporarily offline",
            "KROIRA could not reach your movie source just now. Check the connection or try again shortly.",
            "Movies need a refresh",
            "The latest refresh did not return enough movie data. Review the affected source, then try again.",
            "Open Sources",
            "Open Sources",
            "Open Sources",
            "Open Sources");

        public static readonly SurfaceStateCopy Series = new(
            "\uE8A9",
            "Loading series",
            "Checking your series library.",
            "Add a series source",
            "Connect a source with series data to start browsing Series.",
            "No series imported",
            "Your current sources have not returned any usable series yet.",
            "Series is temporarily offline",
            "KROIRA could not reach your series source just now. Check the connection or try again shortly.",
            "Series needs a refresh",
            "The latest refresh did not return enough series data. Review the affected source, then try again.",
            "Open Sources",
            "Open Sources",
            "Open Sources",
            "Open Sources");

        public static readonly SurfaceStateCopy Favorites = new(
            "\uE734",
            "Loading favorites",
            "Preparing your saved items.",
            "No sources configured",
            "Add a source first, then save channels, movies, or series to Favorites.",
            "No favorites yet",
            "Save a channel, movie, or series and it will appear here.",
            "Favorites are temporarily unavailable",
            "Saved items could not be loaded right now. Try again in a moment.",
            "Favorites are temporarily unavailable",
            "Saved items could not be loaded right now. Try again in a moment.",
            "Open Sources",
            string.Empty,
            string.Empty,
            string.Empty);

        public static readonly SurfaceStateCopy ContinueWatching = new(
            "\uE768",
            "Loading Continue Watching",
            "Preparing your saved playback queue.",
            "No sources configured",
            "Add a source first, then start watching to build your resume queue.",
            "Nothing to resume yet",
            "Start watching a channel, movie, or episode and it will appear here.",
            "Continue Watching is temporarily unavailable",
            "Saved playback could not be loaded right now. Try again in a moment.",
            "Continue Watching is temporarily unavailable",
            "Saved playback could not be loaded right now. Try again in a moment.",
            "Open Sources",
            string.Empty,
            string.Empty,
            string.Empty);
    }

    public sealed record SourceAvailabilitySnapshot(
        int SourceCount,
        bool HasOfflineFailure,
        bool HasImportFailure,
        bool HasSuccessfulSync);

    public interface ISurfaceStateService
    {
        Task<SourceAvailabilitySnapshot> GetSourceAvailabilityAsync(AppDbContext db);
        Task<int> GetCurrentSourceIssueCountAsync(AppDbContext db);
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
            var sources = await db.SourceProfiles
                .AsNoTracking()
                .Select(profile => new
                {
                    profile.Id,
                    profile.LastSync
                })
                .ToListAsync();

            if (sources.Count == 0)
            {
                return new SourceAvailabilitySnapshot(0, false, false, false);
            }

            var sourceIds = sources.Select(profile => profile.Id).ToList();
            var syncStates = await db.SourceSyncStates
                .AsNoTracking()
                .Where(state => sourceIds.Contains(state.SourceProfileId))
                .ToListAsync();
            var syncStateBySource = syncStates
                .GroupBy(state => state.SourceProfileId)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.LastAttempt).First());

            var hasOfflineFailure = false;
            var hasImportFailure = false;
            var hasSuccessfulSync = false;

            foreach (var source in sources)
            {
                if (source.LastSync.HasValue)
                {
                    hasSuccessfulSync = true;
                }

                if (!syncStateBySource.TryGetValue(source.Id, out var state))
                {
                    continue;
                }

                var failureIsCurrent = IsCurrentFailure(state, source.LastSync);
                if (!failureIsCurrent)
                {
                    continue;
                }

                hasOfflineFailure |= IsOfflineFailure(state.HttpStatusCode, state.ErrorLog);
                hasImportFailure |= IsImportFailure(state.HttpStatusCode, state.ErrorLog);
            }

            return new SourceAvailabilitySnapshot(sourceIds.Count, hasOfflineFailure, hasImportFailure, hasSuccessfulSync);
        }

        public async Task<int> GetCurrentSourceIssueCountAsync(AppDbContext db)
        {
            var sources = await db.SourceProfiles
                .AsNoTracking()
                .Select(profile => new
                {
                    profile.Id,
                    profile.LastSync
                })
                .ToListAsync();
            if (sources.Count == 0)
            {
                return 0;
            }

            var sourceIds = sources.Select(profile => profile.Id).ToList();
            var syncStates = await db.SourceSyncStates
                .AsNoTracking()
                .Where(state => sourceIds.Contains(state.SourceProfileId))
                .ToListAsync();
            var syncStateBySource = syncStates
                .GroupBy(state => state.SourceProfileId)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.LastAttempt).First());

            return sources.Count(source =>
                syncStateBySource.TryGetValue(source.Id, out var state) &&
                IsCurrentFailure(state, source.LastSync));
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

        private static bool IsCurrentFailure(SourceSyncState syncState, DateTime? lastSuccessfulSyncUtc)
        {
            if (!IsOfflineFailure(syncState.HttpStatusCode, syncState.ErrorLog) &&
                !IsImportFailure(syncState.HttpStatusCode, syncState.ErrorLog))
            {
                return false;
            }

            if (!lastSuccessfulSyncUtc.HasValue)
            {
                return true;
            }

            if (syncState.LastAttempt == default)
            {
                return false;
            }

            var lastSuccess = NormalizeUtc(lastSuccessfulSyncUtc.Value);
            var lastAttempt = NormalizeUtc(syncState.LastAttempt);
            return lastAttempt > lastSuccess.AddSeconds(1);
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
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
