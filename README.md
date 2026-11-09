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

## Rate limits

Two endpoints are limited per client address, each with its own counter:

| Endpoint | Limit | Why |
|---|---|---|
| `POST /auth/login` | 5 per minute | Stops password guessing, and stops guesses from costing the server a slow hash each |
| `POST /auth/register` | 10 per hour | Stops mass account creation and probing for registered emails (the `409`) |

The next request over a limit gets `429 Too Many Requests` as `application/problem+json`, with a `Retry-After` header (seconds, the length of that endpoint's window) and the code `RateLimit.TooManyRequests`. Every request counts, whether it succeeded, was refused, or had an invalid body. IPv6 clients are grouped by their `/64` prefix. Change the numbers in `GrocerEase.Api/appsettings.json` (`RateLimiting:Login` and `RateLimiting:Register`, each with `PermitLimit` and `WindowSeconds`); the API refuses to start with a value outside 1-1000 requests or 1-3600 seconds.

The limit is per client **address**. Behind a reverse proxy or load balancer every request arrives from the proxy's address, so everyone would share one limit until forwarded headers are configured for the known proxy.

The project changes is going to be documented as md files on `~/Documents/Guides to Great SW/Better NCC DEV/Applications/GrocerEase`

