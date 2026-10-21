# Iteration 104 — SSH terminal toolkit in Development Bootstrap

## Goal

Add a terminal toolkit and simple Russian-language command reference for
administrating the RustServer development machine over SSH.

## Added and updated

- Added automatically discovered Step 22 to the existing numbered
  `scripts/bootstrap.d/` sequence; the main orchestrator is unchanged.
- Installed `micro`, `btop`, `tmux`, `ncdu`, `fzf`, `duf`, `lnav`, and
  `pipx` through Ubuntu APT. Added verified-release installers for
  `systemctl-tui`, `lazyjournal`, `yazi`, `dust` (amd64), and Helix,
  plus a user-local pipx installation for Posting.
- Left TermIDE, mc, lazygit, lazydocker, bat, jq, and ripgrep under existing
  Step 20 ownership, avoiding redundant installers.
- Installed `/usr/local/bin/tools`: friendly descriptions of installed
  utilities, showing installed programs only.
- Added no-argument interactive `help` and `info` functions to the invoking
  user's `~/.bashrc`, retaining normal help and info behavior with arguments.

## Safety

- After deploy/cleanup.sh the installed applications and SSH aliases remain.
  Step 22 checks commands, the current tools helper, and the Bash profile marker
  and returns SKIP immediately when complete (no sudo, APT, or network).
- On partial installations it runs APT only for missing packages and does not
  rewrite existing Bash settings.
- Removed the unnecessary tools --all option and the missing-program list.
- Official GitHub release downloads require SHA-256 verification.
- Optional release failures are logged and do not interrupt the base
  platform setup. No persistent Rust data or secrets are changed.
- Does not modify the production Bootstrap under `deploy/bootstrap/`.

## Validation

- Bash syntax checks and terminal tool catalog smoke test executed locally.
- Full Ubuntu package installation and SSH session behavior are not
  exercised in this environment; validate on the Ubuntu 24.04 VM.
