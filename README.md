# TorBox Refresh for Jellyfin

Jellyfin plugin that refreshes the TorBox Media Center mount when Jellyfin runs a library scan.

This is the standalone plugin repository. The server-side control API lives in the [TorBox Media Center](https://github.com/DanCorley/torbox-media-center) repository.

```text
Jellyfin scan
  -> TorBoxRefreshPostScanTask (ILibraryPostScanTask)
  -> GET  /health                      (probe api_version)
  -> POST /refresh/jobs                (API v2: 202 + job_id)
     or POST /refresh                  (legacy: 200 + counts)
  -> GET  /refresh/jobs/{id}           (poll every 2s until completed/failed)
  -> TorBox Media Center fetches the API, swaps the DB and rebuilds the mount
  -> if records or .strm files changed, Jellyfin queues one follow-up scan
```

If the TMC instance is older and `/health` returns `404` (or an unreadable
payload), the plugin falls back to the legacy synchronous `POST /refresh` and
reads the counts from the response. A `401` is never treated as a fallback.

## Compatibility matrix

| Plugin | TMC control API | TMC version | Jellyfin | Status |
|---|---|---|---|---|
| **1.1.0** | **2** | next 2.x (`feat/jellyfin-refresh-control-api`) | **12.4.0** | Health probe, async job polling, bounded retry, legacy synchronous fallback. |
| 1.0.0 | legacy (no `api_version`) | 2.0.x | 12.4.0 | Synchronous `POST /refresh` only. |
| _1.1.x_ | _2_ | _next 2.x_ | _13.x_ | **Pending.** Jellyfin 13.x NuGet packages are not published; no 13.x artifact exists. |

Notes:

- Plugin versions are independent of TMC versions. Plugin 1.1.0 needs control
  API v2 for async jobs, but still works against a legacy TMC through the
  synchronous fallback.
- Jellyfin **12.4.0** (`targetAbi` `12.4.0.0`) is the tested baseline. Do not
  assume Jellyfin 13.x support until `Jellyfin.Controller` and
  `Jellyfin.Model` 13.x packages are available and a 13.x build is published.
- The authoritative server-side matrix is in the TMC repository:
  [Compatibility matrix](https://github.com/DanCorley/torbox-media-center/blob/feat/jellyfin-refresh-control-api/docker.md#compatibility-matrix).

## How the plugin calls TMC

1. **Health/API-version probe.** `GET /health` with the bearer token. If the
   response contains `api_version >= 2`, the plugin uses async jobs. If
   `/health` returns `404` or the JSON cannot be read, it uses the legacy
   synchronous endpoint. `401` and other errors are real failures.
2. **Async refresh.** `POST /refresh/jobs` with `{"force": false}` returns
   `202` and `{job_id, status, status_url}`. The plugin then polls
   `GET /refresh/jobs/{id}` every 2 seconds until `completed` or `failed`.
3. **Synchronous fallback.** `POST /refresh` returns `200` with the refresh
   result and counts. This path exists for TMC versions without the v2 job API
   and for plugin 1.0.0 compatibility.
4. **Overall timeout.** The **Request timeout** setting (default 120 seconds)
   bounds each HTTP request and the total async polling deadline. When it
   expires the plugin logs a warning and treats the refresh as no change; TMC
   may still finish the job.
5. **Retries.** Transient failures are retried up to 3 attempts with 1s/2s
   backoff: HTTP `5xx` and request timeouts only. `401`, `409` and `429` are
   never retried.
6. **409/429 are no-ops.** `409` means a refresh is already running; `429`
   means TMC's `CONTROL_MIN_INTERVAL` is still cooling down. The plugin logs at
   Information and returns `changed = 0`, so it does **not** queue a follow-up
   scan. Both are expected during overlapping scans.
7. **Completion logging.** Every successful refresh logs at Information with
   job ID, `changed`, `added`, `removed`, `updated` and `partial`, including
   `changed=0`. A failed async job or timeout logs a warning.

The plugin's own **Cooldown** (default 300 seconds) is checked before any HTTP
call. It is independent of TMC's `CONTROL_MIN_INTERVAL`; the effective gap is
the larger of the two. A skipped cooldown or a `409`/`429` never queues a scan.

## The two-scan sequence

`ILibraryPostScanTask` runs **after** Jellyfin has walked the library, so files
that TMC adds during the refresh cannot be seen by that same scan:

1. First scan walks the current mount and finishes.
2. The post-scan task refreshes TMC and receives the change counts.
3. If `changed > 0` and **Queue a follow-up scan when the mount changed** is
   enabled (the default), Jellyfin queues one follow-up scan.
4. The second scan walks the refreshed mount and picks up the new files.
5. The second scan's post-scan task hits the plugin cooldown and stops, so there
   is no scan loop.

An unchanged mount, a failed refresh, a timeout and a `409`/`429` all produce
`changed = 0` and therefore no follow-up scan. Leave the follow-up setting
enabled unless something else watches the mount.

## TorBox Media Center setup

Enable the control API in the TorBox Media Center container:

```yaml
services:
  torbox-media-center:
    expose:
      - "8787"
    environment:
      CONTROL_ENABLED: "true"
      CONTROL_HOST: "0.0.0.0"
      CONTROL_PORT: "8787"
      CONTROL_TOKEN: "change-me"
      CONTROL_MIN_INTERVAL: "60"
```

Put Jellyfin and TorBox Media Center on the same Docker network (or use an external network). Do not publish port 8787 to the host unless you also put authentication/TLS in front of it. The API itself requires the bearer token.

See [docker-compose.example.yaml](docker-compose.example.yaml) for both services. The Jellyfin service needs no plugin environment variables; all plugin settings are entered in the dashboard.

For token generation/rotation, private-network rules, reverse proxies, cooldowns and FUSE propagation, use the TMC docs instead of duplicating them:

- [Network layout (Jellyfin, TMC and the control API)](https://github.com/DanCorley/torbox-media-center/blob/feat/jellyfin-refresh-control-api/docker.md#network-layout-private-network-only)
- [Control API contract and status codes](https://github.com/DanCorley/torbox-media-center/blob/feat/jellyfin-refresh-control-api/docker.md#control-api-contract-v2)
- [Token generation and rotation](https://github.com/DanCorley/torbox-media-center/blob/feat/jellyfin-refresh-control-api/docker.md#generating-and-rotating-the-control-token)
- [Refresh timing and cooldown](https://github.com/DanCorley/torbox-media-center/blob/feat/jellyfin-refresh-control-api/docker.md#refresh-timing-control_min_interval-and-the-plugin-cooldown)
- [Metadata-enabled slow refresh](https://github.com/DanCorley/torbox-media-center/blob/feat/jellyfin-refresh-control-api/docker.md#metadata-enabled-slow-refresh-and-plugin-timeout)
- [FUSE mount visibility](https://github.com/DanCorley/torbox-media-center/blob/feat/jellyfin-refresh-control-api/docker.md#fuse-mount-visibility-and-propagation)

## Build

The default build targets Jellyfin **12.4.0** (`targetAbi` `12.4.0.0`) using the `Jellyfin.Controller` / `Jellyfin.Model` NuGet packages. That matches the current `linuxserver/jellyfin:latest` image.

```bash
dotnet build -c Release
```

Jellyfin 13.x is **not supported yet**: the 13.x `Jellyfin.Controller` and
`Jellyfin.Model` NuGet packages are not published, so restore would fail. When
they are available, add a 13.x matrix entry and build against that version:

```bash
dotnet build -c Release -p:JellyfinVersion=13.0.0
```

or against a local Jellyfin source tree:

```bash
dotnet build -c Release -p:JellyfinRepo=/path/to/jellyfin
```

`scripts/package.sh` stages a `meta.json` with the requested version and
`targetAbi` so the packaged plugin always matches the DLL, and emits the
uppercase MD5 checksum Jellyfin expects.

## Install from the Jellyfin plugin repository

In Jellyfin, open **Dashboard -> Plugins -> Repositories**, add:

```text
https://raw.githubusercontent.com/DanCorley/jellyfin-plugin-torbox-refresh/main/docs/manifest.json
```

Then install **TorBox Refresh** from the Catalog. If you already side-loaded the
plugin, remove the manual `TorBoxRefresh` plugin folder first so Jellyfin does
not see two copies with the same GUID.

Jellyfin may warn that a repository is untrusted or that the plugin is
side-loaded. The catalog route is the supported one; trust the repository in the
dialog and restart Jellyfin after installing or removing files. The catalog
entry currently has no custom icon, so Jellyfin can show a placeholder.

## Install manually

The plugin has no third-party runtime dependencies, so copy only its DLL and manifest into the Jellyfin plugins folder:

```bash
mkdir -p <jellyfin config>/plugins/TorBoxRefresh
cp Jellyfin.Plugin.TorBoxRefresh/bin/Release/net10.0/Jellyfin.Plugin.TorBoxRefresh.dll <jellyfin config>/plugins/TorBoxRefresh/
cp Jellyfin.Plugin.TorBoxRefresh/meta.json <jellyfin config>/plugins/TorBoxRefresh/
```

The resulting folder is:

```text
<jellyfin config>/plugins/TorBoxRefresh/
  Jellyfin.Plugin.TorBoxRefresh.dll
  meta.json
```

Then restart Jellyfin. In Docker, mount the plugin folder:

```yaml
volumes:
  - ./jellyfin-plugins/TorBoxRefresh:/config/plugins/TorBoxRefresh
```

## Configure

Open **Dashboard -> Plugins -> TorBox Refresh**. The configuration page stores:

- **Enabled** - turn the scan hook on or off.
- **Refresh URL** - for example `http://torbox-media-center:8787/refresh`. The plugin derives the base URL by removing the trailing `/refresh`, so it can call `/health` and `/refresh/jobs`.
- **Control token** - must match `CONTROL_TOKEN` on TorBox Media Center.
- **Request timeout (seconds)** - default 120. Per-request timeout and overall async polling deadline. Raise it to 300-600 for metadata-enabled refreshes.
- **Cooldown (seconds)** - default 300. Minimum time between scan-triggered attempts. The second scan in the two-scan sequence is skipped by this cooldown.
- **Queue a follow-up scan when the mount changed** - default enabled. Leave it on unless real-time monitoring already handles changes.

The plugin sends `Authorization: Bearer <token>` on every call. TMC returns
`409` while a refresh is already running and `429` while its own
`CONTROL_MIN_INTERVAL` cooldown is active; the plugin treats both as "no
change" and does not queue a scan.

### Token storage

The token is stored in Jellyfin's plugin configuration XML inside the Jellyfin
config directory (typically under `plugins/configurations/`), in plain text. It
is protected only by filesystem permissions on that directory. Back it up
securely, keep it out of shared or public backups, and rotate the token on both
sides if it leaks. Rotating the TorBox API key does not rotate the control
token.

## Troubleshooting

### `401 unauthorized`

- The token in the plugin does not match `CONTROL_TOKEN`.
- TMC has not been restarted since `CONTROL_TOKEN` changed, or the plugin
  settings were not saved after the token changed.
- A reverse proxy stripped the `Authorization` header.
- The TorBox API key was pasted instead of the control token.

`401` is not retried and **does not** fall back to the legacy endpoint. Fixed by
aligning both values and restarting/re-saving as needed.

### `409` or `429` in the TMC logs

This is expected. `409` means a refresh is already running; `429` means TMC's
cooldown is active. The plugin logs an Information line and treats it as no
change, so no follow-up scan is queued. Wait for the next scan or raise the
plugin cooldown above `CONTROL_MIN_INTERVAL`. Do not add retries in front of the
plugin; retrying cannot make an active refresh finish sooner.

### The plugin silently does nothing

Check, in order:

1. **Enabled** is on.
2. **Refresh URL** points at TMC, for example `http://torbox-media-center:8787/refresh`, and resolves from inside the Jellyfin container.
3. **Control token** is set.
4. The plugin cooldown has elapsed since the last attempt. A failed or timed-out
   attempt also starts the cooldown.
5. Jellyfin's log contains the `TorBox refresh` lines from the plugin.

### Refresh timeout

The plugin logs `TorBox refresh timed out` or `did not finish within Ns`. TMC
may still be running the job. Check `GET /refresh/jobs/{id}` from TMC (or
`/health` for `last_error`) before retrying. Raise **Request timeout** for large
libraries. With `ENABLE_METADATA=true`, the TorBox metadata search is rate
limited and a refresh can take several minutes; prefer disabling metadata or
raise the timeout to 300-600 seconds.

### Files appear only after a second scan

That is the intended two-scan sequence. The first scan cannot see files added
after Jellyfin walked the mount. Ensure **Queue a follow-up scan when the mount
changed** is enabled and that the first refresh reported `changed > 0`.

### Side-loaded plugin or duplicate-plugin warnings

Remove the manual `TorBoxRefresh` folder, add the repository manifest, install
from the catalog, and restart Jellyfin. Two copies with GUID
`a7c2f0e4-6b1d-4f3a-9c8e-2d5b7a1e4f60` cause warnings and undefined behavior.
A missing catalog icon is cosmetic.

## Release and upgrade notes

- Plugin releases are tagged `vX.Y.Z` (for example `v1.1.0`). The release
  workflow builds against Jellyfin 12.4.0, packages `Jellyfin.Plugin.TorBoxRefresh.dll`
  and `meta.json`, computes the uppercase MD5 checksum, publishes the GitHub
  Release asset, and attempts to update `docs/manifest.json`. If branch
  protection blocks the manifest push, the workflow prints the manual
  `scripts/update-manifest.py` command for a pull request.
- Plugin 1.1.0 is the async-capable release line. It requires TMC control API v2 for
  `POST /refresh/jobs`; older TMC versions still work through the synchronous
  fallback. Plugin versions stay independent of TMC versions.
- Upgrade order: TMC first, then the plugin. Verify `GET /health` reports
  `api_version: 2`, then install the plugin update, restart Jellyfin if it was a
  manual install, and run a scan.
- Before upgrading, back up the Jellyfin plugin configuration XML. To roll back,
  reinstall the previous plugin version from the catalog/manifest (or restore
  the previous DLL and `meta.json`) and restore the config XML if the schema
  changed. Restart Jellyfin.
- A rollback to plugin 1.0.0 is safe against a v2 TMC because the synchronous
  `POST /refresh` endpoint is retained.

## Notes

- The plugin runs inside Jellyfin. It cannot remount FUSE or restart the TorBox container. It only asks TorBox Media Center to rebuild its view.
- For FUSE mode, the FUSE mount must already be visible inside the Jellyfin container (host mount with shared propagation or a bind mount). The plugin triggers the VFS rebuild, not the mount itself.
- `ILibraryPostScanTask` runs on full library validation scans (`RefreshLibrary`), not on individual item refreshes.
- With `ENABLE_METADATA=true` the TorBox refresh can take much longer than a plain file listing. Raise **Request timeout** if the library is large or the metadata search API is rate-limiting.
