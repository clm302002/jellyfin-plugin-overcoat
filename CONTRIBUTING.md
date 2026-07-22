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

## Branches and releases

| Branch | Holds |
|---|---|
| `main` | Released code only. Every commit here corresponds to something published. |
| `dev`  | Integration branch. Day-to-day work lands here first. |

Releases are cut by pushing a tag; **pushing to a branch publishes nothing.**

| Tag | Channel | Where it lands |
|---|---|---|
| `v0.7.0` | stable | `releases/latest/download/manifest.json` |
| `v0.7.0-beta.1` | beta | `releases/download/beta/manifest.json` |

GitHub's "latest" excludes prereleases, so a beta can never appear on the stable URL. Each channel
keeps its own manifest history, so older builds stay installable from Jellyfin's version dropdown.

Beta versions put the beta number in the fourth part (`0.7.0.1`); stable is always `.0` (`0.7.0.0`).

Cutting a release:

1. Land the work on `dev`, with `CHANGELOG.md` updated under `## [Unreleased]`.
2. Test it — beta tag from `dev` if you want it on a real server first.
3. Move the `Unreleased` entries under a `## [x.y.z] — date` heading, bump `<Version>` in the csproj,
   merge to `main`, tag, push the tag.

The release notes on GitHub and the changelog text shown *inside Jellyfin's plugin catalogue* are
both extracted from that `## [x.y.z]` section automatically — so write it for users, not for you.

## Pull requests

- Branch off `dev`, keep PRs focused, describe what you changed and how you tested it.
- Match the surrounding code style. Keep new Jellyfin API calls timeout/cancellation aware.
- Update `CHANGELOG.md` under `## [Unreleased]` for any user-visible change.
- CI runs the Release build with `-warnaserror` plus `scripts/check_config_page.js`; run both locally
  first. Note that a clean build proves very little here — several fixes in this project have
  compiled perfectly while doing nothing at all, so test against a real library.
