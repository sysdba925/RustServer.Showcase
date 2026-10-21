# UC-13 — Plugin Update Package — Future

## Status
Future required feature.

## Goal
Update a third-party Rust plugin package from Staging + Development.

## Preconditions
- Environment is `Staging`.
- Source is `Development`.

## Future flow
1. Operator uploads package.
2. Package may be `.cs`, `.zip`, or another supported archive.
3. System inspects the package.
4. System shows detected files.
5. If destinations are ambiguous, operator confirms/mapping is required.
6. Plugin script is routed to Plugins repository.
7. Config/language/other managed files are routed to their managed areas.
8. Operator previews changes.
9. System Commit + Pushes Development changes.
10. Operator applies Development to Staging.
11. Operator starts the Rust server.
12. Plugin may generate/update runtime configs.
13. Operator reviews Runtime Diff.
14. Operator commits selected runtime changes.

## Future catalog
The system should later track:
- plugin identity;
- installed version;
- available version;
- update available;
- source/provider;
- last update.

Storage and exact API are not defined yet.
