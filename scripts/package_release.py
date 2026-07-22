#!/usr/bin/env python3
"""Package Overcoat into a Jellyfin plugin .zip + repository manifest.json.

Used by .github/workflows/release.yml and runnable locally:

  dotnet publish Jellyfin.Plugin.Overcoat/Jellyfin.Plugin.Overcoat.csproj -c Release -o publish
  TAG=v0.1.0 PLUGIN_VER=0.1.0.0 python3 scripts/package_release.py

Note: the .zip is NOT byte-reproducible — archive member timestamps are not normalised, so two runs
over identical inputs produce different checksums. The manifest records the checksum of the archive
actually published, so this affects reproducing a build, not verifying a download.

Two things here are load-bearing and easy to break:

* **Version history is preserved.** The manifest carries every past release, newest first, so
  Jellyfin's version dropdown lets users pick or roll back instead of only ever seeing the newest
  build. PREV_MANIFEST points at the existing manifest to merge with; if it is missing or
  unreadable we start fresh rather than failing the release, but that silently loses history — the
  workflow downloads it first and the log says which happened.
* **The changelog text ends up inside Jellyfin.** The `changelog` field is rendered in the plugin
  catalogue, so it should be the actual notes for this version, not a pointer to a file the user
  would have to go find. `extract_changelog()` pulls the matching `## [x.y.z]` section out of the
  file named by `CHANGELOG_FILE`; set `CHANGELOG` to override it with literal text.

Env:
  TAG            git tag, e.g. v0.1.0 or v0.7.0-beta.1  (required)
  PLUGIN_VER     4-part plugin version, 0.1.0.0         (required)
  REPO           owner/repo                             (default clm302002/jellyfin-plugin-overcoat)
  PUBLISH_DIR    dotnet publish output                  (default publish)
  OUTPUT_DIR     where to write artifacts               (default artifacts)
  CHANGELOG      literal changelog text                 (overrides CHANGELOG_FILE extraction)
  CHANGELOG_FILE markdown to extract this version from  (default CHANGELOG.md)
  PREV_MANIFEST  existing manifest.json to merge into   (optional; history is dropped without it)
  TIMESTAMP      ISO8601 UTC                            (default now)
"""
import os, json, zipfile, hashlib, datetime, sys, re

GUID = "604f4e22-a0a1-490d-b383-d60336318eaa"
NAME = "Overcoat"
OWNER = "clm302002"
# Jellyfin uses targetAbi to decide whether to offer a build to a server, so it must reflect the
# OLDEST version the DLL actually loads on — not the oldest we'd like to support. Verified by matrix
# build 2026-07-22: IUserManager.GetUsers / GetFirstUser (Services/WatchHistory.cs) do not exist
# before 10.11.9, so 10.11.0–10.11.8 fail to compile and would fail to load. Claiming 10.11.0.0 here
# offered those servers a plugin that cannot start. CI locks the floor with a matrix build.
TARGET_ABI = "10.11.9.0"
OVERVIEW = "Overlays status banners and badges onto Jellyfin posters and optional series wide cards."
DESCRIPTION = (
    "Overcoat decorates Jellyfin posters and optional series wide cards with useful info at a glance — status banners like "
    "NEW, AIRING, RETURNING, ENDED and CANCELED on your TV shows, plus badges for trending titles, "
    "IMDb Top 250 picks, and the shows and movies you've actually been watching (using Jellyfin's "
    "built-in activity). Set it up once, choose your libraries, and Overcoat keeps your posters "
    "updated automatically."
)


def extract_section(text, heading):
    """Body of a '## [heading] ...' section, up to the next '## ' heading. Empty if absent."""
    pattern = rf"^##\s*\[{re.escape(heading)}\][^\n]*\n(.*?)(?=^##\s|\Z)"
    m = re.search(pattern, text, re.MULTILINE | re.DOTALL)
    if not m:
        return ""
    body = m.group(1).strip()
    return "" if body.lower().startswith("_nothing yet") else body


def extract_changelog(path, tag, max_chars=None):
    """Release notes for a tag.

    Betas of one version all reduce to the same base number, so keying purely off that made every
    beta of 0.7.0 advertise the identical 5,000-character 0.7.0 section — the plugin page's revision
    history then showed the same wall of text four times over, which is worse than showing nothing.

    So a prerelease prefers the '## [Unreleased]' section, which is where work that has not shipped
    yet is recorded and therefore changes between betas, and is labelled with its beta number so two
    entries are never indistinguishable. A stable release uses its own '## [x.y.z]' section.
    """
    raw = tag.lstrip("v")
    base = raw.split("-")[0]
    prerelease = "-" in raw

    try:
        with open(path, encoding="utf-8") as f:
            text = f.read()
    except OSError:
        return f"Overcoat {tag}."

    if prerelease:
        suffix = raw.split("-", 1)[1]
        body = extract_section(text, "Unreleased") or extract_section(text, base)
        header = f"Pre-release ({suffix}) of {base}. Expect rough edges."
        body = f"{header}\n\n{body}" if body else header
    else:
        body = extract_section(text, base) or f"Overcoat {tag}."

    return truncate_changelog(body, max_chars)


def truncate_changelog(body, max_chars):
    """Trim to a readable length for the manifest — it renders in a narrow dashboard panel."""
    if not max_chars or len(body) <= max_chars:
        return body

    cut = body[:max_chars]
    # Prefer breaking at a line boundary so a bullet is not sliced mid-word.
    nl = cut.rfind("\n")
    if nl > max_chars * 0.6:
        cut = cut[:nl]
    return cut.rstrip() + "\n\n… see the full notes on the release page."


