# Architecture

## Current responsibility

`RustServer.Versions` owns discovery and comparison of version state for the
Rust Dedicated Server and Oxide.Rust. It reports:

- installed state;
- running state, when the server is available;
- available state from the relevant external source; and
- whether an update is available.

This service is responsible for Rust versions and Oxide versions. It does not
perform software or plugin updates itself.

## Future work

### Plugin update discovery

In a future iteration, `RustServer.Versions` should also discover and compare
versions of installed Rust/Oxide plugins. This is discovery and
version-checking functionality only; it must not automatically modify plugin
files, create Git commits, or deploy changes.

#### Initial discovery

An operator should be able to initiate a scan of `oxide/plugins/`. The scan
should discover installed plugin files and, where possible, obtain metadata
such as the plugin name, installed version, author, and other useful identity
data. For C# plugins, version information may be available in plugin metadata
such as `[Info(...)]`. The metadata parser is intentionally not designed yet.

#### Source mapping

For each plugin, an operator should be able to register its update source once,
for example the URL of its plugin page. A conceptual mapping is:

```text
RaidableBases
installedVersion: 3.0.5
sourceUrl: https://...
sourceType: ...
```

The mapping must persist. At minimum it should retain plugin identity, source
URL, source type, and enabled/disabled state. The persistence technology is a
future design decision; SQLite is a possible option but is not prescribed.

The service may also persist the most recent check result, including installed
version, available version, last-checked timestamp, and status or error. A
complete history of checks is not required.

#### External version sources

Available-version discovery should use source-specific adapters or parsers,
because external sites expose different page and API structures. Conceptually:

```text
IPluginVersionSource
├── SourceA
├── SourceB
├── SourceC
└── ...
```

One adapter serves a site or source, not an individual plugin. For example, 50
plugins hosted on one site should use its shared adapter and parsing logic.

#### Check updates operation

An operator should be able to run a `Check Plugin Updates` operation. It would:

1. load registered plugins;
2. group them by source;
3. retrieve the relevant external pages or API data;
4. extract available versions;
5. compare them with installed versions; and
6. return a state for every plugin.

For example:

```text
Plugin          Installed   Available   Status
RaidableBases   3.0.5       3.0.7       UpdateAvailable
Kits            4.1.2       4.1.2       Current
XDQuest         2.3.0       unknown     CheckFailed
```

Checks across many external pages must respect external-site rate limits and
must not issue unbounded concurrent requests. The exact concurrency and
rate-limit policy is intentionally deferred.

#### Boundary with `RustServer.Plugins`

The ownership boundary is explicit:

```text
RustServer.Versions
→ discovers that a newer plugin version exists

RustServer.Plugins
→ later obtains and applies an update package, performs inspection and diff,
  creates a Git commit, and deploys it
```

`RustServer.Plugins` remains responsible for upload, inspection, diff, Git
commit, deployment, runtime changes, and release. No automatic call between
the two services is designed at this stage.

A future web workflow may present an available update from Versions, then let
the operator choose an update action handled by the Plugins workflow. This is a
future UX concern, not a service integration design.

#### Out of scope for the current implementation

This document does not introduce a database, plugin scanning, metadata parsers,
HTTP scraping, scheduled checks, API endpoints, automatic downloads,
integration with `RustServer.Plugins`, or a React UI.
