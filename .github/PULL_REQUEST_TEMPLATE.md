<!-- Thanks for contributing to Overcoat! -->

## What does this change?

<!-- A short description of the change and why. -->

## How was it tested?

<!-- e.g. tools/ParityTest output, tools/TmdbTest run, end-to-end on a test Jellyfin. -->

## Checklist

- [ ] Builds clean (`dotnet build -c Release -warnaserror`)
- [ ] Settings page still valid (`node scripts/check_config_page.js`)
- [ ] Overlay geometry/status constants preserved (or intentional look change noted)
- [ ] `CHANGELOG.md` updated under `## [Unreleased]` for user-visible changes

## If this touches when a poster gets written or reverted

Overcoat treats "no banner and no badges" as *revert this poster* — restoring the clean original and
dropping the cache entry. So anything feeding that decision must be able to say **"I don't know"**
distinctly from **"nothing"**, and the revert must be skipped when anything is unknown. Every
overlays-disappeared bug so far has been a version of this.

- [ ] New data sources report failure distinctly (not an empty set), and feed `badgeDataIncomplete`
- [ ] No code path deletes a vaulted original before its replacement is in hand
- [ ] Changes verified against a real library, not just a clean build — several fixes here have
      compiled perfectly while doing nothing at all
