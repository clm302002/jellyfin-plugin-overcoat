namespace Jellyfin.Plugin.Overcoat.Services;

/// <summary>
/// Decides the status-banner text for a show. Encodes the rules from
/// <c>overlays/SeriesStatus-Overlays-Jellyfin.yml</c> (the weighted filter match in
/// <c>get_overlay_config_for_show</c>) plus the Step 3.1 air-date refinement from <c>process_show</c>.
///
/// Weighted matches (highest weight wins):
///   returning/planned/production + first aired ≤ 90 days  → NEW       (weight 60)
///   returning/planned/production                          → AIRING    (weight 40)
///   ended                                                 → ENDED     (weight 20)
///   canceled                                              → CANCELED  (weight 20)
/// A base match of AIRING is then refined to AIRING / "RETURNING M/D" / RETURNING by air dates.
/// </summary>
public static class StatusOverlayResolver
{
    private const int NewMaxDaysSinceFirstAir = 90;
    private const int MidSeasonDays = 14;   // last episode within this many days → still AIRING
    private const int ImminentDays = 3;     // next episode within this many days → AIRING (not RETURNING)

    /// <summary>Identity-only resolve (no date). Convenience wrapper over <see cref="ResolveIdentity"/>.</summary>
    public static string? Resolve(TmdbService.TvStatusInfo info) => ResolveIdentity(info);

    /// <summary>
    /// Resolves the canonical status identity (NEW / AIRING / RETURNING / ENDED / CANCELED) for a show,
    /// or null if it matches no overlay (caller skips the banner). This is **classification only** —
    /// the date suffix and its window/format are applied by the caller, so they can be configured
    /// per status without affecting which icon/colour/label is chosen.
    /// </summary>
    public static string? ResolveIdentity(TmdbService.TvStatusInfo info)
    {
        if (string.IsNullOrEmpty(info.Status))
        {
            return null;
        }

        var status = info.Status.ToLowerInvariant();
        bool active = status.Contains("returning", StringComparison.Ordinal)
            || status.Contains("planned", StringComparison.Ordinal)
            || status.Contains("production", StringComparison.Ordinal);

        // Pick the highest-weight base overlay.
        string? baseText = null;
        int weight = -1;

        if (active && info.DaysSinceFirstAir is { } d && d <= NewMaxDaysSinceFirstAir && 60 > weight)
        {
            baseText = "NEW";
            weight = 60;
        }

        if (active && 40 > weight)
        {
            baseText = "AIRING";
            weight = 40;
        }

        if (status.Contains("ended", StringComparison.Ordinal) && 20 > weight)
        {
            baseText = "ENDED";
            weight = 20;
        }

        if (status.Contains("canceled", StringComparison.Ordinal) && 20 > weight)
        {
            baseText = "CANCELED";
            weight = 20;
        }

        if (baseText is null)
        {
            return null;
        }

        // Refine the active "AIRING" base: a show that's mid-season (last episode recent) or has an
        // imminent next episode stays AIRING; otherwise it's between seasons → RETURNING.
        if (baseText == "AIRING")
        {
            bool midSeason = info.DaysSinceLastAir is { } sinceLast && sinceLast <= MidSeasonDays;
            bool imminent = info.DaysUntilAir is { } until && until >= 0 && until <= ImminentDays;
            return (midSeason || imminent) ? "AIRING" : "RETURNING";
        }

        return baseText;
    }
}
