# Contributing to Overcoat

Thanks for your interest! Overcoat is a native Jellyfin plugin (C# / .NET 9) that overlays status
pills and badges onto posters and, optionally, existing TV Series Thumb images.

## Prerequisites

- **.NET 9 SDK** (the plugin targets `net9.0` to match Jellyfin 10.11.x).
- A Jellyfin **10.11.9+** test server (Docker is easiest). 10.11.0–10.11.8 lack user-manager
  APIs the plugin uses and cannot load it; CI enforces that floor.
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
    ├── BadgeCompositor.cs          # composites side/corner badges onto portrait or wide artwork
    ├── ProcessingState.cs          # independent poster/Thumb caches + originals vaults + self-heal
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

Wide cards deliberately use a parallel landscape path; do not retune the calibrated portrait
constants to make 16:9 art work. The only supported secondary target is `Series + ImageType.Thumb`.
Episode Primary and Backdrop images are out of scope and must remain untouched. Poster state stays in
`state.json`/`originals/`; Thumb state stays in `thumb-state.json`/`thumb-originals/`.

The Libraries page's all-user artwork buttons update Jellyfin's
`useEpisodeImagesInNextUpAndResume` display preference. Jellyfin stores it per user, so the action
must GET and repost each complete display-preferences DTO while changing only that key; posting a
partial DTO can erase unrelated user settings. New users are not a global default—run the action
again after creating them.

## Testing

- **Renderer:** use `tools/ParityTest` to render banners/badges onto a sample poster and eyeball or
  pixel-diff them.
- **Metadata:** use `tools/TmdbTest` (`TMDB_API_KEY=… dotnet run`) to confirm TMDB id resolution and
  the status→banner mapping against live data.
- **Automated:** `dotnet test tests/Jellyfin.Plugin.Overcoat.Tests/` — the skip cache, originals
  vault, atomic writes and recovery, image-format detection and config clamping. Run it before
  changing `ProcessingState` or `OverlayTask`; that state machine is where every overlay-loss bug in
  this project has lived.
- **Settings page:** `node scripts/check_config_page.js` — nothing else compiles that file.
- **End-to-end:** install the built DLL on a test Jellyfin, run the **Apply Overcoat Overlays** task
  on a small/limited library, and confirm posters update. For wide cards, also enable the library's
  Series Thumb option, exercise both all-user artwork buttons, verify Next Up/Continue Watching,
  then run **Restore Original Artwork (Overcoat)** and confirm both poster and Thumb return clean.

## Branches and releases

| Branch | Holds |
|---|---|
| `main` | Released code only. Every commit here corresponds to something published. |
| `dev`  | Integration branch. Day-to-day work lands here first. |

Releases are cut by pushing a tag; **pushing to a branch publishes nothing.**

| Tag | Version | Channel | Where it lands |
|---|---|---|---|
| `v0.7.0-beta.1` | `0.7.0.1` | beta | `releases/download/beta/manifest.json` |
| `v0.7.0-beta.2` | `0.7.0.2` | beta | " |
| `v0.7.0` | `0.7.0.500` | stable | `releases/latest/download/manifest.json` |
| `v0.8.0-beta.1` | `0.8.0.1` | beta | next line starts over |
| `v0.8.0-beta.2` | `0.8.0.2` | beta | wide-card test build |
| `v0.8.0-beta.3` | `0.8.0.3` | beta | current wide-card test build |

GitHub's "latest" excludes prereleases, so a beta can never appear on the stable URL. The beta channel
is a **superset** — stable releases are published there too — so subscribing to the beta URL alone is
a complete "always newest" channel. Each channel keeps its own manifest history, so older builds stay
installable from Jellyfin's version dropdown.

**Why stable is `.500` and not `.0`.** Jellyfin versions are 4-part numbers with no concept of a
prerelease, so the channel lives in the fourth part. Numbering the release `.0` would put it *below*
every beta of that version — a tester who ran `beta.2` would never be offered the release containing
the fixes their own testing produced. `.500` keeps the ordering monotonic and leaves room for betas.

**A beta is a release candidate for one version, not a parallel version line.** It never gets ahead of
stable and is never promoted by renumbering; `v0.7.0-beta.N` simply becomes `v0.7.0`. If betas are
reaching a version number the release won't have, they're numbered wrong.

Cutting a release:

1. Land the work on `dev`, with `CHANGELOG.md` updated under `## [Unreleased]`.
2. Test it — beta tag from `dev` if you want it on a real server first.
3. Move the `Unreleased` entries under a `## [x.y.z] — date` heading, bump `<Version>` in the csproj,
   merge to `main`, tag, push the tag.

The release notes on GitHub and the changelog text shown *inside Jellyfin's plugin catalogue* are
both extracted from that `## [x.y.z]` section automatically — so write it for users, not for you.

## Settings page assets

The page's CSS lives in `Configuration/configPage.css`, embedded in the assembly and served by
`Api/ConfigurationAssetsController`. If you change it:

- The stylesheet URL carries the plugin version (`?v=…`), so a new release busts the cache. Keep it.
- The endpoint is `[AllowAnonymous]` because a `<link>` tag cannot send an auth header.
- `node scripts/check_config_page.js` validates the page — the inline script parses, every element id
  the script references exists and is unique, and a set of layout/security hooks are present. Nothing
  else compiles that file, so run it.

## Pull requests

- Branch off `dev`, keep PRs focused, describe what you changed and how you tested it.
- Match the surrounding code style. Keep new Jellyfin API calls timeout/cancellation aware.
- Update `CHANGELOG.md` under `## [Unreleased]` for any user-visible change.
- CI runs the Release build with `-warnaserror`, the test suite, `scripts/check_config_page.js`, and a
  matrix build against the minimum supported Jellyfin; run them locally first. Note that a clean build proves very little here — several fixes in this project have
  compiled perfectly while doing nothing at all, so test against a real library.
