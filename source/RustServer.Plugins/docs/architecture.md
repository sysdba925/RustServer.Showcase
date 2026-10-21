# RustServer.Plugins — Application Architecture

**Status:** Current approved and implemented architecture

**Scope:** Structure and interaction rules of the `RustServer.Plugins` backend.  
**Working directory:** `E:\Projects\rust\RustServer.Plugins`  
**Related specifications:** `requirements.md`, `domain-model.md`, `operations.md`, `use-cases/`, `api-contract.md`.

## 1. Decision

Use **Feature-Based Architecture + Command Handlers + Orchestration + Ports & Adapters**, with Dependency Injection (DI) and explicit concurrency/precondition guards.

`RustServer.Plugins` is one deployable ASP.NET Core microservice within RustServer Platform. It owns management of Rust plugins, their files/configurations, releases, updates, and version information. It may contain several cohesive feature modules; a feature is a **functional capability**, not an individual API endpoint or command.

The first feature, provisionally named **PluginManagement**, contains the complete Git/source-to-runtime management workflow: fetch, Git Sync, Git status, releases, baseline, runtime diff, preview, apply, commit+push, revert, and source/release switching. These are related commands/use cases, **not** separate features. Future independent capabilities may include `PluginUpdater` and `PluginVersions` (names not hard external contracts).

No mandatory global `Domain / Application / Infrastructure / Api` project split, no mandatory mediator library, and **no state machine**. Avoid layer proliferation and artificial duplication of models. Implement one ASP.NET Core application project and one tests project initially; only split assemblies for demonstrated reasons.

## 2. Proposed repository layout

```text
E:\Projects\rust\RustServer.Plugins\
├── RustServer.Plugins.sln
├── src/
│   └── RustServer.Plugins/
│       ├── RustServer.Plugins.csproj
│       ├── Program.cs                    # Application composition root
│       ├── Features/
│       │   ├── PluginManagement/
│       │   │   ├── Registration.cs        # Registers this feature with DI
│       │   │   ├── Controllers/           # HTTP entry points; thin mapping
│       │   │   ├── Commands/              # One handler per mutating use case
│       │   │   │   ├── FetchGit/
│       │   │   │   ├── GitSync/
│       │   │   │   ├── ApplySource/
│       │   │   │   ├── CommitRuntime/
│       │   │   │   └── RevertRuntime/
│       │   │   ├── RemoteGit/             # Remote repository-facing capability
│       │   │   ├── LocalGit/              # Local workspace-facing capability
│       │   │   ├── Baseline/              # Persistent applied-state capability
│       │   │   ├── Runtime/               # Managed Rust server files capability
│       │   │   ├── Guards/                # Exclusive operation + preconditions
│       │   │   └── Models/                # Only genuinely cross-cutting feature models
│       │   ├── PluginUpdater/            # Future feature; do not scaffold prematurely
│       │   └── PluginVersions/           # Future feature; do not scaffold prematurely
│       └── Infrastructure/               # Only shared technical adapters if warranted
├── tests/
│   └── RustServer.Plugins.Tests/
├── docs/
│   ├── architecture.md
│   └── ...
├── old/                               # Reference only; never part of new build
└── AGENTS.md
```

**Skeleton rule:** Create only folders/projects needed for the first milestone, rather than generating every illustrative folder/class immediately. `Models/` and shared `Infrastructure/` are optional, not dumping grounds.

## 3. Feature boundary and namespaces

Organize by cohesive features first, and by owned technical locations/capabilities **inside** `PluginManagement`:

- `...Features.PluginManagement.Commands`: user-directed workflows.
- `...Features.PluginManagement.RemoteGit`: remote Git operations and related models.
- `...Features.PluginManagement.LocalGit`: local Git workspace operations and related models.
- `...Features.PluginManagement.Baseline`: persistence of the last successfully applied source.
- `...Features.PluginManagement.Runtime`: managed files physically present on the Rust server.
- `...Features.PluginManagement.Guards`: operation exclusion and execution preconditions.

