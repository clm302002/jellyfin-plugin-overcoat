# Changelog

All notable changes to Overcoat are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/), and the project aims to follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Project renamed to **Overcoat**; open-sourced with README, contributing guide, and issue/PR templates.

## [0.1.0] — MVP

### Added
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
