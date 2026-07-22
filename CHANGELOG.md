# Changelog

All notable changes to Overcoat are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/), and the project aims to follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed
- **Updates now actually appear in Jellyfin.** The packaged metadata set `autoUpdate: false`, and
  Jellyfin skips a plugin entirely when deciding which have updates available if that flag is off —
  so the Plugins page never offered an update and the "Update Plugins" task ignored Overcoat. Anyone
  who installed it stayed on that version indefinitely regardless of what the repository advertised.
  To update an installation packaged before this fix, expand the version you want under **Revision
  History** on the plugin's page and click Install.
- **The settings page can no longer load a stale stylesheet after an update.** Its CSS was cached for
  24 hours, so a version that changed the styling would render new markup against the previous
  stylesheet — a settings page that looks broken, with nothing to suggest a hard refresh would fix it.
  The stylesheet now revalidates against the plugin version, so it updates the moment the plugin does.
- **Revision history no longer repeats itself.** Every pre-release of a version advertised the
  identical multi-thousand-character release notes, so the plugin page showed the same wall of text
  once per build. Pre-releases now carry their own notes and are labelled with their build number,
  superseded ones collapse to a single line, and manifest entries are length-capped for the narrow
  panel they render in. The full notes stay on the release page.
- **Tidied the plugin's description** on the Jellyfin plugins page. It was a dense paragraph ending
  in a shouted all-caps warning; it is now two plain sentences, with the uninstall caveat kept
  because losing it costs people their original posters.

### Changed
- **The settings page now uses the whole screen.** Related controls are grouped into responsive
  cards across wide displays and collapse to one column on phones. Banner and badge previews stay
  visible while editing (sticky on desktop, compact floating preview on mobile), tabs swipe on
  narrow screens, and the TMDB API key is masked until explicitly revealed.
- **Banner editing is denser on desktop.** Wide sections keep related controls together without
  turning them into tall, narrow cards, and the preview rail sits below the page chrome. Badge
  placement is temporarily locked to the supported left-side artwork; right-side support is WIP.

### Added
- **Preview on your own posters.** The Banners and Badges preview studios now have a
  **🎲 Random from my library** button alongside the built-in sample. A banner reads very differently
  over a dark poster than a bright one, so previewing on real art tells you far more than a synthetic
  one does. Click again for another poster.

  It only ever uses **clean** art: the saved original if Overcoat has one for that item, otherwise a
  poster Overcoat has never touched. It will not pick something already overlaid, which would show a
  banner drawn on top of a banner and make the preview misleading.

## [0.7.0] — 2026-07-22

Findings from an independent deep audit. Every item was re-verified before being changed, and the
theme is the same one as 0.6.0/0.6.1: **an unknown must never be treated as an answer.**

### Fixed
- **Overcoat no longer offers itself to Jellyfin versions it cannot run on.** The published metadata
  claimed compatibility with all of 10.11.x, but the plugin uses user-manager APIs added in
  **10.11.9** — servers on 10.11.0–10.11.8 were offered a build that could not load. The declared
  minimum is corrected and CI now matrix-builds against the floor so it can't drift again.
- **A failing badge source no longer removes badges it can't vouch for.** 0.6.1 only protected items
  that would lose *everything*; an item keeping its status banner still had the affected badge
  stripped. Each source is now tracked separately, and a badge whose source couldn't be read keeps
  whatever was last rendered.
- **An unreadable poster is no longer mistaken for a replaced one.** The check had two outcomes, so
  "couldn't read the file" fell through as "confirmed replaced" — which abandons the saved clean
  original on the strength of an I/O error. It now has a third, and only a confirmed replacement
  triggers anything destructive.
- **A poster that disappears is now recovered instead of ignored forever.** The comparison skipped
  items whose current image was missing, so an item whose art vanished was never restored.
- **Restore no longer overwrites artwork Overcoat didn't create.** If you or another plugin changed a
  poster after Overcoat's last run, Restore now leaves it alone and reports the conflict rather than
  replacing it with an older saved copy. A **Force restore** option (Maintenance tab) overrides this.
