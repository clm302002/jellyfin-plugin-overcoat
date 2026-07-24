# Renderer parity check (PIL → SkiaSharp)

`Services/OverlayRenderer.cs` is a 1:1 port of the Python `OverlayProcessor`.

## Verified results (2026-06-25)

Validated against the Python reference on a real 1000×1426 poster using the `tools/ParityTest`
harness (SkiaSharp 2.88.8 + native Linux assets, .NET 10 runtime):

| Status  | RMSE (whole image) | RMSE (top 12% banner) | pixels differing >16/255 |
|---------|--------------------|-----------------------|--------------------------|
| AIRING  | 8.23               | 23.75                 | 0.415%                   |
| ENDED   | 9.78               | 28.25                 | 0.410%                   |
| NEW     | 6.92               | 20.00                 | 0.298%                   |

A 6× amplified diff shows the differences are **confined to glyph outlines** — the pill body is
zero-diff, so fill colour, position, size and corner radius match exactly. The residual is
FreeType-vs-Skia text antialiasing, as expected and acceptable. The badge path (edge-ribbon
resize-to-poster + stacking, and the IMDB `full_overlay`) was confirmed visually via the harness's
`BADGE_DIR` smoke test. **The plugin itself also compiles cleanly against Jellyfin.Controller 10.11.11.**

## Reproduce

The harness lives at `tools/ParityTest` (throwaway, not shipped). It links the real
`OverlayRenderer.cs` and embeds the same font under its real logical name. The plugin targets
net9.0; the harness targets net10.0 only so it runs against whatever runtime is handy.

## 1. Generate the Python reference

From the `Kometa-Jellyfin` repo (needs at least one backed-up poster in `cache/originals/`):

```bash
python3 src/jellymeta.py --test-overlay "AIRING"  --output /tmp/ref-airing.png
python3 src/jellymeta.py --test-overlay "ENDED"   --output /tmp/ref-ended.png
python3 src/jellymeta.py --test-overlay "NEW"     --output /tmp/ref-new.png
```

Each reads `cache/originals/<first>.png`, bakes the status pill, and writes a PNG.

## 2. Render the SkiaSharp version

A throwaway console (or LINQPad) that references the plugin project:

```csharp
using Jellyfin.Plugin.Overcoat.Services;

var poster = File.ReadAllBytes("/path/to/same/cache/originals/<first>.png");
using var renderer = new OverlayRenderer();
foreach (var status in new[] { "AIRING", "ENDED", "NEW" })
{
    using var bmp = OverlayRenderer.Decode(poster)!;
    renderer.DrawStatusBanner(bmp, status);                 // same default 1.5% offset
    File.WriteAllBytes($"/tmp/sk-{status.ToLower()}.png", OverlayRenderer.EncodePng(bmp));
}
```

(Use the **same source poster file** as the Python run so dimensions match exactly.)

## 3. Compare

```bash
# visual
firefox /tmp/ref-airing.png /tmp/sk-airing.png &
# numeric (ImageMagick): near-zero AE/RMSE = parity
compare -metric RMSE /tmp/ref-airing.png /tmp/sk-airing.png /tmp/diff-airing.png ; echo
```

## Expected differences (acceptable)

- **Sub-pixel text antialiasing**: PIL/FreeType and Skia hint and rasterize glyphs slightly
  differently, so per-pixel RMSE on the *text* will be small but non-zero. Pill geometry, colour,
  size and placement should be pixel-identical.
- **Glyph metrics**: pill width derives from measured ink width; if Skia's `MeasureText` ink box
  differs from PIL's `textbbox` by a pixel or two, the pill width/centering shifts equally. If the
  pill looks too tight/loose, that is the knob — not the constants.

## Constants (must stay identical to the script)

| Constant                 | Value                          |
|--------------------------|--------------------------------|
| Font size                | `int(height * 0.053 * 1.105)`  |
| Size multiplier          | `1.3 * 0.85 = 1.105`           |
| Pill fill alpha          | `220` (86%)                    |
| Text colour              | white, alpha 255               |
| Letter spacing           | one space between every char   |
| Padding X / Y            | `0.5 * font` / `0.25 * font`   |
| Corner radius            | `0.56 * pill height`           |
| Vertical offset          | `0.015 * height` (default)     |
| Badge stack scale        | `height / 1500`                |
| Full-canvas badge resize | to poster's own (w, h)         |
| mid-left/right badge Y    | `0.20 * height` + scaled stack |

## Landscape Series Thumb path (added in 0.8.0 beta)

Portrait output above remains the Python-parity contract. Series Thumbs are a separate 16:9 path:

- initial banner font uses `width * 0.053 * 1.105`, then shrinks to keep the banner within 94% of
  the canvas width;
- top badge stacks reserve banner space on landscape cards;
- the IMDb mark is cropped from its transparent poster canvas and placed at 23.4% of card height,
  preserving aspect instead of stretching the full 2:3 canvas over 16:9 art;
- output is never upscaled, is bounded to 1920×1080, and is encoded as WebP quality 92;
- `LandscapeRendererRevision` invalidates only Thumb output, while `RendererRevision` continues to
  control portrait rerenders.

`OverlayRendererTests.WideCardEncoding_DownscalesWithoutUpscaling` pins the output bounds. Visual
review should cover 3840×2160 and 1920×1080 Thumbs with every banner position and badge combination.
