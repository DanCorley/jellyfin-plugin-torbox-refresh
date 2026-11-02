# TorBox Refresh Jellyfin Plugin TODO

Scope: plugin-only work in this repository. Server-side work lives in the TorBox
Media Center repository and is tracked separately:

- TMC repo: https://github.com/DanCorley/torbox-media-center
- TMC TODO: `TODO.md` on `feat/jellyfin-refresh-control-api`

Do not duplicate TMC work here; use the cross-references at the bottom.

## Current state

- [x] Standalone plugin repository.
- [x] Plugin loads on Jellyfin 12.4.0 with `targetAbi` `12.4.0.0`.
- [x] Configuration page stores URL, token, timeout, cooldown and follow-up-scan settings.
- [x] `POST /refresh` integration returns change counts.
- [x] Initial `v1.0.0` release published with a plugin zip.
- [x] Plugin repository manifest published at `docs/manifest.json`.
- [x] Raw manifest URL verified from the Jellyfin container.
- [x] Solution file builds from the repository root with 0 warnings.
- [x] Health probe (`GET /health`) selects async jobs when `api_version >= 2`.
- [x] Async job polling (`POST /refresh/jobs`, `GET /refresh/jobs/{id}`) every 2 seconds.
- [x] Synchronous `POST /refresh` legacy fallback when `/health` returns 404 or an unreadable payload.
- [x] Bounded retry/backoff for `5xx` and timeouts; `401`/`409`/`429` are never retried.
- [x] `409`/`429` handled as no-ops; completion logged at Information including `changed=0`.
- [x] README documents the v2 flow, compatibility matrix, two-scan sequence and troubleshooting.

## Repository and release

- [x] `PLG-REL-1` Split the plugin into its own repository and keep the plugin GUID stable.
- [x] `PLG-REL-2` Add a tag-triggered release workflow: build, zip, MD5 checksum, GitHub Release, and manifest update.
- [ ] `PLG-REL-3` Keep plugin versions independent of TMC versions; reference the minimum supported TMC/API version in release notes. Versions are independent and the README states the minimum; add the minimum/API version to the generated GitHub release notes.
- [ ] `PLG-REL-4` Add branch protection and a pull-request build check. The PR build check exists in `build.yml`; branch protection is not configured yet.

## Plugin hardening

- [x] `PLG-H-1` Generate `meta.json` per build so `targetAbi` always matches the DLL (`scripts/package.sh`).
- [ ] `PLG-H-2` Build and publish separate artifacts for Jellyfin 12.4.x and 13.x. Blocked: 13.x NuGet packages are not published.
- [ ] `PLG-H-3` Add a fake-TMC integration test for `TorBoxRefreshPostScanTask`. Service-level fake-TMC tests exist; the post-scan task and `QueueLibraryScan` path are not covered.
- [ ] `PLG-H-4` Add a build test for every supported Jellyfin ABI. Only the 12.4.0 matrix entry exists; add 13.x when its packages are available.
- [x] `PLG-H-5` Poll an async TMC refresh job instead of holding a long HTTP request.
- [x] `PLG-H-6` Add bounded retry/backoff for TMC `5xx` and timeouts (3 attempts, 1s/2s).
- [x] `PLG-H-7` Treat TMC `409`/`429` as already handled; do not queue a follow-up scan.
- [x] `PLG-H-8` Log at Information whenever a refresh completes, including `changed=0`.
- [x] `PLG-H-9` Keep `QueueFollowUpScan=true` as the default and document the two-scan behavior.
- [ ] `PLG-H-10` Add a config schema version if the saved XML ever changes.
- [x] `PLG-H-11` Document that the token is stored in Jellyfin's plugin XML and protect the config directory.
- [x] `PLG-H-12` Generate Jellyfin's expected MD5 checksum (uppercase hex) for each release zip.
- [x] `PLG-H-13` Check the TMC `CONTROL_API_VERSION` from `/health` before calling `/refresh`.
- [x] `PLG-H-14` Use the documented TMC response schema in the client and docs.

## Distribution

