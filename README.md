# GrocerEase

GrocerEase is an API for web-based grocery shopping platform. It is meant to let customers browse, search, and order basic grocery items remotely, and to give store administrators a way to manage stock.

The planned scope, from the Software Design Description:

- Customer registration, login, and session management
- Product browsing and keyword / category search
- Shopping cart and order placement
- Remote payment (Stripe)
- Order delivery tracking
- Receipt generation and email (SendGrid / SMTP)
- Admin dashboard for inventory: add, update, and remove products, and view stock levels

The planned stack is a ReactJS frontend, an ASP.NET Core REST API, and PostgreSQL.

## Running with Docker

Requires Docker with the Compose plugin.

```bash
cp .env.example .env          # then edit .env: set the three database passwords and JWT_SECRET_KEY
docker compose up --build     # PostgreSQL on 127.0.0.1:5432, then migrations, then the API on http://localhost:8080, and Seq on http://localhost:5341
```

Use a different random value for each of `POSTGRES_PASSWORD`, `DB_MIGRATOR_PASSWORD` and `DB_APP_PASSWORD`, and use hex (`openssl rand -hex 24`) so no character needs escaping in a connection string.

`JWT_SECRET_KEY` signs the login tokens and must be a random string of at least 32 characters, for example the output of `openssl rand -base64 48`. The API refuses to start without it.

Check it: `curl http://localhost:8080/test` returns `test`.

| Command | Effect |
|---|---|
| `docker compose up --build -d` | Rebuild and start in the background |
| `docker compose logs -f api` | Follow the API log |
| `docker compose down` | Stop and remove the containers; **the database data is kept** |
| `docker compose down -v` | Same, and **delete the volumes**: the database and Seq's data |

The API and database containers run as non-root users (`app` for the API, `postgres` for the database) with all Linux capabilities dropped, and CI checks it. The database port is published on loopback only. Seq, a third-party image, is the exception (see *Logging*): it runs as root, and only its port is limited to loopback.

### Migrations

The schema is created and changed by Entity Framework Core migrations in `GrocerEase.Infrastructure/Persistence/Migrations`. **The API never applies migrations when it starts.** Compose runs a one-off `migrate` service first (an EF migration bundle, run as the `grocerease_migrator` role); the API waits for it to finish. Running it again when nothing is new changes nothing.

To add a migration after changing the model (the tool version is pinned in `.config/dotnet-tools.json`):

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project GrocerEase.Infrastructure --output-dir Persistence/Migrations
```

To apply migrations to a database you started yourself (for `dotnet run`), as the migrator role:

```bash
dotnet ef database update --project GrocerEase.Infrastructure \
  --connection "Host=localhost;Database=grocerease;Username=grocerease_migrator;Password=<DB_MIGRATOR_PASSWORD>"
```

A unit test fails if the model has changed without a migration. `./scripts/smoke-test.sh` starts the whole stack with throwaway passwords, checks it end to end, and removes everything (CI runs it too; it needs ports 8080 and 5432 free).

### Database roles

Nothing in the application connects as the PostgreSQL superuser. `docker/postgres/init/01-roles.sh` creates two roles the first time the data volume is created:

| Role | Used by | Can do |
|---|---|---|
| `POSTGRES_USER` (superuser) | the image, for start-up and health checks only | everything; no application uses it |
| `grocerease_migrator` | schema migrations (the `migrate` service) | owns the database and creates and changes tables; not a superuser |
| `grocerease_app` | the API | read and write rows in the tables; **cannot** create, alter or drop anything, see the migration history, or connect to other databases |

If the API is ever tricked into running arbitrary SQL, this limits the damage to the rows it could already touch.

**Init scripts run only on a new volume.** If you already have a `db-data` volume from before this change, the roles do not exist and the API cannot connect: run `docker compose down -v` (this **deletes the database data**) and start again.

## Running without Docker

The signing key must not be written in a committed file, so keep it in [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets), which stores it in your user profile, outside the repository:

```bash
dotnet user-secrets set "Jwt:SecretKey" "$(openssl rand -base64 48)" --project GrocerEase.Api
dotnet run --project GrocerEase.Api
```

The API also needs a PostgreSQL connection string, as the least-privilege role: set `ConnectionStrings:Default` the same way (`dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=grocerease;Username=grocerease_app;Password=<DB_APP_PASSWORD>" --project GrocerEase.Api`). Start only the database with `docker compose up db`, apply the migrations (see Migrations above), then `dotnet run`.