These are **not state-machine states**, and they do not transition themselves between phases. They are components representing distinct sources/locations of data and their allowed operations. **Service status** (running/stopped) is not a PluginManagement operation precondition; the feature manages Oxide files independently of the Rust process.

Models owned by a component live beside it; workflow request/result/report types live beside their command. `Models/` contains only models genuinely shared throughout this feature. A different feature may consume a small, explicitly exposed public contract and its model from `PluginManagement`; don't duplicate models purely for theoretical isolation. Do not expose private internals or make another feature depend on a concrete implementation.

## 4. Roles and responsibilities

| Part | Responsibility | Must not do |
|---|---|---|
| Controller | Validate/map HTTP transport, call handler or read capability, return DTO/error | Coordinate multi-step Git-to-runtime transitions |
| Command handler (orchestrator) | Execute a documented operator use case in explicit order, call guards/capabilities, produce report | Implement Git CLI, filesystem internals, HTTP scraping |
| RemoteGit | Fetch, inspect remote branches/HEAD, validate remotes, push | Write runtime files or update baseline |
| LocalGit | Local branches/HEAD, workspace status, checkout, stage/commit | Initiate source apply or baseline changes |
| Baseline | Read/write persistent description of last successful apply | Decide to apply or checkout |
| Runtime | Inspect/diff/write/remove *managed* Rust files | Pick remote branch or mutate Git |
| Guards | Exclusive execution and Git/Runtime safety preconditions (not Rust lifecycle) | Perform the workflow itself |
| Registration | Wire interfaces, implementations, options, handlers and guards in DI | Contain business workflows |

A capability must not secretly call another capability to perform the full use case. The coordinating command owns the cross-component sequence.

## 5. Command Pattern + Orchestration

A **command** is an operator-requested action such as `ApplySource` or `CommitRuntime`; its **handler** orchestrates the action. Orchestration means sequencing existing capabilities, enforcing checks, handling failure/rollback and returning an operation report. It is **not** a separate service framework or a generic workflow engine.

Example: `ApplySource` (simplified; detailed rules are in `operations.md`):

```text
HTTP POST /api/source/apply
          |
          v
ApplySourceHandler    <-- workflow owner / orchestrator
  1. acquire exclusive mutation guard (fail on conflict)
  2. validate source and Git/Runtime safety independently of Rust lifecycle
  3. RemoteGit: fresh fetch + remote validation for Plugins and Configs
  4. LocalGit: validate and checkout/update both workspaces
  5. Runtime: determine exact managed-file changes; apply add/modify/delete
  6. Baseline: persist last successfully applied source only on success
  7. return detailed report (including failures, final state, rollback)
```

The handler knows **when** and **why** to call each component. Each component knows **how** to perform an operation on its own data. A shared operation report describes the observed result; a diff is a comparison, not a source of truth.

**Illustrative C# (API names are examples, not a replacement for `api-contract.md`):**

```csharp
public sealed class ApplySourceHandler(
    IOperationGuard guard,
    IRemoteGit remoteGit,
    ILocalGit localGit,
    IRuntimeFiles runtime,
    IBaselineStore baseline)
{
    public async Task<OperationReport> HandleAsync(
        ApplySourceCommand command, CancellationToken ct)
    {
        // Acquire must reject conflicting mutations, not silently queue them.
        await using var lease = await guard.TryAcquireOrThrowAsync(ct);

        // Pseudocode: each step may return structured results used in reports.
        var remote = await remoteGit.FetchAndValidateAsync(ct);
        var workspace = await localGit.PrepareSourceAsync(command.Source, remote, ct);
        var changes = await runtime.PreviewAsync(workspace, ct);

        // Real implementation must use a failure-safe apply/rollback protocol.
        await runtime.ApplyAsync(changes, ct);
        await baseline.SaveSuccessfulApplyAsync(workspace, ct);

        return OperationReport.Success(changes);
    }
}
```

