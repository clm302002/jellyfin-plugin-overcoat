<div align="center">

# 🧥 Overcoat

**A status-overlay & badge plugin for Jellyfin posters.**

Overcoat puts a coat *over* your posters — status pills like **NEW / AIRING / RETURNING / ENDED /
CANCELED** on TV shows, plus watch-history, TMDB-trending and IMDB Top 250 badges — all rendered
**inside Jellyfin** and saved straight to your library. No external script, no cron, no upload hacks.

<img src="screenshots/demo-returning.png" width="300" alt="The Apothecary Diaries with a RETURNING overlay"/>

</div>

---

## Status

> **Early but real.** The MVP — TV **status overlays** — is built and has been validated on a live
> Jellyfin server. Badges and the movie pipeline are in progress. See the Roadmap section below.

Overcoat is the native-plugin successor to a Python script ("jellymeta", a Kometa-for-Jellyfin
clone). Running in-process removes that script's whole support burden: the callback HTTP upload
server, the dependency self-heal, the `.env`/`config.yml`/`CALLBACK_HOST` reachability dance, the
cron/User-Scripts scheduling, and the setup wizard. Install becomes: **add a repo URL → Install.**

## Features

- **Status overlays (TV):** a colored pill — NEW, AIRING, `RETURNING <date>`, ENDED, CANCELED —
  derived from TMDB status + air dates.
- **Badges** *(in progress):* watch-history, TMDB-trending, IMDB Top 250.
- **In-process:** posters are rendered with SkiaSharp and saved via Jellyfin's own image API.
- **Per-library:** choose which libraries get overlays/badges.
- **Native scheduling:** runs as a dashboard Scheduled Task.

## Requirements

- Jellyfin **10.11.x** (targets the 10.11 plugin ABI, .NET 9).
- A free **TMDB API key**.

## Install

> A plugin-repository manifest ships with the first tagged release. Until then, build from source
> (see Contributing).

1. **Dashboard → Plugins → Repositories** → add the Overcoat manifest URL.
2. **Catalog → Overcoat → Install**, then restart Jellyfin.
3. **Plugins → Overcoat** → set your TMDB API key and pick your libraries.
4. **Dashboard → Scheduled Tasks → "Apply Overcoat Overlays"** → run it (or wait for the daily run).

## Configure

Everything lives on the plugin's settings page: TMDB key, per-library toggles (status overlays +
each badge), watch-history window, trending window, IMDB Top 250 list ids, and ignore/override
lists.

## Building / Contributing

Overcoat is open source (GPL-3.0). Contributions welcome — see **[CONTRIBUTING.md](CONTRIBUTING.md)**.

```bash
dotnet build Jellyfin.Plugin.Overcoat/Jellyfin.Plugin.Overcoat.csproj -c Release
```

Needs the **.NET 9 SDK**. The compiled `Jellyfin.Plugin.Overcoat.dll` drops into a folder under
Jellyfin's `plugins/` directory (SkiaSharp's native libs are already provided by the server).

## Roadmap (high level)

- Finish badges + the movie pipeline.
- A polished settings page that auto-lists your libraries with toggles.
- Multi-poster selection (pick/rotate candidate posters instead of being stuck with one).
- Flush badge stacking (no gap between adjacent badges).

## License

[GPL-3.0-only](LICENSE) — Jellyfin plugins link GPL server libraries.

## Acknowledgements

Inspired by Kometa (Plex Meta Manager) and the Jellyfin plugin community.