`Jwt:Issuer`, `Jwt:Audience` and `Jwt:ExpiryMinutes` are not secret and live in `GrocerEase.Api/appsettings.json`. User-secrets are only read when `ASPNETCORE_ENVIRONMENT` is `Development` (the `dotnet run` launch profiles set it). In any other environment, set the environment variable `Jwt__SecretKey`.

## Authentication

`POST /auth/register` and `POST /auth/login` return a JWT in `token` and a refresh token in `refreshToken`. Send the JWT on every request to a protected route:

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/auth/login -H 'Content-Type: application/json' \
  -d '{"email":"ada@example.com","password":"s3cret!!"}' | jq -r .token)
curl -s http://localhost:8080/me -H "Authorization: Bearer $TOKEN"
```

| Route | Needs | Returns |
|---|---|---|
| `GET /me` | a valid bearer token | `{ id, firstName, lastName, email, role, permissions }` of the signed-in user |
| `GET /roles` | a valid bearer token with the `roles.read` permission | every role and what it grants, `[{ id, name, permissions }]` (see [Roles and permissions](#roles-and-permissions)) |
| `POST /stores` | the `stores.manage` permission (the admin) | `201` and the new store, `{ id, name, address }`; `409 Store.DuplicateName` if the name is taken (see [Stores and store users](#stores-and-store-users)) |
| `GET /stores`, `GET /stores/{storeId}` | the `stores.read` permission (every role) | a page of stores ordered by name (`{ items, page, pageSize, totalCount }`, see [Paging](#paging)), or one; `404 Store.NotFound` |
| `POST /stores/{storeId}/members` | the `stores.manage` permission | assigns the customer whose email is in the body to the store, which makes them a store user |
| `DELETE /stores/{storeId}/members/{userId}` | the `stores.manage` permission | takes the store user out of the store, which makes them a customer again; `204` |
| `POST /stores/{storeId}/products` | the `products.manage` permission **and** running that store (a store user) | `201` and the product `{ id, storeId, name, description, price }`; `409 Product.DuplicateName` (see [Products](#products)) |
| `PUT /stores/{storeId}/products/{productId}`, `DELETE ...` | the same | edits the product (`200`), or archives it (`204`) |
| `GET /stores/{storeId}/products`, `GET .../{productId}` | the `products.read` permission (every role) | a page of the products on sale in a store, or one; `404 Product.NotFound` |

An access token lives 10 minutes (`Jwt:ExpiryMinutes`); the refresh token keeps the session going. A request without a valid token never reaches the route. It gets `application/problem+json` with a `code` to switch on, plus a `WWW-Authenticate` header:

| Status | `code` | When |
|---|---|---|
| `401` | `Authentication.Required` | no token, or not a bearer one (`Basic ...`) |
| `401` | `Authentication.InvalidToken` | garbage, wrong signature, wrong issuer or audience, no signature, no expiry |
| `401` | `Authentication.TokenExpired` | a real token that has expired: sign in again |
| `403` | `Authorization.Forbidden` | signed in, but the token lacks the permission the route needs (a `Customer` calling `GET /roles`) |

A token is accepted up to 30 seconds past its expiry, to forgive small clock differences between servers. Only HMAC-SHA256 tokens signed with this API's key, for this API's issuer and audience, are accepted. `GrocerEase.Api/GrocerEase.Api.http` has ready-made requests (run `login` first).

### Refresh tokens

A JWT lives 60 minutes. To stay signed in without asking for the password again, exchange the **refresh token** for a new pair:

```bash
curl -s -X POST http://localhost:8080/auth/refresh -H 'Content-Type: application/json' \
  -d "{\"refreshToken\":\"$REFRESH_TOKEN\"}"
