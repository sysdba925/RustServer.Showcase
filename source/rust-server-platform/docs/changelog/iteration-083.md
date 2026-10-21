# Iteration 083 — First-install Rust and Oxide through Web

## Goal

Make the first Rust Dedicated Server installation possible through the
published Web and Control workflows on a clean Production host, without
SSH commands to install the game, duplicating Docker Compose files, or
inventing new backend operations.

## Added and updated

- `RustServer.Versions` represents absent Steam app manifests and absent
  Oxide assemblies as nullable installed-version fields. Existing corrupt
  files still fail validation; absence is not reported as corruption.
- Published Versions OpenAPI/Markdown contract now represents the nullable
  installed-version fields, while keeping existing routes and operations.
- `RustServer.Web` shows `Not installed` with `Install Rust` and
  `Install Oxide` actions using the existing Control update endpoints.
- The Server screen includes the shared Software panel and accepted-operation
  feedback, reusing the same API calls, operation polling and mutation guards.
- Start is disabled while Rust is not installed; Oxide installation remains
  unavailable until Rust installation is recognized.

## Current capability

When Versions can read available version metadata, an uninstalled Rust/Oxide
runtime is no longer treated as an invalid installed version. The operator
can request installation through Web's existing Control action callbacks.
The existing Control SteamCMD/Oxide Compose operations do the actual work;
successful completion is followed by the established Versions refresh.

## Deliberately deferred

- Production releases of the changed services, deployment to the test VM,
  and live end-to-end installation through Docker remain separate rollout
  steps; a Git commit alone does not update an installed release.
- Versions still requires access to Steam/GitHub to obtain available versions;
  upstream lookup failures remain visible as unavailable requests.
- First-run initialization of private Plugins/Configs Git repositories
  remains separate from Rust and Oxide installation.

## Validation

- Reviewed the current Control and Versions OpenAPI/API contracts and the
  Software, Server, and action-state frontend logic.
- Added Versions provider, reader, and endpoint tests for missing files, and
  Web component and page tests for the first-install workflow.
- GitHub commits and changed-file contents verified.
- Local .NET, npm suite execution, generated-contract verification, and
  disposable-host integration have not been executed from this session.

## Decisions

- First installation is an explicit operator action, not an automatic
  side effect of Bootstrap, Updater, or opening a Web page.
- Versions owns installed/available data; Control owns installation and
  lifecycle; Web owns visibility, action gating, and presentation.
