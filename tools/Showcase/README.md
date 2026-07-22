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

`capture-settings.js` loads the real embedded `configPage.html` in headless Chromium, injects local
mock `ApiClient`/`Dashboard` objects and fictional libraries/users, and captures the Banners, Badges,
and Libraries tabs. It intentionally has no server address or credentials. The script expects a
local Playwright installation and Chromium executable. Standard Playwright discovery is used;
`PLAYWRIGHT_MODULE` and `CHROMIUM_EXECUTABLE` may override them on a development workstation.
