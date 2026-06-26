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

    /// <summary>
    /// Returns the banner text (e.g. "NEW", "AIRING", "RETURNING 5/12", "ENDED"), or null if the
    /// show matches no overlay (caller skips the banner).
    /// </summary>
    public static string? Resolve(TmdbService.TvStatusInfo info)
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

        // Step 3.1: refine a "Returning Series" (base AIRING) by actual air dates.
        if (baseText == "AIRING")
        {
            // Actively mid-season: last episode within 14 days.
            if (info.DaysSinceLastAir is { } sinceLast && sinceLast <= 14)
            {
                return "AIRING";
            }

            if (info.NextAirDate is { } next && info.DaysUntilAir is { } until)
            {
                if (until is >= 0 and <= 3)
                {
                    return "AIRING";
                }

                if (until is >= 4 and <= 90)
                {
                    return $"RETURNING {next}";
                }

                return "RETURNING"; // > 90 days out
            }

            // No air-date data for a returning series.
            return "RETURNING";
        }

        return baseText;
    }
}
