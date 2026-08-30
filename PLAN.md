# WoodHeart — Interior Commerce & Consultation Platform

### Architecture & Implementation Plan

|                  |                                                                      |
| ---------------- | -------------------------------------------------------------------- |
| **Product**      | Online store for home interior items + interior consultation booking |
| **Market**       | Bangladesh (BDT, Bangla/English, bKash + Cash on Delivery)           |
| **Backend**      | .NET 10 (ASP.NET Core Web API), layered: Domain / Repository / Service / Presentation |
| **Frontend**     | Angular 21 (standalone, signals, zoneless, SSR) + Bootstrap 5, structured after IMSAnuglar |
| **Database**     | PostgreSQL 16+ via EF Core 10                                        |
| **Repos**        | `WoodHeart` (backend) + `WoodHeart_Web` (frontend), siblings in `D:\Personal_Projects` |
| **Status**       | **Phase 0 complete except the database run** — 73 backend + 8 frontend tests passing. Blocked only on Docker (§17). |
| **Last updated** | 2026-08-30                                                           |

---

## 1. Scope

### 1.1 What we are building

WoodHeart sells **home interior products** and sells **interior consultation as a service**. Two revenue lines, one platform.

**Product catalog (initial categories)**

| Group    | Items                                                    |
| -------- | -------------------------------------------------------- |
| Bedroom  | Bed, dressing table, dress closet / wardrobe, side table |
| Dining   | Dining table, dining wagon, dining chairs                |
| Living   | Mirror, showcase, sofa, centre table                     |
| Bath     | Basin cabinet, vanity, mirror cabinet                    |
| Lighting | Ceiling lighting, pendant, wall light                    |
| Decor    | Handicraft items, wall art, planters                     |

The category tree must be **admin-managed and unlimited-depth** — the list above is seed data, not a hardcoded enum.

**Core capabilities**

1. Public catalog with real merchandising (filters, variants, galleries, room-based collections)
2. Cart → checkout → order, for **both logged-in and guest** users
3. Payments: **Cash on Delivery live now**, **bKash built but toggleable**, admin-configurable provider registry
4. Inventory with a stock ledger, reservations, and made-to-order lead times
5. Discounts / coupons / campaigns engine
6. Notifications (Email + SMS + in-app) driven by domain events
7. **Consultation booking** — services, slots, site visits, deposits, reschedule
8. Three surfaces: **Public site**, **Customer dashboard**, **Admin dashboard**

### 1.2 Explicitly out of scope for v1

Marketplace / multi-vendor · Mobile apps · 3D room planner or AR placement · Loyalty points · Multi-warehouse fulfilment routing · Subscription billing · Card payments (SSLCOMMERZ)

All deliberately deferred. The architecture leaves a seam for each one.

---

## 2. Bangladesh-specific requirements

These are not afterthoughts; they shape the domain model.

| Concern            | Decision                                                                                                                                                                                       |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Currency**       | `BDT` only in v1. Money stored as `decimal(18,2)` plus an ISO currency code. Never `float`/`double`.                                                                                           |
| **Address model**  | Division → District → Upazila/Thana → Area → street line. **Not** the Western `state / zip` shape. Seeded reference data (8 divisions, 64 districts, ~495 upazilas). Postcode optional.        |
| **Phone**          | The primary identity handle. Store E.164 (`+8801XXXXXXXXX`), display local (`01XXXXXXXXX`). Phone beats email for reachability in this market; OTP login is a Phase 3 item.                    |
| **Delivery zones** | `Inside Dhaka` / `Dhaka Suburb` / `Outside Dhaka` bands, plus per-district overrides and a per-product surcharge for bulky furniture. Free-delivery threshold configurable.                    |
| **Courier**        | `ICourierProvider` port. Manual / own-delivery in v1; Pathao, Steadfast, RedX adapters later. Furniture is usually self-delivered, so manual must be first-class, not a fallback.              |
| **Payments**       | COD (with optional partial advance for made-to-order), bKash Tokenized Checkout. Nagad / Rocket / SSLCOMMERZ as future providers behind the same port.                                         |
| **COD risk**       | COD carries real refusal risk here. The order model supports an **advance payment percentage** on custom or large items, and a **customer risk flag** (repeat-refuser detection) from day one. |
| **VAT**            | Configurable rate plus a VAT-inclusive/exclusive pricing switch. Do **not** hardcode a rate — confirm with your accountant (see §16).                                                          |
| **Language**       | English first, Bangla second, via translatable content columns. Bengali numerals as a display-only toggle.                                                                                     |
| **Timezone**       | All timestamps stored `timestamptz` in UTC, presented in `Asia/Dhaka` (+06, no DST). Consultation slots are computed in Dhaka time, stored in UTC.                                             |
| **Holidays**       | Consultation availability must respect the Friday/Saturday weekend and a configurable holiday calendar (Eid, Pohela Boishakh, and so on).                                                      |

---

## 3. Technology decisions

### 3.1 Backend

