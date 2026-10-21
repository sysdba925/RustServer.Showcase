# Server configuration and wipe design

`RustServer.Control` owns two related but independent capabilities:

- Server configuration records the game settings an operator wants on the next
  Rust process.
- Wipe is an asynchronous filesystem operation with an explicit destructive
  plan. It neither changes configuration nor starts Rust.

The generated [API contract](api-contract.md) is the authoritative HTTP
surface. This document records the runtime and filesystem boundaries behind it.

## Desired and effective configuration

`GET /api/server/configuration` returns `desired` and `effective` typed
configuration values. The initial effective value is `null`: Control has not
yet created a Rust process whose settings it can attest.

`PUT /api/server/configuration` changes only `desired`; it does not restart a
running server. Therefore it is normal for `desired` and `effective` to differ.
After Control successfully creates a Rust container and Docker confirms it is
running, Control persists the configuration snapshot supplied to that start as
`effective`.

The initial values and a durable JSON storage location are supplied through
the `ServerConfiguration` options section. Its default and the Development
Bootstrap service unit use
`/var/lib/rustserver/control/server-configuration.json`, so desired/effective
state survives Control updates and removal of the source checkout. Bootstrap
preserves an existing canonical file as authoritative; otherwise it atomically
copies the existing checkout-local JSON without changing its contents and
retains that legacy file as a recoverable backup. The Control service owns the
canonical file and can create a same-directory temporary file before
atomically replacing the JSON when it saves state.

On an installation without either JSON file, the platform seeds the initial
desired values from the existing Compose `.env` settings so the first
Control-managed start preserves the established server settings.

The public model is intentionally limited to `hostname`, `description`,
`maxPlayers`, `tickRate`, `saveInterval`, `seed`, and `worldSize`. Ports,
identity, RCON, log settings, and process flags remain deployment
infrastructure. At start Control translates the typed model to the private
Compose variables used by the current platform (`RUST_SERVER_*`), and the
Compose start script translates those values to Rust settings. The API never
chooses whether a field is supplied by a command-line argument, server cfg, or
another future implementation mechanism.

## Wipe lifecycle

`POST /api/server/wipe` accepts a semantic `WipePlan` and returns an operation
identifier. It uses the same operation store, worker discipline, and server
mutation lock as Rust and Oxide updates. The server must be stopped both when
the plan is accepted and again when the worker obtains the lock. Control never
stops Rust automatically.

The operation report exposed by `GET /api/operations/{id}` includes a
path-free `wipeResult` after execution. Each target reports its requested
action, actual outcome, number of affected items, and a sanitized error where
applicable. A failed target stops later destructive targets; later targets are
reported as `notExecuted` rather than being attempted unexpectedly.

Wipe does not create or restore snapshots, update Rust/Oxide/plugins, start or
restart the server, or perform blue-green deployment. Configuration updates and
wipes are separate operations, so an operator can update a seed and world size,
wipe only the world, and then explicitly start Rust.

## Current safe targets

The configured `RustServerControl:RustServerDataPath` is the host directory
mounted as `/server`; `RustServerControl:RustServerIdentity` is a simple,
deployment-owned identity name. Control resolves the identity directory only as
`<data path>/server/<identity>`. It rejects empty, root, non-absolute, or
traversing values before any destructive action.

The initial target mapping is intentionally narrow:

| Semantic target | `delete` removes | `keep` does |
| --- | --- | --- |
| `world` | Top-level identity files ending in `.map`, plus `.sav` and numeric `.sav.N` save backups | Nothing |
| `blueprints` | Top-level identity `player.blueprints` stores, including known versioned `.db` and SQLite `-wal`, `-shm`, and `-journal` companions | Nothing |

This mapping follows [Rust's documented identity layout](https://wiki.facepunch.com/rust/Getting-Started_w-Server):
map and save files contain the generated world and its entities, while the
player-blueprint store is separate. It deliberately does not claim that buildings, inventories,
players, admins, bans, player states, signs, companion data, or every possible
Rust database can independently be deleted. Those files remain unknown data
and are kept.

The engine enumerates only the identity directory's top-level regular files,
matches known target filenames, revalidates each candidate is inside the
configured directory, and deletes individual files only. It neither accepts
filesystem paths from clients nor recursively deletes a directory. Reparse
points are kept.

## Oxide and future plugins

Wipe never enumerates or changes `oxide/plugins` or `oxide/config`.
`oxide/data` is also left untouched by default; unknown plugin data is always
kept. A future plugin can register one trusted semantic data target with an
`IPluginDataWipeHandler`. The public plan then names that registered target and
uses `keep`, `delete`, or `transform`; it still cannot provide a path. The
handler owns its plugin's file format and transforms, while the generic engine
only dispatches and reports its result.
