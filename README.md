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
cp .env.example .env          # then edit .env: set a real POSTGRES_PASSWORD and JWT_SECRET_KEY
docker compose up --build     # API on http://localhost:8080, PostgreSQL on 127.0.0.1:5432
```

`JWT_SECRET_KEY` signs the login tokens and must be a random string of at least 32 characters, for example the output of `openssl rand -base64 48`. The API refuses to start without it.

Check it: `curl http://localhost:8080/test` returns `test`.

| Command | Effect |
|---|---|
| `docker compose up --build -d` | Rebuild and start in the background |
| `docker compose logs -f api` | Follow the API log |
| `docker compose down` | Stop and remove the containers; **the database data is kept** |
| `docker compose down -v` | Same, and **delete the database volume** |

Both containers run as non-root users (`app` for the API, `postgres` for the database) with all Linux capabilities dropped. The database port is published on loopback only. The API is passed a connection string as `ConnectionStrings__Default` but does not use the database yet.

## Running without Docker

The signing key must not be written in a committed file, so keep it in [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets), which stores it in your user profile, outside the repository:

```bash
dotnet user-secrets set "Jwt:SecretKey" "$(openssl rand -base64 48)" --project GrocerEase.Api
dotnet run --project GrocerEase.Api
```

`Jwt:Issuer`, `Jwt:Audience` and `Jwt:ExpiryMinutes` are not secret and live in `GrocerEase.Api/appsettings.json`. User-secrets are only read when `ASPNETCORE_ENVIRONMENT` is `Development` (the `dotnet run` launch profiles set it). In any other environment, set the environment variable `Jwt__SecretKey`.

## Authentication

`POST /auth/register` and `POST /auth/login` return a JWT in `token`. Send it on every request to a protected route:

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

## Rate limits

Two endpoints are limited per client address, each with its own counter:

| Endpoint | Limit | Why |
|---|---|---|
| `POST /auth/login` | 5 per minute | Stops password guessing, and stops guesses from costing the server a slow hash each |
| `POST /auth/register` | 10 per hour | Stops mass account creation and probing for registered emails (the `409`) |

The next request over a limit gets `429 Too Many Requests` as `application/problem+json`, with a `Retry-After` header (seconds, the length of that endpoint's window) and the code `RateLimit.TooManyRequests`. Every request counts, whether it succeeded, was refused, or had an invalid body. IPv6 clients are grouped by their `/64` prefix. Change the numbers in `GrocerEase.Api/appsettings.json` (`RateLimiting:Login` and `RateLimiting:Register`, each with `PermitLimit` and `WindowSeconds`); the API refuses to start with a value outside 1-1000 requests or 1-3600 seconds.

The limit is per client **address**. Behind a reverse proxy or load balancer every request arrives from the proxy's address, so everyone would share one limit until forwarded headers are configured for the known proxy.

The project changes is going to be documented as md files on `~/Documents/Guides to Great SW/Better NCC DEV/Applications/GrocerEase`