**Important:** This snippet illustrates architectural collaboration only. It does **not** implement atomicity, error recovery, cancellation cleanup, or the full report contract. Before implementing the mutating command, design its rollback/partial-failure behavior against `operations.md` and tests. It is incorrect to mark success if baseline persistence fails after runtime changes.

Read-only use cases may use a query/read service or controller-to-capability call; don't force them into mutating `Commands/` merely for uniformity. Add `Queries/` only if read-side complexity warrants it.

## 6. Ports & Adapters, without technical-folder proliferation

A **port** is an interface expressing what a handler needs. An **adapter** is a concrete implementation providing the functionality through Git CLI, files, JSON, HTTP etc. They belong near the capability they serve; there is no requirement for top-level `Ports/` and `Adapters/` directories.

```text
PluginManagement/LocalGit/
├── ILocalGit.cs       # Port (capability contract)
├── LocalGit.cs        # Adapter (e.g. invokes Git CLI)
└── LocalGitInfo.cs    # Local workspace model
```

```csharp
// LocalGit/ILocalGit.cs — port
public interface ILocalGit
{
    Task<string> GetHeadAsync(CancellationToken ct);
}

// LocalGit/LocalGit.cs — adapter (schematic)
public sealed class LocalGit(IGitProcess git) : ILocalGit
{
    public Task<string> GetHeadAsync(CancellationToken ct) =>
        git.ExecuteForOutputAsync("rev-parse HEAD", ct);
}

// Commands/ApplySource/... — consumer
public sealed class ExampleHandler(ILocalGit localGit)
{
    public Task<string> InspectHeadAsync(CancellationToken ct) =>
        localGit.GetHeadAsync(ct);
}
```

The Git process runner (`IGitProcess`) is a technical detail; share it across `RemoteGit` and `LocalGit` only if genuinely useful. Prefer interfaces at real boundaries and for testability, **not for every class**. Register concrete types centrally through the feature's `Registration.cs` (invoked by `Program.cs`).

## 7. Guards, concurrency, and server lifecycle

Mutating commands that touch the shared workspaces, baseline, or managed files must not run concurrently. Use one feature-level **exclusive operation guard** around the *entire* mutation, including any rollback/final-report capture. If busy, return a conflict response (e.g. `409 OperationInProgress`), not an implicit queue. A bare `SemaphoreSlim.WaitAsync()` without nonblocking rejection does not satisfy this behavior.

Separate:

1. **Concurrency guard:** another incompatible operation is already executing.
2. **Preconditions:** source allowed in environment, remotes valid, release complete, workspace safe, etc.; Rust running/stopped is irrelevant.

Checking preconditions alone is not enough: two requests can pass the checks at the same time. Acquire guard first, then revalidate mutable conditions **inside** the exclusive scope. Permit managed-file and Git mutations while Rust runs; never infer plugin reload success from file changes. Initial in-process exclusion assumes a single service instance; multiple instances require cross-process locking.

Guard release must be guaranteed on exceptions/cancellation. For operations partially modifying disk/Git, record their resulting state and attempted rollback; a lock does not itself make writes atomic.

## 8. Invariants from existing specs

- There are **two repositories**: Plugins and Configs.
- Distinguish Remote Git, Local Git Workspace, Server Baseline, Runtime Files; never merge them into one generic state.
- Allowed environment/source combinations: Staging + Development, Staging + Release, Production + Release. Never Production + Development.
- A release is complete only when the same `release/<version>` branch exists in **both** repos; incomplete releases must be blocked from apply.
- Hotfixes go directly to the current release branch, according to the documented workflow.
- Fresh remote verification/fetch is required before documented Git mutations or applying Git state.
- Runtime commit + push is one operator operation; Production cannot commit.
- PluginManagement Git and managed Oxide Runtime operations can run regardless of Rust lifecycle state; no automatic Rust restart/reload is performed.
- Apply makes the managed runtime file set **exactly** match the chosen source, including deletions/renames (at least delete+add), with coherent checkout/apply/baseline behavior.
- Missing baseline at first start is `Not initialized`, not an exceptional failure.
- Mutating operation reports must be detailed and diagnosable, including phase, repository/file, errors, completed steps, rollback and final state.

