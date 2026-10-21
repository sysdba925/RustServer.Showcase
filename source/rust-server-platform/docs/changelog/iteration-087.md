# Iteration 087 — URL-safe RCON passwords

## Problem

Clean Production Bootstrap generated the shared Rust/WebRCON password with
`openssl rand -base64 48`. Base64 may contain `/`, which is unsafe in
the WebRCON URL path and can produce incorrect-password errors and IP bans.
The RCON and game process may hold identical password bytes but still fail
the WebSocket handshake.

## Changes

- Generate new RCON secrets as 32 random bytes represented by 64 lowercase
  hexadecimal characters (`openssl rand -hex 32`) in Production Bootstrap.
- Use the same generation format in local development bootstrap.
- Assert the Production-generated password is a single 64-character
  lowercase hexadecimal line in the Bootstrap tests.
- Keep existing-password preservation semantics: Bootstrap never silently
  rotates operator credentials.

## Host migration

For an existing host with an unsafe generated secret, rotate the canonical
`/etc/rustserver/rcon-password` securely in place, then restart the Rust
container and `rustserver-rcon-grpc.service`. Allow any temporary RCON
IP ban to expire, or inspect the ban state before retrying.

## Validation

- Source and tests updated in GitHub.
- Bootstrap and runtime test suites not executed in this session.
