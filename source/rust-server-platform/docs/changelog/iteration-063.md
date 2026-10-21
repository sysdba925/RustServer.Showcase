# Iteration 063 — StaticWeb deployment smoke coverage

## Goal

Make the Stage 01–03 StaticWeb smoke checks repeatable in the existing Updater
and Bootstrap test suites without requiring production services or paths.

## Added and updated

- The Updater tests cover legacy DotNet type defaulting, valid StaticWeb
  registration, forbidden StaticWeb service fields, minimal static archive
  preparation, complete manifest inventory, and staging tamper detection.
- `PreparedPackageLocator` now selects the registration's validated entry file
  by application type, allowing a verified StaticWeb package to reach the
  existing Executor admission path instead of dereferencing a DotNet entrypoint.
- The existing Deployment Executor test script now uses a temporary test root
  to verify the independent Python parser accepts the same legacy and StaticWeb
  cases, rejects a StaticWeb service field, rejects a modified manifest, rolls
  back `current` when the release-identifying readiness header is missing, and
  never invokes systemd for StaticWeb.

## Validation

- `dotnet test RustServer.Updater.slnx --no-build --no-restore`
- `bash deploy/bootstrap/tests/deployment-executor-tests.sh`

No production configuration, service, application registry, or Nginx route is
used by these tests.
