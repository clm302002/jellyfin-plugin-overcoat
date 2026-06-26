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
