#!/usr/bin/env python3
"""Package Overcoat into a Jellyfin plugin .zip + repository manifest.json.

Reproducible, no jprm. Used by .github/workflows/release.yml and runnable locally:

  dotnet publish Jellyfin.Plugin.Overcoat/Jellyfin.Plugin.Overcoat.csproj -c Release -o publish
  TAG=v0.1.0 PLUGIN_VER=0.1.0.0 python3 scripts/package_release.py

Env:
  TAG          git tag, e.g. v0.1.0            (required)
  PLUGIN_VER   4-part plugin version, 0.1.0.0  (required)
  REPO         owner/repo                      (default clm302002/jellyfin-plugin-overcoat)
  PUBLISH_DIR  dotnet publish output           (default publish)
  OUTPUT_DIR   where to write artifacts        (default artifacts)
  CHANGELOG    changelog text for this version (default generic)
  TIMESTAMP    ISO8601 UTC                      (default now)
"""
import os, json, zipfile, hashlib, datetime, sys

GUID = "604f4e22-a0a1-490d-b383-d60336318eaa"
NAME = "Overcoat"
OVERVIEW = "Status overlays & badges for Jellyfin posters."
DESCRIPTION = (
    "In-process Jellyfin plugin that overlays TV status banners "
    "(NEW/AIRING/RETURNING/ENDED/CANCELED) onto posters, rendered with SkiaSharp and saved via "
    "Jellyfin's image API. Badges and the movie pipeline are in progress."
)

def main():
    tag = os.environ["TAG"]
    ver = os.environ["PLUGIN_VER"]
    repo = os.environ.get("REPO", "clm302002/jellyfin-plugin-overcoat")
    pub = os.environ.get("PUBLISH_DIR", "publish")
    out = os.environ.get("OUTPUT_DIR", "artifacts")
    changelog = os.environ.get("CHANGELOG", f"Overcoat {tag}.")
    ts = os.environ.get("TIMESTAMP") or datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    os.makedirs(out, exist_ok=True)
    dll = os.path.join(pub, "Jellyfin.Plugin.Overcoat.dll")
    if not os.path.isfile(dll):
        sys.exit(f"ERROR: {dll} not found — run dotnet publish first.")

    # meta.json goes INSIDE the zip; Jellyfin reads it after extracting.
    meta = {
        "category": "Metadata", "guid": GUID, "name": NAME, "overview": OVERVIEW,
        "description": DESCRIPTION, "owner": "clm302002", "targetAbi": "10.11.0.0",
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

    manifest = [{
        "guid": GUID, "name": NAME, "description": DESCRIPTION, "overview": OVERVIEW,
        "owner": "clm302002", "category": "Metadata",
        "imageUrl": f"https://raw.githubusercontent.com/{repo}/main/assets/overcoat-hero.png",
        "versions": [{
            "version": ver, "changelog": changelog, "targetAbi": "10.11.0.0",
            "sourceUrl": f"https://github.com/{repo}/releases/download/{tag}/{zipname}",
            "checksum": md5, "timestamp": ts,
        }],
    }]
    with open(os.path.join(out, "manifest.json"), "w") as f:
        json.dump(manifest, f, indent=2)

    print(f"zip:      {zpath} ({os.path.getsize(zpath)} bytes)")
    print(f"md5:      {md5}")
    print(f"manifest: {os.path.join(out, 'manifest.json')}")

if __name__ == "__main__":
    main()