Where this document is illustrative, **behavioral specifications and API contract remain authoritative**. Resolve discrepancies in docs before implementation.

## 9. DI registration and entry points

```csharp
// Program.cs (schematic)
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddApiDocumentation(builder.Configuration);
builder.Services.AddPluginManagement();
var app = builder.Build();
app.UseApiDocumentation();
app.MapControllers();
app.Run();
```

```csharp
// Features/PluginManagement/Registration.cs (schematic)
public static class Registration
{
    public static IServiceCollection AddPluginManagement(this IServiceCollection services)
    {
        services.AddScoped<ApplySourceHandler>();
        services.AddSingleton<IOperationGuard, OperationGuard>();
        services.AddScoped<IRemoteGit, RemoteGit>();
        services.AddScoped<ILocalGit, LocalGit>();
        services.AddScoped<IBaselineStore, BaselineStore>();
        services.AddScoped<IRuntimeFiles, RuntimeFiles>();
        return services;
    }
}
```

Lifetimes above are **illustrative**; choose actual lifetimes based on resource safety, thread-safety, and ownership. The feature registration establishes where the feature is composed; `Program.cs` stays thin.

### 9.1 Stage 02 technical configuration

Stage 02 introduces only the technical `ApiDocumentation` configuration
section under `Infrastructure/OpenApi`. It supplies the OpenAPI document
metadata and is not a PluginManagement domain configuration:

```text
ApiDocumentation:Title
ApiDocumentation:Version
```

Both values are required, must contain non-whitespace text, and are validated
at application startup through the standard Options mechanism. The checked-in
application defaults are `RustServer.Plugins API` and `v1`; standard ASP.NET
Core configuration providers may override them. Validation is deterministic
and side-effect free: it performs no Git, workspace, runtime, baseline, or
filesystem operation.

No Git, Rust-server, baseline, environment, or persistent-storage setting is
introduced until the capability that owns it is implemented and documented.

### 9.2 Stage 04 PluginManagement configuration

Stage 04 introduces the first PluginManagement-owned configuration:

```text
PluginManagement:StateRootPath
```

It is required and identifies the service-owned root for persistent state. A
local development default may be relative to the application working directory;
deployment Bootstrap must provide the canonical absolute path
`<WORKSPACE_ROOT>/state/RustServer.Plugins` through
`PluginManagement__StateRootPath`.

The Environment persistence implementation, its store contract and persistence-state exception are located in `Infrastructure/EnvironmentState/` using namespace `RustServer.Plugins.Infrastructure.EnvironmentState`. The folder is deliberately not named `Environment`: a nested `...PluginManagement.Environment` namespace shadows `System.Environment` in unrelated feature code. Business Environment models and commands remain in `Features/PluginManagement`. This is a narrowly scoped naming/persistence placement decision, not a global layer split.

The Environment capability owns `environment.json` directly beneath this root.
On first start its absence means `Staging` and a read-only request does not
create it. The store uses a temporary file in the same directory followed by a
replacement of the target file. An unreadable file or an unknown persisted
value is a diagnostic error, never an implicit reset to `Staging`.

### 9.3 Stage 05 LocalGit configuration

Stage 05 adds the two Local Git workspace paths owned by PluginManagement:

```text
PluginManagement:Repositories:Plugins:WorkspacePath
PluginManagement:Repositories:Configs:WorkspacePath
```

Both paths are required. Deployment Bootstrap supplies the canonical existing
workspaces through `PluginManagement__Repositories__Plugins__WorkspacePath`
and `PluginManagement__Repositories__Configs__WorkspacePath`; it does not
create, synchronize, or otherwise mutate Git repositories in the service
installation step.

