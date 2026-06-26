using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using JellyfinUser = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.Overcoat.Services;

/// <summary>
/// Recently-watched lookup for the watch-history badge — the in-process port of the Python
/// <c>get_recently_watched_shows</c>/<c>_movies</c>. Play tracking is episode-level (Jellyfin never
/// sets a series-level LastPlayedDate), so for TV it collects the SeriesId of episodes played within
/// the window; for movies it collects the movie's own id.
///
/// Plays are scanned newest-first and the walk stops as soon as it crosses the look-back window, so
/// it normally reads only the handful of pages that fall inside the window regardless of how big the
/// user's total history is. <c>maxScan</c> is only a safety ceiling for pathological cases.
/// </summary>
public sealed class WatchHistory
{
    private const int PageSize = 500;

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger _logger;

    public WatchHistory(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILogger logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _logger = logger;
    }

    /// <summary>Jellyfin SeriesIds of shows with an episode played in the last <paramref name="days"/> days.</summary>
    public HashSet<Guid> RecentlyWatchedSeriesIds(int days, bool allUsers, string? userId, int maxScan)
        => Collect(BaseItemKind.Episode, days, allUsers, userId, maxScan, item => (item as Episode)?.SeriesId);

    /// <summary>Jellyfin item ids of movies played in the last <paramref name="days"/> days.</summary>
    public HashSet<Guid> RecentlyWatchedMovieIds(int days, bool allUsers, string? userId, int maxScan)
        => Collect(BaseItemKind.Movie, days, allUsers, userId, maxScan, item => item.Id);

    private HashSet<Guid> Collect(
        BaseItemKind kind,
        int days,
        bool allUsers,
        string? userId,
        int maxScan,
        Func<BaseItem, Guid?> idSelector)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var result = new HashSet<Guid>();
        if (maxScan < PageSize)
        {
            maxScan = PageSize;
        }

        foreach (var user in ResolveUsers(allUsers, userId))
        {
            var scanned = 0;
            var stop = false;
            var lastPageFull = false;

            while (!stop && scanned < maxScan)
            {
                IReadOnlyList<BaseItem> page;
                try
                {
                    page = _libraryManager.GetItemList(new InternalItemsQuery(user)
                    {
                        IncludeItemTypes = new[] { kind },
                        IsPlayed = true,
                        Recursive = true,
                        OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                        Limit = PageSize,
                        StartIndex = scanned,
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Overcoat: watch-history query failed for a user.");
                    break;
                }

                if (page.Count == 0)
                {
                    break;
                }

                foreach (var item in page)
                {
                    var data = _userDataManager.GetUserData(user, item);
                    if (data?.LastPlayedDate is { } playedAt)
                    {
                        // Newest-first by play date → once we cross the window, everything after is
                        // older too, so stop paging this user.
                        if (playedAt < cutoff)
                        {
                            stop = true;
                            break;
                        }

                        if (idSelector(item) is { } id && id != Guid.Empty)
                        {
                            result.Add(id);
                        }
                    }
                }

                lastPageFull = page.Count == PageSize;
                scanned += page.Count;
                if (!lastPageFull)
                {
                    break;
                }
            }

            if (!stop && lastPageFull && scanned >= maxScan)
            {
                _logger.LogInformation(
                    "Overcoat: watch-history scan hit the {Max}-play safety cap for a user; older in-window plays may be missed. Raise the cap if needed.",
                    maxScan);
            }
        }

        return result;
    }

    private IEnumerable<JellyfinUser> ResolveUsers(bool allUsers, string? userId)
    {
        if (allUsers)
        {
            foreach (var u in _userManager.GetUsers())
            {
                yield return u;
            }

            yield break;
        }

        if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var g))
        {
            var u = _userManager.GetUserById(g);
            if (u is not null)
            {
                yield return u;
                yield break;
            }
        }

        var first = _userManager.GetFirstUser();
        if (first is not null)
        {
            yield return first;
        }
    }
}
