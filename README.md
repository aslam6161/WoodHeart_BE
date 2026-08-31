# WoodHeart

Interior commerce and consultation platform — home interior products plus interior design consultation booking, built for the Bangladeshi market.

**Architecture and roadmap:** [PLAN.md](PLAN.md)
**How work moves through the repo:** [CONTRIBUTING.md](CONTRIBUTING.md) — branches, pull requests, CI/CD

| | |
|---|---|
| Backend | .NET 10 · ASP.NET Core Web API · layered (Domain / Repository / Service / Presentation) |
| Frontend | Angular 21 — **separate repo**, `WoodHeart_FE` |
| Database | PostgreSQL 17 · EF Core 10 — swappable, see [PLAN.md §11.1](PLAN.md#111-changing-the-database-provider-later) |
| Market | Bangladesh — BDT, Bangla/English, Cash on Delivery + bKash |

---

## Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | 10.0+ | `dotnet --version` |
| dotnet-ef | 10.0+ | `dotnet ef --version` |
| Node.js | only for the frontend repo | `node --version` |
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
cd backend/WoodHeart.Presentation
dotnet user-secrets set "Jwt:SigningKey" "<at least 32 random characters>"
```

### 3. Apply migrations

```bash
cd backend
dotnet ef database update \
  --project WoodHeart.Repository \
  --startup-project WoodHeart.Repository \
  --context DataContext
```

### 4. Run the API

```bash
cd backend/WoodHeart.Presentation
dotnet run
```

| Endpoint | Purpose |
|---|---|
| `/scalar/v1` | Interactive API reference (development only) |
| `/openapi/v1.json` | OpenAPI document |
| `/health/live` | Liveness — does not touch the database |
| `/health/ready` | Readiness — checks dependencies |
| `/api/diagnostics/ping` | Smoke test, returns UTC and Dhaka time |

### 5. Verify

```bash
curl http://localhost:5199/api/diagnostics/ping

curl -X POST http://localhost:5199/api/diagnostics/echo \
  -H "Content-Type: application/json" \
  -d '{"message":"hello","phoneNumber":"017-1234-5678"}'
```

The second call round-trips the entire pipeline and should normalise the phone number to `+8801712345678`. Send an empty `message` to see the validation pipeline return a `400` with a per-field `errors` dictionary.

---

## Solution layout

```
backend/
├─ WoodHeart.Domain/        entities, enums, constants, settings, value objects
├─ WoodHeart.Repository/    DataContext, Repository<T>, per-entity repositories, migrations
├─ WoodHeart.Service/       business logic, DTOs, service interfaces, infrastructure adapters
├─ WoodHeart.Presentation/  controllers, middleware, DI composition, configuration
└─ WoodHeart.Tests/         one test project, subdivided by feature
```

**The dependency rule:** each layer references only the one beneath it.
`Domain → Repository → Service → Presentation`. Domain references nothing of
ours; Presentation is the only project that composes the whole graph.

Enforced by `WoodHeart.Tests/Architecture` — a violation is a **build failure**,
not a code-review opinion. If one of those tests fails, the fix is essentially
never to relax the test; it is to move the code into the layer it belongs in.

Inside each layer, folders are named by **feature** (`Catalog`, `Inventory`,
`Ordering`, `Payments`, `Promotions`, `Consultations`, `Notifications`,
`Identity`) rather than by technical noun, and interfaces sit in a parallel
`Interfaces/` tree beside their implementations.

| Layer | Contains | Never contains |
|---|---|---|
| `Domain` | `BaseEntity`, entities, enums, `Money`, `PhoneNumber`, constants | queries, HTTP, business workflows |
| `Repository` | `DataContext`, `Repository<T>`, `IUnitOfWork`, migrations, seed | business rules, DTOs |
| `Service` | services, DTOs, mapping, payment/SMS adapters | controllers, `HttpContext` handling beyond `ICurrentUserService` |
| `Presentation` | controllers, middleware, `ApplicationServiceExtensions` | any decision a service should be making |

---

## Testing

```bash
cd backend
dotnet test                                              # everything
dotnet test --filter "FullyQualifiedName~Architecture"   # the layer rules
dotnet test --filter "FullyQualifiedName~Common"         # domain rules, no mocks
```

| Folder | What it covers |
|---|---|
| `Common/` | Money arithmetic, phone normalisation, slugs — no mocks, no I/O |
| `Diagnostics/` | Service orchestration against fake ports |
| `Architecture/` | The dependency rules above |
| `Integration/` | The real pipeline in memory via `WebApplicationFactory` |

The integration tests run with seeding disabled and exercise only DB-free
paths, so the suite passes on a machine with no PostgreSQL. Anything that
genuinely needs the database belongs in a Testcontainers-backed fixture.

---

## Conventions worth knowing before you write code

- **Business failures are `GeneralResponse` values, not exceptions.** "Coupon expired" is not exceptional — it is Tuesday. Exceptions are for bugs and infrastructure faults.
- **Every error has a stable machine-readable code** (`ordering.insufficient_stock`) in `GeneralResponse.ErrorCode`. The Angular client branches on the code, never on the English message, which gets reworded and translated to Bangla.
- **Only the service that owns a use case commits.** Stage work through repositories, commit once via `IUnitOfWork`. A helper that saves flushes its caller's half-finished work — see the comment on `IRepository<T>`.
- **Never call `DateTime.UtcNow`.** Inject `IDateTimeProvider`. Slots, discount windows and reservation expiry are all time-dependent and must be testable.
- **Money is `Money`, never `decimal` and never `double`.** Mixing currencies throws rather than producing a plausible wrong number.
- **Phone numbers are `PhoneNumber`.** Normalised to `+8801XXXXXXXXX` so the same customer typing their number four ways is recognised as one person.
- **Never mutate stock without writing a `StockMovement`.** The ledger is the truth; `OnHand` is a cached projection of it.
- **Snapshot prices onto order lines at placement.** An order that joins live to `Product` silently rewrites last month's invoices when a price changes.
- **Controllers contain no business logic.** Build a DTO, call a service, hand the result to `HandleResult`.

---

## Frontend

**Lives in a separate repository:** `WoodHeart_FE`, checked out beside this one.

```
D:\Personal_Projects\
├─ WoodHeart\        ← this repo  · github.com/aslam6161/WoodHeart_BE
└─ WoodHeart_Web\    ← Angular 21 · github.com/aslam6161/WoodHeart_FE
```

```bash
cd ../WoodHeart_Web        # repo: WoodHeart_FE
npm install
npm start            # http://localhost:4200
```

Run this API first — the storefront calls `/api/diagnostics/ping` on load and will report "Could not reach the API" without it.

### The contract between them

Three types are shared by convention, not by a package. If one changes here, change it there in the same pull request.

| This repo | WoodHeart_FE |
|---|---|
| `GeneralResponse` / `GeneralResponse<T>` | `_models/generalResponse.ts` |
| `PagedList<T>`, `PaginationParams`, `PaginationHeader` | `_models/pagination.ts` |
| The `X-Pagination` response header | `_services/paginationHelper.ts` |

Two rules that keep the seam honest:

- **Every response is a `GeneralResponse`**, validation failures and business failures alike. The client unpacks one envelope, not two.
- **`ErrorCode` is the contract; `Message` is not.** The client branches on `ordering.insufficient_stock`. The message is prose that gets reworded and translated to Bangla, and any client matching on it will break silently.

`X-Pagination` only reaches the browser because it is listed in `Access-Control-Expose-Headers` in [CorsExtension.cs](backend/WoodHeart.Presentation/Extensions/CorsExtension.cs). Remove it there and every pager in the app silently shows a single page.

---

## Documentation

| Path | Contents |
|---|---|
| [PLAN.md](PLAN.md) | Architecture, domain model, roadmap, open business questions |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Branching model, pull request flow, reviews, CI/CD |
| `docs/architecture/` | ADRs and diagrams |
| `docs/api/` | Exported OpenAPI spec |
| `docs/runbooks/` | Deploy, backup/restore, bKash go-live |