```

The response has the same shape as login: a new `token` and a **new** `refreshToken`. The one you sent is now used up, so store the new one and discard the old.

- **In the JSON body, never the URL.** A URL is written to access logs and proxies; a body is not. `GET /auth/refresh` is `405`.
- **Lives 3 days** (`RefreshToken:LifetimeDays`), counted from when it was issued. Each refresh starts a new 3-day window.
- **A session ends after 30 days at most** (`RefreshToken:MaxLifetimeDays`), however often it is refreshed: the token issued near the end is cut short to the 30th day, and after that the user signs in again. Neither setting may be above 30 (the API refuses to start), and the database refuses a row that lives longer.
- **One use each.** A token that is presented a second time means someone kept a copy. The server then **revokes the whole session** (every token descended from that sign-in), so the thief and the owner are both signed out and the owner signs in again. Other sessions of the same user (another device) are not touched. An access token that was already issued stays valid until it expires (at most 60 minutes): it is a signed token the server does not look up.
- **Two requests with the same token at once:** exactly one succeeds. The other is treated as a reuse, so a client must not refresh from two places in parallel with the same token.
- **Only a SHA-256 hash is stored.** A copy of the database cannot be used to refresh a session. The token itself (32 random bytes, 43 base64url characters) is never stored or logged.
- **Failures** are `application/problem+json`: `401` with code `Authentication.InvalidRefreshToken` for a token that is unknown, expired, used or revoked (one answer for all, so nothing is learned from the difference); `400` for an empty or oversized token; `429` over the limit (30 per minute per address, `RateLimiting:Refresh`).

### Roles and permissions

Every user holds **exactly one role**, stored in `users.role_id`. A role **grants permissions**, stored as claims in `role_claims` (`claim_type` is `permission`). Three roles and what they grant are created by a migration, with fixed ids:

| Role | Id | Who | May do |
|---|---|---|---|
| `Customer` | `019e0000-0000-7000-8000-000000000001` | everyone who registers | `profile.read`, `stores.read`, `products.read` |
| `Admin` | `019e0000-0000-7000-8000-000000000002` | made by hand, see below | `profile.read`, `roles.read`, `stores.read`, `stores.manage`, `products.read` |
| `StoreUser` | `019e0000-0000-7000-8000-000000000003` | a customer an admin assigned to a store | `profile.read`, `stores.read`, `products.read`, `products.manage` |

| Permission | Needed by |
|---|---|
| `profile.read` | reading one's own profile (`GET /me` needs only a signed-in user for now) |
| `roles.read` | `GET /roles` |
| `stores.read` | `GET /stores`, `GET /stores/{storeId}` |
| `stores.manage` | `POST /stores`, and assigning or removing a store's users |
| `products.read` | browsing the products of a store |
| `products.manage` | adding, editing and archiving products, **in a store the caller runs** (see [Products](#products)) |

The token carries the role's name as `role` and the permissions as a `permission` array (`["products.read","profile.read","stores.read"]`). A route names the permission it needs, `[Authorize(Policy = "roles.read")]`, not a role: the policy checks the `permission` claims of the token, so giving the permission to another role changes no endpoint. `GET /me` shows the role and the permissions the token carries.

- **They are read when a token is made** (register, login, refresh), not on every request. So a changed role, or a changed permission, applies from the **next token**: an access token lives up to 10 minutes (`Jwt:ExpiryMinutes`), and until then it keeps saying what it said. Logging in, or refreshing, gives a token with the current ones. A token made before permissions existed has a role and no permissions, so it is refused by `GET /roles` until its owner logs in or refreshes.
- **The token grows with the permissions.** A handful is fine; the refresh endpoint accepts up to 2048 characters so an access token sent there by mistake still gets the usual single `401`.
- **Only one thing changes a role: assigning a customer to a store** (below). **Nothing changes a permission, and nothing makes an admin.** To make the first admin, update the row, then log in again (the stack from `docker compose up` must be running):

  ```bash
  docker compose exec -T db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<'SQL'
  update users set role_id = '019e0000-0000-7000-8000-000000000002' where email = 'ada@example.com';
  SQL
  ```

  To let another role do something, add a claim (one of the permissions above: a name the application does not know has no policy and opens nothing):

  ```bash
  docker compose exec -T db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<'SQL'
  insert into role_claims (role_id, claim_type, claim_value)
  values ('019e0000-0000-7000-8000-000000000001', 'permission', 'roles.read');
  SQL
  ```

- **A role in use cannot be deleted** (the foreign key restricts it), but deleting a role deletes its claims. A user cannot be saved without a role, and a role cannot state the same claim twice. A user who registers while the `Customer` row is missing gets a generic `500`, not a business error: that is a broken database, and the cause is in the log.

### Stores and store users

An admin creates stores, and **makes a customer a store user by assigning them to a store**: one call, no separate role change.

```bash
curl -s -X POST http://localhost:8080/stores -H "Authorization: Bearer $ADMIN_TOKEN" -H 'Content-Type: application/json' \
  -d '{"name":"Fresh Mart","address":"1 High Street"}'                       # 201, { "id": "...", ... }
