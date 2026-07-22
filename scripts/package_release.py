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
OVERVIEW = "Overlays status banners and badges onto your Jellyfin posters."
DESCRIPTION = (
    "Overcoat decorates your Jellyfin posters with useful info at a glance — status banners like "
    "NEW, AIRING, RETURNING, ENDED and CANCELED on your TV shows, plus badges for trending titles, "
    "IMDb Top 250 picks, and the shows and movies you've actually been watching (using Jellyfin's "
    "built-in activity). Set it up once, choose your libraries, and Overcoat keeps your posters "
    "updated automatically."
)


def extract_changelog(path, tag):
    """Pull the '## [x.y.z] — date' section for this tag out of a Keep-a-Changelog file.

    Falls back to a one-liner rather than failing the release: a missing heading should not block
    shipping, but the generic text is a signal the CHANGELOG wasn't updated.
    """
    version = tag.lstrip("v").split("-")[0]
    try:
        with open(path, encoding="utf-8") as f:
            text = f.read()
    except OSError:
        return f"Overcoat {tag}."

    # Match '## [0.6.1]' (any trailing date/text), up to the next '## ' heading.
    pattern = rf"^##\s*\[{re.escape(version)}\][^\n]*\n(.*?)(?=^##\s|\Z)"
    m = re.search(pattern, text, re.MULTILINE | re.DOTALL)
    if not m:
        return f"Overcoat {tag}."

    body = m.group(1).strip()
    return body if body else f"Overcoat {tag}."


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
        print(f"history:  {len(kept)} previous version(s) carried over from {path}")
        return kept
    except (OSError, ValueError, KeyError, IndexError) as exc:
        print(f"history:  WARNING could not read {path} ({exc}) — publishing without history")
        return []


def main():
    tag = os.environ["TAG"]
    ver = os.environ["PLUGIN_VER"]
    repo = os.environ.get("REPO", "clm302002/jellyfin-plugin-overcoat")
    pub = os.environ.get("PUBLISH_DIR", "publish")
    out = os.environ.get("OUTPUT_DIR", "artifacts")
    prev_manifest = os.environ.get("PREV_MANIFEST", "")
    ts = os.environ.get("TIMESTAMP") or datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    changelog = os.environ.get("CHANGELOG") or extract_changelog(
        os.environ.get("CHANGELOG_FILE", "CHANGELOG.md"), tag)

    os.makedirs(out, exist_ok=True)
    dll = os.path.join(pub, "Jellyfin.Plugin.Overcoat.dll")
    if not os.path.isfile(dll):
        sys.exit(f"ERROR: {dll} not found — run dotnet publish first.")

    # meta.json goes INSIDE the zip; Jellyfin reads it after extracting.
    meta = {
        "category": "Metadata", "guid": GUID, "name": NAME, "overview": OVERVIEW,
        "description": DESCRIPTION, "owner": OWNER, "targetAbi": TARGET_ABI,
        "timestamp": ts, "version": ver, "status": "Active", "autoUpdate": False, "assemblies": [],
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
