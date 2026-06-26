# Changelog

All notable changes to Overcoat are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/), and the project aims to follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
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