curl -s -X POST http://localhost:8080/stores/$STORE_ID/members -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H 'Content-Type: application/json' -d '{"email":"asha@example.com"}'      # 200: asha is now a StoreUser of that store
curl -s -X DELETE http://localhost:8080/stores/$STORE_ID/members/$USER_ID -H "Authorization: Bearer $ADMIN_TOKEN"   # 204: a customer again
```

| Status | `code` | When |
|---|---|---|
| `404` | `Store.NotFound` | no store with that id |
| `404` | `User.NotFound` | no user with that email (the admin may learn which addresses are registered) |
| `404` | `Store.MemberNotFound` | the user does not run that store (a member of another store is not touched) |
| `409` | `User.NotACustomer` | the user is an admin, or already runs a store |
| `409` | `Store.DuplicateName` | a store with that name exists (exact match, after trimming) |

- **The membership and the role are written together or not at all** (a transaction), and each write is guarded in the database: the unique index on `store_members.user_id` allows one store per user, and the role changes only if the user still holds the role it is changing from. So two admins assigning the same customer at once cannot both win.
- **Access follows the database, the token follows later.** The new store user's access to a store is checked against `store_members` on every request, so it starts, and ends, at once. Their *token* still says `Customer` until it is refreshed or they sign in again, which is at most 10 minutes (`Jwt:ExpiryMinutes`); after a removal it keeps saying `StoreUser` for that long, which opens nothing.
- A store user belongs to **one** store. A store cannot be deleted, or renamed, through the API yet.

### Products

A store user adds, edits and archives the products of **their own store**; anyone signed in may browse them.

```bash
curl -s -X POST http://localhost:8080/stores/$STORE_ID/products -H "Authorization: Bearer $STORE_USER_TOKEN" -H 'Content-Type: application/json' \
  -d '{"name":"Milk","description":"Fresh","price":2.50}'                 # 201 { "id": "...", "storeId": "...", "name": "Milk", ... }
curl -s "http://localhost:8080/stores/$STORE_ID/products?page=1&pageSize=20" -H "Authorization: Bearer $TOKEN"   # anyone signed in
curl -s -X PUT    http://localhost:8080/stores/$STORE_ID/products/$PRODUCT_ID -H "Authorization: Bearer $STORE_USER_TOKEN" \
  -H 'Content-Type: application/json' -d '{"name":"Oat milk","description":"Vegan","price":3.20}'                # 200
