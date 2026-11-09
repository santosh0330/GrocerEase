#!/usr/bin/env bash
# Starts the whole Compose stack with throwaway passwords and checks it end to end, over HTTP and inside the database:
# migrations applied by the migrate service, the API working against PostgreSQL, data surviving a restart of the stack,
# and nothing running as root or connecting as more than it needs.
#
# Needs: Docker with the Compose plugin, curl, jq, openssl, and the host ports 8080 and 5432 free.
# Leaves nothing behind: it uses its own Compose project and volume and removes them at the end, pass or fail.
set -euo pipefail

cd "$(dirname "$0")/.."
project="grocerease-smoke-$$"
env_file="$(mktemp)"
work="$(mktemp -d)"
compose() { docker compose --project-name "$project" --env-file "$env_file" "$@"; }

cleanup() {
  local status=$?
  if [ "$status" -ne 0 ]; then
    echo "--- FAILED (exit $status). Last log lines of every container:"
    compose logs --no-color --tail 30 || true
  fi
  compose down --volumes --remove-orphans > /dev/null 2>&1 || true
  rm -rf "$env_file" "$work"
}
trap cleanup EXIT

step() { echo "== $*"; }
fail() { echo "FAIL: $*" >&2; exit 1; }
expect() { [ "$2" = "$3" ] || fail "$1: expected '$3' but got '$2'"; }
secret() { openssl rand -hex 24; }

cat > "$env_file" <<ENV
POSTGRES_DB=grocerease
POSTGRES_USER=grocerease
POSTGRES_PASSWORD=$(secret)
DB_MIGRATOR_PASSWORD=$(secret)
DB_APP_PASSWORD=$(secret)
JWT_SECRET_KEY=$(openssl rand -base64 48)
ENV

