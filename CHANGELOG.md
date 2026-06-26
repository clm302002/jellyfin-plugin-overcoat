# Changelog

All notable changes to Overcoat are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/), and the project aims to follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **AIRING next-episode date** — the AIRING banner can now show when the next episode drops, like
  RETURNING does for the next season.
- **Per-status date controls** (Banners tab → "Status dates"): AIRING and RETURNING each get their own
  **format** (Date `6/28` / Day of week `Tue` / Countdown `3d`) and **window** (Never / 30 / 60 / 90 /
  Always-when-known) — the date only appears when the next episode is within the window.

### Changed
- The Banners tab controls are now organised into labelled groups (Style · Shape & layout · Effects ·
  Status dates · Colours · Statuses).
- A banner's shown date now stays current: a changed next-air date re-renders the poster on the next
  run (previously the returning date was collapsed in the skip-cache and could go stale). Countdown
  format therefore re-renders date-bearing shows ~daily; Date/Day only when the date actually changes.

## [0.4.0] — 2026-06-26

### Added
- **Badges tab** with a live preview studio (mirrors the Banners tab): adjust the side badges'
  **side** (left/right), **vertical anchor** (top/middle/bottom), **size**, and **gap**, and see them
  composited on a sample poster **together with your saved banner**. Preview toggles let you show/hide
  each badge (Watch / Trending / Top 250). Backed by a new authenticated `Overcoat/BadgePreview`
  endpoint. (The IMDb Top 250 corner ribbon is baked art and isn't affected by these placement
  options.) Changing badge layout re-renders badged posters on the next run.

### Changed
- Badge "Top" anchor now sits near the very top edge (was too low / mid-ish); default vertical anchor
  is now **Middle** so badges don't collide with a top banner out of the box.

### Known issues
- Right-side badge placement isn't mirrored yet — the current ribbon art is flat-left/rounded-right,
  so it looks off on the right. Left side is the good one until mirrored art lands.

## [0.3.0] — 2026-06-26

### Added
- **Status banner styles** (new **Banners** settings tab): choose the banner **style** — `Solid`
  (the classic filled pill), `Glass` (a frosted, translucent panel that blurs the poster and shows
  the status via **coloured text**), or `Neon` (a dark pill with a coloured outer glow + bright edge
  in the status colour) — its **shape** (`Pill` / `Square` / `Drop`, where Drop sits flush against
  the edge with only the inner corners rounded), its **position** (`Top` / `Bottom`), and a
  **text-size** multiplier. Changing any of these re-renders existing banners on the next run.
- **Per-status banner icons** (toggleable): ★ NEW, ⟳ RETURNING, ✓ ENDED, ✕ CANCELED, and a live-dot
  for AIRING, drawn beside the text and coloured to match. A **Show status icons** switch turns them
  off for text-only banners.
- **Custom status colours**: a colour picker per status (New / Airing / Returning / Ended / Canceled)
  on the Banners tab, overriding the built-in palette.
- **Live banner preview studio** on the Banners tab — controls on the left, a sample poster preview
  on the right (rendered server-side via a new authenticated `Overcoat/BannerPreview` endpoint) that
  updates instantly as you change options, plus a **status switcher** (NEW / AIRING / RETURNING /
  ENDED / CANCELED) so you can preview every state without running the task.
- **Glass frost tint** — a tint colour, strength, and blur for the glass style, revealed on the tab
  only when Glass is selected.
- **Full-width band + alignment** — stretch the banner edge-to-edge as a ribbon, and align it (or its
  text, when full-width) left / center / right.
- **Drop shadow** — optional soft shadow (with strength) under the banner so it lifts off the poster.
- **Per-status show/hide + custom labels** — choose which statuses get a banner at all, and rename
  any of them (e.g. RETURNING → RETURNS). Renaming no longer breaks the icon/colour (status identity
  is tracked separately from the display label), and the returning date still doesn't churn the cache.
- **Neon glow intensity** — a slider (revealed when Neon is selected) controlling how far/bright the
  glow spreads.
- **Font choice** — pick the banner font: the bundled display font, or the server's sans-serif /
  serif / monospace.