`LocalGit` observes only local worktree and ref information: repository
validity, branch, HEAD, working-tree cleanliness, configured tracking branch,
and the ahead/behind relation available from existing local refs. It does not
fetch, discover remote branches, authenticate, checkout, commit, push, or
repair a workspace. Expected local problems are returned as diagnostic
snapshots for the future Git-status workflow.

### 9.4 Stage 06 RemoteGit and Fetch

`origin` is the canonical remote managed by PluginManagement. The remote URL
must not embed credentials and must never be returned by the HTTP API.

**GitHub credential ownership (approved target; staged integration):** The host
source `/etc/rustserver/plugins-github-token` is separately provisioned from
Updater's `/etc/rustserver/updater-github-token`. Bootstrap supplies the Plugins
source via systemd `LoadCredential` and sets
`RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE` to the systemd credential copy's path.
The `IGitHubTokenProvider` port / `SystemdGitHubTokenProvider` adapter under
`PluginManagement/Git` reads this file on demand, with explicit non-secret
errors on missing/invalid credentials; it must not log or store the value.
The credential is intended for read/write access exclusively to the Plugins
and Configs GitHub repositories. Both sources may initially contain the same
value, but are configured and rotated independently.

**Stage 02 — authenticated Git transport:** Only pinned HTTPS origins for
`sysdba925/RustServer.PluginFiles` and `sysdba925/RustServer.ConfigFiles`
use a short-lived Git askpass helper. The helper reads the Plugins credential
copy pointed to by `RUSTSERVER_PLUGINS_GITHUB_TOKEN_FILE`; the token value is
not written to the helper, configuration, command line, or process environment.
Git credential helpers and HTTP redirects are disabled for these calls. Unknown
HTTPS origins are rejected. Existing SSH and local origins retain their
previous authentication until the explicit migration stage.

**Stage 03 — workspace ownership:** `GitWorkspaceInitializer` is a
PluginManagement remote-Git capability. When
`PluginManagement:WorkspaceInitialization:Enabled` is true, a hosted
initialization task verifies existing checkout roots and origins without
changing their refs or worktree contents. It creates missing parent
directories, clones Configs and then Plugins from canonical HTTPS GitHub
URLs at `develop`, stages each clone in a unique temporary sibling, and
moves it into a missing/empty workspace only on success. Nonempty foreign
directories, symlink targets and invalid repositories are rejected. A
failure never becomes an automatic reset or deletion of operator content;
status/read APIs remain available.

**Stage 04 — provisioning:** Production Bootstrap creates the independent
root-owned `0600` Plugins token file on first provision, preserving an
existing file byte-for-byte. It may copy the existing Updater token *value*
into that separate file or accept an explicitly supplied token. Production
uses a Plugins systemd credential drop-in; Development generates equivalent
`LoadCredential` settings in its service unit when a token is available.
The two token files may be rotated independently. Bootstrap's development
workspace-sync steps still exist until the later ownership-cutover stage.

**Stage 01 boundary (historical):** Registering the credential reader alone does not
authenticate `git fetch` or `git push`. Stage 02 provides secure Git transport integration. Git workspace
initialization/clone and Bootstrap wiring are still separate pending tasks.
The existing development Bootstrap workspace-sync steps must be removed or
reassigned as part of that later migration, not silently assumed absent.

Stage 06 introduces a small concrete `GitProcess` component under
`PluginManagement/Git`. `LocalGit` and `RemoteGit` use it for explicit Git CLI
invocation, while keeping their own local- and remote-state interpretation in
their respective capabilities. The component is not a generic shell framework
and does not require an interface until a real alternative implementation is
needed.

`RemoteGit` fetches only canonical `origin`, reads existing remote-tracking
refs and pushes an explicitly selected checked-out supported branch.
`FetchGitHandler` fetches Plugins and Configs in order and does not roll back
a successful first Fetch if the second fails. It never updates a local branch,
checks out, merges, rebases, resets or pushes. `GitSyncHandler` owns the
separate one-repository Sync orchestration: after a fresh Fetch it validates
the current supported branch and `origin/<branch>` upstream, then performs
only the action implied by the local/remote relation. `LocalGit` provides the
explicit fast-forward and regular merge primitives; it does not decide the
workflow. Merge conflict cleanup is visible in the operation report and never
uses a destructive reset. `GET /api/git/status` only reads the available local
and remote-tracking state; it never starts a Fetch.

