using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Overcoat.Configuration;

/// <summary>
/// Strongly-typed plugin configuration. Replaces the old <c>config.yml</c> + <c>.env</c> +
/// interactive setup wizard. Persisted by Jellyfin as XML and edited from the dashboard
/// (<c>configPage.html</c>). Credentials that used to live in <c>.env</c> are now just fields —
/// there is no callback host/port any more because uploads are in-process.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // --- Credentials (was .env) ---

    /// <summary>Gets or sets the TMDB v3 API key.</summary>
    public string TmdbApiKey { get; set; } = string.Empty;

    // --- Global behaviour (was settings:) ---

    /// <summary>
    /// Gets or sets a value indicating whether the per-item skip cache is honoured.
    /// False reprocesses every item each run (state is still recorded).
    /// </summary>
    public bool CacheEnabled { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the clean original poster is vaulted before overlaying.</summary>
    public bool BackupOriginals { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether orphaned cache entries / vaulted originals for
    /// items no longer in Jellyfin are pruned at the end of a full run.
    /// </summary>
    public bool CleanupOrphans { get; set; }

    /// <summary>Gets or sets a value indicating whether the task computes overlays but skips saving (diagnostics).</summary>
    public bool DryRun { get; set; }

    // --- Badge globals (was badges:) ---

    /// <summary>Gets or sets the master kill-switch for all badges.</summary>
    public bool BadgesEnabled { get; set; } = true;

    /// <summary>Gets or sets the look-back window (days) for the watch-history badge.</summary>
    public int WatchHistoryDays { get; set; } = 20;

    /// <summary>Gets or sets a value indicating whether watch history considers all users (vs only <see cref="WatchHistoryUserId"/>).</summary>
    public bool WatchHistoryAllUsers { get; set; } = true;

    /// <summary>Gets or sets the single user (by id) to scope watch history to when <see cref="WatchHistoryAllUsers"/> is false. Empty = first admin.</summary>
    public string WatchHistoryUserId { get; set; } = string.Empty;

    /// <summary>Gets or sets the TMDB trending window: "day" or "week".</summary>
    public string TrendingTimeWindow { get; set; } = "week";

    /// <summary>Gets or sets the TMDB list id used for the IMDB Top 250 movie badge.</summary>
    public string ImdbTop250MovieListId { get; set; } = "10265";

    /// <summary>Gets or sets the TMDB list id used for the IMDB Top 250 TV badge.</summary>
    public string ImdbTop250TvListId { get; set; } = "8647022";

    // --- Selection (was jellyfin.libraries / ignore_shows / tmdb_overrides) ---

    /// <summary>Gets or sets the per-library overlay/badge selection.</summary>
    public List<LibraryConfig> Libraries { get; set; } = new();

    /// <summary>Gets or sets show/movie titles to skip entirely.</summary>
    public List<string> IgnoreTitles { get; set; } = new();

    /// <summary>
    /// Gets or sets an allow-list of titles. When non-empty, ONLY these titles are processed
    /// (targeted reprocessing / safe single-show testing). Empty = process everything.
    /// </summary>
    public List<string> LimitToTitles { get; set; } = new();

    /// <summary>Gets or sets manual title→TMDB id overrides for items that resolve incorrectly.</summary>
    public List<TmdbOverride> TmdbOverrides { get; set; } = new();
}

/// <summary>Per-library configuration. Mirrors a normalized entry from the old <c>jellyfin.libraries</c>.</summary>
public class LibraryConfig
{
    /// <summary>Gets or sets the Jellyfin library (virtual folder) display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the library type: "tv", "movie", or empty to auto-detect from Jellyfin.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether this library is processed.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether status banners are drawn (TV only; ignored for movies).</summary>
    public bool StatusOverlays { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the watch-history badge is eligible in this library.</summary>
    public bool WatchHistoryBadge { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the TMDB-trending badge is eligible in this library.</summary>
    public bool TrendingBadge { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the IMDB Top 250 badge is eligible in this library.</summary>
    public bool ImdbTop250Badge { get; set; } = true;
}

/// <summary>A manual title→TMDB id mapping. Mirrors an entry from the old <c>tmdb_overrides</c>.</summary>
public class TmdbOverride
{
    /// <summary>Gets or sets the item title to match.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the production year (0 = any).</summary>
    public int Year { get; set; }

    /// <summary>Gets or sets the TMDB id to force.</summary>
    public int TmdbId { get; set; }
}