### Fixed
- **Revert on drop-to-zero**: when an item no longer qualifies for any banner or badge (e.g. you
  untick all of a library's overlays), Overcoat now restores its vaulted clean original poster
  instead of leaving the stale overlay in place. If the poster was changed outside Overcoat in the
  meantime, your art is left untouched and Overcoat simply stops tracking the item.
- **Config page**: library names from the Jellyfin API are now rendered safely (a name containing
  `&`, `<`, or quotes no longer breaks the Libraries tab layout).

### Added
- **Watch-history user picker**: a "Whose plays count" dropdown on the General tab lets you scope the
  watch-history badge to a single user (shown when "Count all users' plays" is off).
- **Watch-history scan cap**: `WatchHistoryMaxScan` (General tab, default 10000) bounds how many recent
  plays per user are scanned. The scan now pages newest-first and stops at the look-back window, so a
  very heavy watcher's older in-window plays are no longer dropped by a flat 2000-play limit.

### Changed
- Status lookup for a TV item now hits TMDB once per run instead of twice (banner + poster fallback
  reuse the same result).
- Removed the unused `BadgeStackOffset` configuration field (dead since badges switched to
  flush-by-height stacking); will return when configurable badge spacing lands.
- Internal: added locking to the processing-state cache and the run log file (defense-in-depth; the
  scheduled task was already serialized).

### Docs
- Corrected the badge naming: the side badges are **TMDB Trending** and **watch-history** (the latter
  uses the Jellyfin logo) — previously mislabeled "Jellyfin Trending" / "TVDB Trending".

## [0.2.0] — 2026-06-26

### Added
- **Own log file**: each run writes `Overcoat_<date>.log` into Jellyfin's log directory, so it shows
  as its own entry under Dashboard → Logs (run start, data-set counts, per-item results, restores,
  warnings, errors) — separate from the noisy main server log.
- **Settings page (v1)**: redesigned, tabbed dashboard page (General / API &amp; Lists / Libraries /
  Maintenance) with card-style sections, full-width inputs, and a short description under every
  control. Libraries are **auto-detected** from Jellyfin and laid out in a **responsive grid**
  (multiple columns on wide screens, single column on mobile), each with a per-library checkbox and
  per-badge/overlay toggles (no more typing names). API key + Top 250 list ids live on their own tab.
  Maintenance tab adds **Run now** and **Restore originals** buttons and a single-show field.
- **Badges**: watch-history (Jellyfin play activity), TMDB-trending, and IMDB Top 250 — applied to
  TV and movie posters, gated by per-library toggles and the global badge switch. Watch-history is
  resolved in-process from recent play data; trending/Top 250 from TMDB. The side ribbons use
  cropped art and stack **flush** (each placed beneath the previous by its real height), so they sit
  close together and can be repositioned later.
- **Movie pipeline**: movies get badges only (no status banner), with TMDB id from ProviderIds
  (Tmdb, or Imdb via /find).
- **Skip cache + originals vault + self-heal** (`ProcessingState`): the clean original poster is
  vaulted once and overlays always render from it (so repeated runs never stack banners); unchanged
  items are skipped; and a poster changed outside Overcoat is detected (via the primary image's
  modified time) and re-baselined on the new art.
- **"Restore Original Posters (Overcoat)"** scheduled task — re-applies the vaulted clean posters to
  undo Overcoat's overlays.

## [0.1.0] — 2026-06-26

First public release — installable from the repository.

### Added
- Project renamed to **Overcoat**; open-sourced with README, contributing guide, and issue/PR templates.
- Reproducible release pipeline (`scripts/package_release.py` + tag-triggered GitHub Action) that
  publishes the plugin `.zip` + repository `manifest.json` as release assets.
- Native Jellyfin plugin scaffold (net9.0, Jellyfin 10.11.x), dashboard settings page, and a
  "Apply Overcoat Overlays" scheduled task.
- SkiaSharp poster renderer ported faithfully from the Python reference (status pill geometry,
  colors, and badge compositing), with a pixel-parity check harness.
- **TV status overlays**: NEW / AIRING / `RETURNING <date>` / ENDED / CANCELED, resolved from TMDB
  status + air dates.
- In-process poster save via Jellyfin's image API (no callback HTTP server).
- TMDB poster fallback when an item has no usable Jellyfin poster.

### Notes
- Validated end-to-end on a live Jellyfin 10.11 server.
