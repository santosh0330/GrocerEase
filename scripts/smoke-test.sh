#!/usr/bin/env bash
# Starts the whole Compose stack with throwaway passwords and checks it end to end, over HTTP and inside the database:
# migrations applied by the migrate service, the API working against PostgreSQL, data surviving a restart of the stack,
# the API's logs reaching Seq and the console (the container writes no log files), and nothing running as root or connecting as more than it needs.
#
# Needs: Docker with the Compose plugin, curl, jq, openssl, and the host ports 8080, 5341 and 5432 free.
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

# Seq answers on its own API once it is up (a few seconds after the container starts).
wait_for_seq() {
  for _ in $(seq 1 60); do
    if curl -fsS -o /dev/null http://localhost:5341/api 2> /dev/null; then return 0; fi
    sleep 1
  done
  fail "Seq did not answer on http://localhost:5341/api within 60 seconds"
}

# Events reach Seq in batches, a couple of seconds after the request, so poll for them instead of sleeping.
# $1 is a Seq filter, for example: RequestPath = '/auth/login'. Prints how many matching events Seq has (up to 100).
seq_count() { curl -sS -G http://localhost:5341/api/events --data-urlencode "filter=$1" --data-urlencode "count=100" | jq 'length'; }
wait_for_seq_events() {
  local filter="$1" wanted="$2" found=0
  for _ in $(seq 1 40); do
    found="$(seq_count "$filter")"
    if [ "$found" -ge "$wanted" ]; then return 0; fi
    sleep 1
  done
  fail "Seq has $found event(s) for [$filter], expected at least $wanted"
}

# What the API wrote to its console, as Docker kept it.
console_logs() { compose logs --no-color --no-log-prefix api | tr -d '\r'; }

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

step "seq is up"
wait_for_seq

step "the migrate service ran and exited cleanly"
migrate_container="$(compose ps --all --quiet migrate)"
expect "migrate exit code" "$(docker inspect --format '{{.State.ExitCode}}' "$migrate_container")" "0"

step "register, log in, and refuse a duplicate over HTTP"
expect "register status" "$(post /auth/register "$register_body")" "200"
registered_id="$(jq -r .id "$work/body.json")"
jq -e '.token | split(".") | length == 3' "$work/body.json" > /dev/null || fail "the token is not a JWT"
registered_refresh_token="$(jq -r .refreshToken "$work/body.json")"
[ "${#registered_refresh_token}" = "43" ] || fail "the refresh token is not 43 characters (32 random bytes in base64url)"
expect "login status" "$(post /auth/login "$login_body")" "200"
expect "login returns the registered user" "$(jq -r .id "$work/body.json")" "$registered_id"
upper="$(echo "$register_body" | sed "s/$email/$(echo "$email" | tr '[:lower:]' '[:upper:]')/")"
expect "duplicate register status (email in another case)" "$(post /auth/register "$upper")" "409"

step "refresh: a token works once, and using an old one ends the session"
expect "refresh status" "$(post /auth/refresh "{\"refreshToken\":\"$registered_refresh_token\"}")" "200"
refreshed_token="$(jq -r .refreshToken "$work/body.json")"
[ "$refreshed_token" != "$registered_refresh_token" ] || fail "refreshing returned the same refresh token"
jq -e '.token | split(".") | length == 3' "$work/body.json" > /dev/null || fail "the refreshed access token is not a JWT"
expect "an old refresh token is refused" "$(post /auth/refresh "{\"refreshToken\":\"$registered_refresh_token\"}")" "401"
expect "the refusal says why, in a stable code" "$(jq -r .code "$work/body.json")" "Authentication.InvalidRefreshToken"
expect "reusing the old one revoked the session: the newest token is refused too" "$(post /auth/refresh "{\"refreshToken\":\"$refreshed_token\"}")" "401"
expect "an empty refresh token is a validation error" "$(post /auth/refresh '{"refreshToken":""}')" "400"

step "logs: every request is one structured event in Seq, with its request and its response"
wait_for_seq_events "RequestPath = '/auth/login' and RequestMethod = 'POST' and StatusCode = 200" 1
curl -sS -G http://localhost:5341/api/events --data-urlencode "filter=RequestPath = '/auth/login' and StatusCode = 200" --data-urlencode "count=1" > "$work/seq-event.json"
jq -e '.[0].Properties | map(.Name) | contains(["RequestMethod","RequestPath","StatusCode","Elapsed","RequestHost","RequestScheme","Endpoint","RequestContentType","ResponseContentType","ResponseBytes","TraceId","RequestId","Application","Environment"])' "$work/seq-event.json" > /dev/null \
  || fail "the login event in Seq is missing request or response details: $(jq -c '.[0].Properties | map(.Name)' "$work/seq-event.json")"
expect "an ordinary request is Information" "$(jq -r '.[0].Level' "$work/seq-event.json")" "Information"
wait_for_seq_events "RequestPath = '/auth/register' and StatusCode = 409" 1
expect "a refused request is a Warning" "$(curl -sS -G http://localhost:5341/api/events --data-urlencode "filter=RequestPath = '/auth/register' and StatusCode = 409" --data-urlencode "count=1" | jq -r '.[0].Level')" "Warning"
# Only request events have a RequestMethod: other events of the same request (a warning from the middleware) also carry the path.
expect "healthy probes are not logged at Information (Docker probes /health every 10 seconds)" "$(seq_count "RequestMethod is not null and StartsWith(RequestPath, '/health')")" "0"

step "logs: the container writes no log files, and its console output has the request event and no secret"
expect "the image turns the log files off" "$(compose exec -T api printenv LogFiles__Enabled | tr -d '\r')" "false"
expect "docker rotates the API's console output" "$(docker inspect --format '{{index .HostConfig.LogConfig.Config "max-size"}}/{{index .HostConfig.LogConfig.Config "max-file"}}' "$(compose ps --quiet api)")" "10m/3"
expect "the container has no log folder" "$(compose exec -T api sh -c 'test -e /app/logs && echo yes || echo no' | tr -d '\r')" "no"
console_logs | grep 'HTTP POST /auth/login responded 200 in' | grep -q '"ResponseBytes"' \
  || fail "the login event is not on the console with its message and properties"
for secret in 'Sup3r-s3cret' "$registered_refresh_token" "$refreshed_token"; do
  expect "the console output does not contain a secret" "$(console_logs | grep -cF -- "$secret" || true)" "0"
  expect "Seq does not contain a secret" "$(curl -sS -G http://localhost:5341/api/events --data-urlencode "count=1000" | grep -cF -- "$secret" || true)" "0"
done
seq_logins_before_restart="$(seq_count "RequestPath = '/auth/login'")"

step "what is stored in the database"
expect "users" "$(admin_sql 'select count(*) from users')" "1"
expect "id is a version 7 uuid" "$(admin_sql 'select uuid_extract_version(id) from users')" "7"
expect "id matches the API response" "$(admin_sql 'select id from users')" "$registered_id"
expect "email is stored in lower case" "$(admin_sql 'select email from users')" "$email"
expect "the password is not stored" "$(admin_sql "select count(*) from users where password_hash like '%Sup3r%'")" "0"
expect "timestamps are filled in" "$(admin_sql 'select created_at is not null and updated_at is not null from users')" "t"
expect "a new user holds the Customer role" "$(admin_sql 'select r.name from users u join roles r on r.id = u.role_id')" "Customer"
expect "all migrations are recorded, in their own schema" "$(admin_sql 'select count(*) from migrations."__EFMigrationsHistory"')" "11"
expect "the roles are seeded, under their fixed ids" "$(admin_sql "select string_agg(id || ':' || name, ',' order by name) from roles")" "019e0000-0000-7000-8000-000000000002:Admin,019e0000-0000-7000-8000-000000000001:Customer,019e0000-0000-7000-8000-000000000003:StoreUser"
expect "the roles grant what they should" "$(admin_sql "select string_agg(r.name || ':' || c.claim_value, ',' order by r.name, c.claim_value) from role_claims c join roles r on r.id = c.role_id where c.claim_type = 'permission'")" "Admin:products.read,Admin:profile.read,Admin:roles.read,Admin:stores.manage,Admin:stores.read,Customer:products.read,Customer:profile.read,Customer:stores.read,StoreUser:inventory.manage,StoreUser:products.manage,StoreUser:products.read,StoreUser:profile.read,StoreUser:stores.read"

step "refresh tokens are stored as hashes only"
registered_hash="$(printf %s "$registered_refresh_token" | openssl dgst -sha256 -hex | awk '{print $NF}')"
expect "the SHA-256 of the token is stored" "$(admin_sql "select count(*) from refresh_tokens where encode(token_hash, 'hex') = '$registered_hash'")" "1"
expect "the token itself is nowhere in the table" "$(admin_sql "select count(*) from refresh_tokens t where t::text like '%$registered_refresh_token%'")" "0"
expect "the used token is marked consumed" "$(admin_sql "select consumed_at is not null from refresh_tokens where encode(token_hash, 'hex') = '$registered_hash'")" "t"
expect "the whole session was revoked" "$(admin_sql "select count(*) from refresh_tokens where family_id = (select family_id from refresh_tokens where encode(token_hash, 'hex') = '$registered_hash') and revoked_at is null")" "0"
expect "a token lives 3 days, a session at most 30" "$(admin_sql "select (expires_at - created_at = interval '3 days') and (family_expires_at - created_at = interval '30 days') from refresh_tokens where encode(token_hash, 'hex') = '$registered_hash'")" "t"

step "the database roles"
expect "the migrator owns the table" "$(admin_sql "select tableowner from pg_tables where tablename = 'users'")" "grocerease_migrator"
expect "the migrator owns the refresh token table" "$(admin_sql "select tableowner from pg_tables where tablename = 'refresh_tokens'")" "grocerease_migrator"
expect "the migrator owns the roles table" "$(admin_sql "select tableowner from pg_tables where tablename = 'roles'")" "grocerease_migrator"
expect "the migrator owns the role claims table" "$(admin_sql "select tableowner from pg_tables where tablename = 'role_claims'")" "grocerease_migrator"
expect "the migrator owns the stores, products and inventory tables" "$(admin_sql "select string_agg(tableowner, ',') from pg_tables where tablename in ('stores', 'store_members', 'products', 'inventory_items')")" "grocerease_migrator,grocerease_migrator,grocerease_migrator,grocerease_migrator"
app_sql() { compose exec -T db sh -c 'PGPASSWORD="$APP_PASSWORD" psql -h localhost -U grocerease_app -d "$POSTGRES_DB" -Atc "$1" 2>&1' sh "$1"; }
expect "the app role can read rows" "$(app_sql 'select count(*) from users')" "1"
expect "the app role can use the refresh token table" "$(app_sql 'select count(*) from refresh_tokens')" "3"
expect "the app role can read the roles" "$(app_sql 'select count(*) from roles')" "3"
expect "the app role can read the role claims" "$(app_sql 'select count(*) from role_claims')" "13"
expect "the app role can use the stores tables" "$(app_sql 'select count(*) from stores')$(app_sql 'select count(*) from store_members')$(app_sql 'select count(*) from products')$(app_sql 'select count(*) from inventory_items')" "0000"
if out="$(app_sql 'create table should_fail (id int)')"; then fail "the app role was allowed to create a table"; fi
echo "$out" | grep -q "permission denied" || fail "unexpected answer to CREATE TABLE as the app role: $out"
if out="$(app_sql 'select * from migrations."__EFMigrationsHistory"')"; then fail "the app role can read the migration history"; fi

step "roles: a new user is a customer, only an admin reaches GET /roles, and a role is read when a token is made"
# A second user, so this step does not disturb the first one's counts. It uses refresh, not login, to get its new token: login is
# limited to 5 a minute per address, and the checks that follow already use most of that.
admin_email="smoke.admin.$(date +%s)@example.com"
expect "register a second user" "$(post /auth/register "{\"firstName\":\"Grace\",\"lastName\":\"Hopper\",\"email\":\"$admin_email\",\"password\":\"Sup3r-s3cret!\"}")" "200"
customer_token="$(jq -r .token "$work/body.json")"
customer_refresh_token="$(jq -r .refreshToken "$work/body.json")"

# A claim of a JWT. The payload is base64url, so restore the standard alphabet and the padding before decoding it.
jwt_claim() {
  local payload
  payload="$(printf %s "$1" | cut -d. -f2 | tr '_-' '/+')"
  while [ $(( ${#payload} % 4 )) -ne 0 ]; do payload="$payload="; done
  printf %s "$payload" | base64 -d | jq -r --arg name "$2" '.[$name]'
}
# The same claim as compact JSON, for the ones that are arrays (the permissions).
jwt_claim_json() {
  local payload
  payload="$(printf %s "$1" | cut -d. -f2 | tr '_-' '/+')"
  while [ $(( ${#payload} % 4 )) -ne 0 ]; do payload="$payload="; done
  printf %s "$payload" | base64 -d | jq -c --arg name "$2" '.[$name]'
}
get() { curl -sS -o "$work/get.json" -w '%{http_code}' "http://localhost:8080$1" ${2:+-H "Authorization: Bearer $2"}; }
expect "the token of a new user says Customer" "$(jwt_claim "$customer_token" role)" "Customer"
expect "and carries what a customer may do" "$(jwt_claim_json "$customer_token" permission)" '["products.read","profile.read","stores.read"]'
expect "GET /me shows the role" "$(get /me "$customer_token" > /dev/null; jq -r .role "$work/get.json")" "Customer"
expect "GET /me shows what the role may do" "$(jq -c .permissions "$work/get.json")" '["products.read","profile.read","stores.read"]'
expect "GET /roles with no token" "$(get /roles)" "401"
expect "GET /roles as a customer" "$(get /roles "$customer_token")" "403"
expect "the refusal has a stable code" "$(jq -r .code "$work/get.json")" "Authorization.Forbidden"

admin_sql "update users set role_id = '019e0000-0000-7000-8000-000000000002' where email = '$admin_email'" > /dev/null
expect "a token issued before the change keeps the old role" "$(get /roles "$customer_token")" "403"
expect "refreshing gives a token with the current role" "$(post /auth/refresh "{\"refreshToken\":\"$customer_refresh_token\"}")" "200"
admin_token="$(jq -r .token "$work/body.json")"
expect "the new token says Admin" "$(jwt_claim "$admin_token" role)" "Admin"
expect "and carries what an admin may do" "$(jwt_claim_json "$admin_token" permission)" '["products.read","profile.read","roles.read","stores.manage","stores.read"]'
expect "GET /roles as an admin" "$(get /roles "$admin_token")" "200"
expect "GET /roles lists both roles" "$(jq -r '[.[].name] | join(",")' "$work/get.json")" "Admin,Customer,StoreUser"
expect "GET /roles says what each role may do" "$(jq -c '[.[] | {name, permissions}]' "$work/get.json")" '[{"name":"Admin","permissions":["products.read","profile.read","roles.read","stores.manage","stores.read"]},{"name":"Customer","permissions":["products.read","profile.read","stores.read"]},{"name":"StoreUser","permissions":["inventory.manage","products.manage","products.read","profile.read","stores.read"]}]'
expect "GET /me shows the new role" "$(get /me "$admin_token" > /dev/null; jq -r .role "$work/get.json")" "Admin"
expect "and the new permissions" "$(jq -c .permissions "$work/get.json")" '["products.read","profile.read","roles.read","stores.manage","stores.read"]'

step "stores: an admin creates a store and assigns a customer to it, which makes them a store user"
# A third user, who registers and then waits to be assigned. Refresh, not login, gives the new token (login is limited to 5 a minute).
member_email="smoke.member.$(date +%s)@example.com"
expect "register a third user" "$(post /auth/register "{\"firstName\":\"Asha\",\"lastName\":\"Rai\",\"email\":\"$member_email\",\"password\":\"Sup3r-s3cret!\"}")" "200"
member_token="$(jq -r .token "$work/body.json")"
member_refresh_token="$(jq -r .refreshToken "$work/body.json")"
member_id="$(jq -r .id "$work/body.json")"
send_as() { curl -sS -o "$work/body.json" -w '%{http_code}' -X "$1" "http://localhost:8080$2" -H "Authorization: Bearer $3" ${4:+-H 'Content-Type: application/json' -d "$4"}; }
expect "a customer cannot create a store" "$(send_as POST /stores "$member_token" '{"name":"Nope","address":"x"}')" "403"
expect "an admin creates a store" "$(send_as POST /stores "$admin_token" '{"name":"Fresh Mart","address":"1 High Street"}')" "201"
store_id="$(jq -r .id "$work/body.json")"
expect "the same name again is a conflict" "$(send_as POST /stores "$admin_token" '{"name":"Fresh Mart","address":"elsewhere"}')" "409"
expect "the conflict has a stable code" "$(jq -r .code "$work/body.json")" "Store.DuplicateName"
expect "a customer can read the store" "$(send_as GET "/stores/$store_id" "$member_token")" "200"
expect "a customer cannot assign anyone" "$(send_as POST "/stores/$store_id/members" "$member_token" "{\"email\":\"$member_email\"}")" "403"
expect "an admin assigns the customer to the store" "$(send_as POST "/stores/$store_id/members" "$admin_token" "{\"email\":\"$member_email\"}")" "200"
expect "the database says store user, in one store" "$(admin_sql "select r.name || ':' || (select count(*) from store_members m where m.user_id = u.id) from users u join roles r on r.id = u.role_id where u.email = '$member_email'")" "StoreUser:1"
expect "their old token still says Customer" "$(jwt_claim "$member_token" role)" "Customer"
expect "refreshing gives them a token that says StoreUser" "$(post /auth/refresh "{\"refreshToken\":\"$member_refresh_token\"}")" "200"
expect "with the store user's permissions" "$(jwt_claim_json "$(jq -r .token "$work/body.json")" permission)" '["inventory.manage","products.manage","products.read","profile.read","stores.read"]'
expect "a store user cannot create a store" "$(send_as POST /stores "$(jq -r .token "$work/body.json")" '{"name":"Nope","address":"x"}')" "403"
expect "assigning them again is refused" "$(send_as POST "/stores/$store_id/members" "$admin_token" "{\"email\":\"$member_email\"}")" "409"
expect "with a stable code" "$(jq -r .code "$work/body.json")" "User.NotACustomer"
expect "an admin takes them out again" "$(send_as DELETE "/stores/$store_id/members/$member_id" "$admin_token")" "204"
expect "the database says customer, in no store" "$(admin_sql "select r.name || ':' || (select count(*) from store_members m where m.user_id = u.id) from users u join roles r on r.id = u.role_id where u.email = '$member_email'")" "Customer:0"

step "products: a store user manages the products of their own store, and only theirs; anyone signed in may browse them"
# A fourth user, registered and then assigned to the store, with a token from refreshing (login is limited to 5 a minute).
owner_email="smoke.owner.$(date +%s)@example.com"
expect "register a store user to be" "$(post /auth/register "{\"firstName\":\"Ben\",\"lastName\":\"Rai\",\"email\":\"$owner_email\",\"password\":\"Sup3r-s3cret!\"}")" "200"
owner_refresh_token="$(jq -r .refreshToken "$work/body.json")"
owner_id="$(jq -r .id "$work/body.json")"
expect "an admin creates a second store" "$(send_as POST /stores "$admin_token" '{"name":"Green Basket","address":"2 Park Lane"}')" "201"
other_store_id="$(jq -r .id "$work/body.json")"
other_owner_email="smoke.other.$(date +%s)@example.com"
expect "register a store user for the other store" "$(post /auth/register "{\"firstName\":\"Cy\",\"lastName\":\"Rai\",\"email\":\"$other_owner_email\",\"password\":\"Sup3r-s3cret!\"}")" "200"
other_owner_refresh="$(jq -r .refreshToken "$work/body.json")"
expect "an admin assigns them to the other store" "$(send_as POST "/stores/$other_store_id/members" "$admin_token" "{\"email\":\"$other_owner_email\"}")" "200"
expect "refreshing gives them a token that says StoreUser" "$(post /auth/refresh "{\"refreshToken\":\"$other_owner_refresh\"}")" "200"
other_owner_token="$(jq -r .token "$work/body.json")"
expect "an admin assigns them to the first store" "$(send_as POST "/stores/$store_id/members" "$admin_token" "{\"email\":\"$owner_email\"}")" "200"
expect "refreshing gives them a token that says StoreUser" "$(post /auth/refresh "{\"refreshToken\":\"$owner_refresh_token\"}")" "200"
owner_token="$(jq -r .token "$work/body.json")"
expect "an admin cannot add a product (that is the store user's job)" "$(send_as POST "/stores/$store_id/products" "$admin_token" '{"name":"Milk","price":2.5}')" "403"
expect "a customer cannot add one either" "$(send_as POST "/stores/$store_id/products" "$member_token" '{"name":"Milk","price":2.5}')" "403"
expect "the store user adds a product" "$(send_as POST "/stores/$store_id/products" "$owner_token" '{"name":"Milk","description":"Fresh","price":2.5}')" "201"
product_id="$(jq -r .id "$work/body.json")"
expect "the product has its price and its store" "$(jq -c '[(.price == 2.5), .storeId]' "$work/body.json")" "[true,\"$store_id\"]"
expect "a price of zero is a validation error" "$(send_as POST "/stores/$store_id/products" "$owner_token" '{"name":"Free","price":0}')" "400"
expect "the same name twice is a conflict" "$(send_as POST "/stores/$store_id/products" "$owner_token" '{"name":"Milk","price":3}')" "409"
expect "with a stable code" "$(jq -r .code "$work/body.json")" "Product.DuplicateName"
expect "the other store is not theirs: the store is not found" "$(send_as POST "/stores/$other_store_id/products" "$owner_token" '{"name":"Milk","price":3}')" "404"
expect "the same answer as for a store that does not exist" "$(jq -r .code "$work/body.json")" "Store.NotFound"
expect "nothing was added to the other store" "$(admin_sql "select count(*) from products where store_id = '$other_store_id'")" "0"
expect "a customer can read the product" "$(send_as GET "/stores/$store_id/products/$product_id" "$member_token")" "200"
expect "and browse the store, a page at a time" "$(send_as GET "/stores/$store_id/products?pageSize=1" "$member_token")" "200"
expect "the page says how long the list is" "$(jq -c '[(.items | length), .page, .pageSize, .totalCount]' "$work/body.json")" "[1,1,1,1]"
expect "a page size over the limit is refused" "$(send_as GET "/stores/$store_id/products?pageSize=101" "$member_token")" "400"
expect "the store user edits the product" "$(send_as PUT "/stores/$store_id/products/$product_id" "$owner_token" '{"name":"Oat milk","description":"Vegan","price":3.2}')" "200"
expect "and archives it" "$(send_as DELETE "/stores/$store_id/products/$product_id" "$owner_token")" "204"
expect "it is gone from the catalogue" "$(send_as GET "/stores/$store_id/products/$product_id" "$member_token")" "404"
expect "but its row stays, archived" "$(admin_sql "select archived_at is not null from products where id = '$product_id'")" "t"
expect "its name is free again" "$(send_as POST "/stores/$store_id/products" "$owner_token" '{"name":"Oat milk","price":3.2}')" "201"
expect "the list of stores is paged too" "$(send_as GET "/stores?pageSize=1" "$member_token")" "200"
expect "with its total" "$(jq -c '[(.items | length), (.totalCount >= 2)]' "$work/body.json")" "[1,true]"

step "inventory: stock is changed by amounts, never below zero, and only by the people who run the store"
expect "the store user has a product to stock" "$(send_as POST "/stores/$store_id/products" "$owner_token" '{"name":"Eggs","price":3}')" "201"
egg_id="$(jq -r .id "$work/body.json")"
expect "a customer cannot change stock" "$(send_as POST "/stores/$store_id/products/$egg_id/stock-adjustments" "$member_token" '{"delta":5}')" "403"
expect "an admin cannot either" "$(send_as POST "/stores/$store_id/products/$egg_id/stock-adjustments" "$admin_token" '{"delta":5}')" "403"
expect "removing from a product that never had stock is refused" "$(send_as POST "/stores/$store_id/products/$egg_id/stock-adjustments" "$owner_token" '{"delta":-1}')" "409"
expect "with a stable code" "$(jq -r .code "$work/body.json")" "Inventory.NotEnoughStock"
expect "the store user adds stock" "$(send_as POST "/stores/$store_id/products/$egg_id/stock-adjustments" "$owner_token" '{"delta":5}')" "200"
expect "and the quantity is what was added" "$(jq -r .quantityOnHand "$work/body.json")" "5"
expect "a change of zero is a validation error" "$(send_as POST "/stores/$store_id/products/$egg_id/stock-adjustments" "$owner_token" '{"delta":0}')" "400"
expect "the other store's user cannot touch it: the store is not found" "$(send_as POST "/stores/$store_id/products/$egg_id/stock-adjustments" "$other_owner_token" '{"delta":-5}')" "404"
expect "and nothing changed" "$(admin_sql "select quantity_on_hand from inventory_items where product_id = '$egg_id'")" "5"
# Twelve removals of one at the same moment, from stock of five: the database lets exactly five through, and the quantity ends at zero.
for i in $(seq 1 12); do
  send_codes_file="$work/removal-$i"
  ( curl -sS -o /dev/null -w '%{http_code}\n' -X POST "http://localhost:8080/stores/$store_id/products/$egg_id/stock-adjustments" \
      -H "Authorization: Bearer $owner_token" -H 'Content-Type: application/json' -d '{"delta":-1}' > "$send_codes_file" ) &
done
wait
expect "twelve removals at once from stock of five: five succeed" "$(cat "$work"/removal-* | grep -c '^200$')" "5"
expect "and seven are refused" "$(cat "$work"/removal-* | grep -c '^409$')" "7"
expect "the quantity is zero, never below" "$(admin_sql "select quantity_on_hand from inventory_items where product_id = '$egg_id'")" "0"
expect "the inventory lists the store's products with their stock" "$(send_as GET "/stores/$store_id/inventory" "$owner_token")" "200"
expect "eggs have none left" "$(jq -r '.items[] | select(.productName == "Eggs") | .quantityOnHand' "$work/body.json")" "0"
expect "a customer cannot read the inventory" "$(send_as GET "/stores/$store_id/inventory" "$member_token")" "403"

step "a store user taken out of the store loses access at once, token or no token"
expect "an admin takes the store user out" "$(send_as DELETE "/stores/$store_id/members/$owner_id" "$admin_token")" "204"
expect "their token still says StoreUser, but it opens nothing: the store is not found for them" "$(send_as POST "/stores/$store_id/products" "$owner_token" '{"name":"Cheese","price":4}')" "404"

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

step "the API does not depend on Seq: stop it under the running API, then start it again"
compose stop seq > /dev/null 2>&1
expect "login works while Seq is down" "$(post /auth/login "$login_body")" "200"
expect "the API is still healthy while Seq is down" "$(curl -sS -o /dev/null -w '%{http_code}' http://localhost:8080/health)" "200"
compose start seq > /dev/null 2>&1
wait_for_seq
expect "login works after Seq is back" "$(post /auth/login "$login_body")" "200"

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
wait_for_seq
wait_for_seq_events "RequestPath = '/auth/login'" "$seq_logins_before_restart"

echo "ALL CHECKS PASSED"