| Choice                             | Version                                   | Why                                                                                                                                               | Rejected alternative                                                               |
| ---------------------------------- | ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| **.NET**                           | 10 (LTS)                                  | Already installed here (`10.0.301`). LTS = three years of support.                                                                                | .NET 8 — older, no reason to start there.                                          |
| **ASP.NET Core Web API**           | 10                                        | Controller-based, not Minimal APIs — better fit for a large command/query surface, cleaner filters and conventions, easier for a growing team.    | Minimal APIs — excellent for small services, verbose at this endpoint count.       |
| **PostgreSQL** | 17 | Free, strong JSONB (variant attributes), full-text search plus `pg_trgm` for fuzzy Bangla product names, cheap on any VPS. | **SQL Server** — licence cost on a VPS. Not a one-way door either way: see [§11.1](#111-changing-the-database-provider-later) for the exact seams a swap touches. |
| **EF Core**                        | 10 (`dotnet-ef 10.0.7` already installed) | Migrations, LINQ, compiled queries.                                                                                                               | Dapper — added as a _supplement_ for heavy report queries, not as the primary ORM. |
| **Service layer** (no mediator) | — | Services behind interfaces, matching Bento_BE. A dispatcher buys indirection this codebase does not need. | **Mediator / MediatR** — MediatR is commercial from v13 in any case. |
| **Mapperly** | 4.3.x | Source-generated mapping, compile-time safe, **MIT**. | **AutoMapper** — every version below 15.1.1 carries GHSA-rvv3-g6hj-g44x (DoS via uncontrolled recursion), and 15.x is commercially licensed. |
| **DataAnnotations + `ValidationFilter`** | in-box | Shape validation on the DTO; conditional business rules live in the service, not in a second rules engine beside it. | **FluentValidation** — excellent, but it splits the rules across two places. |
| **Serilog** + **OpenTelemetry**    | latest                                    | Structured logs (file/Seq) plus traces and metrics.                                                                                               | Bare `ILogger` — no correlation across a checkout flow.                            |
| **Built-in OpenAPI + Scalar UI**   | .NET 10 in-box                            | `Microsoft.AspNetCore.OpenApi` ships in the box now; Scalar renders the browsable UI.                                                             | Swashbuckle — no longer necessary.                                                 |
| **Hangfire**                       | latest                                    | Persistent background jobs with a dashboard: reservation expiry, notification retries, abandoned cart, low-stock digest, payment reconciliation.  | Quartz (no dashboard) or a bare `BackgroundService` (no persistence, no retry).    |
| **HybridCache**                    | .NET 10 in-box                            | L1 in-memory now, L2 Redis later with no code change.                                                                                             | Bare `IMemoryCache`.                                                               |
| **ImageSharp**                     | latest                                    | Server-side thumbnail and WebP/AVIF pipeline. Note the Six Labors split licence — free below the revenue threshold, worth reviewing as you scale. | Client-only resizing — bad for SEO and LCP.                                        |
| **xUnit + Shouldly + NSubstitute** | latest                                    | Testing. **Shouldly, not FluentAssertions** — FA v8 went commercial.                                                                              | —                                                                                  |
| **Testcontainers**                 | latest                                    | Real PostgreSQL in integration tests. **Requires Docker, which is not installed yet.**                                                            | The EF in-memory provider — it lies about SQL behaviour.                           |

### 3.2 Frontend

| Choice                                | Why                                                                                                                                                                                                                                                   |
| ------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Angular 21** | Standalone components throughout, **signals** for state, **zoneless** change detection, the `@if` / `@for` control flow, `inject()` over constructor injection. Chosen over 22 because 21 runs on Node `^20.19 \|\| ^22.12 \|\| >=24`, which this machine already satisfies; 22 requires Node 22.22.3+ for nothing this project uses. |
| **Angular SSR (`@angular/ssr`)**      | **Non-negotiable for the public site.** Furniture shopping starts on Google and Facebook. SSR plus prerendering gives crawlable product pages, correct OG tags on shares, and a fast LCP over 4G. Admin and customer dashboards stay client-rendered. |
| **Bootstrap 5 + Angular CDK** | Matches IMSAnuglar, so markup and conventions carry across. Bootstrap 5 drops the jQuery dependency that Bootstrap 4 carries. CDK supplies the accessible primitives (overlay, a11y, drag-drop) Bootstrap does not. |
| **@ngrx/signals (SignalStore)**       | Only for genuinely shared state: cart, auth/session, wishlist, admin filters. Everything else is local signals plus `httpResource`/`resource()`. **No full NgRx Store + Effects** — the ceremony is not worth it at this size.                        |
| **Transloco**                         | Runtime EN/BN switching without a second build (native Angular i18n requires one build per locale).                                                                                                                                                   |
| **Playwright**                        | E2E on the three journeys that must never break: guest checkout, member checkout, consultation booking.                                                                                                                                               |
| **Nx workspace** _(deferred)_         | Only worth it if a second app appears (say a delivery-rider PWA). Start with a plain Angular workspace and well-drawn internal boundaries.                                                                                                            |

---

## 4. Repository structure

**Two repositories, not a monorepo.** They deploy separately, are built by different toolchains, and are worked on at different times — and this matches how Bento_BE and IMSAnuglar already sit side by side.

```
D:\Personal_Projects\
├─ WoodHeart\          ← backend repo (this document lives here)
└─ WoodHeart_Web\      ← frontend repo
```

The backend is modelled on `D:\Chromatics\Bento_BE`, the frontend on `D:\Personal_Projects\IMSAnuglar`, so navigating either teaches the other.

### 4.1 Backend — `WoodHeart`

Projects sit flat inside `backend/`, as they sit flat at the root of Bento_BE.

```
WoodHeart\
├─ PLAN.md                        ← this file, the single source of truth
├─ README.md
├─ .gitignore  .editorconfig
│
├─ docs/
│   ├─ architecture/              ADRs, C4 diagrams, domain model notes
│   ├─ api/                       exported OpenAPI spec
│   └─ runbooks/                  deploy, backup/restore, bKash go-live
│
├─ backend/
│   ├─ WoodHeart.sln
│   ├─ Directory.Build.props  Directory.Packages.props
│   ├─ WoodHeart.Domain/         ← entities, enums, constants, settings, value objects
│   ├─ WoodHeart.Repository/     ← DataContext, Repository<T>, migrations, seed
│   ├─ WoodHeart.Service/        ← business logic, DTOs, adapters
│   ├─ WoodHeart.Presentation/   ← controllers, middleware, DI composition
│   └─ WoodHeart.Tests/          ← one project, feature folders
│
└─ deploy/
    ├─ docker/                    Dockerfile.api, docker-compose (Postgres, pgAdmin)
    ├─ nginx/                     reverse proxy + TLS
    └─ github-actions/            CI/CD workflows
```

### 4.2 Frontend — `WoodHeart_Web`

```
WoodHeart_Web\
├─ README.md  angular.json  package.json  .nvmrc
├─ .github/workflows/ci.yml
└─ src/
    ├─ environments/            environment.ts + environment.prod.ts
    ├─ styles.scss              Bootstrap 5 + brand tokens
    └─ app/                     see §9 for the full structure
```

### 4.3 The seam between them

Three types are shared by convention rather than by a package. Change one and the other changes in the same pull request.

| Backend | Frontend |
| ------- | -------- |
| `GeneralResponse` / `GeneralResponse<T>` | `_models/generalResponse.ts` |
| `PagedList<T>`, `PaginationParams`, `PaginationHeader` | `_models/pagination.ts` |
| The `X-Pagination` response header | `_services/paginationHelper.ts` |

**Why convention and not a shared package.** A generated client or an npm contract package is the textbook answer, and it is worth revisiting once the API surface stops moving. Today it would mean a publish step between every backend change and the frontend that consumes it, on a two-repo project with one developer. The three types above are small, stable and covered by tests on both sides; the cost of drift is currently lower than the cost of the machinery. Generating the client from the OpenAPI spec is a Phase 3 candidate, once the endpoint list has settled.

**Why two repos rather than one.** Independent deploy cadence — the storefront ships CSS fixes far more often than the API ships schema changes — and CI that does not run a .NET build because a stylesheet changed. The cost is real and worth naming: a change that spans both repos is two pull requests, and nothing mechanically stops the contract types drifting apart. That is what the table above exists to prevent.

---

## 5. Backend architecture — layered

### 5.1 The layers, and the one rule

```
            ┌──────────────────────────────────────────┐
            │         WoodHeart.Presentation           │  Controllers, middleware,
            │        (HTTP + composition root)         │  auth, DI wiring, Hangfire
            ├──────────────────────────────────────────┤
            │           WoodHeart.Service              │  Services, DTOs, mapping,
            │       (Business logic + adapters)        │  bKash/SMS/email adapters
            ├──────────────────────────────────────────┤
            │          WoodHeart.Repository            │  DataContext, Repository<T>,
            │              (Data access)               │  IUnitOfWork, migrations
            ├──────────────────────────────────────────┤
            │            WoodHeart.Domain              │  Entities, enums, constants,
            │          (The model — the core)          │  settings, value objects
            └──────────────────────────────────────────┘

   Dependency direction: each layer references only the one beneath it.
```

**The rule:** *no outer layer's type may appear in an inner layer's signature, and no inner layer may reference an outer assembly.* Enforced mechanically by `WoodHeart.Tests/Architecture` — a build failure, not a code-review opinion. Concretely:

- `Domain` references only ASP.NET Identity's EF package, because `AppUser` derives from `IdentityUser<long>`. No `DbContext`, no `DbSet`, no Npgsql.
- `Repository` owns persistence. It may not call a service.
- `Service` holds the business rules and may not know that HTTP exists — every service must be callable from a Hangfire job, which is where a good deal of this application's work runs.
- `Presentation` holds **no business logic**. A controller builds a DTO, calls a service, hands the result to `HandleResult`. If a controller contains an `if` about pricing, it is in the wrong layer.

### 5.2 Feature folders inside each layer

Every layer is subdivided by **feature**, not by technical noun — the same convention Bento uses, and what allows a module to be extracted later without an archaeological dig.

```
WoodHeart.Domain/
├─ Entity/
│   ├─ Common/          OutboxMessage, StoreSetting, FeatureFlag
│   ├─ Identity/        AppUser, AppRole, AppUserRole, UserRefreshToken
│   ├─ Catalog/         Product, ProductVariant, Category, Media, Review
│   ├─ Inventory/       StockItem, StockMovement, StockReservation
│   ├─ Ordering/        Customer, Address, Cart, CartLine, Order, OrderLine, Shipment
│   ├─ Promotions/      Discount, Coupon, PromotionRule, PromotionUsage
│   ├─ Payments/        Payment, PaymentAttempt, Refund, PaymentMethodConfig
│   ├─ Consultations/   ConsultationType, Consultant, AvailabilityRule, Booking
│   ├─ Notifications/   NotificationTemplate, Subscription
│   └─ Content/         Page, Banner, Collection, Faq, Testimonial
├─ Enums/<Feature>/
├─ ValueObjects/        Money, PhoneNumber, LocalizedText, Slug, EmailAddress
├─ Constants/           Roles, Policies, GlobalConstants, SettingKeys, FeatureFlags
├─ Settings/            JwtSettings, SmsSettings, BkashSettings…
└─ Helpers/             IDateTimeProvider
```

`Repository`, `Service` and `Tests` mirror these feature names:

```
WoodHeart.Repository/          WoodHeart.Service/
├─ Repositories/<Feature>/     ├─ Services/<Feature>/
├─ Interfaces/<Feature>/       ├─ Interfaces/<Feature>/
├─ Configurations/<Feature>/   ├─ DTOs/<Feature>/
├─ Migrations/                 ├─ Infrastructure/   Time, Correlation, Security, Payments
└─ Data/  Seed.cs              ├─ Mapping/
                               └─ Helper/  Exceptions/
```

**Module dependency policy:** a service may call another module's service through its interface, but never another module's repository directly. Side effects that must survive a crash — SMS, email — go through the outbox rather than a direct call.

### 5.3 Request shape

```
POST /api/orders
   → OrdersController.Place(PlaceOrderDto)          ← ValidationFilter has already run
   → IOrderService.PlaceAsync(dto)
        │  inside the service:
        │    1. load the cart, validate the rules
        │    2. IUnitOfWork.ExecuteInTransactionAsync:
        │         draw down stock, write the order, write the payment,
        │         queue the confirmation SMS into the outbox
        │    3. one commit — all of it, or none of it
        └──▶ GeneralResponse<OrderDto>
   → BaseApiController.HandleResult → 200 / 4xx
```

- **Services return `GeneralResponse<T>`.** Business failures are *values*, not exceptions — exceptions are reserved for bugs and infrastructure faults, which is what lets the exception middleware treat everything it catches as a 500 worth investigating.
- **Every failure carries a stable `ErrorCode`** (`ordering.insufficient_stock`). Angular branches on the code; the message is prose that gets reworded and translated to Bangla.
- **Only the entry-point service commits.** Repositories stage; `IUnitOfWork` commits. This is the one place WoodHeart deliberately departs from Bento, whose `IRepository.SaveAllAsync` is the pattern behind its own recorded `notification-insert-commits-callers-pending-changes` bug.
- **Reads may bypass repositories** and project straight to DTOs with `.Select()`. Read paths are allowed to be pragmatic; write paths are not.

---

## 6. Domain model — key aggregates

### 6.1 Catalog

```
Product (aggregate root)
  Id, Sku, Name{en,bn}, Slug, ShortDescription, Description{en,bn}
  CategoryId, BrandId, ProductType (Stocked | MadeToOrder | Service)
  BasePrice: Money, CompareAtPrice: Money?
  Dimensions (L×W×H cm), WeightKg, Material, FinishType, WarrantyMonths
  LeadTimeDays (made-to-order), AssemblyRequired, DeliverySurcharge: Money?
  Status (Draft | Active | Archived), PublishedAt
  SeoMeta (Title, Description, OgImage)
  → Variants[]   → Media[]   → Attributes[]   → Reviews[]

ProductVariant
  Id, ProductId, Sku, VariantName, PriceOverride: Money?
  OptionValues[]  (Wood=Segun, Size=6ft, Finish=Matte, Fabric=Velvet)
  Barcode, IsDefault, Media[]
```

**Why variants matter here:** a bed in Segun vs Mehogoni at two sizes is four SKUs with four prices and four stock counts. Modelling that as four separate products destroys reviews, SEO and merchandising. Get this right on day one — retrofitting variants onto a flat catalog is one of the most expensive refactors in commerce software.

**Room-based merchandising:** `Collection` (many-to-many with `Product`) powers "Shop the Bedroom", "Minimalist Living", "Eid Collection". Curated sets are how interior brands actually sell — cheap to build, high conversion.

### 6.2 Inventory

```
StockItem         VariantId, WarehouseId, OnHand, Reserved, Available (computed), ReorderLevel
StockMovement     (append-only ledger)
                  Type: Purchase | Sale | Return | Adjustment | Damage | TransferIn | TransferOut
                  Quantity(±), Reference (OrderId / PurchaseOrderId), Reason, PerformedBy, OccurredAt
StockReservation  VariantId, Quantity, CartId | OrderId, ExpiresAt, Status
```

**Never mutate a stock number without writing a `StockMovement`.** `OnHand` is a cached projection of the ledger; the ledger is the truth. This is what makes "why does the system say 5 when we have 3 beds" an answerable question instead of a mystery.

Made-to-order products skip reservation entirely and carry `LeadTimeDays` onto the order line instead.

### 6.3 Ordering

```
Cart    Id, CustomerId?, AnonymousToken?, Currency, Lines[], AppliedCoupons[],
        Subtotal, DiscountTotal, DeliveryFee, VatAmount, GrandTotal, ExpiresAt

Order   OrderNumber (WH-YYMM-#####), CustomerId?, GuestContact?,
        ShippingAddress, BillingAddress,
        Lines[]  ← price, product name and discount SNAPSHOTTED at placement
        Status, PaymentStatus, FulfilmentStatus, Totals, PlacedAt, Notes,
        RequiredAdvanceAmount, Timeline[] (append-only status history)
```

**Order status machine**

```
Pending → Confirmed → Processing → ReadyToShip → Shipped → Delivered → Completed
   │           │            │                                    │
   └──────── Cancelled ─────┘                              Returned → Refunded
```

Payment status is **separate and orthogonal**: `Unpaid | AdvancePaid | Paid | PartiallyRefunded | Refunded | Failed`. A COD order is `Confirmed` + `Unpaid` for its entire life until delivery. Conflating the two axes is a classic modelling mistake that makes COD reporting impossible.

Every transition writes an `OrderTimelineEntry` (who, when, from, to, note). Admins will ask "who cancelled this order", and there has to be an answer.

**Line-item snapshotting is mandatory.** An order line stores the price, product name and applied discount _as they were at placement_. If it joins live to `Product`, then editing a price next month silently rewrites last month's invoices and your accounts stop reconciling.

### 6.4 Payments — the configurable provider registry

```
PaymentMethodConfig   (DB-backed, admin-editable, no redeploy needed)
  Code ("cod" | "bkash" | "nagad" | "sslcommerz")
  DisplayName{en,bn}, Description, IconUrl, IsEnabled, SortOrder, Mode (Sandbox | Live)
  MinOrderAmount?, MaxOrderAmount?, AllowedDeliveryZones[], AllowedCustomerGroups[]
  ExtraChargeType (None | Fixed | Percent), ExtraChargeValue
  Credentials  ← ENCRYPTED JSON blob (ASP.NET Data Protection); never returned to any client
```

```csharp
// Application layer — the port
public interface IPaymentProvider
{
    string Code { get; }
    PaymentCapabilities Capabilities { get; }   // refunds? webhooks? redirect flow?
    Task<InitiateResult>      InitiateAsync(PaymentContext ctx, CancellationToken ct);
    Task<ExecuteResult>       ExecuteAsync(string reference, CancellationToken ct);
    Task<PaymentStatusResult> QueryAsync(string reference, CancellationToken ct);
    Task<RefundResult>        RefundAsync(RefundRequest req, CancellationToken ct);
}
```

`IPaymentProviderResolver` returns only providers that are (a) registered in DI, (b) enabled in `PaymentMethodConfig`, and (c) eligible for _this_ cart (amount band, zone, customer group). The admin toggles bKash on or off, swaps sandbox for live credentials, and sets a COD advance rule — **all without a redeploy**. That is precisely the "admin can configure the payment system" requirement.

**COD provider** — `InitiateAsync` simply returns `Confirmed`; no external call. Optionally it requires an advance through another provider when the order total exceeds a configured threshold or the order contains a made-to-order line.

**bKash provider** — Tokenized Checkout: `grant-token` (cached ~55 min, renewed via `refresh-token`) → `create` → customer redirected to bKash → `execute` → `query` for reconciliation. Hard requirements: an **idempotency key per attempt**, every request and response persisted to `PaymentAttempt`, a **scheduled status-query reconciliation job** for the classic "customer paid but the callback never arrived" case, and refunds via `refund` + `refund-status`. Build it now behind `IsEnabled = false`; flip it on the day the merchant account is approved.

### 6.5 Promotions and discounts

```
Discount
  Name, Code?  (null ⇒ automatic promotion, no code needed)
  Type (Percentage | FixedAmount | FreeShipping | BuyXGetY | BundlePrice)
  Value, MaxDiscountAmount        ← caps the damage on "20% off"
  Scope (Cart | LineItem | Shipping)
  Conditions[] — MinSubtotal, Categories[], Products[], CustomerGroups[],
                 FirstOrderOnly, MinQuantity, DeliveryZones[], PaymentMethods[]
  StartsAt, EndsAt, UsageLimitTotal, UsageLimitPerCustomer, Stackable, Priority, Status

PromotionUsage   DiscountId, OrderId, CustomerId, Amount, UsedAt   ← enforces the limits
```

The **discount engine** is a pure, deterministic function living in the Domain layer:

```
DiscountEngine.Evaluate(cart, candidateDiscounts, customerContext) → DiscountResult[]
```

No database access, no clock, no randomness — therefore exhaustively unit-testable, and the _same_ engine runs at cart preview and at order placement, so the two can never disagree. Non-stackable discounts resolve by `Priority`, then by best value to the customer. **Discount amounts are always recomputed server-side at placement and snapshotted onto the order** — a client-supplied total is never trusted.

### 6.6 Consultations

```
ConsultationService    Name, Description, Mode (Online | InStudio | SiteVisit),
                       DurationMinutes, Fee: Money (0 = free), RequiresAdvance,
                       AdvanceAmount, BufferBeforeMinutes, BufferAfterMinutes, IsActive
Consultant             Name, Photo, Bio, Specialities[], ServiceIds[], IsActive
AvailabilityRule       ConsultantId, DayOfWeek, StartTime, EndTime, SlotMinutes
AvailabilityException  ConsultantId, Date, IsClosed | custom window   ← holidays, leave
Booking                BookingNumber, ServiceId, ConsultantId?, CustomerId? | GuestContact,
                       ScheduledAtUtc, Status (Requested | Confirmed | Rescheduled |
                       Completed | Cancelled | NoShow),
                       SiteAddress?, ProjectBrief, BudgetRange, RoomTypes[],
                       PaymentId?, InternalNotes, Timeline[]
```

Slot generation = `AvailabilityRule` − `AvailabilityException` − existing bookings − buffers, computed in `Asia/Dhaka` and stored in UTC.

**Double-booking is prevented at the database level** with a unique index on `(ConsultantId, ScheduledAtUtc)` filtered to active statuses. An application-level check loses that race under concurrency, and losing it means two customers in your studio at 4pm.

A completed consultation can be converted into a **Quotation**, and a quotation into an Order. That funnel is what makes consultation profitable rather than a cost centre, and it is the reason `Consultations` and `Ordering` share one customer identity.

### 6.7 Notifications

```
NotificationTemplate  Code ("order.placed"), Channel (Email | Sms | InApp | Push),
                      Subject{en,bn}, Body{en,bn} (Scriban / Liquid), IsEnabled
NotificationMessage   TemplateCode, Channel, Recipient, RenderedSubject, RenderedBody,
                      Payload, Status (Queued | Sent | Failed | Suppressed),
                      Attempts, LastError, SentAt
```

**Flow:** domain event → handler builds a `NotificationRequest` → a row is written to the **outbox inside the same database transaction as the business change** → a Hangfire worker renders the template and calls `IEmailSender` / `ISmsSender` → exponential-backoff retry → final status recorded.

The outbox is the entire point: an order is never confirmed without its notification being _guaranteed_ to eventually send, and an SMS-gateway outage never rolls back an order. SMS costs real money in Bangladesh (roughly 0.25–0.45 BDT per message), so each template is individually toggleable and previewable by an admin before it goes live.

Event → notification map for v1:

`OrderPlaced` · `OrderConfirmed` · `OrderShipped` · `OrderDelivered` · `OrderCancelled` · `PaymentReceived` · `PaymentFailed` · `RefundIssued` · `BookingRequested` · `BookingConfirmed` · `BookingReminder` (T−24h and T−2h) · `AbandonedCart` (T+4h) · `LowStock` (admin) · `NewOrderReceived` (admin)

---

## 7. Cross-cutting concerns

| Concern                  | Approach                                                                                                                                                                                                                                                      |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Domain events**        | Raised on the aggregate, collected by `SaveChangesAsync`, dispatched **after** the transaction commits. In-process now; a message broker later needs only a new dispatcher.                                                                                   |
| **Transactional outbox** | One `OutboxMessage` table for both notifications and future integration events. Guarantees "business change and its side effect succeed or fail together".                                                                                                    |
| **Auditing**             | `ICurrentUser` + an EF interceptor stamps `CreatedBy/At`, `ModifiedBy/At`. A separate `AuditLog` records before/after JSON for sensitive entities: prices, stock, discounts, payment config, roles.                                                           |
| **Soft delete**          | `ISoftDeletable` plus a global query filter. Products and orders are _never_ hard-deleted; reference data may be.                                                                                                                                             |
| **Concurrency**          | `xmin` as the row version on Order, StockItem and Booking. A concurrency conflict returns `409` with a clear message rather than silently overwriting.                                                                                                        |
| **Idempotency**          | An `Idempotency-Key` header on `POST /orders`, `/payments/*` and `/bookings`. Keys stored with the response for 24h; a replay returns the original response instead of creating a duplicate order. Essential on flaky mobile networks where users double-tap. |
| **Caching**              | `HybridCache` for the category tree, active discounts, payment config and CMS content. Keys are tagged by module and evicted on the relevant domain event.                                                                                                    |
| **Rate limiting**        | ASP.NET Core built-in limiter: strict on auth, OTP, coupon validation and checkout; relaxed on catalog browsing.                                                                                                                                              |
| **Correlation**          | A `X-Correlation-Id` flows Angular → API → logs → outbound gateway calls. Non-negotiable for debugging a payment that "just failed".                                                                                                                          |
| **Feature flags**        | Simple DB-backed flags (`bkash.enabled`, `reviews.enabled`, `consultation.deposit.required`) read through `IFeatureManager`.                                                                                                                                  |

---

## 8. API surface (outline)

Base path `/api`, route template `api/[controller]` as in Bento_BE. JSON, page/size pagination via `PaginationParams` with the counts returned in an `X-Pagination` header, and one error shape everywhere: `GeneralResponse` carrying a stable `errorCode`.

**Public — no auth**

```
GET  /catalog/categories                 tree
GET  /catalog/products                   filter, sort, facets, search, paging
GET  /catalog/products/{slug}            detail + variants + media + related
GET  /catalog/collections/{slug}
GET  /content/pages/{slug}  /banners  /faqs  /testimonials
GET  /geo/divisions  /districts  /upazilas
POST /carts                              create anonymous cart → token
GET  /carts/{token}
POST /carts/{token}/lines                add / update / remove
POST /carts/{token}/coupons              apply / remove
POST /carts/{token}/estimate             delivery fee + VAT + discount preview
GET  /checkout/payment-methods           only the ones eligible for THIS cart
POST /checkout/place-order               guest or authenticated
GET  /consultations/services  /consultants
GET  /consultations/availability?serviceId=&consultantId=&from=&to=
POST /consultations/bookings             guest booking allowed
POST /newsletter/subscribe
```

**Auth**

```
POST /auth/register  /login  /refresh  /logout
POST /auth/forgot-password  /reset-password
POST /auth/otp/request  /otp/verify      (Phase 3)
```

**Customer — role: Customer**

```
GET  /me  /me/orders  /me/orders/{id}  /me/bookings
GET/POST/PUT/DELETE  /me/addresses
POST /me/orders/{id}/cancel  /return-request
GET  /me/wishlist            POST/DELETE /me/wishlist/{productId}
POST /me/reviews
GET  /me/notifications
```

**Admin — role: Admin | Manager | StaffScoped**

```
/admin/products  /variants  /categories  /brands  /collections  /media   CRUD + bulk
/admin/inventory/stock  /movements  /adjustments  /low-stock
/admin/orders            list, detail, status transitions, invoice PDF, timeline
/admin/payments          transactions, manual COD settle, refunds, reconciliation
/admin/payment-methods   ← enable/disable, credentials, rules  (the config module)
/admin/discounts  /coupons  /usage-report
/admin/consultations/bookings  /consultants  /availability  /services
/admin/customers  /groups  /risk-flags
/admin/notifications/templates  /messages  /resend
/admin/content/pages  /banners  /faqs
/admin/settings          store info, VAT, delivery zones, currency, SMS/email gateways
/admin/reports           sales, top products, stock valuation, discount cost, funnel
```

**Versioning:** none in v1, matching Bento_BE. There is a single first-party client and it deploys with the API, so a version segment would be ceremony. If a second consumer ever appears — a mobile app, a partner integration — that is the moment to add `/api/v2`, and the route template makes it a one-line change.

---

## 9. Frontend architecture

Lives in the **`WoodHeart_Web`** repository (§4.2). Modelled on `D:\Personal_Projects\IMSAnuglar`, so that navigating one Angular project teaches the other. Folder names, the underscore-prefixed shared directories, and the shared contract types are all carried across verbatim.

### 9.1 Structure

```
WoodHeart_Web/src/
├─ environments/            environment.ts + environment.prod.ts (apiUrl)
├─ styles.scss             Bootstrap 5 + brand tokens
└─ app/
    ├─ _directives/         hasRole, onlyNumber, lazyImg
    ├─ _forms/              reusable form-control components
    ├─ _guards/             auth, admin, staff, preventUnsavedChanges
    ├─ _interceptors/       jwt, error, loading
    ├─ _layout/
    │   ├─ default-layout/      storefront shell (nav + footer)
    │   ├─ admin-layout/        admin shell (sidebar + navbar + footer)
    │   └─ adminComponents/     admin-navbar, admin-sidebar, admin-footer
    ├─ _models/             one interface per model, + dtos/
    │                       generalResponse.ts, pagination.ts, user.ts…
    ├─ _resolvers/
    ├─ _services/           one per feature + paginationHelper, busy, toast
    │
    ├─ home/  nav/  footer/  errors/  search/
    ├─ Items/               product-list, product-detail, product-card,
    │                       product-filter, user-cart, checkout
    ├─ User/                account dashboard, orders, addresses, wishlist
    ├─ authentication/      login, register, phone-verify
    └─ admin/               dashboard, products, categories, orders,
                            inventory, discounts, consultations, settings
```

### 9.2 Conventions

Carried over from IMSAngular:

- **`_`-prefixed folders for shared concerns**, feature folders for everything else. The prefix sorts them to the top, which is the whole point.
- **One service per feature**, `providedIn: 'root'`, built on `environment.apiUrl`.
- **`GeneralResponse` is the response envelope**, and `Pagination` / `PaginatedResult` / `PaginationParams` the paging contract — matching the backend types of the same names. `paginationHelper` reads the counts from the `X-Pagination` header.
- **Three interceptors**: jwt, error, loading. Same names, same jobs.
- **Guards per role**: auth, admin, staff, plus preventUnsavedChanges.

Modernised, because the Angular 12 idioms no longer apply on 21:

- **Standalone components.** No `NgModule` anywhere, so there is no `shared.module.ts` to keep in sync.
- **Signals rather than `BehaviorSubject` + `take(1)`.** IMSAngular's jwt interceptor subscribes to `currentUser$` and reads the value out of the callback — correct, but it reads as async code that only works because the subject happens to be synchronous. A signal is simply read.
- **Functional interceptors and guards** (`HttpInterceptorFn`, `CanActivateFn`) rather than class-based.
- **Zoneless change detection** from day one. Retrofitting it later is a slog.
- **Bootstrap 5, no jQuery.** Bootstrap 4 in IMSAngular needs jQuery and Popper; 5 needs neither, and that is ~90 KB of JavaScript a customer never downloads.
- **SSR for the storefront.** This is the commercially significant one: a client-rendered catalog is one Google cannot index, and organic search is how a furniture brand is found.
- **Every route lazy-loaded** via `loadComponent` / `loadChildren`. The admin bundle must never reach a public visitor.

### 9.3 Performance

- **Budget:** initial bundle 600 KB raw / 700 KB error, enforced by the production build in CI. The figure that actually matters is the **gzipped transfer size** — currently ~105 KB — because Bootstrap's CSS is 227 KB raw but ~23 KB over the wire. Raw-byte budgets systematically overstate CSS.
- **Images:** `NgOptimizedImage`, AVIF/WebP with fallbacks, explicit width and height, blur-up placeholders. A furniture site is 80% photography — this *is* the performance work.
- **Route-level SEO:** each public route sets title, meta description, canonical URL, OG tags and JSON-LD (`Product`, `Offer`, `BreadcrumbList`, `LocalBusiness`).
- **Accessibility:** keyboard-navigable, visible focus rings, labelled controls, `aria-live` on cart updates. Target WCAG 2.1 AA.

### 9.4 The three surfaces

| Surface            | Rendering       | Auth                        | Notes                                                                                                       |
| ------------------ | --------------- | --------------------------- | ----------------------------------------------------------------------------------------------------------- |
| Public site        | SSR + prerender | Anonymous or optional login | SEO-critical. Product and category pages prerendered where possible, revalidated on publish.                |
| Customer dashboard | CSR             | Customer role               | Orders, bookings, addresses, wishlist, reviews.                                                             |
| Admin dashboard    | CSR             | Admin/Manager/Staff role    | Dense tables, bulk actions, inline edit, image upload with crop, chart-based reports. Lazy-loaded behind a role guard, so it costs a public visitor zero bytes. |

---

## 10. Key flows

### 10.1 Guest and member checkout (one path, not two)

```
Cart (anonymous token in localStorage, cart row in DB)
  │
  ├─ Step 1  Contact       name, phone (required), email (optional)
  ├─ Step 2  Delivery      division → district → upazila → area → address
  │                        → delivery fee recalculated live
  ├─ Step 3  Payment       only eligible methods from PaymentMethodConfig
  ├─ Step 4  Review        server-recomputed totals, coupon re-validated
  │
  └─ POST /checkout/place-order  (with Idempotency-Key)
         ├─ revalidate stock and reserve it
         ├─ re-run the discount engine (never trust client totals)
         ├─ recompute delivery fee and VAT
         ├─ create Order (Pending)
         ├─ IPaymentProvider.InitiateAsync
         │     COD   → Confirmed immediately
         │     bKash → return redirect URL; order stays Pending until execute
         ├─ raise OrderPlaced → outbox → SMS + email
         └─ return order number + tracking link
```

A guest order stores `GuestContact` and is **claimable**: if the guest later registers with the same phone number, the orders link to the new account automatically. This single detail converts a large share of guests into repeat customers, and it costs almost nothing to build up front.

Cart merge on login: anonymous cart lines merge into the member cart, quantities summed, stock re-checked.

### 10.2 Stock reservation lifecycle

```
add to cart      → no reservation (avoid locking stock for browsers)
begin checkout   → soft reserve, ExpiresAt = now + 30 min
order placed     → reservation bound to the order
payment success  → StockMovement (Sale), reservation released, OnHand decremented
payment failure
  or 30 min idle → Hangfire job releases the reservation, stock returns to Available
order cancelled  → StockMovement (Return) if it was already deducted
```

### 10.3 bKash payment (once enabled)

```
place order → InitiateAsync → bKash create → redirect customer
   ↳ success callback  → ExecuteAsync → PaymentStatus = Paid → Order = Confirmed
   ↳ failure/cancel    → Order stays Pending, cart restored, customer can retry
   ↳ NO callback       → reconciliation job runs QueryAsync every 5 min for 2 hours,
                         then flags the order for manual admin review
```

Every attempt is persisted with the full request and response payload. When a customer says "টাকা কেটে নিয়েছে" (the money was taken), you need the transaction ID and the raw gateway response in front of you within seconds.

### 10.4 Consultation booking

```
choose service → choose consultant (or "any") → calendar shows generated slots
  → pick slot → brief form (room types, budget range, project description, photos)
  → if RequiresAdvance: pay deposit ; else: submit
  → Booking(Requested) → admin confirms → SMS + email
  → reminders at T−24h and T−2h
  → after the meeting: admin marks Completed, attaches notes, optionally creates a Quotation
```

### 10.5 Admin configuring a payment method

```
Admin → Settings → Payment Methods
  toggle "bKash"  → enter merchant credentials (encrypted at rest, write-only in the UI)
  choose Sandbox / Live · set min-max order amount · restrict by zone or customer group
  set an extra charge (fixed or percentage) · reorder the list
  Save → cache invalidated → the checkout page reflects it on the next request
```

No deployment, no config-file edit, no developer involvement. That is the requirement, implemented literally.

---

## 11. Data and persistence

- **Naming:** `snake_case` tables and columns (PostgreSQL convention) via an EF naming convention. Plural tables.
- **Keys:** `long` identity on `BaseEntity` — narrower indexes than a GUID, and readable down a phone line. Human-facing identifiers (`OrderNumber`, `BookingNumber`) are separate, sequential and prefixed (`WH-2608-00042`), so nothing a customer or courier quotes aloud exposes the internal key or the order volume.
- **Money:** `decimal(18,2)` + currency code, mapped as an owned type. `Money` is a value object with arithmetic that refuses to mix currencies.
- **Translations:** owned JSONB column `{ "en": "...", "bn": "..." }` for names and descriptions. Simpler than a translation table, and Postgres indexes JSONB well.
- **Variant attributes:** JSONB, with a GIN index for faceted filtering.
- **Search:** Postgres `tsvector` + `pg_trgm` for product search with fuzzy matching. No Elasticsearch until the catalog exceeds ~10k SKUs.
- **Migrations:** EF Core migrations, one per logical change, reviewed like code. Seed data (divisions, districts, upazilas, categories, roles, notification templates, payment methods) lives in idempotent seeders, not in migrations.
- **Indexes to create deliberately:** product slug (unique), variant SKU (unique), order number (unique), `(customer_id, placed_at desc)`, `(status, placed_at)`, stock `(variant_id, warehouse_id)` unique, booking `(consultant_id, scheduled_at)` unique-filtered, coupon code (unique, case-insensitive).
- **Backups:** nightly `pg_dump` to off-server storage, plus WAL archiving once order volume justifies it. **A restore is tested before go-live** — an untested backup is a rumour.

### 11.1 Changing the database provider later

PostgreSQL is the v1 choice, but it is not a one-way door. Moving to SQL Server — or MySQL, or anything else EF Core supports — is a contained job, and this section exists so that a future swap is a checklist rather than an archaeology exercise.

**What does not change.** Entities, repositories, services, controllers, DTOs, LINQ queries, and every test. That is the overwhelming majority of the code, and it is the whole reason for going through EF Core and `IRepository<T>` rather than writing SQL in services.

**What does change** — the complete list as of Phase 0, five code seams and four packages:

| Seam | Where | SQL Server equivalent |
| ---- | ----- | --------------------- |
| Provider registration + retry policy | `ApplicationServiceExtensions.AddDataAccess`, `DesignTimeDataContextFactory` | `UseSqlServer(..., o => o.EnableRetryOnFailure())` |
| Concurrency token | `DataContext.ApplyConcurrencyTokens` maps `Version` to the `xmin` system column, type `xid` | `rowversion` — and `BaseEntity.Version` changes from `uint` to `byte[]` |
| JSON column type | `OutboxMessageConfiguration` — `HasColumnType("jsonb")` | `nvarchar(max)`, or the native `json` type on SQL Server 2025+ |
| Outbox claim query | `OutboxRepository.ClaimDueBatchAsync` — `FOR UPDATE SKIP LOCKED` | `WITH (UPDLOCK, READPAST)` |
| Naming convention | `UseSnakeCaseNamingConvention()` | Drop it for SQL Server's PascalCase, or keep it — it works on either |
| Background jobs | `Hangfire.PostgreSql` | `Hangfire.SqlServer` |
| Health check | `AddNpgSql` | `AddSqlServer` |
| Migrations | `WoodHeart.Repository/Migrations/` | Delete and regenerate — migration SQL is provider-specific and cannot be translated |
| Local environment | `deploy/docker/docker-compose.yml` + the `pg_trgm` / `unaccent` / `pgcrypto` init script | A SQL Server image; the extensions have no equivalent |

**The one genuine leak.** `BaseEntity.Version` is `uint` because that is what `xmin` is. SQL Server's `rowversion` is `byte[]`. So a provider change touches the Domain layer in exactly one property — worth knowing, because everything else in Domain is provider-agnostic and that is not an accident.

**The part that is not a find-and-replace: search.** `tsvector` and `pg_trgm` are what make fuzzy Bangla product search work without a separate search server. SQL Server's full-text search is a different tool with different behaviour, and porting that is redesign, not translation. This is the cost that grows: today a swap is roughly a day's work, and it gets more expensive with every Postgres-specific feature Phase 1 onward adds.

**Practical guidance.** If SQL Server is a real possibility — an existing licence, a host that only offers it, a team that knows it — decide **before Phase 1 builds catalog search**, because that is the point where the cost stops being flat. After that, the honest answer is to keep PostgreSQL unless there is a business reason worth paying a redesign for.

**What not to do.** Do not add a provider-abstraction layer now to "keep options open". EF Core already is that abstraction; a second one on top would cost more to build and maintain than the swap it is insuring against, and it would make every query harder to read in exchange for a migration that may never happen. The nine rows above are cheaper than an abstraction, and they are honest about where the real cost sits.

---

## 12. Security

| Area                      | Measure                                                                                                                                                                                                                                                                      |
| ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **AuthN**                 | ASP.NET Core Identity + JWT access token (15 min) and refresh token (14 days, rotated, stored hashed, revocable). Refresh token in an `HttpOnly` `Secure` `SameSite=Strict` cookie; access token in memory only — never `localStorage`.                                      |
| **AuthZ**                 | Roles (Admin, Manager, Staff, Customer) plus permission claims for finer control (`orders.refund`, `products.publish`, `settings.payments`). Policy-based, checked in an Application pipeline behaviour so it cannot be bypassed by a controller that forgets the attribute. |
| **Secrets**               | User Secrets in dev; environment variables or a secret store in production. Gateway credentials encrypted at rest with ASP.NET Data Protection, and **never** returned by any API — write-only fields in the admin UI.                                                       |
| **Transport**             | HTTPS enforced, HSTS, TLS 1.2+.                                                                                                                                                                                                                                              |
| **Input**                 | FluentValidation on every command; HTML sanitisation on all rich-text fields; EF parameterisation everywhere (no string-concatenated SQL).                                                                                                                                   |
| **Uploads**               | Extension and magic-byte validation, size caps, re-encode images through ImageSharp to strip payloads, store outside the web root, serve via a controlled endpoint or CDN.                                                                                                   |
| **Rate limiting / abuse** | Per-IP and per-account limits on login, OTP, coupon validation and checkout. Lockout after repeated failures.                                                                                                                                                                |
| **Web**                   | CORS restricted to known origins; CSP, `X-Content-Type-Options`, `Referrer-Policy` headers; antiforgery on cookie-based flows.                                                                                                                                               |
| **PCI**                   | No card data ever touches our servers — bKash is a hosted redirect. This is a deliberate scope-reduction decision.                                                                                                                                                           |
| **Privacy**               | Customer data export and delete endpoints. Phone numbers masked in logs. Payment payloads redacted before logging.                                                                                                                                                           |
| **Admin**                 | Every privileged action audit-logged with actor, IP and timestamp.                                                                                                                                                                                                           |

---

## 13. Testing strategy

| Layer          | Type                                 | What is actually tested                                                                                                      |
| -------------- | ------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------- |
| Domain         | Unit, no mocks                       | Discount engine (the largest suite), order status transitions, stock ledger arithmetic, slot generation, `Money` arithmetic. |
| Application    | Unit, mocked ports                   | Handler orchestration, validation rules, authorization decisions, `Result` failure paths.                                    |
| Infrastructure | Integration, Testcontainers Postgres | EF mappings, migrations apply cleanly, query correctness, concurrency behaviour.                                             |
| API            | Integration, `WebApplicationFactory` | Auth, routing, status codes, ProblemDetails shape, idempotency replay.                                                       |
| Architecture   | NetArchTest                          | The layer rules from §5.1 — build fails on a violation.                                                                      |
| Frontend       | Vitest / Karma                       | Signal stores, pipes, guards, pure components.                                                                               |
| E2E            | Playwright                           | Guest checkout, member checkout, consultation booking, admin order transition, coupon apply.                                 |

**Non-negotiable coverage:** the discount engine, the checkout total calculation, and stock reservation. Those three are where money is lost silently. Everything else is judgement.

---

## 14. DevOps and deployment

### 14.1 Environments

`Local` → `Staging` (bKash sandbox) → `Production` (bKash live).

### 14.2 Recommended production topology (cost-appropriate for Bangladesh)

```
Cloudflare (DNS, CDN, TLS, WAF, image caching)
        │
   Nginx reverse proxy  ── /api/*  → ASP.NET Core container (Kestrel)
        │                └── /*     → Angular SSR container (Node)
        │
   PostgreSQL (same VPS initially, managed instance once revenue supports it)
   Redis (Phase 5, when a second app instance appears)
   Object storage for media (Cloudflare R2 or DigitalOcean Spaces)
```

A single 4–8 GB VPS (Hetzner, DigitalOcean or Contabo) comfortably runs this to a few thousand orders a month. Azure App Service is the alternative if you prefer managed over cheap — the architecture does not care either way.

### 14.3 CI/CD (GitHub Actions)

```
PR       → restore, build, unit + integration tests, arch tests,
           lint, Angular build with bundle budgets, CodeQL
main     → build Docker images, push to registry, deploy to staging,
           run EF migrations, smoke tests
tag v*   → manual approval → production deploy → migrate → health check → rollback on failure
```

Migrations run as a **separate step before** the app rollout, never on application startup — startup migrations in a multi-instance deployment are a race condition waiting to corrupt your schema.

### 14.4 Observability

Serilog → file plus Seq (or Grafana Loki). OpenTelemetry traces on the checkout and payment paths. Health endpoints `/health/live` and `/health/ready` covering DB, Hangfire, SMS and payment gateway. Uptime monitoring with alerts to the admin phone. A weekly digest email: orders, revenue, failed payments, low stock.

---

## 15. Delivery roadmap

Each phase ends with something demonstrable. Sequenced so revenue arrives as early as possible.

| Phase                         | Goal                                  | Deliverables                                                                                                                                                                                                                   |
| ----------------------------- | ------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **0 — Foundation**            | Skeleton that builds and deploys      | Solution + all four projects, layering + arch tests wired, Docker Compose (Postgres + API + web), EF context, Identity, JWT, logging, error handling, OpenAPI, Angular workspace with layouts, Bootstrap 5 brand theme, CI pipeline.       |
| **1 — Catalog**               | Products visible to the public        | Category tree, products, variants, media pipeline, admin product CRUD, public listing with filters/search/sort, product detail page, SSR + SEO, seed data for real WoodHeart products.                                         |
| **2 — Commerce core**         | **First real order — revenue starts** | Cart (guest + member), delivery zones and fees, VAT, checkout flow, COD provider, order placement, order confirmation, customer order history, admin order management, invoice PDF, basic email + SMS.                         |
| **3 — Inventory & Discounts** | Operations become manageable          | Stock ledger, reservations, low-stock alerts, stock-in/adjustment screens, full discount engine, coupon codes, automatic promotions, campaign scheduling, usage reports.                                                       |
| **4 — Consultation**          | Second revenue line live              | Services, consultants, availability rules and exceptions, slot generation, booking wizard, deposits, admin calendar, reminders, quotation → order conversion.                                                                  |
| **5 — bKash & Notifications** | Digital payments and full comms       | bKash tokenized checkout, reconciliation job, refunds, admin payment-method configuration UI, notification template manager, full event coverage, in-app notification centre, abandoned-cart recovery.                         |
| **6 — Growth & polish**       | Convert better, operate better        | Reviews with photos, wishlist, related and recently-viewed products, room-based collections, reports dashboard, customer groups, returns/refund workflow, Bangla localisation, courier integration, performance and a11y pass. |

**Dependencies worth naming now:** Phase 5 is blocked by bKash merchant onboarding, which takes real calendar time — **start that paperwork during Phase 1**, not when the code is ready. An SMS gateway account (Alpha SMS, BulkSMSBD or SSL Wireless) is needed in Phase 2.

---

## 16. Open questions — decisions I need from you

These change the model, so answering them early avoids rework. Where an answer does not arrive, I will build the assumption noted and flag it in code.

1. **VAT** — what rate applies to your products, and are your displayed prices VAT-inclusive or exclusive? _(Assumption: configurable rate, prices displayed VAT-inclusive.)_
2. **Delivery pricing** — actual charges for inside Dhaka vs outside, and how bulky furniture is priced. Is there a free-delivery threshold? _(Assumption: zone-based table plus per-product surcharge, admin-editable.)_
3. **Made-to-order** — which categories are built to order rather than stocked, and what lead times? Do you require an advance payment on those? _(Assumption: `MadeToOrder` product type with a configurable advance percentage.)_
4. **Consultation commercials** — free, paid, or free-then-adjusted-against-an-order? What duration and what fee? _(Assumption: per-service fee, `0` allowed, optional deposit.)_
5. **Delivery operations** — own vehicle and team, or third-party courier? This decides whether courier integration is Phase 6 or never. _(Assumption: own delivery first, manual status updates.)_
6. **Installation/assembly** — is it a chargeable line item, a free service, or out of scope? _(Assumption: a per-product `AssemblyRequired` flag plus an optional service fee.)_
7. **Returns policy** — what window, who pays return delivery, are custom items non-returnable? _(Assumption: 7-day window on stocked items, custom items non-returnable.)_
8. **bKash merchant status** — do you already have a merchant account, or does onboarding need to start? This directly gates Phase 5.
9. **Bangla** — full Bangla UI, or English UI with Bangla product content? Full bilingual roughly doubles the content-entry effort. _(Assumption: English UI in v1, bilingual product content, full Bangla UI in Phase 6.)_
10. **Team** — solo build or a team? A team changes how aggressively the modules should be split and how strict the CI gates should be.

---

## 17. Immediate next steps

**Environment status**

| Item                | Status                    | Action                                                                     |
| ------------------- | ------------------------- | -------------------------------------------------------------------------- |
| .NET SDK 10.0.301   | ✅ installed              | —                                                                          |
| `dotnet-ef` 10.0.7  | ✅ installed              | Tools lag the 10.0.11 runtime; `dotnet tool update -g dotnet-ef` when convenient |
| Git repository      | ✅ initialised            | Phase 0 committed                                                          |
| PostgreSQL          | ⏳ via Docker             | `docker compose -f deploy/docker/docker-compose.yml up -d`                 |
| Docker Desktop      | ❌ **not installed**      | **Needed** for local Postgres and Testcontainers. Install from docker.com   |
| Node.js             | ✅ 22.12.0                | Satisfies Angular 21. An upgrade is optional, not blocking.                    |
| Angular CLI         | ✅ 21.2                   | Workspace scaffolded, builds with SSR, 8 tests passing.                          |

**Phase 0 — delivered**

| Item | Status |
| ---- | ------ |
| Solution, four projects, layer dependency direction | ✅ |
| `WoodHeart.Tests/Architecture` enforcing the layer rules | ✅ 7 tests |
| Domain building blocks — `BaseEntity`, `SoftDeletableEntity`, `ValueObject` | ✅ |
| Bangladesh value objects — `Money`, `PhoneNumber`, `LocalizedText`, `Slug`, `EmailAddress` | ✅ 49 tests |
| `GeneralResponse` / `GeneralResponse<T>` with stable error codes, `PagedList<T>`, `PaginationParams` | ✅ |
| `Repository<T>` + `IUnitOfWork` — staging separated from committing | ✅ |
| `DataContext` — snake_case, soft delete, audit stamping via injected clock, `xmin` concurrency | ✅ |
| Initial migration — 11 tables | ✅ generated, **not yet applied** |
| Seeder — roles, store settings, feature flags (`bkash.enabled` off) | ✅ idempotent |
| ASP.NET Identity (phone as login handle), JWT policies, rotating refresh tokens | ✅ |
| `ValidationFilter` — one error shape for binding and business failures alike | ✅ |
| Correlation ids, tiered rate limiting, health checks, Serilog, Scalar | ✅ |
| Walking skeleton (`/api/diagnostics/ping`, `/echo`) | ✅ 9 integration tests |
| Docker Compose (Postgres + pgAdmin), production `Dockerfile.api` | ✅ |
| GitHub Actions CI — build, arch, tests, migration verification, vulnerability audit | ✅ |
| Angular workspace — Angular 21, SSR, Bootstrap 5, IMSAnuglar structure | ✅ 8 tests, in the `WoodHeart_Web` repo |

**Total: 73 backend + 8 frontend tests passing, zero build warnings on either side.**

### 17.1 Where this deliberately differs from Bento_BE

Three departures, each with a reason rather than a preference.

| Bento_BE | WoodHeart | Why |
| -------- | --------- | --- |
| `IRepository.SaveAllAsync` — any repository can commit | Committing lives only on `IUnitOfWork` | Bento's own `DOCs/bugs/notification-insert-commits-callers-pending-changes.md` records the failure mode: a helper that saves flushes its caller's half-finished work. Stock drawdown and order placement must not be able to half-commit. |
| `BaseEntity.CreatedAt = DateTime.UtcNow` | Stamped in `DataContext` from an injected `IDateTimeProvider` | A field that always says "now" cannot be tested. Discount windows, consultation slots and reservation expiry are all time-dependent. |
| AutoMapper 12.0.1 with `NuGetAuditLevel=critical` | Mapperly (source generator) | GHSA-rvv3-g6hj-g44x covers every AutoMapper below 15.1.1, and 15.x is the commercial licence. A category tree mapped from request data is exactly the self-referential shape the advisory describes. |

Two smaller ones: PostgreSQL rather than SQL Server (already wired, and `pg_trgm` gives Bangla product search), and Central Package Management rather than inline versions — the latter is what pins Hangfire's vulnerable transitive Newtonsoft.Json.

Everything else follows Bento: the four projects and their names, feature folders, the parallel `Interfaces/` tree, `BaseApiController.HandleResult`, `GeneralResponse`, `PagedList<T>`, all DI in `ApplicationServiceExtensions`, one test project.

### 17.2 What happens next

1. **Install Docker Desktop**, then apply the migration and confirm the schema. Until that runs, the migration is a hypothesis rather than a fact.
2. ~~Scaffold the Angular workspace.~~ **Done** — Angular 21 on the existing Node, so the upgrade turned out not to be needed. It now lives in the separate `WoodHeart_Web` repository (§4).
3. **Phase 1 — Catalog.** Category tree, products, variants, media, admin CRUD, public listing and detail pages, SSR and SEO, seed data from real WoodHeart products. Neither blocker above stops this starting.

Answering §16 questions 1, 2 and 8 — VAT, delivery charges, bKash merchant status — matters before Phase 2, which is where money starts arriving. Question 8 in particular has a long calendar lead time, so the paperwork is worth starting during Phase 1.