### 9.5 Stage 08 Baseline persistence

The Baseline capability owns `baseline.json` directly beneath the existing
`PluginManagement:StateRootPath`, alongside `environment.json`; it introduces
no new root or configuration key. `IBaselineStore` is the capability boundary
used by read endpoints and future Apply orchestration. Its file adapter uses a
same-directory temporary file followed by replacement when saving.

An absent baseline file is the normal `Not initialized` first-start state and
is not created by a read. A malformed, incomplete, inconsistent, or otherwise
invalid baseline is a diagnostic `BaselineStateCorrupted` state; it is never
silently reset or deleted. At this stage the store offers saving for the future
Apply handler, but no current workflow writes a baseline.

### 9.6 Stage 09 Runtime file access

Stage 09 introduces the required `PluginManagement:Runtime:ServerRootPath`
configuration. Deployment Bootstrap passes the canonical existing Rust server
root from `RUST_SERVER_PATH`; it does not create Runtime files, copy repository
files, or add a second source of truth for this path. `Runtime` owns byte-safe
enumeration, reading, writing, deletion, availability checks, and the explicit
repository-path to logical-runtime-path mapping. It does not choose a source,
inspect Git branches, fetch, make baseline decisions, or orchestrate Apply.

The managed filesystem boundary is `<ServerRootPath>/oxide`. The capability
accepts only recognized mappings and rejects traversal, rooted input, and any
managed path containing a symlink/reparse point. Plugins maps only top-level
`.cs` files to `oxide/plugins`; Configs maps only `config`, `data`, `lang`,
`discord`, `oxide.config.json`, and `discord.config.json` to their explicit
oxide destinations. Plugin archives and repository/tooling content are not
Runtime files.

`SystemStateReader` is a small read capability, not a state machine: baseline
absence is `uninitialized`; a corrupted baseline or unavailable/degraded Git
or Runtime is `degraded`; only a valid baseline with ready dependencies is
`ready`. It performs no Fetch, Git mutation, or filesystem mutation.

### 9.7 Stage 10 Runtime Diff

`RuntimeDiffReader` is a read component owned by PluginManagement. It combines
Baseline, LocalGit object reads at the two saved commit SHA, and Runtime files
to classify `added`, `modified`, and `deleted` managed files. It never treats
current workspace/remote HEAD as baseline content and never checks out,
fetches, or changes Git state. Git blobs and Runtime files are read as bytes
so binary files remain safe to compare. Missing Baseline is the normal
`notInitialized` result; missing baseline Git objects or Runtime availability
are explicit diagnostics rather than an empty diff.

### 9.8 Runtime Changes notifications and content

`PluginManagement/RuntimeChanges` groups the read-side Runtime change
capabilities: `RuntimeChangeWatcher`, `RuntimeChangeNotifier`,
`RuntimeDiffReader`, and `RuntimeDiffContentReader`. This is a module inside
PluginManagement, not a feature or state machine. It reuses the existing
Runtime, Baseline, LocalGit and `ManagedFileMapping` capability boundaries.

The watcher observes only managed filesystem areas and sends an invalidation to
the notifier after debounce/coalescing. The notifier holds only live SSE
subscriptions; it persists no event or diff state. It neither computes nor
caches a diff. A watcher overflow is treated as an invalidation, so a client
may refresh; it cannot affect the correctness of a later full Runtime Diff.

`RuntimeDiffContentReader` resolves a requested managed identity, then reads
only that entry's baseline side from the saved Baseline SHA and its Runtime
side. It remains read-only and has no command handler.

### 9.9 Stage 11 Command Execution Guards

