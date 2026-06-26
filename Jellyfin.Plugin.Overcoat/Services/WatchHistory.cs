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
/// </summary>
public sealed class WatchHistory
{
    private const int QueryLimit = 2000;

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
    public HashSet<Guid> RecentlyWatchedSeriesIds(int days, bool allUsers, string? userId)
        => Collect(BaseItemKind.Episode, days, allUsers, userId, item => (item as Episode)?.SeriesId);

    /// <summary>Jellyfin item ids of movies played in the last <paramref name="days"/> days.</summary>
    public HashSet<Guid> RecentlyWatchedMovieIds(int days, bool allUsers, string? userId)
        => Collect(BaseItemKind.Movie, days, allUsers, userId, item => item.Id);

    private HashSet<Guid> Collect(
        BaseItemKind kind,
        int days,
        bool allUsers,
        string? userId,
        Func<BaseItem, Guid?> idSelector)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var result = new HashSet<Guid>();

        foreach (var user in ResolveUsers(allUsers, userId))
        {
            IReadOnlyList<BaseItem> played;
            try
            {
                played = _libraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[] { kind },
                    IsPlayed = true,
                    Recursive = true,
                    OrderBy = new[] { (ItemSortBy.DatePlayed, SortOrder.Descending) },
                    Limit = QueryLimit,
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Overcoat: watch-history query failed for a user.");
                continue;
            }

            foreach (var item in played)
            {
                var data = _userDataManager.GetUserData(user, item);
                if (data?.LastPlayedDate is { } played_at && played_at >= cutoff
                    && idSelector(item) is { } id && id != Guid.Empty)
                {
                    result.Add(id);
                }
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