def tidy_historical_changelog(version, changelog):
    """Shorten a carried-over entry so the plugin page's revision history stays readable.

    Superseded prereleases are collapsed to a one-line label. Builds published before the notes were
    per-beta all carried the same multi-thousand-character section, so the panel showed the identical
    wall of text once per beta — the label is both shorter and more accurate about what that entry is.
    Stable entries keep their notes and are only length-capped.
    """
    parts = version.split(".")
    if len(parts) == 4 and parts[3].isdigit():
        build = int(parts[3])
        if 1 <= build < 500:
            base = ".".join(parts[:3])
            return f"Pre-release (build {build}) of {base}. Superseded — see the release page for details."

    return truncate_changelog(changelog, 900)


def load_previous_versions(path, current_version):
    """Existing manifest's version entries, minus any entry for the version we're publishing.

    Dropping the same-version entry makes re-running a release idempotent instead of producing a
    duplicate the dropdown would show twice.
    """
    if not path or not os.path.isfile(path):
        print(f"history:  none found at {path!r} — publishing a single-version manifest")
        return []
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        versions = data[0].get("versions", []) if isinstance(data, list) and data else []
        kept = [v for v in versions if v.get("version") != current_version]
        for v in kept:
            v["changelog"] = tidy_historical_changelog(v.get("version", ""), v.get("changelog", ""))
        print(f"history:  {len(kept)} previous version(s) carried over from {path}")
        return kept
    except (OSError, ValueError, KeyError, IndexError) as exc:
        print(f"history:  WARNING could not read {path} ({exc}) — publishing without history")
        return []


# Jellyfin skips a plugin entirely when working out available updates if its installed meta.json
# says autoUpdate false (InstallationManager.GetAvailablePluginUpdates). Shipping false therefore
# does not mean "let the user choose" — it means the Plugins page never offers an update at all and
# the "Update Plugins" task ignores the plugin, so users silently sit on whatever they first
# installed. Jellyfin's own default is true; matching it is what makes the repository URL useful.


def main():
    tag = os.environ["TAG"]
    ver = os.environ["PLUGIN_VER"]
    repo = os.environ.get("REPO", "clm302002/jellyfin-plugin-overcoat")
    pub = os.environ.get("PUBLISH_DIR", "publish")
    out = os.environ.get("OUTPUT_DIR", "artifacts")
    prev_manifest = os.environ.get("PREV_MANIFEST", "")
    ts = os.environ.get("TIMESTAMP") or datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    # The manifest's changelog renders inside Jellyfin's plugin page, in a narrow column stacked
    # once per version — long entries there are unreadable. The GitHub release body keeps the full
    # text (the workflow calls extract_changelog separately without a cap).
    MANIFEST_CHANGELOG_LIMIT = 900
    changelog = os.environ.get("CHANGELOG") or extract_changelog(
        os.environ.get("CHANGELOG_FILE", "CHANGELOG.md"), tag, max_chars=MANIFEST_CHANGELOG_LIMIT)

    os.makedirs(out, exist_ok=True)
    dll = os.path.join(pub, "Jellyfin.Plugin.Overcoat.dll")
    if not os.path.isfile(dll):
        sys.exit(f"ERROR: {dll} not found — run dotnet publish first.")

    # meta.json goes INSIDE the zip; Jellyfin reads it after extracting.
    meta = {
        "category": "Metadata", "guid": GUID, "name": NAME, "overview": OVERVIEW,
        "description": DESCRIPTION, "owner": OWNER, "targetAbi": TARGET_ABI,
        "timestamp": ts, "version": ver, "status": "Active", "autoUpdate": True, "assemblies": [],
    }
    meta_path = os.path.join(out, "meta.json")
    with open(meta_path, "w") as f:
        json.dump(meta, f, indent=2)

    zipname = f"overcoat_{ver}.zip"
    zpath = os.path.join(out, zipname)
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(dll, "Jellyfin.Plugin.Overcoat.dll")
        z.write(meta_path, "meta.json")

    md5 = hashlib.md5(open(zpath, "rb").read()).hexdigest()

    entry = {
        "version": ver,
        "changelog": changelog,
        "targetAbi": TARGET_ABI,
        "sourceUrl": f"https://github.com/{repo}/releases/download/{tag}/{zipname}",
        "checksum": md5,
        "timestamp": ts,
    }

    # Newest first — Jellyfin offers the top entry by default and lists the rest for rollback.
    versions = [entry] + load_previous_versions(prev_manifest, ver)

    manifest = [{
        "guid": GUID, "name": NAME, "description": DESCRIPTION, "overview": OVERVIEW,
        "owner": OWNER, "category": "Metadata",
        "imageUrl": f"https://raw.githubusercontent.com/{repo}/main/assets/showcase-hero.png",
        "versions": versions,
    }]
    with open(os.path.join(out, "manifest.json"), "w") as f:
        json.dump(manifest, f, indent=2)

    print(f"zip:      {zpath} ({os.path.getsize(zpath)} bytes)")
    print(f"md5:      {md5}")
    print(f"versions: {', '.join(v['version'] for v in versions)}")
    print(f"manifest: {os.path.join(out, 'manifest.json')}")


if __name__ == "__main__":
    main()
