using SkiaSharp;

namespace Jellyfin.Plugin.Overcoat.Services;

/// <summary>
/// Composites the qualifying badges onto a poster. The side ribbons (watch-history, TMDB-trending)
/// use **cropped** art and are stacked flush from a start anchor — each placed directly beneath the
/// previous by its actual height — so they sit close together regardless of which are present, and
/// so they can be repositioned later. The IMDB Top 250 badge is a full-canvas corner overlay.
/// Order: watch-history (top) → TMDB-trending → IMDB.
/// </summary>
public sealed class BadgeCompositor
{
    public const string WatchHistory = "watch_history";
    public const string TmdbTrending = "tmdb_trending";
    public const string ImdbTop250 = "imdb_top250";

    /// <summary>Placement options for the side-ribbon stack.</summary>
    public readonly record struct BadgeLayout(bool RightSide, string Vertical, int ScalePercent, int GapPercent, int ReservedTopPercent = 0)
    {
        public static BadgeLayout Default => new(false, "top", 100, 1);
    }

    // "top" anchor starts here (fraction of poster height) — near the very top edge.
    private const float TopAnchorFraction = 0.06f;

    // A height-relative badge that reads well on a portrait poster looks much smaller against the
    // width of a 16:9 card. Give landscape artwork its own optical scale without changing the
    // configured size (or the established portrait rendering).
    private const float LandscapeScaleMultiplier = 2f;

    // Side ribbons in stack order (top → down) — cropped art, positioned/stacked dynamically.
    private static readonly (string Key, string Resource)[] Ribbons =
    {
        (WatchHistory, "Jellyfin.Plugin.Overcoat.Resources.Badges.JellyfinLeftCropped.png"),
        (TmdbTrending, "Jellyfin.Plugin.Overcoat.Resources.Badges.TMDBLeftCropped.png"),
    };

    private const string ImdbResource = "Jellyfin.Plugin.Overcoat.Resources.Badges.IMDB.png";

    private readonly Dictionary<string, byte[]> _art = new(StringComparer.Ordinal);

    /// <summary>Draws each qualifying badge with the default layout.</summary>
    public void Apply(OverlayRenderer renderer, SKBitmap poster, ISet<string> badges)
        => Apply(renderer, poster, badges, BadgeLayout.Default);

    /// <summary>Draws each qualifying badge onto <paramref name="poster"/> (mutated in place) per <paramref name="layout"/>.</summary>
    public void Apply(OverlayRenderer renderer, SKBitmap poster, ISet<string> badges, BadgeLayout layout)
    {
        if (badges.Count == 0)
        {
            return;
        }

        float scale = Math.Clamp(layout.ScalePercent, 25, 300) / 100f;
        if (poster.Width > poster.Height)
        {
            scale *= LandscapeScaleMultiplier;
        }
        int gap = (int)(poster.Height * (Math.Clamp(layout.GapPercent, 0, 20) / 100f));

        // The side ribbons that are actually present, in stack order.
        var present = Ribbons.Where(rb => badges.Contains(rb.Key)).ToList();
        if (present.Count > 0)
        {
            // Measure the stack first so we can anchor it top / middle / bottom.
            var heights = present.Select(rb => renderer.MeasureRibbonHeight(Art(rb.Resource), poster.Height, scale)).ToList();
            int stackH = heights.Sum() + (gap * (present.Count - 1));

            int startY = (layout.Vertical ?? "top").ToLowerInvariant() switch
            {
                "middle" => (poster.Height - stackH) / 2,
                "bottom" => poster.Height - stackH - (int)(poster.Height * 0.06f),
                _ => (int)(poster.Height * TopAnchorFraction),
            };
            if (startY < 0)
            {
                startY = 0;
            }

            startY = Math.Max(startY, (int)(poster.Height * (Math.Clamp(layout.ReservedTopPercent, 0, 50) / 100f)));

            // NOTE: right-side placement is just an x-shift to the right edge — the art is NOT mirrored.
            // The current ribbons are flat on the left and rounded on the right, so on the right side
            // the rounding ends up on the wrong (inner) edge. Proper right-side support needs mirrored
            // ribbon art (or a horizontal flip that doesn't reverse the logo). Tracked in the roadmap.
            var cursorY = startY;
            for (int i = 0; i < present.Count; i++)
            {
                var h = renderer.DrawRibbonBadge(poster, Art(present[i].Resource), layout.RightSide, cursorY, scale);
                cursorY += (h > 0 ? h : heights[i]) + gap;
            }
        }

        // IMDB Top 250: full-canvas corner ribbon (placement baked into the art — unaffected by layout).
        if (badges.Contains(ImdbTop250))
        {
            renderer.DrawBadge(poster, Art(ImdbResource), "mid-left", 0, fullOverlay: true);
        }
    }

    private byte[] Art(string resource)
    {
        if (!_art.TryGetValue(resource, out var bytes))
        {
            bytes = OverlayRenderer.ReadEmbeddedResource(resource);
            _art[resource] = bytes;
        }

        return bytes;
    }
}