- **Apply and Restore can no longer run at the same time.** They previously could interleave and
  leave a poster overlaid with its only clean copy deleted.
- **Dry run no longer changes anything.** It was still writing cache entries and saved originals
  despite promising otherwise.
- **Saved originals and the state file are written atomically**, with a backup the plugin falls back
  to if the state file is ever damaged — an interrupted write could previously truncate the only
  recoverable copy of a poster.
- **Restore sends the correct image type.** Saved originals are whatever format the source poster
  was (mostly WebP and JPEG in practice), but every one was being handed to Jellyfin labelled PNG.
- **Cancelling a run now stops it promptly** instead of being swallowed by error handling.
- **Saving settings can no longer wipe your library configuration.** If the library list failed to
  load, the page had no rows to read and Save wrote an empty list over your per-library settings.
  The saved list is now preserved and the failure is explained on the page.
- **Bad configuration values can't reach the renderer.** Font scale, badge size, blur, glow, scan
  limits and colours are clamped server-side — the settings page only enforced them in the browser,
  and the same values are reachable from a hand-edited config file.
- **A malformed TMDB override is ignored instead of used.** A bad line parsed to TMDB id 0, and
  every lookup for that title then failed permanently. Invalid lines are now skipped and logged.
- **Changing the overlay artwork or drawing code now refreshes existing posters.** The skip cache
  only tracked your settings, so a rendering change reached items that happened to change for some
  other reason and silently left everything else on the old art.

### Changed
- Releases now build with the exact version they publish, and fail if the two disagree — the
  mismatch that could make Jellyfin show one version while logging another.
- Manual release runs must check out the tag they claim to publish, and are rejected otherwise.
- The preview endpoints behind the settings page now require an administrator, matching Jellyfin's
  own plugin endpoints. Previously any signed-in user could reach them.
- **"Run automatically every day" is now "Let Overcoat set the run time".** The old label promised
  something it didn't do: turning it off only stops Overcoat *managing* the trigger — it never
  stopped the task running. The setting now says so.
- Added **THIRD_PARTY_NOTICES.md** and the attribution TMDB requires. It also records two unresolved
  licensing items honestly: the bundled font has no recorded licence, and the watch-history badge art
  is derived from the Jellyfin logo. Both need resolving before wider promotion.

### Added
- **A test suite** (44 tests) covering the skip cache, the originals vault, atomic writes and state
  recovery, image-format detection, and configuration clamping — the state machine every
  overlay-loss bug so far has lived in. Runs in CI on every push and pull request.
- CI also matrix-builds against the minimum supported Jellyfin version, and Dependabot now watches
  Actions and NuGet.
- **The beta channel now carries stable releases too**, so subscribing to the beta repository alone
  is a complete "always newest" channel rather than one that silently misses stable-only releases.

## [0.6.1] — 2026-07-21

Follow-up to 0.6.0, same theme: **never take a destructive action on incomplete information.** 0.6.0
fixed one place where "we couldn't find out" was treated as "we found out it's nothing". A full audit
found three more — two of which could destroy the vaulted clean original, the only route back to an
un-overlaid poster.

### Fixed
- **Badge lookups no longer strip posters when they fail.** TMDB trending, the TMDB list used for
  IMDb Top 250, and the watch-history scan all returned an empty (or silently *partial*) set when
  they failed. Movies carry no status banner, so an empty badge set sent them down the revert path —
  restoring the clean original and dropping the item from the cache. A single rate-limit or timeout
  at the start of a run could therefore un-overlay a movie library, with the overlays returning on
  the next run: poster flapping. Each source now reports failure, and any item that would lose all
  its overlays while badge data is incomplete is **left exactly as it is** and retried next run.
- **The IMDb Top 250 list no longer returns a partial set.** A failure part-way through pagination
  used to return the pages fetched so far, which looks complete — every title on the unread pages
  silently lost its badge. It now reports the whole set as unknown.
