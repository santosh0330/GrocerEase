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
docker compose up --build     # PostgreSQL on 127.0.0.1:5432, then migrations, then the API on http://localhost:8080
```

Use a different random value for each of `POSTGRES_PASSWORD`, `DB_MIGRATOR_PASSWORD` and `DB_APP_PASSWORD`, and use hex (`openssl rand -hex 24`) so no character needs escaping in a connection string.

`JWT_SECRET_KEY` signs the login tokens and must be a random string of at least 32 characters, for example the output of `openssl rand -base64 48`. The API refuses to start without it.

Check it: `curl http://localhost:8080/test` returns `test`.

| Command | Effect |
|---|---|
| `docker compose up --build -d` | Rebuild and start in the background |
| `docker compose logs -f api` | Follow the API log |
| `docker compose down` | Stop and remove the containers; **the database data is kept** |
| `docker compose down -v` | Same, and **delete the database volume** |

Both containers run as non-root users (`app` for the API, `postgres` for the database) with all Linux capabilities dropped, and CI checks it. The database port is published on loopback only.

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
| `GET /me` | a valid bearer token | `{ id, firstName, lastName, email }` of the signed-in user |

A token lives 60 minutes (`Jwt:ExpiryMinutes`). A request without a valid token never reaches the route. It gets `application/problem+json` with a `code` to switch on, plus a `WWW-Authenticate` header:

| Status | `code` | When |
|---|---|---|
| `401` | `Authentication.Required` | no token, or not a bearer one (`Basic ...`) |
| `401` | `Authentication.InvalidToken` | garbage, wrong signature, wrong issuer or audience, no signature, no expiry |
| `401` | `Authentication.TokenExpired` | a real token that has expired: sign in again |
| `403` | `Authorization.Forbidden` | signed in, but not allowed to do this (no route needs a permission yet) |

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

