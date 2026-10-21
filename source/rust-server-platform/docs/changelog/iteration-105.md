# Iteration 105 — Production SSH terminal toolkit

## Problem

Development and Production have independent Bootstrap pipelines.
Production Installer discovers only `deploy/bootstrap/steps/`; a toolkit
installed solely by Development Bootstrap was never executed, hence no
installation log in Production.

## Correction

- Added self-contained `deploy/bootstrap/steps/22-install-terminal-tools.sh`
  and `deploy/bootstrap/assets/tools`.
- Production Step 22 uses **only** `deploy/bootstrap/lib/common.sh`;
  it does not import or run Development Bootstrap or files under `scripts/`.
- The Development Step 22 source is unchanged. No dependency or reference
  from Production to Development remains in this pull request.
- Installs missing tools only; when already present, returns SKIP before
  APT, sudo and network activity. `deploy/cleanup.sh` retains tools and Bash
  aliases, so a subsequent Bootstrap normally skips this step.
- Installs APT-managed tools and uses SHA-256-verified official release
  assets for the terminal applications outside APT, plus pipx for Posting.
- Added a Production test contract verifying local asset paths, no
  Development dependency, no-op guard and installed-only help output.

## Validation

Source layout and changed-file set were reviewed. Full Ubuntu 24.04 x64
installation and rerun tests must still be performed in the target VM.
