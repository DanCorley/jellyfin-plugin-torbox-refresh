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

## Repository and release

- [x] `PLG-REL-1` Split the plugin into its own repository and keep the plugin GUID stable.
- [ ] `PLG-REL-2` Add a tag-triggered release workflow: build, zip, MD5 checksum, GitHub Release, and manifest update.
- [ ] `PLG-REL-3` Keep plugin versions independent of TMC versions; reference the minimum supported TMC/API version in release notes.
- [ ] `PLG-REL-4` Add branch protection and a pull-request build check.

## Plugin hardening

- [ ] `PLG-H-1` Generate `meta.json` per build so `targetAbi` always matches the DLL.
- [ ] `PLG-H-2` Build and publish separate artifacts for Jellyfin 12.4.x and 13.x.
- [ ] `PLG-H-3` Add a fake-TMC integration test for `TorBoxRefreshPostScanTask`.
- [ ] `PLG-H-4` Add a build test for every supported Jellyfin ABI.
- [ ] `PLG-H-5` Poll an async TMC refresh job instead of holding a long HTTP request. Depends on `TMC-API-2`.
- [ ] `PLG-H-6` Add bounded retry/backoff for TMC `5xx` and timeouts.
- [ ] `PLG-H-7` Treat TMC `409`/`429` as already handled; do not queue a follow-up scan.
- [ ] `PLG-H-8` Log at Information whenever a refresh completes, including `changed=0`.
- [ ] `PLG-H-9` Keep `QueueFollowUpScan=true` as the default and document the two-scan behavior.
- [ ] `PLG-H-10` Add a config schema version if the saved XML ever changes.
- [ ] `PLG-H-11` Document that the token is stored in Jellyfin's plugin XML and protect the config directory.
- [ ] `PLG-H-12` Generate Jellyfin's expected MD5 checksum (uppercase hex) for each release zip.
- [ ] `PLG-H-13` Check the TMC `CONTROL_API_VERSION` from `/health` before calling `/refresh`. Depends on `TMC-API-1`.
- [ ] `PLG-H-14` Use the documented TMC response schema in the client and docs. Depends on `TMC-API-4`.

## Distribution

- [x] `PLG-DIST-1` Create and validate the plugin repository manifest.
- [x] `PLG-DIST-2` Add the plugin repository URL to the README.
- [x] `PLG-DIST-3` Publish the plugin zip as a GitHub Release asset.
- [ ] `PLG-DIST-4` Update `docs/manifest.json` automatically on each release.
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
        "version": "1.0.0.0",
        "changelog": "Initial release.",
        "targetAbi": "12.4.0.0",
        "sourceUrl": "https://github.com/DanCorley/jellyfin-plugin-torbox-refresh/releases/download/v1.0.0/Jellyfin.Plugin.TorBoxRefresh_1.0.0.0.zip",
        "checksum": "<md5>",
        "timestamp": "2026-11-02T00:00:00Z"
      }
    ]
  }
]
```

## Documentation

- [x] `PLG-DOC-1` Document catalog and manual installation.
- [x] `PLG-DOC-2` Document the configuration page fields.
- [ ] `PLG-DOC-3` Add troubleshooting for `401`, `409`, `429` and side-loaded-plugin repository warnings.
- [ ] `PLG-DOC-4` Add the compatibility matrix (Jellyfin version, plugin version, minimum TMC version). Depends on `TMC-DOC-6`.
- [ ] `PLG-DOC-5` Add plugin release and upgrade notes.
- [ ] `PLG-DOC-6` Document the two-scan sequence and `QueueFollowUpScan` behavior. Depends on `PLG-H-9`.
- [ ] `PLG-DOC-7` Link to the TMC network diagram, token rotation, cooldown and FUSE docs rather than duplicating them.

## Operations

- [ ] `PLG-OPS-1` Back up the Jellyfin plugin config XML.
- [ ] `PLG-OPS-2` Document plugin update and rollback.
- [ ] `PLG-OPS-3` Add a smoke test that installs the release zip into a Jellyfin container and verifies the plugin loads.

## First steps

1. `PLG-REL-2`: add the tag-triggered build/release workflow.
2. `PLG-H-2`, `PLG-H-4`: add the Jellyfin 12.4/13 ABI matrix.
3. `PLG-H-3`, `PLG-OPS-3`: add fake-TMC and container smoke tests.
4. `PLG-H-13`: add the TMC API version check when `TMC-API-1` lands.
5. `PLG-H-5`: switch to async job polling when `TMC-API-2` lands.
6. `PLG-DIST-4`, `PLG-DOC-4`: automate the manifest update and publish the compatibility matrix.

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
