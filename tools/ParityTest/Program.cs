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
};
foreach (var (label, opts, text) in styleMatrix)
{
    using var bmp = OverlayRenderer.Decode(poster)!;
    renderer.DrawStatusBanner(bmp, text, opts);
    var outPath = Path.Combine(outDir, $"style-{label}.png");
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
