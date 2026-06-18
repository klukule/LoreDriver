# Horde patches

Small, set of patches to existing Horde source. Each patch is a plain `git diff` against a pristine Horde tree. Apply from source root:

```
git apply patches/01-perforce-decouple.patch
```

(or `git apply --check <file>` first to dry-run). Stage them however you like.

## 01-perforce-decouple.patch

**Goal:** a non-Perforce (Lore) stream dispatches jobs (and conforms) with no perforce server needed.

Horde's dispatch built the agent workspace message via the Perforce path (`FindPerforceCluster` + `PerforceLoadBalancer.SelectServerAsync`, which requires a healthy `p4 info`). This patch makes Build skip Perforce for any workspace whose materializer isn't Perforce, deciding per-workspace from the workspace `method`:

- `PerforceWorkspace.cs` - adds `RequiresPerforce(method)` (true for no materializer name, or `Perforce`/`ManagedWorkspace`) and `AddWorkspaceMessage(workspace, messages)` (builds the agent workspace message with no server/credentials; a VCS provider plugin's `IWorkspaceMessageEnricher` fills the rest).
- `JobTaskSource.cs` - `CreateExecuteJobTaskAsync` takes the Perforce path only when `RequiresPerforce(workspace.Method)`; otherwise builds the message via `AddWorkspaceMessage`.


## dashboard-vcs-extension-points.patch

**Goal:** a common extension point so any VCS provider plugin can add a servers page under **Server > Monitoring** (not just a top-nav page).

- `HordeDashboard/plugins/index.ts` - adds `MountType.ServerMonitoring` to the existing mount enum.
- `HordeDashboard/src/components/TopNav.tsx` - renders `ServerMonitoring` plugin mounts in the Server > Monitoring menu section (mirrors how `TopNav` mounts are already rendered into the Tools menu).

A plugin opts in with `mount: { type: MountType.ServerMonitoring, text: "...", route: "/..." }` plus a route. Plugin routes already auto-inject into the router (`App.tsx`), so no router change is needed.

## dashboard-plugin-registry.patch

One-line patch adding `import "./lore"` to `HordeDashboard/plugins/registry.ts`. To allow the lore extensions to show up in horde dashboard.

## 02-lore-conform.patch

**Goal:** run conforms for a Lore stream.

Four existing files:
- `conform_task.proto` - adds `ConformTask.Driver` to the conform protobuf.
- `ConformTaskSource.cs` - derives the target driver assembly and sets it on the conform task; also applies the same Perforce decouple as patch 01 (uses `RequiresPerforce`/`AddWorkspaceMessage` so non-Perforce conform workspaces need no Perforce cluster/server). Depends on patch 01.
- `ConformHandler.cs` - runs `conformTask.Driver`, defaulting to `JobDriver` when empty.
- `ConformExecutor.cs` - routes named, non-`ManagedWorkspace` materializers (Perforce, Lore, ...) to the materializer-based conform (`ConformMaterializersAsync`) instead of the raw managed path.