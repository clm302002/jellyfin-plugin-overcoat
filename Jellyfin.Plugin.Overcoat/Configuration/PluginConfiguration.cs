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

    // --- Schedule ---
    // Jellyfin only reads IScheduledTask.GetDefaultTriggers() the first time it registers a task;
    // after that the trigger lives in the server's own task config. These settings are applied to the
    // live task at startup and on every config save (see ScheduledTasks.ScheduleSync), which is what
    // makes the run time editable from the plugin page instead of only from Dashboard → Scheduled Tasks.

    /// <summary>
    /// Gets or sets a value indicating whether Overcoat manages its own schedule. Turn this off to
    /// hand control back to Dashboard → Scheduled Tasks (Overcoat then leaves the triggers alone).
    /// </summary>
    public bool ScheduleEnabled { get; set; } = true;

    /// <summary>Gets or sets the hour (0–23, server local time) the overlay task runs.</summary>
    public int ScheduleHour { get; set; } = 3;

    /// <summary>Gets or sets the minute (0–59) past <see cref="ScheduleHour"/> the overlay task runs.</summary>
    public int ScheduleMinute { get; set; }

    /// <summary>
    /// Gets the configured run time as a trigger offset from midnight, clamped to a valid
    /// time of day so a bad hand-edit of the XML can't produce an out-of-range trigger.
    /// </summary>
    public TimeSpan ScheduleTimeOfDay => new TimeSpan(
        Math.Clamp(ScheduleHour, 0, 23),
        Math.Clamp(ScheduleMinute, 0, 59),
        0);

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

    // --- Status banner appearance ---

    /// <summary>Gets or sets the banner fill treatment: "solid" (filled pill) or "glass" (frosted translucent chip).</summary>
    public string BannerStyle { get; set; } = "solid";

    /// <summary>Gets or sets the banner shape: "pill" (fully rounded), "square", or "drop" (flush to the edge, rounded inner corners).</summary>
    public string BannerShape { get; set; } = "pill";

    /// <summary>Gets or sets where the banner sits: "top" or "bottom".</summary>
    public string BannerPosition { get; set; } = "top";

    /// <summary>Gets or sets a multiplier on the computed banner font size (1.0 = calibrated default).</summary>
    public double BannerFontScale { get; set; } = 1.0;

    /// <summary>Gets or sets a value indicating whether the per-status icon is drawn beside the banner text.</summary>
    public bool BannerIcons { get; set; } = true;

    // --- Status date display (AIRING shows the next episode; RETURNING shows the upcoming date) ---

    /// <summary>Gets or sets how the AIRING next-episode is shown: "date" (6/28), "day" (Tue), or "countdown" (3d). Always shown when known (no window).</summary>
    public string AiringDateFormat { get; set; } = "date";

    /// <summary>Gets or sets how the RETURNING date is shown: "date" (7/14), "day" (Mon), or "countdown" (21d).</summary>
    public string ReturningDateFormat { get; set; } = "date";

    /// <summary>Gets or sets the RETURNING date window in days: show the date only when the next episode is within this many days. -1 = never, large = always-when-known.</summary>
    public int ReturningDateWindowDays { get; set; } = 90;

    /// <summary>Gets or sets the banner colour for NEW (hex).</summary>
    public string ColorNew { get; set; } = "#5EBD3E";

    /// <summary>Gets or sets the banner colour for AIRING (hex).</summary>
    public string ColorAiring { get; set; } = "#149BDA";

    /// <summary>Gets or sets the banner colour for RETURNING (hex).</summary>
    public string ColorReturning { get; set; } = "#A020F0";

    /// <summary>Gets or sets the banner colour for ENDED (hex).</summary>
    public string ColorEnded { get; set; } = "#424242";

    /// <summary>Gets or sets the banner colour for CANCELED (hex).</summary>
    public string ColorCanceled { get; set; } = "#D32F2F";

    /// <summary>Resolves the configured banner colour for a banner text by its status keyword.</summary>
    public string ColorForStatus(string text)
    {
        var u = (text ?? string.Empty).ToUpperInvariant();
        if (u.Contains("RETURNING", StringComparison.Ordinal))
        {
            return ColorReturning;
        }

        if (u.Contains("CANCELED", StringComparison.Ordinal))
        {
            return ColorCanceled;
        }

        if (u.Contains("AIRING", StringComparison.Ordinal))
        {
            return ColorAiring;
        }

        if (u.Contains("ENDED", StringComparison.Ordinal))
        {
            return ColorEnded;
        }

        if (u.Contains("NEW", StringComparison.Ordinal))
        {
            return ColorNew;
        }

        return "#262626";
    }

    // --- Glass-specific appearance ---

    /// <summary>Gets or sets the glass frost tint colour (hex).</summary>
    public string GlassTint { get; set; } = "#0E1018";

    /// <summary>Gets or sets the glass frost tint strength (0–100 → veil opacity).</summary>
    public int GlassTintStrength { get; set; } = 49;

    /// <summary>Gets or sets the glass frost blur amount (0–100).</summary>
    public int GlassBlur { get; set; } = 50;

    /// <summary>Gets or sets the neon glow intensity (0–100).</summary>
    public int NeonGlow { get; set; } = 60;

    /// <summary>Gets or sets the banner font: "default" (embedded), "sans", "serif", or "mono".</summary>
    public string BannerFont { get; set; } = "default";

    // --- Banner layout ---

    /// <summary>Gets or sets a value indicating whether the banner spans the full poster width (a band).</summary>
    public bool BannerFullWidth { get; set; }

    /// <summary>Gets or sets the horizontal alignment: "left", "center", or "right".</summary>
    public string BannerAlign { get; set; } = "center";

    /// <summary>Gets or sets a value indicating whether a drop shadow is drawn under the banner.</summary>
    public bool BannerShadow { get; set; }

    /// <summary>Gets or sets the drop-shadow strength (0–100 → shadow opacity).</summary>
    public int BannerShadowStrength { get; set; } = 60;

    // --- Per-status visibility + labels ---

    /// <summary>Gets or sets a value indicating whether a NEW banner is drawn.</summary>
    public bool ShowNew { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether an AIRING banner is drawn.</summary>
    public bool ShowAiring { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether a RETURNING banner is drawn.</summary>
    public bool ShowReturning { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether an ENDED banner is drawn.</summary>
    public bool ShowEnded { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether a CANCELED banner is drawn.</summary>
    public bool ShowCanceled { get; set; } = true;

    /// <summary>Gets or sets the display label for NEW.</summary>
    public string LabelNew { get; set; } = "NEW";

    /// <summary>Gets or sets the display label for AIRING.</summary>
    public string LabelAiring { get; set; } = "AIRING";

    /// <summary>Gets or sets the display label for RETURNING.</summary>
    public string LabelReturning { get; set; } = "RETURNING";

    /// <summary>Gets or sets the display label for ENDED.</summary>
    public string LabelEnded { get; set; } = "ENDED";

    /// <summary>Gets or sets the display label for CANCELED.</summary>
    public string LabelCanceled { get; set; } = "CANCELED";

    /// <summary>Whether a banner should be drawn for the given status identity.</summary>
    public bool IsStatusShown(string identity) => identity switch
    {
        "NEW" => ShowNew,
        "AIRING" => ShowAiring,
        "RETURNING" => ShowReturning,
        "ENDED" => ShowEnded,
        "CANCELED" => ShowCanceled,
        _ => true,
    };

    /// <summary>The display label for a status identity (falls back to the identity if cleared).</summary>
    public string LabelForStatus(string identity)
    {
        var label = identity switch
        {
            "NEW" => LabelNew,
            "AIRING" => LabelAiring,
            "RETURNING" => LabelReturning,
            "ENDED" => LabelEnded,
            "CANCELED" => LabelCanceled,
            _ => identity,
        };
        return string.IsNullOrWhiteSpace(label) ? identity : label.Trim();
    }

    /// <summary>The configured colour (hex) for a status identity.</summary>
    public string ColorForIdentity(string identity) => identity switch
    {
        "NEW" => ColorNew,
        "AIRING" => ColorAiring,
        "RETURNING" => ColorReturning,
        "ENDED" => ColorEnded,
        "CANCELED" => ColorCanceled,
        _ => "#262626",
    };

    // --- Badge globals (was badges:) ---

    /// <summary>Gets or sets the master kill-switch for all badges.</summary>
    public bool BadgesEnabled { get; set; } = true;

    /// <summary>Gets or sets the look-back window (days) for the watch-history badge.</summary>
    public int WatchHistoryDays { get; set; } = 20;

    /// <summary>Gets or sets a value indicating whether watch history considers all users (vs only <see cref="WatchHistoryUserId"/>).</summary>
    public bool WatchHistoryAllUsers { get; set; } = true;

    /// <summary>Gets or sets the single user (by id) to scope watch history to when <see cref="WatchHistoryAllUsers"/> is false. Empty = first admin.</summary>
    public string WatchHistoryUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the safety cap on how many recent plays per user the watch-history scan walks.
    /// The scan stops early once it leaves the look-back window, so this only bounds pathological
    /// histories; raise it if a very heavy watcher's older in-window plays are being missed.
    /// </summary>
    public int WatchHistoryMaxScan { get; set; } = 10000;

    /// <summary>Gets or sets the TMDB trending window: "day" or "week".</summary>
    public string TrendingTimeWindow { get; set; } = "week";

    /// <summary>Gets or sets the TMDB list id used for the IMDB Top 250 movie badge.</summary>
    public string ImdbTop250MovieListId { get; set; } = "10265";

    /// <summary>Gets or sets the TMDB list id used for the IMDB Top 250 TV badge.</summary>
    public string ImdbTop250TvListId { get; set; } = "8647022";

    // --- Badge layout (side ribbons: watch-history + TMDB-trending) ---

    /// <summary>Gets or sets the side the badge ribbons sit on: "left" or "right".</summary>
    public string BadgeSide { get; set; } = "left";

    /// <summary>Gets or sets the vertical anchor of the badge stack: "top", "middle", or "bottom".</summary>
    public string BadgeVertical { get; set; } = "middle";

    /// <summary>Gets or sets the badge size as a percentage of the calibrated size (50–200).</summary>
    public int BadgeScale { get; set; } = 100;

    /// <summary>Gets or sets the gap between stacked badges as a percentage of poster height (0–10).</summary>
    public int BadgeGapPercent { get; set; } = 1;

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
