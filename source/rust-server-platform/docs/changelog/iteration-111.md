# Iteration 111 — Safe Plugin Catalog PostgreSQL Bootstrap parameters

## Goal

Make the Plugin Catalog PostgreSQL Bootstrap queries work with `psql` variable
substitution while retaining the existing create-only, read-only-on-existing
database contract.

## Added and updated

- Parameterized the four PostgreSQL catalog role and database lookup queries
  through `psql -f -` and standard input instead of `psql -c`.
- Added a lightweight Bootstrap contract check for safe parameterized queries
  and the no-delete database guarantee.

## Current capability

Bootstrap safely checks or creates only the dedicated Plugin Catalog role and
database. Existing databases are inspected and verified; their schema and
data are not dropped, recreated, or migrated.

## Deliberately deferred

- No VM operation, PostgreSQL schema migration, or changes to existing Catalog
  data are part of this iteration.

## Validation

- `bash -n deploy/bootstrap/provision-services.sh` passed.
- `RUSTSERVER_BOOTSTRAP_TEST=test_plugin_catalog_production_contract bash
  deploy/bootstrap/tests/bootstrap-tests.sh` passed.
- `git diff --check` passed.

## Decisions

- SQL values continue to be supplied as `psql` variables and referenced as
  SQL literals with `:'variable'`; Bash does not concatenate values into SQL.
