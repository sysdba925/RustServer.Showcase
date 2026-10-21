# Iteration 081 — Installer-provisioned Auth credentials

## Goal

Make a clean Production installation usable over SSH without manually creating
or editing `/etc/rustserver/auth.env`, while preserving the canonical file and
the existing deployment security boundary.

## Added and updated

- The Production Installer now collects missing Bootstrap and Updater GitHub
  tokens plus Google and first-admin Auth credentials in one interactive flow.
- Secret input acknowledges every typed or pasted character with `*`, handles
  Backspace, and never renders the original value.
- The Installer passes Auth credentials only through a mode-`0600` temporary
  handoff inside its existing secure workspace. Bootstrap validates that
  handoff and atomically creates `/etc/rustserver/auth.env` as
  `root:rustserver-auth`, mode `0640`.
- Existing valid Auth and Updater canonical credentials remain authoritative.
  Invalid existing Auth configuration now fails clearly instead of being
  replaced.
- Updated Installer, Bootstrap, architecture, and operator documentation to
  establish Installer-owned first-run Auth provisioning.

## Current capability

A fresh interactive Production installation provisions the one canonical Auth
environment file without putting credentials in command-line arguments,
systemd units, normal Installer output, or an additional persistent file.
Deployment Executor and Auth continue to consume or validate the canonical
file only.

## Deliberately deferred

- Auth release activation remains an explicit Updater operation after the
  initial infrastructure installation.
- Rotation of an existing Auth credential remains an explicit operator change;
  Installer reruns do not overwrite it.

## Validation

- Added Installer and Bootstrap tests for masked-input logic, private handoff
  cleanup, fresh Auth provisioning, required Auth values, mode/owner contract,
  preserved valid files, invalid-file rejection, and non-interactive GitHub
  token flows.
- Ran the Installer, Bootstrap, and Deployment Executor test suites; all
  passed.
- Ran a local PTY check of typing, paste, Backspace, Enter, masking, and
  captured-value handling.

## Decisions

- `/etc/rustserver/auth.env` remains the sole persistent source of Auth
  credentials. The Installer owns initial collection; Bootstrap owns secure
  persistence; Updater and Deployment Executor never collect credentials.
- The checked-in Bootstrap defaults file remains secret-free and is not an
  Auth credential source.
