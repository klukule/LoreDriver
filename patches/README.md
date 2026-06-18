# Horde patches

Small, set of patches to existing Horde source. Each patch is a plain `git diff` against a pristine Horde tree. Apply from source root:

```
git apply patches/01-perforce-assume-healthy.patch
```

(or `git apply --check <file>` first to dry-run). Stage them however you like.

## 01-perforce-assume-healthy.patch

**Goal:** let a non-Perforce (Lore) stream dispatch jobs without a real Perforce server being bound.

Horde gates every job/conform dispatch on `PerforceLoadBalancer.SelectServerAsync` returning a server that passed a live `p4 info` health check. This patch adds an opt-in `AssumeHealthy` flag to a Perforce server config; when set, the health ticker marks that server `Healthy` and skips the `p4 info` probe. Files:

- `BuildConfig.cs` - adds `PerforceServer.AssumeHealthy` (bool).
- `PerforceLoadBalancer.cs` - `UpdateHealthAsync` short-circuits to `Healthy` for `AssumeHealthy` servers (+ `IsAssumeHealthy` helper).

**NOTE:** this is a temporary solution, I am currently working on better solution that fully decouples jobs from perforce if the cluster isn't perforce cluster.

**Config to use it** - point the Lore stream's placeholder cluster at any address and mark it assumed-healthy:

```jsonc
"perforceClusters": [
  {
    "name": "Default",
    "servers": [ { "serverAndPort": "lore-placeholder:1666", "assumeHealthy": true } ]
  }
]
```

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
- `ConformTaskSource.cs` - adds logic to derive target Driver assembly and passes it to the conform_task protobuf.
- `ConformHandler.cs` - runs `conformTask.Driver`, defaulting to `JobDriver` when empty.
- `ConformExecutor.cs` - routes named, non-`ManagedWorkspace` materializers (Perforce, Lore, ...) to the materializer-based conform (`ConformMaterializersAsync`) instead of the raw managed path.