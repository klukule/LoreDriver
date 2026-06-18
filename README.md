# Horde.Lore

**Work in progress** Lore integration for Unreal Horde, shipped as two plugins so you can drop it into a Horde build without editing Horde itself.

- `HordeServer.Lore` - server plugin: reads commit metadata from the Lore server over gRPC and turns a `vcs: "Lore"` stream's agent workspace into a Lore workspace.
- `LoreDriver` - a job driver (based on the stock `JobDriver`) that syncs the Lore workspace with the native Lore SDK and runs the job.

## What works

- A Lore stream resolves commit metadata over gRPC.
- A job dispatches to an agent, the agent syncs the Lore workspace to a given revision, and BuildGraph runs to completion.
- Conforms (incremental and full). Incremental resets the workspace to the synced revision; full verifies + heals (re-fetch/repair) then resets.

## Untested

- **Auth** I have bare minimum local deployment without auth or even usernames set up for now so auth might not work, currently everything is mapped onto perforce-first structures.

## Install

1. Drop the `Engine/` folder from this repo onto the root of your source tree.
2. Reference the server plugin - add to `HordeServer/HordeServer.csproj`:
   ```xml
   <ProjectReference Include="..\Plugins\Lore\HordeServer.Lore\HordeServer.Lore.csproj" />
   ```
3. Add to the solution - add `Plugins/Lore/HordeServer.Lore/HordeServer.Lore.csproj` and `Drivers/LoreDriver/LoreDriver.csproj` to `Horde.sln`.
4. Apply the Horde patches in `patches/` (small edits to existing Horde files - see `patches/README.md`):
   - `01-perforce-decouple.patch` - dispatch a job for a non-Perforce stream with no Perforce cluster present.
   - `02-lore-conform.patch` - route conform to the stream's configured driver and run the materializer conform path.
   - `dashboard-vcs-extension-points.patch` + `dashboard-plugin-registry.patch` - generic dashboard mount points the Lore dashboard plugin hooks into.

## Configure (see `Examples/`)

- Enable the plugin - `Examples/appsettings.json`.
- Add the `lore` plugin config - `Examples/globals.plugins.json`.
- Example stream/project (`vcs: "Lore"`, template with `jobOptions.driver: "LoreDriver"`) - `Examples/Defaults/`.

Make sure to deploy (or have deployed) the `LoreDriver` into `LoreDriver/LoreDriver.dll` next to the HordeAgent executable (the agent runs `<driverName>/<driverName>.dll`) - see the base JobDriver for reference.

NOTE: The server-side plugin uses raw protobufs copied from <https://github.com/EpicGames/lore/tree/main/lore-proto/proto/lore> to access the low-level gRPC API directly instead of the Lore SDK.
