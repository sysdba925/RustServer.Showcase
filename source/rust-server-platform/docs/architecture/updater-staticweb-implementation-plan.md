# RustServer.Updater StaticWeb — implementation stages

Status: **plan only**. No repository changes, commits, system services, or live deployment are authorized by this document.

Read the [Updater StaticWeb architecture](updater-staticweb-architecture.md) first. Review current `develop`, repository instructions (`AGENTS.md` where present), contracts, tests and Bootstrap before each stage. Maintain `DotNet` compatibility. Record meaningful changes in `rust-server-platform/docs/changelog` following its current template and iteration numbering, not by guessing iteration IDs.

The approved runtime configuration target is
[`runtime-configuration-layout.md`](runtime-configuration-layout.md), with the
verified dependency inventory in
[`runtime-configuration-migration.md`](runtime-configuration-migration.md).
That migration is not authorized by this plan; stages that alter Bootstrap,
systemd, Docker Compose, or service integration must use the canonical layout
and an explicitly approved migration scope.

## Stage 1 — Registration contract, parser and validation

**Repository:** `RustServer.Updater` (plus contract parity requirements documented for Executor).

Tasks: add `ApplicationType` (`DotNet`/`StaticWeb`); keep legacy missing `Type` = `DotNet`; add type-specific fields and safe validation (`Service`+`.dll` vs `EntryFile`); optional per-app `HealthPath`; preserve root paths, duplicate checks and unknown-key rejection. Update DTO serialization and relevant tests. Review `GET /api/applications/registrations` OpenAPI/Markdown impact and regenerate/verify contract using repository tooling if available.

**Acceptance:** old INI and DotNet JSON parse unchanged; valid StaticWeb registration parses; unsafe/unknown/mixed configurations fail; no deployment behavior changes. Stop here and request review of contract shape before Stage 2.

## Stage 2 — Type-specific package validation in Prepare

**Repository:** `RustServer.Updater`.

Tasks: retain GitHub Release discovery/channel, archive naming, download, checksum, extraction limits, link/traversal protections, inventory manifest and staging. Replace DLL-only payload-root detection with type-aware payload-root validation (`.dll` for DotNet; safe `EntryFile=index.html` for StaticWeb), including archive root/single top-level directory. Preserve manifest format or explicitly version it if needed. Ensure staging reuse checks use the same type rules.

**Acceptance:** prepared Web dist archive accepted; missing Web entry rejected; `.dll`-based DotNet preparation unchanged; tampering of inventory rejected; no privileged activation yet.

## Stage 3 — Privileged Executor: activation policies and rollback

**Repository:** `rust-server-platform`.

Tasks: update Executor's independent INI parser to the Stage 1 contract; keep UUID-only socket, root policy authority, inventory snapshot and recopy verification, global lock and transaction recovery. Dispatch `validate_release_directory`, pre/post-activation and readiness by type. DotNet retains stop/start/is-active/HTTP; StaticWeb switches `current` without `systemctl` and verifies intended release via Nginx HTTP. Preserve rollback semantics, no-current cases, and power-loss recovery. Ensure executor installation/upgrade process is explicit and safe for existing hosts.

**Acceptance:** parity tests pass for both parsers; no arbitrary root command/URL injection; DotNet deployment and self-update continue passing; StaticWeb install/update/rollback/recovery succeed; negative readiness triggers rollback with a durable outcome; static operations do not stop Nginx.

## Stage 4 — Platform Nginx and production Bootstrap

**Repository:** `rust-server-platform`.

Tasks: add an idempotent production Nginx installation/configuration step with root pointing to `/opt/rustserver/web/current`, SPA fallback, local-only reliable release-specific readiness, and same-origin reverse proxies matching the Vite development prefixes (`control`, `versions`, `plugins`, `snapshots`). Validate config (`nginx -t`), service startup and route rewrites; maintain strict separation from Vite development bootstrap `92/94` scripts. Avoid modifying existing production INI automatically unless an explicit migration procedure is accepted. Document binding, TLS termination, caching and permissions.

**Acceptance:** valid config and routing tests; SPA deep links work; static assets served from `current`; upstream paths are correctly rewritten; no backend published unexpectedly; no Nginx restart required per Web update; readiness cannot pass on another site or old release.

## Stage 5 — Web release publishing and registration rollout

**Repositories:** `RustServer.Web`, `rust-server-platform`, optionally `RustServer.Updater` docs.

Tasks: inspect Web `AGENTS.md`; design CI/publisher `npm ci` -> tests/checks -> `npm run build` -> package **dist output only** -> `.sha256` -> GitHub Release. Keep asset/tag naming consistent with Updater. Verify `/control-api`, `/versions-api`, `/plugins-api`, `/snapshots-api` at production build time and parity with Nginx routes. Document reviewed `applications.ini` StaticWeb registration, `prepare` -> `install` -> `update` -> `rollback` operator runbook and safe upgrade/migration sequence.

**Acceptance:** test release downloads, passes checks, and is installed from staging without Node on production; API browser paths are correct; operation history and rollback are observable; old DotNet workloads remain healthy.

## Stage 6 — Final end-to-end acceptance / documentation

**Repositories:** relevant owning repos.

Matrix: DotNet first install/update/rollback, Updater self-update, StaticWeb first install/update/rollback, bad `.sha256`, missing entry, malicious tar, staging mutation, invalid/mixed INI, interrupted transaction/reboot, wrong Nginx vhost, HTTP timeout/invalid response, API upstream unavailable, concurrent executor/bootstrap lock. Compare C# and Python INI behavior. Update OpenAPI only for actual public contract differences, document deferrals and changelog.

**Acceptance:** repeatable automated tests plus safe test-environment smoke run; no direct modifications on production as part of testing; no regressions of existing Updater behavior.

## Prompt boundaries

The next prompt should authorize **Stage 1 only**. Do not allow agents to implement all stages in one pass. A future Stage 1 prompt should request: read current AGENTS/contracts; show modified file list; implement parser/model/validation and tests; run targeted/full tests; report API contract consequences; update shared changelog when required; return commit SHA and clean git status. It should expressly forbid Stage 2–6, Nginx, production scripts and root Executor modifications.

**Important sequencing caveat:** Stage 1 and Stage 3 parsers temporarily accept different schemas. Until Stage 3 is deployed and validated, **do not add any StaticWeb entry** to the live root-owned `applications.ini` and do not attempt Web deployment. Legacy DotNet entries remain usable in the meantime.
