using Jellyfin.Plugin.Overcoat.Services;

// Usage: ParityTest <sourcePoster.png> <outDir> [STATUS ...]
var sourcePath = args[0];
var outDir = args[1];
var statuses = args.Length > 2 ? args[2..] : new[] { "AIRING", "ENDED", "NEW" };

Directory.CreateDirectory(outDir);
var poster = File.ReadAllBytes(sourcePath);

using var renderer = new OverlayRenderer();
foreach (var status in statuses)
{
    using var bmp = OverlayRenderer.Decode(poster)
        ?? throw new InvalidOperationException("Could not decode source poster.");
    renderer.DrawStatusBanner(bmp, status); // default 1.5% offset, colour derived from status
    var outPath = Path.Combine(outDir, $"sk-{status.ToLowerInvariant()}.png");
    File.WriteAllBytes(outPath, OverlayRenderer.EncodePng(bmp));
    Console.WriteLine($"wrote {outPath} ({bmp.Width}x{bmp.Height})");
}

// Banner style matrix — render each shape/style combo so we can eyeball them.
var styleMatrix = new (string Label, OverlayRenderer.BannerOptions Opts, string Text)[]
{
    ("solid-pill-top",   new() { Style = "solid", Shape = "pill",   Position = "top" },    "AIRING"),
    ("glass-pill-top",   new() { Style = "glass", Shape = "pill",   Position = "top" },    "AIRING"),
    ("glass-drop-top",   new() { Style = "glass", Shape = "drop",   Position = "top" },    "NEW"),
    ("solid-drop-top",   new() { Style = "solid", Shape = "drop",   Position = "top" },    "RETURNING 6/26"),
    ("glass-square-top", new() { Style = "glass", Shape = "square", Position = "top" },    "ENDED"),
    ("glass-pill-bottom",new() { Style = "glass", Shape = "pill",   Position = "bottom" }, "CANCELED"),
    ("neon-pill-new",    new() { Style = "neon",  Shape = "pill",   Position = "top" },    "NEW"),
    ("neon-pill-return", new() { Style = "neon",  Shape = "pill",   Position = "top" },    "RETURNING 6/26"),
    ("neon-pill-ended",  new() { Style = "neon",  Shape = "pill",   Position = "top" },    "ENDED"),
    ("neon-pill-airing", new() { Style = "neon",  Shape = "pill",   Position = "top" },    "AIRING"),
    ("fullwidth-band",   new() { Style = "solid", Shape = "drop",   Position = "top", FullWidth = true, IconKey = "NEW" }, "NEW"),
    ("fullwidth-glass",  new() { Style = "glass", Shape = "drop",   Position = "top", FullWidth = true, IconKey = "RETURNING" }, "RETURNING 6/26"),
    ("align-right-shadow", new() { Style = "solid", Shape = "pill", Position = "top", Align = "right", Shadow = true, IconKey = "ENDED" }, "ENDED"),
    ("glass-tint-amber", new() { Style = "glass", Shape = "pill",   Position = "top", GlassTint = "#3A1E00", GlassTintStrength = 65, IconKey = "AIRING" }, "AIRING"),
    ("shadow-pill",      new() { Style = "neon",  Shape = "pill",   Position = "top", Shadow = true, IconKey = "CANCELED" }, "CANCELED"),
    ("font-serif",       new() { Style = "solid", Shape = "pill",   Position = "top", Font = "serif", IconKey = "NEW" }, "NEW"),
    ("font-mono",        new() { Style = "solid", Shape = "pill",   Position = "top", Font = "mono",  IconKey = "AIRING" }, "AIRING"),
    ("neon-glow-low",    new() { Style = "neon",  Shape = "pill",   Position = "top", NeonGlow = 10, IconKey = "ENDED" }, "ENDED"),
    ("neon-glow-high",   new() { Style = "neon",  Shape = "pill",   Position = "top", NeonGlow = 100, IconKey = "RETURNING" }, "RETURNING 6/26"),
    ("airing-date",      new() { Style = "neon",  Shape = "pill",   Position = "top", IconKey = "AIRING" }, "AIRING 6/28"),
    ("airing-day",       new() { Style = "neon",  Shape = "pill",   Position = "top", IconKey = "AIRING" }, "AIRING TUE"),
    ("airing-countdown", new() { Style = "neon",  Shape = "pill",   Position = "top", IconKey = "AIRING" }, "AIRING 3D"),
    ("returning-day",    new() { Style = "glass", Shape = "pill",   Position = "top", IconKey = "RETURNING" }, "RETURNING MON"),
};
foreach (var (label, opts, text) in styleMatrix)
{
    using var bmp = OverlayRenderer.Decode(poster)!;
    renderer.DrawStatusBanner(bmp, text, opts);
    var outPath = Path.Combine(outDir, $"style-{label}.png");
    File.WriteAllBytes(outPath, OverlayRenderer.EncodePng(bmp));
    Console.WriteLine($"wrote {outPath} ({bmp.Width}x{bmp.Height})");
}

// Badge layout matrix via BadgeCompositor (embedded art) — banner + ribbons per layout.
foreach (var (lbl, layout, set) in new (string, BadgeCompositor.BadgeLayout, string[])[]
{
    ("badges-left-top",        new(false, "top",    100, 1), new[] { "watch_history", "tmdb_trending", "imdb_top250" }),
    ("badges-right-middle",    new(true,  "middle", 120, 2), new[] { "watch_history", "tmdb_trending" }),
    ("badges-left-bottom-sm",  new(false, "bottom",  70, 1), new[] { "watch_history", "tmdb_trending" }),
})
{
    using var bmp = OverlayRenderer.Decode(poster)!;
    renderer.DrawStatusBanner(bmp, "RETURNING 6/26", new() { Style = "glass", IconKey = "RETURNING" });
    new BadgeCompositor().Apply(renderer, bmp, new HashSet<string>(set), layout);
    var outPath = Path.Combine(outDir, $"badge-{lbl}.png");
    File.WriteAllBytes(outPath, OverlayRenderer.EncodePng(bmp));
    Console.WriteLine($"wrote {outPath} ({bmp.Width}x{bmp.Height})");
}

// Badge-path smoke test: status banner + two edge-ribbon badges (stacked) + IMDB full-overlay.
var badgeDir = Environment.GetEnvironmentVariable("BADGE_DIR");
if (badgeDir is not null)
{
    using var bmp = OverlayRenderer.Decode(poster)!;
    renderer.DrawStatusBanner(bmp, "AIRING");
    renderer.DrawBadge(bmp, File.ReadAllBytes(Path.Combine(badgeDir, "JellyfinLeft.png")), "mid-left", 0, false);
    renderer.DrawBadge(bmp, File.ReadAllBytes(Path.Combine(badgeDir, "TMDBLeft.png")), "mid-left", 260, false);
    renderer.DrawBadge(bmp, File.ReadAllBytes(Path.Combine(badgeDir, "IMDB.png")), "bottom-right", 0, true);
    var outPath = Path.Combine(outDir, "sk-badges.png");
    File.WriteAllBytes(outPath, OverlayRenderer.EncodePng(bmp));
    Console.WriteLine($"wrote {outPath} ({bmp.Width}x{bmp.Height})");
}