curl -s -X DELETE http://localhost:8080/stores/$STORE_ID/products/$PRODUCT_ID -H "Authorization: Bearer $STORE_USER_TOKEN"   # 204: archived
```

**Two questions, answered in two places.** `products.manage` says *what kind of user* may add a product, and it is in the token. Whether they may do it *in this store* is a **membership check against the database, on every request**, made by the use case: so a store user taken out of a store is refused at once, even with a token that still says `StoreUser` and still carries `products.manage`. A store the caller does not run is answered **`404 Store.NotFound`, the same as a store that does not exist**: a `403` would tell them the store is there. Browsing needs no membership.

| Status | `code` | When |
|---|---|---|
| `400` | | a blank or too long name or description, a price that is not above zero, has more than 2 decimals or does not fit `numeric(12, 2)`; a `page` or `pageSize` outside the limits |
| `404` | `Store.NotFound` | no such store, **or** a store the caller does not run (adding, editing, archiving) |
| `404` | `Product.NotFound` | no such product in that store, it is another store's, or it is archived |
| `409` | `Product.DuplicateName` | the store already sells a live product with that name |

- **A product is archived, not deleted.** `archived_at` is set, it leaves the catalogue and the list, and its name is free for a new product (the unique index on `(store_id, name)` is partial: `WHERE archived_at IS NULL`). The row stays, so an order placed later can say what it bought. An archived product cannot be edited or brought back.
- **Money** is `numeric(12, 2)` in the database and a `decimal` in the API, never a floating point number. A price with half a cent is refused with a `400`, not rounded. One currency, no currency field.
- **Every write is one statement with its conditions inside it** (`WHERE id = ... AND store_id = ... AND archived_at IS NULL`), so a product of another store is never touched, and two requests archiving, or renaming to one name, cannot both win. Two people editing the same product do not know about each other: the last write wins (a version on the product is planned).

### Paging

Every list is paged, with one set of limits: `?page=` counts **from 1** (at most 100 000), `?pageSize=` is **1 to 100** (20 by default). A value outside them is a `400` naming the parameter. The answer is `{ "items": [...], "page": 1, "pageSize": 20, "totalCount": 57 }`, and a page past the end has no items but still says `totalCount`. `GET /stores` and `GET /stores/{storeId}/products` are paged; the roles are not (there are three).

## Health checks

Two endpoints, open to anyone (a probe has no token):

| Endpoint | Question | Runs | Answer |
|---|---|---|---|
| `GET /health` | Can this instance serve requests that need the database? (readiness) | every check: the database | `200` healthy, `503` unhealthy |
| `GET /health/live` | Is the process up? (liveness) | no check | `200` whenever it can answer |

```json
{ "status": "Healthy", "totalDurationMs": 4.1, "checks": [ { "name": "database", "status": "Healthy", "durationMs": 3.8 } ] }
```

The database check reads from the `users` table, so it passes only if the database is reachable, the API's role may use it, **and the migrations have run**. A check that failed is reported by name and status only. **The reason is never in the response** (it would name hosts and users): it is in the API's log, with the exception.

Use `/health/live` for a probe that **restarts** the app: restarting does not fix a database outage, so liveness must not depend on it. Use `/health` to decide whether to **send traffic** or to show status. `/health` queries the database on every call, so it is rate limited per client address (`RateLimiting:Health`, 60 per minute by default); `/health/live` is not. Responses are never cached.

## Rate limits

Three endpoints are limited per client address, each with its own counter:

| Endpoint | Limit | Why |
|---|---|---|
| `POST /auth/login` | 5 per minute | Stops password guessing, and stops guesses from costing the server a slow hash each |
| `POST /auth/register` | 10 per hour | Stops mass account creation and probing for registered emails (the `409`) |
| `POST /auth/refresh` | 30 per minute | Stops guessing and replaying refresh tokens |

The next request over a limit gets `429 Too Many Requests` as `application/problem+json`, with a `Retry-After` header (seconds, the length of that endpoint's window) and the code `RateLimit.TooManyRequests`. Every request counts, whether it succeeded, was refused, or had an invalid body. IPv6 clients are grouped by their `/64` prefix. Change the numbers in `GrocerEase.Api/appsettings.json` (`RateLimiting:Login`, `RateLimiting:Register` and `RateLimiting:Refresh`, each with `PermitLimit` and `WindowSeconds`); the API refuses to start with a value outside 1-1000 requests or 1-3600 seconds.

The limit is per client **address**. Behind a reverse proxy or load balancer every request arrives from the proxy's address, so everyone would share one limit until forwarded headers are configured for the known proxy.

The project changes is going to be documented as md files on `~/Documents/Guides to Great SW/Better NCC DEV/Applications/GrocerEase`


## Logging

The API writes **structured** logs with [Serilog](https://serilog.net): every event keeps its values as named properties (`StatusCode`, `Elapsed`, ...), not only as a line of text, so they can be filtered and counted, not just read. Up to three places receive the same events:

| Where | Form | For |
|---|---|---|
| **Console** (`docker compose logs api`, or the terminal of `dotnet run`) | one readable line per event, then its properties as JSON | watching the API run. In Docker this is the record: Docker keeps the last 30 MB per service |
| **Files** (`logs/`, **only when the API runs outside Docker**, for example `dotnet run`) | one JSON document per line, **a new file every day**: `grocerease-20261110.log` | `jq` and `grep` on a machine where nothing else collects the logs. The container writes none |
| **Seq** (`http://localhost:5341`) | searchable events | finding things |

