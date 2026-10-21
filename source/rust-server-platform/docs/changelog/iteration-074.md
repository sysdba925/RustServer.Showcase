# Iteration 074 — Installed Compose runtime assets

## Goal

Remove routine Rust lifecycle dependencies on the `rust-server-platform`
checkout by installing the Compose definitions and required scripts below
`/opt/rustserver/runtime/`.

## Added and updated

- Added Development Bootstrap step 82, which installs the Rust Dedicated,
  SteamCMD, and Oxide Compose/script pairs as root-managed runtime assets.
- Made installation idempotent: a rerun replaces an asset only when its
  content, owner, or mode differs from the versioned definition.
- Switched the generated Control service configuration and direct development
  launcher to absolute Compose paths below `/opt/rustserver/runtime/`.
- Documented the installed layout, its persistent configuration boundaries,
  and the source-checkout independence boundary.

## Deliberately unchanged

- Production Bootstrap, Updater, Control handlers, orchestration, APIs, and
  automated tests.
- `/srv/rust/server`, `/etc/rustserver/rust-server.env`,
  `/etc/rustserver/rcon-password`, and
  `/var/lib/rustserver/control/server-configuration.json`.

## Validation

- Bash syntax, diff whitespace, static installed-layout checks, and the
  checkout-dependency audit are recorded in the Stage 7 change report.