- [x] `PLG-DIST-1` Create and validate the plugin repository manifest.
- [x] `PLG-DIST-2` Add the plugin repository URL to the README.
- [x] `PLG-DIST-3` Publish the plugin zip as a GitHub Release asset.
- [x] `PLG-DIST-4` Update `docs/manifest.json` automatically on each release. The release workflow attempts the update and prints the manual PR command if branch protection blocks the push.
- [ ] `PLG-DIST-5` Add a plugin icon/image URL for the catalog.
- [ ] `PLG-DIST-6` Optionally enable a dedicated Pages subdomain; the raw GitHub manifest URL is canonical.
- [x] `PLG-DIST-7` Keep the plugin GUID stable:

  `a7c2f0e4-6b1d-4f3a-9c8e-2d5b7a1e4f60`

Example manifest entry:

```json
[
  {
    "guid": "a7c2f0e4-6b1d-4f3a-9c8e-2d5b7a1e4f60",
    "name": "TorBox Refresh",
    "description": "Refreshes the TorBox mount when Jellyfin scans.",
    "overview": "Calls the TorBox Media Center control API after a library scan.",
    "owner": "Dan Corley",
    "category": "Library",
    "imageUrl": "https://example.com/torbox-refresh.png",
    "versions": [
      {
        "version": "1.1.0.0",
        "changelog": "Adds the health/API-version probe and async refresh job polling, with synchronous fallback for older TMC.",
        "targetAbi": "12.4.0.0",
        "sourceUrl": "https://github.com/DanCorley/jellyfin-plugin-torbox-refresh/releases/download/v1.1.0/Jellyfin.Plugin.TorBoxRefresh_1.1.0.0.zip",
        "checksum": "<uppercase-md5>",
        "timestamp": "2026-11-02T00:00:00Z"
      }
    ]
  }
]
```

The live `docs/manifest.json` is authoritative; the entry above shows the
expected shape for the 1.1.0 async release.

## Documentation

- [x] `PLG-DOC-1` Document catalog and manual installation.
- [x] `PLG-DOC-2` Document the configuration page fields.
- [x] `PLG-DOC-3` Add troubleshooting for `401`, `409`, `429` and side-loaded-plugin repository warnings.
- [x] `PLG-DOC-4` Add the compatibility matrix (Jellyfin version, plugin version, minimum TMC version).
- [x] `PLG-DOC-5` Add plugin release and upgrade notes.
- [x] `PLG-DOC-6` Document the two-scan sequence and `QueueFollowUpScan` behavior.
- [x] `PLG-DOC-7` Link to the TMC network diagram, token rotation, cooldown and FUSE docs rather than duplicating them.

## Operations

- [x] `PLG-OPS-1` Back up the Jellyfin plugin config XML.
- [x] `PLG-OPS-2` Document plugin update and rollback.
- [x] `PLG-OPS-3` Add a smoke test that installs the release zip into a Jellyfin container and verifies the plugin loads (`smoke.yml`; local Jellyfin 12.4 validation passed).

## First steps

1. `PLG-REL-4`: enable branch protection; the pull-request build check is
   already in `build.yml`.
2. `PLG-H-2`, `PLG-H-4`: add the Jellyfin 13.x build matrix entry once
   `Jellyfin.Controller` / `Jellyfin.Model` 13.x NuGet packages are published.
3. `PLG-H-3`, `PLG-OPS-3`: add fake-TMC and container smoke tests.
4. `PLG-DIST-5`: add a catalog icon.
5. `PLG-H-10`: add a config schema version if the saved XML changes.

## Related TMC work

These are tracked in the TorBox Media Center TODO, not here:

| TMC task | Plugin task it enables |
|---|---|
| `TMC-API-1` | `PLG-H-13` |
| `TMC-API-2` | `PLG-H-5` |
| `TMC-API-4` | `PLG-H-14` |
| `TMC-REL-4` | `PLG-REL-3`, `PLG-DOC-4` |
| `TMC-DOC-6` | `PLG-DOC-4` |
| `TMC-DOC-1`, `TMC-DOC-3`, `TMC-DOC-4`, `TMC-DOC-5` | `PLG-DOC-1`, `PLG-DOC-3` |