# Runs psql inside the database container, as the superuser, for inspection only.
admin_sql() { compose exec -T db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc "$1"' sh "$1"; }

# Liveness (/health/live) answers as soon as the process is up, and does not touch the database or count against /health's rate limit.
wait_for_api() {
  for _ in $(seq 1 60); do
    if curl -fsS -o /dev/null http://localhost:8080/health/live 2> /dev/null; then return 0; fi
    sleep 1
  done
  fail "the API did not answer on http://localhost:8080/health/live within 60 seconds"
}

# Waits until Docker's own health check of the api container (which calls /health) reports "healthy".
wait_for_healthy_container() {
  local container status
  container="$(compose ps --quiet api)"
  for _ in $(seq 1 60); do
    status="$(docker inspect --format '{{.State.Health.Status}}' "$container")"
    if [ "$status" = "healthy" ]; then return 0; fi
    sleep 1
  done
  fail "the api container did not become healthy within 60 seconds (last status: $status)"
}

# Polls /health until it answers with the given HTTP status, every two seconds, for up to $2 seconds.
wait_for_health_status() {
  local wanted="$1" seconds="$2" code
  for _ in $(seq 1 $((seconds / 2))); do
    code="$(curl -sS -o "$work/health.json" -w '%{http_code}' http://localhost:8080/health 2> /dev/null || true)"
    if [ "$code" = "$wanted" ]; then return 0; fi
    sleep 2
  done
  fail "/health did not answer $wanted within $seconds seconds (last answer: $code)"
}

# POST JSON, print the HTTP status, keep the body in $work/body.json.
post() { curl -sS -o "$work/body.json" -w '%{http_code}' -X POST "http://localhost:8080$1" -H 'Content-Type: application/json' -d "$2"; }

email="smoke.$(date +%s)@example.com"
register_body="{\"firstName\":\"Ada\",\"lastName\":\"Lovelace\",\"email\":\"$email\",\"password\":\"Sup3r-s3cret!\"}"
login_body="{\"email\":\"$email\",\"password\":\"Sup3r-s3cret!\"}"

step "start the stack (database, migrations, API)"
compose up --detach --build > /dev/null 2>&1 || { compose up --detach --build; fail "compose up failed"; }
wait_for_api

step "docker reports the api container healthy (its health check calls /health, which includes the database)"
wait_for_healthy_container

step "the migrate service ran and exited cleanly"
migrate_container="$(compose ps --all --quiet migrate)"
expect "migrate exit code" "$(docker inspect --format '{{.State.ExitCode}}' "$migrate_container")" "0"

step "register, log in, and refuse a duplicate over HTTP"
expect "register status" "$(post /auth/register "$register_body")" "200"
registered_id="$(jq -r .id "$work/body.json")"
jq -e '.token | split(".") | length == 3' "$work/body.json" > /dev/null || fail "the token is not a JWT"
expect "login status" "$(post /auth/login "$login_body")" "200"
expect "login returns the registered user" "$(jq -r .id "$work/body.json")" "$registered_id"
upper="$(echo "$register_body" | sed "s/$email/$(echo "$email" | tr '[:lower:]' '[:upper:]')/")"
expect "duplicate register status (email in another case)" "$(post /auth/register "$upper")" "409"

step "what is stored in the database"
expect "users" "$(admin_sql 'select count(*) from users')" "1"
expect "id is a version 7 uuid" "$(admin_sql 'select uuid_extract_version(id) from users')" "7"
expect "id matches the API response" "$(admin_sql 'select id from users')" "$registered_id"
expect "email is stored in lower case" "$(admin_sql 'select email from users')" "$email"
expect "the password is not stored" "$(admin_sql "select count(*) from users where password_hash like '%Sup3r%'")" "0"
expect "timestamps are filled in" "$(admin_sql 'select created_at is not null and updated_at is not null from users')" "t"
expect "both migrations are recorded, in their own schema" "$(admin_sql 'select count(*) from migrations."__EFMigrationsHistory"')" "2"

step "the database roles"
expect "the migrator owns the table" "$(admin_sql "select tableowner from pg_tables where tablename = 'users'")" "grocerease_migrator"
app_sql() { compose exec -T db sh -c 'PGPASSWORD="$APP_PASSWORD" psql -h localhost -U grocerease_app -d "$POSTGRES_DB" -Atc "$1" 2>&1' sh "$1"; }
expect "the app role can read rows" "$(app_sql 'select count(*) from users')" "1"
if out="$(app_sql 'create table should_fail (id int)')"; then fail "the app role was allowed to create a table"; fi
echo "$out" | grep -q "permission denied" || fail "unexpected answer to CREATE TABLE as the app role: $out"
if out="$(app_sql 'select * from migrations."__EFMigrationsHistory"')"; then fail "the app role can read the migration history"; fi

step "health: /health includes the database, /health/live does not"
expect "/health status" "$(curl -sS -o "$work/health.json" -w '%{http_code}' http://localhost:8080/health)" "200"
expect "/health overall" "$(jq -r .status "$work/health.json")" "Healthy"
expect "/health checks" "$(jq -r '[.checks[] | "\(.name)=\(.status)"] | join(",")' "$work/health.json")" "database=Healthy"
expect "/health/live status" "$(curl -sS -o "$work/live.json" -w '%{http_code}' http://localhost:8080/health/live)" "200"
expect "/health/live runs no check" "$(jq -r '.checks | length' "$work/live.json")" "0"

step "health follows the database: stop it under the running API, then start it again (the API is NOT restarted)"
compose stop db > /dev/null 2>&1
wait_for_health_status 503 40
expect "/health overall while the database is down" "$(jq -r .status "$work/health.json")" "Unhealthy"
expect "the failing check is named" "$(jq -r '[.checks[] | "\(.name)=\(.status)"] | join(",")' "$work/health.json")" "database=Unhealthy"
if grep -qiE 'npgsql|password|grocerease_app|refused|exception|host' "$work/health.json"; then fail "/health leaked a detail: $(cat "$work/health.json")"; fi
expect "/health/live while the database is down" "$(curl -sS -o /dev/null -w '%{http_code}' http://localhost:8080/health/live)" "200"
compose start db > /dev/null 2>&1
wait_for_health_status 200 90
expect "/health overall after the database is back" "$(jq -r .status "$work/health.json")" "Healthy"
expect "login works again, without restarting the API" "$(post /auth/login "$login_body")" "200"

step "nothing runs as root"
[ "$(compose exec -T db id -u | tr -d '\r')" != "0" ] || fail "the database container runs as root"
[ "$(compose exec -T api id -u | tr -d '\r')" != "0" ] || fail "the API container runs as root"

step "the data survives taking the whole stack down and starting it again (the volume is kept)"
compose down > /dev/null 2>&1
compose up --detach > /dev/null 2>&1
wait_for_api
expect "migrate runs again without error" "$(docker inspect --format '{{.State.ExitCode}}' "$(compose ps --all --quiet migrate)")" "0"
expect "login after the restart" "$(post /auth/login "$login_body")" "200"
expect "same user after the restart" "$(jq -r .id "$work/body.json")" "$registered_id"

echo "ALL CHECKS PASSED"