- **"Restore original posters" no longer deletes originals it didn't restore.** The vaulted copy was
  cleared unconditionally, so an item that failed to restore lost its clean poster anyway — with no
  error and no way to retry. (This is why a restore could report e.g. 548/549 and leave one poster
  permanently overlaid.) Failed items now **keep** their vaulted original for a retry, and the
  summary reports restored / dropped-orphan / failed separately.
- **A touched timestamp is no longer mistaken for a replaced poster.** External-change detection
  compared only the image's modified time, so anything that touched the file without changing it — a
  library scan, a metadata write, a copy, a permissions fix — read as a replacement. That deleted the
  vaulted clean original and re-overlaid the *current* (already-overlaid) poster. Overcoat now
  records a hash of what it wrote and confirms the content actually changed before re-baselining.
- **The clean original is never deleted before its replacement exists.** Re-baselining used to delete
  the vaulted file first; if fetching the new art then failed, the only clean copy was already gone.
- Existing items get their content hash recorded on the first run after upgrading. Without this the
  content check above would never engage on a settled library — nothing re-renders, so no hash would
  ever be written — and every item would stay exposed to the very false positive this release fixes.

### Changed
- TMDB and watch-history failures now log at `Warning` (they were `Debug`, i.e. invisible by
  default), and the run's data-set line prints `FAILED` instead of `0` — a bare zero couldn't be told
  apart from "that badge is switched off".
- Removed two settings that did nothing: **"Keep clean originals"** and **"Prune removed items"**.
  Both were saved and loaded but read by no code — the clean original is always vaulted (that is what
  makes Restore work), and orphan pruning was never implemented. Existing config files still load.
- `ScheduleSync` now logs when it finds the schedule already correct, so "ran, nothing to do" can be
  told apart from "never loaded".

## [0.6.0] — 2026-07-21

### Fixed
- **Overlays no longer disappear when TMDB is unreachable.** A failed TMDB request (rate limit, bad or
  expired API key, 5xx, DNS/timeout) returned the same "no data" as a genuine no-status answer. The run
  loop read that as "this show no longer qualifies for anything" and took the revert path — restoring
  the clean original poster and dropping the item from the cache. A single bad night could therefore
  strip overlays across a library, leaving them off until the next run or a manual apply. TMDB failures
  are now distinguished from real answers, and an affected item is **skipped with its poster left
  exactly as it is** and retried on the next run.
- TMDB request failures were logged at `Debug`, so at default log level they were invisible. They now
  log at `Warning`, with the API key redacted from the logged URL, plus an end-of-run summary line
  naming how many requests failed.

### Added
- **Configurable run time** (Settings → General → Schedule): choose the hour and minute the overlay job
  runs, instead of being stuck on the hard-coded 3 AM default. Applied to the live scheduled task as
  soon as you save — no restart. Unticking "Run automatically every day" hands scheduling back to
  Dashboard → Scheduled Tasks, where you can set intervals or multiple triggers, and Overcoat then
  leaves the triggers alone.

## [0.5.1] — 2026-06-26

### Changed
- **AIRING shows the next episode again** with a chosen **format** (Date `6/28` / Day of week `Tue` /
  Countdown `3d`) — always, with no "within N days" window (that window only applies to RETURNING).
  v0.5.0 had reduced AIRING to a plain word; this restores the format dropdown.
- The AIRING-off fallback now reuses the next-episode data already fetched (no extra TMDB call) —
  removed the `/tv/{id}/season/{n}` lookup to keep API usage minimal.

## [0.5.0] — 2026-06-26

### Added
- **AIRING = "new episode today"** — the AIRING banner now appears automatically on the day a new
  episode is out (no date, no settings). Between episodes a show is RETURNING with its next date.
- **RETURNING date control** (Banners tab → "Status dates"): choose the **format** (Date `7/14` /
  Day of week `Mon` / Countdown `21d`) and a **window** (Never / 30 / 60 / 90 / Always-when-known) —
  the date appears only when the upcoming episode/season is within the window.
- **AIRING opt-out fallback**: if you turn AIRING off (Statuses group), a show on its air day falls
  back to RETURNING showing the **following** episode's date (fetched from TMDB when needed).

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
