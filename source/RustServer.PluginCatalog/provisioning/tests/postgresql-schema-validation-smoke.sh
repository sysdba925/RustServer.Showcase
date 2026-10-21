#!/usr/bin/env bash
# Requires one explicitly designated, empty test database; leaves it intact.
set -Eeuo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)"
: "${CATALOG_SCHEMA_TEST_URL:?Set to an empty dedicated test database}"
psql_run() { psql -X -v ON_ERROR_STOP=1 "${CATALOG_SCHEMA_TEST_URL}" "$@"; }
count="$(psql_run -Atc "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%' AND c.relkind IN ('r','p','v','m','S','f');")"
[[ "${count}" == 0 ]] || { printf 'Test database is not empty; stopping.\n' >&2; exit 1; }
psql_run -f "${root}/provisioning/database/postgresql/initial-schema-current.sql"
psql_run -f "${root}/provisioning/database/postgresql/validate-existing-schema.sql"
psql_run -c "INSERT INTO public.plugins (id,name,slug,description,created_at,updated_at) VALUES ('d27007ea-3ba4-4138-9124-99edc083f864','Existing','existing','Preserved',now(),now())"
psql_run -f "${root}/provisioning/database/postgresql/validate-existing-schema.sql"
[[ "$(psql_run -Atc "SELECT description FROM public.plugins WHERE slug='existing'")" == Preserved ]]
printf 'PostgreSQL initial schema and repeat validation passed; test database left intact.\n'