`Guards` owns one in-process, feature-level exclusive guard for every
PluginManagement mutation. Its non-blocking acquire returns a small lease; a
busy guard rejects the command rather than queueing it. A command holds the
lease across its complete mutation, cleanup, rollback, and final-state
capture. PluginManagement mutation handlers do not request or require Rust lifecycle
state. The exclusive guard protects shared Git workspaces, Baseline and
managed Oxide files independently from RustServer.Control availability.

### 9.10 Stage 12 Operation Reporting

`Operations` is a small persistent reporting capability. A command handler
starts and updates its own report while retaining ownership of every workflow
step; reporting never invokes Git, Runtime, Baseline, or a command itself.
Reports retain phase, completed/failed/not-completed steps, failure context,
rollback result, timestamps and final state supplied by the handler. The
capability persists complete reports in SQLite through a narrowly scoped
`IOperationHistoryStore`, not a generic repository or unit-of-work layer.

The SQLite adapter owns schema initialization/versioning, WAL, parameterized
queries, and a `(startedAt, operationId)` pagination index. Its configurable
database path is outside the publish directory. It stores a compact indexed
summary with the complete report JSON, supports read-by-ID and cursor history
queries, and marks records left `inProgress` after a restart as diagnostic
`interrupted` records. Persistence failure is visible to the command: a report
must be durably registered before mutation starts and a failed final write must
not be returned as success. Fetch records its actual final LocalGit snapshots
without rolling back already fetched remote-tracking refs.

### 9.11 Commit Runtime metadata and author configuration

Commit Runtime uses these PluginManagement settings:

```text
PluginManagement:GitCommitAuthor:Name
PluginManagement:GitCommitAuthor:Email
```

The checked-in defaults are `Rust Server Platform` and `rustserver@local`.
Both settings are required, validated through standard Options at application
startup, and can be overridden through normal ASP.NET Core configuration,
including `PluginManagement__GitCommitAuthor__Name` and
`PluginManagement__GitCommitAuthor__Email` environment variables.

After the command begins its operation report, `CommitRuntimeHandler` composes
the Git commit message: it preserves the operator message and appends fixed
operation, environment and source trailers using the report ID and validated
command state. The handler passes this metadata and configured identity to
`LocalGit` for every affected repository. `LocalGit` executes the Git commit
with command-scoped identity configuration; it does not write global or
repository Git configuration and does not own metadata derivation. Only after
every affected repository has successfully pushed does the handler persist a
new Baseline with the exact created commit SHA for each affected repository.
An unaffected repository keeps its previous SHA. Push, partial-push, or
Baseline-persistence failure leaves Baseline unchanged.

## 10. Tests and implementation order

- Keep tests small and readable: protect key behavior, dangerous invariants,
  and representative feature usage rather than framework plumbing or coverage.
  Destructive Git/runtime operations, guards, and baseline consistency require
  focused tests when introduced.
- Test command orchestration using fake ports: expected call sequence, guard refusal, precondition refusal, failed fetch/checkout/apply/baseline, rollback and report.
- Test each adapter separately with controlled temp directories / disposable Git repos as appropriate.
- Test actual HTTP contract and error mapping. Verify the build and full applicable test suite.
- Start with a new solution and minimal executable skeleton **without business behavior**, then implement domain types/read-only paths before dangerous file/Git mutations.
- Do not copy old services by default. `old/` is reference only and must remain out of the build.

## 11. Platform integration and documentation workflow

Before any implementation or contract change, read `AGENTS.md`, then the relevant `docs/` specifications. Update specs before code when behavior changes.

Review sibling `rust-server-platform/docs/changelog/` for meaningful architecture/contract/deployment/operator workflow changes. Review platform Bootstrap when project layout, assembly/executable name, publish paths, config/env vars, permissions, service registration or validation may change. Record either the required integration changes or explicit confirmation that none are required.

**For agents:** Do not replace this feature-first design with a four-assembly Clean Architecture template, a collection of unrelated endpoint services, a state machine, or a mediator framework without an explicit architectural decision and documentation update.
