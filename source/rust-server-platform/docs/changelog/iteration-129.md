# Iteration 129 — public landing and Dashboard authorization

- Production Bootstrap installs a root-owned Platform landing page independently of Web releases, preserving matching files and rejecting conflicts.
- Nginx serves the landing only at `/` and redirects Dashboard authorization 401/403 responses there. API status codes, Auth routes, unknown paths, and StaticWeb readiness retain their boundaries.
- Added Bootstrap regression coverage for routing and conservative landing installation.
