# Production TODO

Working notes for taking the TorBox Refresh Jellyfin plugin from prototype to a
maintainable, distributable plugin.

## Repository layout

- [x] Split the Jellyfin plugin into this standalone repository.
- [x] Keep the server-side control API in the TorBox Media Center repository.
- [x] Put the TorBox Media Center changes on a feature branch (`feat/jellyfin-refresh-control-api`).
- [ ] Decide whether the TorBox Media Center branch is upstreamed to `TorBox-App/torbox-media-center` or kept in the fork.
- [ ] Point the TMC fork's Docker workflow at your own image namespace (the upstream workflow pushes to `anonymoussystems/...` and `ghcr.io/torbox-app/...`).
- [ ] Keep personal deployment files out of upstream PRs; use a local compose override.

## Current state

- [x] Plugin loads on Jellyfin 12.4.0 (`targetAbi` `12.4.0.0`).
- [x] Control API reachable from the Jellyfin container over the Compose network.
- [x] `POST /refresh` returns change counts.
- [x] TMC refresh staging, DB replacement, `.strm` reconciliation and FUSE refresh are implemented.
- [x] Python unit tests cover refresh staging, diffing, `.strm` writes/cleanup and control auth.
- [ ] Plugin build/release automation is not set up yet.

## TorBox Media Center hardening

- [ ] Add a `CONTROL_API_VERSION` constant and return it from `GET /health`.
- [ ] Reject or warn when the plugin's requested API version is unsupported.
- [ ] Replace the synchronous `POST /refresh` with an async job:
  - [ ] `POST /refresh` returns `202 {job_id}`.
  - [ ] `GET /refresh/{job_id}` returns `queued|running|completed|failed` plus counts/errors.
  - [ ] Keep one active job; return `409` while busy.
  - [ ] Retain the last N job results for diagnostics.
- [ ] Add an idempotency key or request dedupe for repeated scan triggers.
- [ ] Keep the bearer-token requirement and constant-time comparison.
- [ ] Document that the control API must stay on a private network; do not publish `8787`.
- [ ] Add optional TLS/reverse-proxy guidance for cross-host deployments.
- [ ] Do not reuse the TorBox API key as the control token.
- [ ] Add structured logs with refresh duration, counts, partial failures and request IDs.
- [ ] Never log the control token or TorBox API key.
- [ ] Add a Docker healthcheck for `/health`.
- [ ] Add log rotation for the TMC container.
- [ ] Add integration tests for partial API failure, TMC restart and concurrent scan/refresh.
- [ ] Add a fake TorBox API mode or fixture for CI so tests do not hit the real API.
- [ ] Add a non-root mode for `strm` deployments (FUSE still needs `/dev/fuse` and `SYS_ADMIN`).
- [ ] Pin production image tags; do not deploy `latest`.
- [ ] Document upgrade order (TMC first, then plugin) and rollback.
- [ ] Keep `.env`, `config/`, `torbox/` and local compose files out of the image and Git.

## Jellyfin plugin hardening

- [ ] Generate `meta.json` per build so `targetAbi` always matches the DLL.
- [ ] Build and publish separate artifacts for Jellyfin 12.4.x and 13.x.
- [ ] Add a fake-TMC integration test for `TorBoxRefreshPostScanTask`.
- [ ] Add a build test for every supported Jellyfin ABI.
- [ ] Poll the async refresh job instead of holding a long HTTP request.
- [ ] Add bounded retry/backoff for TMC `5xx` and timeouts.
- [ ] Treat TMC `409`/`429` as already handled; do not queue a follow-up scan.
- [ ] Log at Information whenever a refresh completes, including `changed=0`.
- [ ] Keep `QueueFollowUpScan=true` as the default; document the two-scan behavior.
- [ ] Add a config schema version if the saved XML ever changes.
- [ ] Document that the token is stored in Jellyfin's plugin XML and protect the config directory.
- [x] Package a zip with the DLL and `meta.json` at the archive root.
- [ ] Generate Jellyfin's expected MD5 checksum (uppercase hex) for each release zip.

## Distribution

- [x] Create a plugin repository manifest (`manifest.json`) on GitHub Pages or another static host.
- [x] Add the plugin repository URL to the README so users can install through Jellyfin.
- [x] Publish release zips as GitHub Release assets.
- [ ] Update the manifest automatically on each plugin release.
- [ ] Add a plugin icon/image URL for the catalog.
- [ ] Optionally enable a dedicated Pages subdomain (e.g. `torbox-plugin.dancorley.com`); Pages is currently disabled and the raw GitHub manifest URL is canonical because the account-level `dancorley.com` custom domain catches project Pages paths.
- [x] Keep the plugin GUID stable:

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
        "checksum": "<sha256>",
        "timestamp": "2026-11-02T00:00:00Z"
      }
    ]
  }
]
```

## Documentation

- [ ] Add a network diagram showing Jellyfin, TMC and the control API.
- [ ] Document token generation and rotation.
- [ ] Document the `CONTROL_MIN_INTERVAL` and plugin cooldown interaction.
- [ ] Document the metadata-enabled slow-refresh case and how to raise the timeout.
- [ ] Document FUSE requirements (mount visibility and propagation).
- [ ] Add troubleshooting for `401`, `409`, `429` and repository-details warnings.
- [ ] Add a compatibility matrix (Jellyfin version, plugin version, TMC version).
- [ ] Add release/upgrade notes.

## Operations

- [ ] Add a TMC healthcheck and alert when `/health` fails or `last_error` is set.
- [ ] Back up the Jellyfin plugin config XML.
- [ ] Monitor refresh duration and failure counts.
- [ ] Decide whether to expose a `/metrics` endpoint.
- [ ] Review the existing TorBox API-key exposure in `.strm` files and document it as an accepted design constraint.
- [ ] Keep Docker secrets or restricted env files out of Git and images.

## First steps

1. Commit the TMC feature branch and open a PR (or keep it in the fork).
2. Add the async refresh job API to TMC.
3. Add the plugin ABI build matrix and release workflow.
4. Publish the plugin manifest via GitHub Pages.
5. Add integration tests for scan -> `/refresh` -> follow-up scan.
6. Write the deployment and troubleshooting docs.
