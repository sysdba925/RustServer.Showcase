# Iteration 127 — Dedicated host cleanup

## Changes

- Cleanup removes the Catalog database and role from local PostgreSQL clusters,
  RustServer NetworkAccess host integration and nftables rules, remaining service
  identities, managed Nginx configuration and named runtime artifacts.
- It validates and reloads Nginx, restores the distribution default when the
  managed site indicates Bootstrap disabled it, and verifies removal. Failures
  prevent the success message; unrelated databases and host firewall rules remain.
- Cleanup logs individual operations and failure location; systemd output uses no pager.
- Docker cleanup retains its existing host-wide behavior. Cleanup was not run.
