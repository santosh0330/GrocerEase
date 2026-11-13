#!/bin/bash
# Creates the database roles. The official postgres image runs every script in /docker-entrypoint-initdb.d once,
# when the data directory is first created, connected as POSTGRES_USER (the bootstrap superuser) to POSTGRES_DB.
# It does NOT run again on an existing volume: change a role by editing it in the database, or recreate the volume.
#
# Three roles, so that no part of the system holds more power than its job needs:
#
#   POSTGRES_USER          superuser. Used by the image to bootstrap and to answer health checks. No application uses it.
#   grocerease_migrator    owns the database and its tables. Runs migrations (DDL). Not a superuser.
#   grocerease_app         what the API connects as. Can read and write rows in the tables the migrator creates, and nothing
#                          else: no creating, altering or dropping, no other schemas, no other databases.
#
# The role names are used by docker-compose.yml, by the integration tests, and by the migration that makes the stock ledger append-only
# (it takes UPDATE and DELETE back from grocerease_app): change them in all four places.
set -euo pipefail

: "${MIGRATOR_PASSWORD:?set MIGRATOR_PASSWORD}"
: "${APP_PASSWORD:?set APP_PASSWORD}"

# \getenv reads the passwords from the environment inside psql, so they never appear in a process argument list.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<'SQL'
\getenv migrator_password MIGRATOR_PASSWORD
\getenv app_password APP_PASSWORD
\getenv db POSTGRES_DB

CREATE ROLE grocerease_migrator LOGIN PASSWORD :'migrator_password'
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE grocerease_app LOGIN PASSWORD :'app_password'
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;

-- The migrator owns the database, and so (PostgreSQL 15+) can create objects in its public schema.
ALTER DATABASE :"db" OWNER TO grocerease_migrator;

-- Only the two roles may connect to the application database, and nobody but the superuser to the default one.
REVOKE ALL ON DATABASE :"db" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"db" TO grocerease_migrator, grocerease_app;
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;

-- Nobody but the owner creates objects in the public schema (this is already the default since PostgreSQL 15).
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- The app role may use the schema, and gets row access to every table the migrator creates in it from now on.
-- Entity Framework's history table lives in a different schema ("migrations"), so the app role never sees it.
GRANT USAGE ON SCHEMA public TO grocerease_app;
ALTER DEFAULT PRIVILEGES FOR ROLE grocerease_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO grocerease_app;
ALTER DEFAULT PRIVILEGES FOR ROLE grocerease_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO grocerease_app;
SQL
