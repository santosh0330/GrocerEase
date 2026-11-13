# GrocerEase

GrocerEase is a grocery shopping REST API built with Clean Architecture on .NET 10, ASP.NET Core and PostgreSQL. It is meant to let customers browse, search, and order basic grocery items remotely, and to give store administrators a way to manage stock. It covers authentication, orders, stock and fulfilment, and runs with Docker Compose.

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
| `POST /stores/{storeId}/products/{productId}/stock-adjustments` | the `inventory.manage` permission **and** running that store | body `{ "delta": 12 }` (positive adds, negative removes); `200` and `{ productId, quantityOnHand }`; `409 Inventory.NotEnoughStock` (see [Inventory](#inventory)) |
| `GET /stores/{storeId}/inventory` | the same | a page of every product on sale with `{ productId, productName, quantityOnHand, lowStockThreshold, isLow }`, zero when it has never had stock; `?lowStock=true` keeps only the products at or below their low-stock level |
| `PUT /stores/{storeId}/products/{productId}/low-stock-threshold` | the same | body `{ "threshold": 5 }` (`null` clears it); `200` and the inventory entry; `404` for a product that is not on sale in the store (see [Low stock](#low-stock)) |
| `POST /stores/{storeId}/orders` | the `orders.place` permission (a customer) | body `{ "deliveryAddress": "...", "lines": [{ "productId": "...", "quantity": 2 }] }`; `201` and the order; `409 Order.InsufficientStock`; limited to 20 a minute (see [Orders](#orders)) |
| `GET /orders`, `GET /orders/{orderId}` | the `orders.read` permission (a customer) | a page of the caller's **own** orders, newest first, or one of them; someone else's is `404 Order.NotFound` |
| `POST /orders/{orderId}/cancel` | the `orders.cancel` permission (a customer), **their own order** | withdraws an order while it is still `Pending` (`200` and the order, stock back); `409 Order.InvalidTransition` once the store has verified it; someone else's is `404 Order.NotFound` (see [Fulfilling orders](#fulfilling-orders)) |
| `GET /stores/{storeId}/orders` | the `orders.manage` permission **and** running that store (a store user) | a page of the store's orders, **oldest first**, optionally `?status=Pending` (any case); `400` for a status that is not one (see [Fulfilling orders](#fulfilling-orders)) |
| `POST /stores/{storeId}/orders/{orderId}/verify`, `.../ship`, `.../cancel` | the same | moves the order along (`200` and the order); `409 Order.InvalidTransition`, `409 Order.ChangedMeanwhile`; cancelling puts the stock back |

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
| `Customer` | `019e0000-0000-7000-8000-000000000001` | everyone who registers | `profile.read`, `stores.read`, `products.read`, `orders.place`, `orders.read`, `orders.cancel` |
| `Admin` | `019e0000-0000-7000-8000-000000000002` | made by hand, see below | `profile.read`, `roles.read`, `stores.read`, `stores.manage`, `products.read` |
| `StoreUser` | `019e0000-0000-7000-8000-000000000003` | a customer an admin assigned to a store | `profile.read`, `stores.read`, `products.read`, `products.manage`, `inventory.manage`, `orders.manage` |

| Permission | Needed by |
|---|---|
| `profile.read` | reading one's own profile (`GET /me` needs only a signed-in user for now) |
| `roles.read` | `GET /roles` |
| `stores.read` | `GET /stores`, `GET /stores/{storeId}` |
| `stores.manage` | `POST /stores`, and assigning or removing a store's users |
| `products.read` | browsing the products of a store |
| `products.manage` | adding, editing and archiving products, **in a store the caller runs** (see [Products](#products)) |
| `inventory.manage` | reading and changing the stock, **in a store the caller runs** (see [Inventory](#inventory)) |
| `orders.place` | `POST /stores/{storeId}/orders` (see [Orders](#orders)) |
| `orders.read` | reading one's **own** orders |
| `orders.cancel` | `POST /orders/{orderId}/cancel`: a customer cancels their **own** order while it is pending |
| `orders.manage` | the orders of a store: list them, verify, ship, cancel, **in a store the caller runs** |

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

### Inventory

Each store has stock for its products: **how many units are on hand**, changed by the people who run the store.

```bash
curl -s -X POST http://localhost:8080/stores/$STORE_ID/products/$PRODUCT_ID/stock-adjustments -H "Authorization: Bearer $STORE_USER_TOKEN" \
  -H 'Content-Type: application/json' -d '{"delta": 12}'      # 200 { "productId": "...", "quantityOnHand": 12 }
curl -s -X POST ... -d '{"delta": -5}'                          # 200 ... "quantityOnHand": 7
curl -s "http://localhost:8080/stores/$STORE_ID/inventory?page=1" -H "Authorization: Bearer $STORE_USER_TOKEN"   # every product on sale, with its quantity
```

- **A change is an amount, never a new total.** `{"delta": 12}` means "12 more arrived"; there is no "set it to 40", because that would overwrite whatever changed while the person was counting. A change of zero, or of more than a billion either way, is a `400`.
- **The quantity is never below zero, however many requests arrive at once.** Each change is **one SQL statement with the limits inside it** (`UPDATE ... SET quantity = quantity + @delta WHERE ... AND quantity + @delta >= 0`, and an `INSERT ... ON CONFLICT DO UPDATE` for adding), not a read followed by a write: twenty requests removing one each from stock of five give exactly five `200` and fifteen `409`, and a quantity of zero. The table's `CHECK` is the last line of defence.
- **No row means none.** The row for a product is made by its first addition; before that it has no stock, removing is refused, and the list shows zero.
- **Stock belongs to a store.** An item carries its product's store, tied to it by a composite foreign key, so the two cannot disagree, and every statement names the store. Only a store user of that store may see or change it (the same ownership rule as products: a `404` for a store that is not yours); a customer or an admin gets `403`.

| Status | `code` | When |
|---|---|---|
| `409` | `Inventory.NotEnoughStock` | the change would take the quantity below zero (including removing from a product that has never had stock) |
| `409` | `Inventory.TooMuchStock` | the quantity would pass a billion |
| `404` | `Store.NotFound` / `Product.NotFound` | a store the caller does not run, an unknown product, an archived one, or another store's |

### Low stock

A store user can say at what quantity a product is running out, and ask which products have fallen to it.

```bash
curl -s -X PUT http://localhost:8080/stores/$STORE_ID/products/$PRODUCT_ID/low-stock-threshold -H "Authorization: Bearer $STORE_USER_TOKEN" \
  -H 'Content-Type: application/json' -d '{"threshold": 5}'   # 200 { "productId": "...", "productName": "Milk", "quantityOnHand": 7, "lowStockThreshold": 5, "isLow": false }
curl -s -X PUT ... -d '{"threshold": null}'                    # clears the level: the product is never low
curl -s "http://localhost:8080/stores/$STORE_ID/inventory?lowStock=true" -H "Authorization: Bearer $STORE_USER_TOKEN"   # only what is at or below its level
```

- **At the level counts as low.** A level of 5 is low at 5 and not at 6; a level of 0 means "tell me when it is gone". A product with **no level is never low**, even with nothing on hand: nobody asked to be told. The rule is written **once**, as an expression on the entity (`InventoryItem.IsLowExpression`): compiled, it answers `isLow` for a stock row in memory; handed to the query, Entity Framework turns the same expression into the SQL. Nothing else says what "low" means, so the two cannot drift.
- **The level lives on the stock row**, which exists only after a first adjustment. Setting a level for a product that has never had stock makes the row at zero, with the same upsert the adjustments use. A level is from 0 to a billion (`400` otherwise), and the table's `CHECK` says so too.
- **A setting, not an amount.** It is a `PUT`: sending the same body twice leaves the same state, and the last one wins. The statement writes **only the level**, never the quantity, so setting it cannot undo a sale at the same moment, and an adjustment or an order cannot undo the level.
- **The list is paged like every list**, and `isLow` and `lowStockThreshold` are on every entry of the ordinary inventory list too. `?lowStock=true` never shows an archived product, a product with no level, or another store's product; the total counts only the low ones. A value that is not a boolean is a `400`.
- **Why a read model.** "The live products whose stock is at or below its level" needs products and stock rows at once. Asking the stock table for its low rows and then the product table for names would page over archived products too, and a page would come back short while the total still counted them. This is the usual command/query split: a repository loads and saves what the domain changes, while a listing is a **read model** (`LowStockRepository`) that returns a view built for one screen. Writes still go through a table's own repository; a read model may join tables, but must take the store on every method and never write, and it is named in an architecture test so adding another is a visible decision.
- There is **no notification**: a list a person has to open is enough until there is email.

### Orders

A customer orders products from **one store**, and the stock is reserved at once.

```bash
curl -s -X POST http://localhost:8080/stores/$STORE_ID/orders -H "Authorization: Bearer $CUSTOMER_TOKEN" -H 'Content-Type: application/json' \
  -d '{"deliveryAddress":"1 High Street","lines":[{"productId":"'$MILK_ID'","quantity":2},{"productId":"'$BREAD_ID'","quantity":1}]}'
# 201 { "id": "...", "storeId": "...", "status": "Pending", "total": 6.10, "lines": [ { "productName": "Milk", "unitPrice": 2.50, "quantity": 2, "lineTotal": 5.00 }, ... ] }
curl -s "http://localhost:8080/orders?page=1" -H "Authorization: Bearer $CUSTOMER_TOKEN"        # my orders, newest first
curl -s http://localhost:8080/orders/$ORDER_ID -H "Authorization: Bearer $CUSTOMER_TOKEN"        # one of them
```

- **Only a customer orders.** A store user or an admin gets `403`. The customer is **the one in the token**, never a field in the body, so an order cannot be placed or read in someone else's name; someone else's order is `404 Order.NotFound`, exactly as an order that does not exist.
- **The stock is reserved when the order is placed, all or nothing.** Every line takes its stock with the guarded statement the [inventory](#inventory) uses; if any line cannot be filled, the stock the earlier lines took goes back and no order exists (`409 Order.InsufficientStock`, naming the product). So twenty customers ordering one each from stock of five give five orders and a quantity of zero, and the last item goes to exactly one customer.
- **A line is a product as it was bought.** Its name and unit price are copied, so a later price change, rename or archive does not rewrite the order. The total is computed from the lines.
- **A product may appear once** in an order (ask for a bigger quantity), an order has at most 100 lines, and a line asks for 1 to a billion. An unknown store, an unknown or archived product, and another store's product are `404`, and nothing is reserved.
- **Lines are reserved in order of product id**, whatever order they were asked for, so two orders for the same products cannot deadlock on each other's rows.
- A new order is `Pending`. The store user moves it to `Verified`, `Shipped` or `Cancelled` (see [Fulfilling orders](#fulfilling-orders)); there is no payment.
- **Placing an order is limited to 20 a minute per client address** (see [Rate limits](#rate-limits)).

| Status | `code` | When |
|---|---|---|
| `409` | `Order.InsufficientStock` | a line asks for more than the store has (including a product that has never had stock) |
| `404` | `Store.NotFound` / `Product.NotFound` | the store does not exist, or a product is unknown, archived, or another store's |
| `404` | `Order.NotFound` | no such order, or it is someone else's |
| `400` | | no lines, a product twice, a quantity of zero, a blank address, more than 100 lines |

### Fulfilling orders

The people who run a store see its orders and move them along. A customer follows their own order at `GET /orders/{orderId}`.

```bash
curl -s "http://localhost:8080/stores/$STORE_ID/orders?status=Pending" -H "Authorization: Bearer $STORE_USER_TOKEN"   # the queue, oldest first
curl -s -X POST http://localhost:8080/stores/$STORE_ID/orders/$ORDER_ID/verify -H "Authorization: Bearer $STORE_USER_TOKEN"   # Pending -> Verified
curl -s -X POST http://localhost:8080/stores/$STORE_ID/orders/$ORDER_ID/ship   -H "Authorization: Bearer $STORE_USER_TOKEN"   # Verified -> Shipped
curl -s -X POST http://localhost:8080/stores/$STORE_ID/orders/$ORDER_ID/cancel -H "Authorization: Bearer $STORE_USER_TOKEN"   # Pending or Verified -> Cancelled, stock back
```

```text
Pending ──verify──▶ Verified ──ship──▶ Shipped (final)
   │                    │
   └────────cancel──────┴──────────▶ Cancelled (final, stock back)
```

- **Only a store user, and only in their own store.** `orders.manage` says what kind of user; the membership (checked against the database on every request) says which store. A store the caller does not run is `404 Store.NotFound`, like one that does not exist; an order of another store, or an unknown order, is `404 Order.NotFound`, so nobody can probe for orders. A customer or an admin gets `403`.
- **The rules are in the entity.** `Order.Verify()`, `Ship()` and `Cancel()` are the only way to change a status, and each refuses a status it may not leave: `409 Order.InvalidTransition`, naming the status the order is in. Every combination of the four statuses and the three actions is a test.
- **When two people act at once, the database decides.** A move is one SQL statement that changes the row only if it still has the status that was read. Of two store users verifying the same order, one gets `200`; the other gets `409` (`Order.InvalidTransition` if they read it after the winner, `Order.ChangedMeanwhile` if they read it before and lost the write). Nothing is written twice.
- **Cancelling puts the stock back, once.** The status changes first and the stock goes back in the same transaction, so only the winner of a race restores: ten cancellations of one order restore its quantity once, and an order that shipped first is refused and its stock stays gone. Lines are restored in order of product id, like they are reserved, so two cancellations cannot deadlock. If a quantity cannot be put back because the store has since filled the stock to its limit, the answer is `409 Order.CannotRestoreStock` and nothing changes.
- **Shipping touches no stock**: it left when the order was placed.
- **A customer can cancel their own order, but only while it is `Pending`** (`POST /orders/{orderId}/cancel`, the `orders.cancel` permission). The entity's `Withdraw()` is legal from `Pending` only, so once the store has verified an order, only the store can cancel it: the customer gets `409 Order.InvalidTransition`. It ends in the same `Cancelled`, with the stock back, in one transaction, using the same compare-and-set: a customer cancelling while a store user verifies has exactly one winner, and the stock goes back only if the cancellation won. Someone else's order is `404 Order.NotFound`, exactly as one that does not exist; a store user or an admin gets `403` here.

| Status | `code` | When |
|---|---|---|
| `409` | `Order.InvalidTransition` | the move is not allowed from the status the order is in (shipping a pending order, cancelling a shipped one, a customer cancelling a verified one, anything on a final one) |
| `409` | `Order.ChangedMeanwhile` | someone moved the order between this request reading it and writing it |
| `409` | `Order.CannotRestoreStock` | cancelling would take a product's stock past its limit |
| `404` | `Store.NotFound` / `Order.NotFound` | not your store, or not an order of this store |
| `400` | | `?status=` is not `Pending`, `Verified`, `Shipped` or `Cancelled`; a bad `page` or `pageSize` |

### Paging

Every list is paged, with one set of limits: `?page=` counts **from 1** (at most 100 000), `?pageSize=` is **1 to 100** (20 by default). A value outside them is a `400` naming the parameter. The answer is `{ "items": [...], "page": 1, "pageSize": 20, "totalCount": 57 }`, and a page past the end has no items but still says `totalCount`. `GET /stores`, `GET /stores/{storeId}/products`, `GET /stores/{storeId}/inventory` and `GET /orders` are paged; the roles are not (there are three).

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

Four endpoints are limited per client address, each with its own counter:

| Endpoint | Limit | Why |
|---|---|---|
| `POST /auth/login` | 5 per minute | Stops password guessing, and stops guesses from costing the server a slow hash each |
| `POST /auth/register` | 10 per hour | Stops mass account creation and probing for registered emails (the `409`) |
| `POST /auth/refresh` | 30 per minute | Stops guessing and replaying refresh tokens |
| `POST /stores/{storeId}/orders` | 20 per minute | Placing an order takes stock and writes several rows, so a client hammering it costs the most |

The next request over a limit gets `429 Too Many Requests` as `application/problem+json`, with a `Retry-After` header (seconds, the length of that endpoint's window) and the code `RateLimit.TooManyRequests`. Every request counts, whether it succeeded, was refused, or had an invalid body. IPv6 clients are grouped by their `/64` prefix. Change the numbers in `GrocerEase.Api/appsettings.json` (`RateLimiting:Login`, `RateLimiting:Register`, `RateLimiting:Refresh` and `RateLimiting:PlaceOrder`, each with `PermitLimit` and `WindowSeconds`); the API refuses to start with a value outside 1-1000 requests or 1-3600 seconds.

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
