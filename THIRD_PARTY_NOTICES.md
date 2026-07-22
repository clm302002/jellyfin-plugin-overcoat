# Third-party notices

Overcoat is GPL-3.0 (see [LICENSE](LICENSE)). This file records the third-party data, marks and
assets it depends on or bundles.

---

## TMDB

**This product uses the TMDB API but is not endorsed or certified by TMDB.**

TMDB supplies the series status, air dates, trending lists and fallback poster artwork Overcoat
uses. An API key is required and is supplied by the user, not by this project. TMDB's terms are at
<https://www.themoviedb.org/api-terms-of-use>.

The TMDB name and logo are trademarks of TMDB and are used here for identification only.

## IMDb

The "IMDb Top 250" badge identifies membership of a list sourced via TMDB. IMDb is a trademark of
IMDb.com, Inc. Overcoat is not affiliated with, endorsed by, or certified by IMDb.

## Jellyfin

Overcoat is a third-party plugin. It is not an official Jellyfin project and is not endorsed by the
Jellyfin team. The Jellyfin name and logo are trademarks of the Jellyfin project; see their
[branding guidelines](https://jellyfin.org/docs/general/community-standards/branding/).

> **Open item.** The watch-history badge art is derived from the Jellyfin logo. That needs either
> confirmed permission under Jellyfin's branding rules or replacement with original artwork —
> Jellyfin explicitly advises third-party projects to use their own identity. Tracked as an
> outstanding compliance task; it is *not* resolved by this notice.

## Bundled font — Juventus Fans Bold

`Jellyfin.Plugin.Overcoat/Resources/Fonts/Juventus-Fans-Bold.ttf` is embedded in the plugin assembly
and used for banner text.

> **Open item.** This font was added without a recorded licence or attribution, and its
> redistribution terms have not been confirmed. Until they are, it must be treated as
> **unverified for redistribution**. Resolution is either documented permission/licence here, or
> replacement with a font whose licence clearly permits bundling (for example an SIL OFL face).
> Overcoat already supports `sans` / `serif` / `mono` system fonts, so replacing it is viable.

## NuGet dependencies

| Package | Used for | Licence |
| --- | --- | --- |
| `Jellyfin.Controller` / `Jellyfin.Model` | Jellyfin server APIs | GPL-2.0-or-later |
| `SkiaSharp` | Poster rendering | MIT |

Both are referenced at build time only — neither is redistributed inside the plugin `.zip`, which
contains just `Jellyfin.Plugin.Overcoat.dll` and `meta.json`. The server provides them at runtime.

## Poster and screenshot imagery

The public README showcase uses poster artwork sourced from TVDB. Raw clean posters are kept only in
the private, gitignored developer input directory; the repository commits finished demonstration
plates. Artwork copyright remains with the respective studios and distributors and is shown only to
illustrate Overcoat's visual output. No affiliation with or endorsement by TVDB or any depicted title
is implied.

TVDB and TheTVDB.com are trademarks of TheTVDB.com, LLC. TVDB's terms are available at
<https://thetvdb.com/terms-of-service>.
