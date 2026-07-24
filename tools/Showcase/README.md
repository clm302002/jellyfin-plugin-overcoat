# Showcase generator

Developer-only, file-to-file tooling for the public README gallery. It links the production
`OverlayRenderer` and `BadgeCompositor` source directly, but has no Jellyfin packages, provider
manager, configuration/database access, mounted-poster discovery, or network/API client.

Place clean posters named `game-of-thrones`, `friends`, `the-office`, `breaking-bad`,
`stranger-things`, and `the-sopranos` (JPG, PNG, or WebP) in a private directory, then run:

```bash
dotnet run --project tools/Showcase/Showcase.csproj -c Release -- \
  --input private/showcase-input --output assets
```

The output directory must be separate from the input directory. The harness hashes every input
before rendering and fails if any hash changes. It always places side ribbons on the left.

The generator writes five portrait plates plus `showcase-wide-cards.png`. The wide-card plate uses
the production landscape renderer on 16:9 crops of the same clean, privately stored source art, with
badges at the product's default scale (100%, doubled by the landscape optical multiplier) so the
plate matches what a server actually draws.

`capture-settings.js` loads the real embedded `configPage.html` in headless Chromium, injects local
mock `ApiClient`/`Dashboard` objects and fictional libraries/users, and captures the Posters, Wide
Cards, Libraries, and Maintenance tabs (`SHOWCASE_CAPTURE_ALL=1` adds TMDB API). It intentionally has
no server address or credentials. The script expects a local Playwright installation and Chromium
executable — `npm install playwright && npx playwright install chromium`. Standard Playwright discovery
is used; `PLAYWRIGHT_MODULE` and `CHROMIUM_EXECUTABLE` may override them on a development workstation.
