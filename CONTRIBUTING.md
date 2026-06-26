# Contributing to Overcoat

Thanks for your interest! Overcoat is a native Jellyfin plugin (C# / .NET 9) that overlays status
pills and badges onto posters.

## Prerequisites

- **.NET 9 SDK** (the plugin targets `net9.0` to match Jellyfin 10.11.x).
- A Jellyfin **10.11.x** test server (Docker is easiest).
- A TMDB API key for testing the metadata paths.

## Build

```bash
dotnet build Jellyfin.Plugin.Overcoat/Jellyfin.Plugin.Overcoat.csproj -c Release
```

The output `Jellyfin.Plugin.Overcoat.dll` goes in a folder under your server's `plugins/` directory
(e.g. `plugins/Overcoat_x.y.z/`) alongside a `meta.json`. SkiaSharp's native libraries are already
shipped by the Jellyfin server, so only the managed assembly is packaged.

## Project layout

```
Jellyfin.Plugin.Overcoat/
├── Plugin.cs                       # BasePlugin + IHasWebPages (settings page)
├── Configuration/                  # PluginConfiguration + configPage.html (dashboard UI)
├── ScheduledTasks/OverlayTask.cs   # the run loop (enumerate → resolve → render → save in-process)
└── Services/
    ├── OverlayRenderer.cs          # SkiaSharp rendering (status pill + badges)
    ├── BadgeCompositor.cs          # composites the side/corner badges onto the poster
    ├── ProcessingState.cs          # skip cache + originals vault + self-heal
    ├── WatchHistory.cs             # recent play activity (watch-history badge)
    ├── StatusOverlayResolver.cs    # TMDB status + air dates → banner text
    └── TmdbService.cs              # TMDB v3 over HttpClient
badges/                             # badge art (see below) — embedded as the default set
assets/                             # README images (hero + screenshots)
tools/                              # throwaway dev harnesses (not shipped)
├── ParityTest/                     # renders sample overlays to compare output
└── TmdbTest/                       # live-checks TMDB resolution + the status resolver
```

## Badge art (`badges/`)

All badge images live in the repo-root **`badges/`** folder — this is the place to recolor existing
badges or add new ones. Every PNG there is automatically embedded as a **default** badge (under
`Jellyfin.Plugin.Overcoat.Resources.Badges.<file>`), so adding art needs no code change, just a rebuild.

- The side ribbons use a **cropped** image (just the ribbon graphic, e.g. `JellyfinLeftCropped.png`,
  `TMDBLeftCropped.png`) so they stack flush and can be positioned freely. The full-canvas originals
  (`JellyfinLeft.png`, `TMDBLeft.png`) are kept for reference.
- `IMDB.png` is a full-poster-canvas corner overlay.
- **Planned:** runtime-selectable badges — Overcoat will read a badges folder on the server and let you
  pick which image each badge uses from the settings page, so swapping art won't require a rebuild.

## Design philosophy

The rendering and status logic are a **faithful port of the original Python reference**
("jellymeta"). When changing overlay geometry or status rules, preserve the calibrated constants
(font size = `height × 0.053 × 1.105`, pill alpha 220, corner radius 56%, etc.) unless intentionally
changing the look — and update the parity notes.

## Testing

- **Renderer:** use `tools/ParityTest` to render banners/badges onto a sample poster and eyeball or
  pixel-diff them.
- **Metadata:** use `tools/TmdbTest` (`TMDB_API_KEY=… dotnet run`) to confirm TMDB id resolution and
  the status→banner mapping against live data.
- **End-to-end:** install the built DLL on a test Jellyfin, run the **Apply Overcoat Overlays** task
  on a small/limited library, and confirm posters update.

## Pull requests

- Branch off `main`, keep PRs focused, describe what you changed and how you tested it.
- Match the surrounding code style. Keep new Jellyfin API calls timeout/cancellation aware.
- Update `CHANGELOG.md` for any user-visible change.