Levels come from `Serilog:MinimumLevel` in `appsettings.json`: `Information`, with the framework's own chatter (`Microsoft`, `System`) at `Warning`. `appsettings.Development.json` lowers the default to `Debug`.

### One event per request

When a response has been sent, the API logs **one** event that says what was asked and what was answered. A line of the file (formatted here to read it):

```json
{
  "Timestamp": "2026-11-10T21:47:49.9754497+05:45",
  "Level": "Warning",
  "RenderedMessage": "HTTP \"POST\" \"/auth/login\" responded 401 in 463.4458 ms",
  "Properties": {
    "RequestMethod": "POST", "RequestPath": "/auth/login", "StatusCode": 401, "Elapsed": 463.445802,
    "RequestHost": "localhost", "RequestScheme": "http", "Protocol": "HTTP/1.1",
    "Endpoint": "GrocerEase.Api.Controllers.AuthController.Login (GrocerEase.Api)",
    "ClientIp": "203.0.113.7", "UserAgent": "curl/8.18.0",
    "RequestContentType": "application/json; charset=utf-8", "RequestContentLength": 48,
    "ResponseContentType": "application/problem+json; charset=utf-8", "ResponseBytes": 251,
    "RequestId": "0HNPV8G8O8FUD", "Application": "GrocerEase.Api", "Environment": "Development", "MachineName": "SPC"
  },
  "TraceId": "80bf0f047baec1b69381b431979f551f"
}
```

- **When it was logged:** `Timestamp`, with its UTC offset. **What was asked:** `RequestMethod`, `RequestPath`, `RequestHost`, `RequestScheme`, `Protocol`, `Endpoint` (the controller action that handled it), `ClientIp`, `UserAgent`, `RequestContentType`, `RequestContentLength`. **Who:** `UserId` (the `sub` of the access token) for a signed-in request, absent for an anonymous one. **What was answered:** `StatusCode`, `ResponseContentType`, `ResponseBytes` (the bytes actually written; a streamed JSON answer has no `Content-Length`), `Elapsed` in milliseconds.
- **The level says how it went:** `Information` for a success, `Warning` for a refused request (any `4xx`: bad input, wrong credentials, a rate limit), `Error` for a `5xx`. The health probes are `Debug` when healthy (Docker asks every 10 seconds) and `Warning` when not.
- **A failure keeps its exception.** An unhandled exception is logged by its own event (`Level: Error`, the exception with its full stack trace in `Exception`, still on one line in the file), and the request's event is the `500`. Both carry the same **`RequestId`** and **`TraceId`**, and the `traceId` in the `500` response body contains the same trace id: a user's report leads to the exact log lines. Events the application writes while handling a request (a revoked refresh token family, say) carry them too.

**What is never logged,** on purpose: request and response **bodies** (passwords, access tokens and refresh tokens are in them), the **`Authorization` header** and cookies, and the **query string** (a URL is where people put values they should not). Tests send canary secrets through every path and search the console, the files and what Seq receives for them. If a request needs more detail to debug, add the one property it needs: do not turn on body logging.

`ClientIp`, `UserId` and `UserAgent` identify people. Treat the logs as personal data: files, when they are on, are kept 14 days by default (below), Docker keeps only the last 30 MB of console output, and Seq is for the developers of this API.

