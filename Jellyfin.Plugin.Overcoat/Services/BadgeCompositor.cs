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

    // Where the side-ribbon stack starts (fraction of poster height) and the gap between ribbons.
    // (Will become configurable when the badge settings land; small gap ≈ the original flush design.)
    private const float RibbonStartFraction = 0.34f;
    private const float RibbonGapFraction = 0.010f;

    // Side ribbons in stack order (top → down) — cropped art, positioned/stacked dynamically.
    private static readonly (string Key, string Resource)[] Ribbons =
    {
        (WatchHistory, "Jellyfin.Plugin.Overcoat.Resources.Badges.JellyfinLeftCropped.png"),
        (TmdbTrending, "Jellyfin.Plugin.Overcoat.Resources.Badges.TMDBLeftCropped.png"),
    };

    private const string ImdbResource = "Jellyfin.Plugin.Overcoat.Resources.Badges.IMDB.png";

    private readonly Dictionary<string, byte[]> _art = new(StringComparer.Ordinal);

    /// <summary>Draws each qualifying badge onto <paramref name="poster"/> (mutated in place).</summary>
    public void Apply(OverlayRenderer renderer, SKBitmap poster, ISet<string> badges)
    {
        if (badges.Count == 0)
        {
            return;
        }

        // Side ribbons: stack flush, each directly beneath the previous.
        var cursorY = (int)(poster.Height * RibbonStartFraction);
        var gap = (int)(poster.Height * RibbonGapFraction);
        foreach (var (key, resource) in Ribbons)
        {
            if (!badges.Contains(key))
            {
                continue;
            }

            var h = renderer.DrawRibbonBadge(poster, Art(resource), rightSide: false, cursorY);
            if (h > 0)
            {
                cursorY += h + gap;
            }
        }

        // IMDB Top 250: full-canvas corner ribbon (placement baked into the art).
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
