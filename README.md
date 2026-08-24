# WoodHeart

Interior commerce and consultation platform — home interior products plus interior design consultation booking, built for the Bangladeshi market.

**Architecture and roadmap:** [PLAN.md](PLAN.md)

| | |
|---|---|
| Backend | .NET 10 · ASP.NET Core Web API · Onion architecture · modular monolith |
| Frontend | Angular · SSR for the public storefront |
| Database | PostgreSQL 17 · EF Core 10 |
| Market | Bangladesh — BDT, Bangla/English, Cash on Delivery + bKash |

---

## Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | 10.0+ | `dotnet --version` |
| dotnet-ef | 10.0+ | `dotnet ef --version` |
| Node.js | see [frontend](#frontend) | `node --version` |
| Docker Desktop | any recent | `docker --version` |
| PostgreSQL | 17 (via Docker) | — |

Install the EF tools if missing:

```bash
dotnet tool install --global dotnet-ef --version 10.0.*
```

---

## Getting started

### 1. Start the database

```bash
docker compose -f deploy/docker/docker-compose.yml up -d
```

This starts PostgreSQL on `localhost:5432` (`woodheart` / `woodheart` / `woodheart`) and creates the `pg_trgm`, `unaccent` and `pgcrypto` extensions the schema needs.

Add pgAdmin on `localhost:5050` when you want it:

```bash
docker compose -f deploy/docker/docker-compose.yml --profile tools up -d
```

### 2. Set the JWT signing key

The API **refuses to start** without one, deliberately — a weak or shared signing key means anyone can mint an Admin token.

```bash
cd backend/src/WoodHeart.Api
dotnet user-secrets set "Jwt:SigningKey" "<at least 32 random characters>"
```

### 3. Apply migrations

```bash
cd backend
dotnet ef database update \
  --project src/WoodHeart.Infrastructure \
  --startup-project src/WoodHeart.Infrastructure \
  --context WoodHeartDbContext
```

### 4. Run the API

```bash
cd backend/src/WoodHeart.Api
dotnet run
```

| Endpoint | Purpose |
|---|---|
| `/scalar/v1` | Interactive API reference (development only) |
| `/openapi/v1.json` | OpenAPI document |
| `/health/live` | Liveness — does not touch the database |
| `/health/ready` | Readiness — checks dependencies |
| `/api/v1/diagnostics/ping` | Smoke test, returns UTC and Dhaka time |

### 5. Verify

```bash
curl http://localhost:5199/api/v1/diagnostics/ping

curl -X POST http://localhost:5199/api/v1/diagnostics/echo \
  -H "Content-Type: application/json" \
  -d '{"message":"hello","phoneNumber":"017-1234-5678"}'
```

The second call round-trips the entire pipeline and should normalise the phone number to `+8801712345678`. Send an empty `message` to see the validation pipeline return a `400` with a per-field `errors` dictionary.

---

## Solution layout

```
backend/src/
├─ WoodHeart.Domain           entities, value objects, domain events — zero dependencies
├─ WoodHeart.Application      use cases + ports (interfaces); implements none of them
├─ WoodHeart.Infrastructure   EF Core, Identity, payment/SMS/email adapters
└─ WoodHeart.Api              controllers, middleware, DI composition root
```

**The dependency rule:** always inward. `Domain` depends on nothing; `Api` is the only project that may reference `Infrastructure`.

This is enforced by `WoodHeart.ArchitectureTests` — a violation is a **build failure**, not a code-review opinion. If one of those tests fails, the fix is essentially never to relax the test; it is to move the code into the layer it belongs in.

Each layer is subdivided by **module** (Catalog, Inventory, Ordering, Payments, Promotions, Consultations, Notifications, Identity, Content) rather than by technical noun, so a module can later be extracted without an archaeological dig. Modules communicate through domain events and Application-layer ports, never by reaching into each other's data.

---

## Testing

```bash
cd backend
dotnet test                                     # everything
dotnet test tests/WoodHeart.ArchitectureTests   # the layer rules
dotnet test tests/WoodHeart.Domain.UnitTests    # business rules, no mocks
```

| Suite | What it covers |
|---|---|
| `Domain.UnitTests` | Money arithmetic, phone normalisation, slugs, `Result` — no mocks, no I/O |
| `Application.UnitTests` | Handler orchestration and validation, against fake ports |
| `Api.IntegrationTests` | The real pipeline in memory via `WebApplicationFactory` |
| `ArchitectureTests` | The dependency rules above |

Integration tests that need a database read `ConnectionStrings__Default` from the environment and skip cleanly when it is absent, so the suite runs on a machine with no Postgres.

---

## Conventions worth knowing before you write code

- **Business failures are `Result` values, not exceptions.** "Coupon expired" is not exceptional — it is Tuesday. Exceptions are for bugs and infrastructure faults.
- **Every error has a stable machine-readable code** (`ordering.insufficient_stock`) returned as the RFC 9457 `type`. The Angular client branches on the code, never on the English message.
- **Never call `DateTime.UtcNow`.** Inject `IDateTimeProvider`. Slots, discount windows and reservation expiry are all time-dependent and must be testable.
- **Money is `Money`, never `decimal` and never `double`.** Mixing currencies throws rather than producing a plausible wrong number.
- **Phone numbers are `PhoneNumber`.** Normalised to `+8801XXXXXXXXX` so the same customer typing their number four ways is recognised as one person.
- **Never mutate stock without writing a `StockMovement`.** The ledger is the truth; `OnHand` is a cached projection of it.
- **Snapshot prices onto order lines at placement.** An order that joins live to `Product` silently rewrites last month's invoices when a price changes.
- **Controllers contain no business logic.** Build a command, dispatch it, map the result.

---

## Frontend

Not yet scaffolded — see the note at the top of [PLAN.md §17](PLAN.md#17-immediate-next-steps) for the outstanding Node version decision.

---

## Documentation

| Path | Contents |
|---|---|
| [PLAN.md](PLAN.md) | Architecture, domain model, roadmap, open business questions |
| `docs/architecture/` | ADRs and diagrams |
| `docs/api/` | Exported OpenAPI spec |
| `docs/runbooks/` | Deploy, backup/restore, bKash go-live |