### Files

| Setting (`LogFiles:`) | Default | Meaning |
|---|---|---|
| `Enabled` | `true` (the container image sets `false`) | whether files are written at all. When `false` no file or folder is created |
| `Directory` | `logs` | where the files go; created if missing. Relative paths are relative to the working directory |
| `RetainedDays` | `14` | files older than this are deleted when the next file is opened. `1`-`365` |
| `MaxFileSizeKilobytes` | `102400` (100 MB) | a day's file that reaches this is closed and a numbered one started (`grocerease-20261110_001.log`). `64`-`1048576` |

The API refuses to start with a value outside these ranges. Several writers (two instances on one volume) can share a file; lines are never cut in half.

```bash
# the failed requests so far, from the files of a `dotnet run` (the `logs/` folder of the directory you ran it in)
cat logs/grocerease-*.log \
  | jq -c 'select(.Properties.StatusCode >= 400) | {t: .Timestamp, m: .Properties.RequestMethod, p: .Properties.RequestPath, s: .Properties.StatusCode, ms: .Properties.Elapsed}'
```

### In Docker: the console, not files

The container **writes no log files**. A container's job is to write to its console and leave keeping, rotating and shipping to whatever runs it, so the image sets `LogFiles__Enabled=false` and Compose has no log volume. What Docker keeps is bounded: every service in `docker-compose.yml` keeps at most **3 files of 10 MB** (`x-default-logging`), and CI checks that no service is left without it (Docker's default has no limit). That is also what you have while Seq is down:

```bash
docker compose logs api | grep -E '\[[^]]* (WRN|ERR|FTL)\]'     # warnings and errors, newest 30 MB at most
```

For a longer record use Seq. To get files from the image anyway, run it with `LogFiles__Enabled=true` and `LogFiles__Directory` on a volume that the `app` user can write to.

If you ran an earlier version of this stack, its log volume is no longer used: `docker volume ls` shows it (named like `grocerease_api-logs`) and `docker volume rm` deletes it.

### Seq

```bash
docker compose up -d seq        # the UI is at http://localhost:5341
```

`dotnet run` sends to `Seq:ServerUrl`, `http://localhost:5341` by default, so the same Seq serves both. Compose sets it to `http://seq:5341` for the API container. Leave it empty (`Seq__ServerUrl=`) to turn Seq off. Useful filters, typed in the Seq search box:

| Filter | Finds |
|---|---|
| `StatusCode >= 500` | server errors |
| `RequestPath = '/auth/login' and StatusCode = 401` | failed sign-ins |
| `Elapsed > 1000` | requests slower than a second |
| `UserId = '0192f1a2-...'` | everything one user did |
| `TraceId = '80bf0f04...'` | one request, and everything the application logged while handling it |

- **Seq never slows the API or stops it.** Events are sent in the background in batches. If Seq is stopped or unreachable the API keeps answering, and the console still has the newest 30 MB of events in Docker (and the files too, when they are on); events past the sink's in-memory limit are dropped. When the API stops it posts what it still holds, so the last events before a shutdown are not lost.
- **Local development only, as configured here.** The Compose `seq` service has **no login** (Seq 2026 refuses to start without either an admin password or an explicit opt-out; `SEQ_FIRSTRUN_NOAUTHENTICATION: "true"` is that opt-out) and its ports are on `127.0.0.1` only, like the database. CI checks that no Seq port is published on another address. The image runs as root and does not start with its capabilities dropped, so it has less hardening than the other services. For anything shared: remove the opt-out, set `SEQ_FIRSTRUN_ADMINPASSWORDHASH` (the hash: `echo 'a password' | docker run --rm -i datalust/seq config hash`), create an API key in Seq, and give it to the API as `Seq__ApiKey` (a secret: user-secrets or the environment, never a committed file). These settings apply only when the data volume is first created.
- **`ACCEPT_EULA: "Y"`** in `docker-compose.yml` accepts Seq's license agreement, which the image requires; Seq is free for individual use.
