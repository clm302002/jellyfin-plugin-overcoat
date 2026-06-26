using SkiaSharp;

namespace Jellyfin.Plugin.Overcoat.Services;

/// <summary>
/// Composites the qualifying badges onto a poster, in the fixed order watch-history → TMDB-trending
/// → IMDB Top 250 (mirrors the Python <c>apply_badges_to_poster</c>). The two edge ribbons stack at
/// mid-left by <c>stackOffset</c>; the IMDB badge is a full-canvas overlay. Badge art is embedded.
/// </summary>
public sealed class BadgeCompositor
{
    /// <summary>Badge keys, in apply order, with their embedded art + placement.</summary>
    public const string WatchHistory = "watch_history";
    public const string TmdbTrending = "tmdb_trending";
    public const string ImdbTop250 = "imdb_top250";

    private static readonly (string Key, string Resource, string Position, bool FullOverlay)[] Defs =
    {
        (WatchHistory, "Jellyfin.Plugin.Overcoat.Resources.Badges.JellyfinLeft.png", "mid-left", false),
        (TmdbTrending, "Jellyfin.Plugin.Overcoat.Resources.Badges.TMDBLeft.png", "mid-left", false),
        (ImdbTop250, "Jellyfin.Plugin.Overcoat.Resources.Badges.IMDB.png", "mid-left", true),
    };

    private readonly Dictionary<string, byte[]> _art = new(StringComparer.Ordinal);

    /// <summary>
    /// Draws each badge the item qualifies for onto <paramref name="poster"/> (mutated in place).
    /// </summary>
    public void Apply(OverlayRenderer renderer, SKBitmap poster, ISet<string> badges, int stackOffset)
    {
        if (badges.Count == 0)
        {
            return;
        }

        var stackIndex = 0;
        foreach (var def in Defs)
        {
            if (!badges.Contains(def.Key))
            {
                continue;
            }

            if (!_art.TryGetValue(def.Key, out var bytes))
            {
                bytes = OverlayRenderer.ReadEmbeddedResource(def.Resource);
                _art[def.Key] = bytes;
            }

            renderer.DrawBadge(poster, bytes, def.Position, stackIndex * stackOffset, def.FullOverlay);
            stackIndex++;
        }
    }
}
