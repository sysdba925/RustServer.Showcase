# UI information architecture

The current product shell has four implemented top-level areas. Its current
navigation shows only implemented destinations; it does not advertise
non-functional destinations.

| Area | Route | Responsibility |
| --- | --- | --- |
| Overview | `/` | Read-oriented server summary, attention context, shortcuts, software and known update activity. |
| Server | `/server` | Rust Dedicated runtime lifecycle controls and detailed startup tracking. |
| World | `/world` | Stable domain boundary for future world data, snapshots, wipe, and restore. |
| Plugins | `/dashboard/plugins` | Git-backed plugin-management workflows and the independent Plugin Catalog. |

Plugins is a planned, currently-backed top-level product area. Its public
backend contract already supports operator workflows around Environment,
Source, Development, Releases, update packages, and runtime changes. This
document does not choose its route, navigation treatment, page count, tabs,
cards, or component tree; those are subsequent UI-design decisions.

## Responsibility boundaries

Product-area ownership is deliberately separate:

| Area | Responsibility |
| --- | --- |
| Plugins | Manages Git-backed plugin/config source, release/development deployment, package update workflow, runtime changes, and the logical Plugins Environment. |
| Plugin Catalog | `/dashboard/plugins/catalog` and `/dashboard/plugins/catalog/:pluginId` manage known catalog metadata, sources, and media. They never state or change installed, deployed, or running status. |
| Server | Rust lifecycle. |
| World | Wipe, future snapshots, and world operations. |

`Staging` and `Production` are logical Plugins Environment modes, not Rust
lifecycle states. Plugins neither starts, stops, nor restarts Rust.

Overview is an operator summary, not a second operational page. It presents the
derived server state and a `Manage server` shortcut, but never Start, Stop, or
Restart controls. While the server is Starting, it can show a compact startup
step count. It does not display startup milestone labels or a completed
milestone list.

Server owns the lifecycle view: Running and Ready source values, derived
runtime state, Start/Stop/Restart availability, lifecycle feedback, and the
full ordered startup milestone presentation. Startup labels and order remain
provided by the Control schema; snapshot state remains positional and is never
reconstructed from logs.

World is intentionally a lightweight placeholder. Current public APIs do not
provide a current world, seed, map size, snapshots, backups, wipe, or restore,
so this route offers no implied controls or data.

Software and known update Activity remain on Overview in this iteration.
There is still no durable operation-history route because the Control contract
does not offer operation discovery or history.

## Shell behavior

The persistent shell has a header, sidebar, and routed main content. Its primary
navigation is exactly `Overview`, `Server`, and `World`; active styling is based
on the exact route so Overview is not also active on Server or World.

Control-plane request, polling, and mutation state is application-shell scoped.
Navigating between Overview and Server preserves the one authoritative resource
set rather than starting independent lifecycle or startup pollers.

## Deferred extension points

These areas remain future work and must not appear as non-functional
destinations: Players, Configuration, Economy, Automation, Logs, Console,
persistent activity history, notifications, metrics, and world-management
actions. Plugins is excluded from this list because its backend capability is
currently published; its UI representation remains a separate design and
implementation step.
