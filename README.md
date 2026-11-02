# TorBox Refresh for Jellyfin

Jellyfin plugin that refreshes the TorBox Media Center mount when Jellyfin runs a library scan.

This is the standalone plugin repository. The server-side control API lives in the [TorBox Media Center](https://github.com/DanCorley/torbox-media-center) repository.

```text
Jellyfin scan
  -> TorBoxRefreshPostScanTask (ILibraryPostScanTask)
  -> POST http://torbox-media-center:8787/refresh
  -> TorBox Media Center fetches the API, swaps the DB and rebuilds the mount
  -> if records or .strm files changed, Jellyfin queues one follow-up scan
```

The follow-up scan is required because a post-scan task runs after Jellyfin has already walked the mount. The second scan's post-scan task hits the cooldown and stops.

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

## Build

The default build targets Jellyfin **12.4.0** (`targetAbi` `12.4.0.0`) using the `Jellyfin.Controller` / `Jellyfin.Model` NuGet packages. That matches the current `linuxserver/jellyfin:latest` image.

```bash
cd Jellyfin.Plugin.TorBoxRefresh
dotnet build -c Release
```

If you run a different Jellyfin ABI (for example a local 13.x build), change `targetAbi` in `meta.json` and build against that source tree:

```bash
dotnet build -c Release -p:JellyfinRepo=/path/to/jellyfin
```

## Install

The plugin has no third-party runtime dependencies, so copy only its DLL and manifest into the Jellyfin plugins folder:

```bash
mkdir -p <jellyfin config>/plugins/TorBoxRefresh
cp bin/Release/net10.0/Jellyfin.Plugin.TorBoxRefresh.dll <jellyfin config>/plugins/TorBoxRefresh/
cp meta.json <jellyfin config>/plugins/TorBoxRefresh/
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
- **Refresh URL** - for example `http://torbox-media-center:8787/refresh`.
- **Control token** - must match `CONTROL_TOKEN` on TorBox Media Center.
- **Request timeout (seconds)** - how long to wait for the refresh.
- **Cooldown (seconds)** - minimum time between scan-triggered refreshes. The second scan in the two-scan sequence is skipped by this cooldown.
- **Queue a follow-up scan when the mount changed** - leave enabled unless you have real-time monitoring handling the changes.

The plugin sends `POST /refresh` with `Authorization: Bearer <token>`. TorBox Media Center returns 409 while a refresh is already running and 429 while its own `CONTROL_MIN_INTERVAL` cooldown is active; the plugin treats both as "no change" and does not queue a scan.

## Notes

- The plugin runs inside Jellyfin. It cannot remount FUSE or restart the TorBox container. It only asks TorBox Media Center to rebuild its view.
- For FUSE mode, the FUSE mount must already be visible inside the Jellyfin container (host mount with shared propagation or a bind mount). The plugin triggers the VFS rebuild, not the mount itself.
- `ILibraryPostScanTask` runs on full library validation scans (`RefreshLibrary`), not on individual item refreshes.
- With `ENABLE_METADATA=true` the TorBox refresh can take much longer than a plain file listing. Raise **Request timeout (seconds)** if the library is large or the metadata search API is rate-limiting.
