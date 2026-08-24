# WoodHeart — Interior Commerce & Consultation Platform

### Architecture & Implementation Plan

|                  |                                                                      |
| ---------------- | -------------------------------------------------------------------- |
| **Product**      | Online store for home interior items + interior consultation booking |
| **Market**       | Bangladesh (BDT, Bangla/English, bKash + Cash on Delivery)           |
| **Backend**      | .NET 10 (ASP.NET Core Web API), Onion Architecture, modular monolith |
| **Frontend**     | Angular 22 (standalone, signals, zoneless, SSR for public pages)     |
| **Database**     | PostgreSQL 16+ via EF Core 10                                        |
| **Repo root**    | `D:\Personal_Projects\WoodHeart`                                     |
| **Status**       | **Phase 0 backend complete** — 88 tests passing. Frontend pending a Node upgrade (§17). |
| **Last updated** | 2026-08-24                                                           |

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
| **PostgreSQL**                     | 16+                                       | Free, strong JSONB (variant attributes), full-text search plus `pg_trgm`, cheap on any VPS.                                                       | SQL Server — licence cost on a VPS; use it only if you already own one.            |
| **EF Core**                        | 10 (`dotnet-ef 10.0.7` already installed) | Migrations, LINQ, compiled queries.                                                                                                               | Dapper — added as a _supplement_ for heavy report queries, not as the primary ORM. |
| **Mediator** (martinothamar)       | latest                                    | Source-generated, reflection-free CQRS dispatch, **MIT licensed**.                                                                                | **MediatR** — commercial from v13. Avoid the licence trap up front.                |
| **Mapperly**                       | latest                                    | Source-generated mapping, compile-time safe, **MIT**.                                                                                             | **AutoMapper** — also commercial now.                                              |
| **FluentValidation**               | 11.x                                      | Request/command validation in a pipeline behaviour.                                                                                               | DataAnnotations — too weak for conditional rules.                                  |
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
| **Angular 22**                        | Latest (`@angular/cli 22.1.5` on npm). Standalone components throughout, **signals** for state, **zoneless** change detection, the `@if` / `@for` control flow, `inject()` over constructor injection.                                                |
| **Angular SSR (`@angular/ssr`)**      | **Non-negotiable for the public site.** Furniture shopping starts on Google and Facebook. SSR plus prerendering gives crawlable product pages, correct OG tags on shares, and a fast LCP over 4G. Admin and customer dashboards stay client-rendered. |
| **Tailwind CSS v4** + **Angular CDK** | WoodHeart is a _design_ brand — the storefront has to look bespoke, not like a component-library demo. Tailwind carries the design system; CDK supplies accessible primitives (overlay, a11y, drag-drop in admin).                                    |
| **@ngrx/signals (SignalStore)**       | Only for genuinely shared state: cart, auth/session, wishlist, admin filters. Everything else is local signals plus `httpResource`/`resource()`. **No full NgRx Store + Effects** — the ceremony is not worth it at this size.                        |
| **Transloco**                         | Runtime EN/BN switching without a second build (native Angular i18n requires one build per locale).                                                                                                                                                   |
| **Playwright**                        | E2E on the three journeys that must never break: guest checkout, member checkout, consultation booking.                                                                                                                                               |
| **Nx workspace** _(deferred)_         | Only worth it if a second app appears (say a delivery-rider PWA). Start with a plain Angular workspace and well-drawn internal boundaries.                                                                                                            |

---

## 4. Repository structure

```
D:\Personal_Projects\WoodHeart\
│
├─ PLAN.md                        ← this file
├─ README.md
├─ .gitignore  .editorconfig  Directory.Build.props  Directory.Packages.props
│
├─ docs/
│   ├─ architecture/              ADRs, C4 diagrams, domain model notes
│   ├─ api/                       exported OpenAPI spec
│   └─ runbooks/                  deploy, backup/restore, bKash go-live
│
├─ backend/
│   ├─ WoodHeart.sln
│   ├─ src/
│   │   ├─ WoodHeart.Domain/            ← the core, zero dependencies
│   │   ├─ WoodHeart.Application/       ← use cases + ports (interfaces)
│   │   ├─ WoodHeart.Infrastructure/    ← EF Core, Identity, adapters
│   │   └─ WoodHeart.Api/               ← controllers, DI composition root
│   └─ tests/
│       ├─ WoodHeart.Domain.UnitTests/
│       ├─ WoodHeart.Application.UnitTests/
│       ├─ WoodHeart.Api.IntegrationTests/
│       └─ WoodHeart.ArchitectureTests/  ← NetArchTest: enforces the layer rules
│
├─ frontend/
│   └─ woodheart-web/             ← Angular 22 workspace (public + customer + admin)
│
└─ deploy/
    ├─ docker/                    Dockerfile.api, Dockerfile.web, compose files
    ├─ nginx/                     reverse proxy + TLS
    └─ github-actions/            CI/CD workflows
```

**Why one Angular app for all three surfaces:** shared design tokens, one API client, one auth interceptor, one deployment. Admin is a lazily loaded route group (`/admin/**`) behind a role guard, so it costs a public visitor **zero bytes** — it never enters the initial chunk. Splitting into separate apps is a Phase 6 decision, not a Phase 1 one.

**Why `Infrastructure` is one project, not four:** splitting persistence, identity, payments and messaging into separate assemblies is a common instinct, and at this size it buys nothing but 20 extra `.csproj` files to keep in sync. Folders inside one `Infrastructure` project give the same separation. Split later only if a module is genuinely extracted.

---

## 5. Backend architecture — Onion

### 5.1 The layers, and the one rule

```
            ┌──────────────────────────────────────────┐
            │            WoodHeart.Api                 │  Controllers, middleware,
            │       (Presentation / Composition)       │  auth, DI wiring, Hangfire UI
            ├──────────────────────────────────────────┤
            │        WoodHeart.Infrastructure          │  EF Core, Identity, bKash,
            │        (Adapters — implements ports)     │  SMS/Email, storage, jobs
            ├──────────────────────────────────────────┤
            │         WoodHeart.Application            │  Commands, Queries, Handlers,
            │      (Use cases + port interfaces)       │  Validators, DTOs, policies
            ├──────────────────────────────────────────┤
            │           WoodHeart.Domain               │  Entities, Value Objects,
            │       (Enterprise model — the core)      │  Domain Events, Specs, rules
            └──────────────────────────────────────────┘

   Dependency direction: ALWAYS inward.  Domain depends on NOTHING.
```

**The rule, stated once:** _no outer layer's type may appear in an inner layer's signature, and no inner layer may reference an outer assembly._ This is enforced mechanically by `WoodHeart.ArchitectureTests` — a build failure, not a code-review opinion. Concretely:

- `Domain` references **no NuGet packages at all** — no EF, no JSON attributes, no mediator types.
- `Application` _declares_ ports (`IOrderRepository`, `IPaymentProvider`, `ISmsSender`, `IUnitOfWork`, `IDateTime`, `ICurrentUser`) and never implements them.
- `Infrastructure` implements those ports and is referenced **only** by `Api`, and only at DI-registration time.
- `Api` holds **no business logic**. A controller action builds a command, dispatches it, maps the result to an HTTP response. If a controller contains an `if` about pricing, it is in the wrong layer.

### 5.2 Modular monolith — vertical slices inside each layer

Every layer is subdivided by **module**, not by technical noun. This is what allows a module to be extracted into its own service later without an archaeological dig.

```
WoodHeart.Domain/
├─ Common/            Entity, AggregateRoot, ValueObject, IDomainEvent, Money, Slug, PhoneNumber
├─ Catalog/           Product, ProductVariant, Category, Brand, Attribute, Media, Review
├─ Inventory/         StockItem, StockMovement, StockReservation, Warehouse
├─ Pricing/           PriceList, TaxRule, DeliveryRate
├─ Promotions/        Discount, Coupon, PromotionRule, PromotionUsage
├─ Ordering/          Cart, CartLine, Order, OrderLine, Shipment, ReturnRequest
├─ Payments/          Payment, PaymentAttempt, Refund, PaymentMethodConfig
├─ Consultations/     ConsultationService, Consultant, AvailabilityRule, Booking
├─ Notifications/     NotificationTemplate, NotificationMessage, Subscription
├─ Identity/          Customer, Address, CustomerGroup
└─ Content/           Page, Banner, Collection, Faq, Testimonial
```

`Application` and `Infrastructure` mirror these folder names exactly.

**Module dependency policy:** modules communicate through **domain events** and **Application-layer ports**, never by reaching into another module's repository or `DbSet`. `Ordering` does not decrement stock; it raises `OrderPlaced`, and an `Inventory` handler reacts.

### 5.3 CQRS shape

```
POST /api/v1/orders
   → OrdersController.Place(PlaceOrderRequest)
   → PlaceOrderCommand  ──dispatch──▶  PlaceOrderCommandHandler
        │  pipeline behaviours, in order:
        │    1. RequestLogging        4. UnitOfWork / transaction
        │    2. Authorization         5. DomainEventDispatch (post-commit)
        │    3. Validation (FluentValidation)
        └──▶ loads the Cart aggregate, calls domain methods, persists, returns OrderResult
   → 201 Created + OrderDto
```

- **Commands** return `Result<T>`. Business failures are _values_, not exceptions — exceptions are reserved for bugs and infrastructure faults.
- **Queries** may bypass repositories and project straight to DTOs with EF `.Select()` or Dapper. Read models are allowed to be pragmatic; write models are not.
- Business failures surface as **RFC 9457 `ProblemDetails`** with a stable machine-readable `type` (`woodheart/insufficient-stock`, `woodheart/coupon-expired`) so Angular can branch on a code instead of string-matching a message.

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

## 8. API surface (v1 outline)

Base path `/api/v1`. JSON, cursor or page/size pagination, RFC 9457 errors, ETag on catalog reads.

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

**Versioning:** URL segment (`/api/v1`). Breaking changes create `/v2`; the Angular client pins one version.

---

## 9. Frontend architecture

### 9.1 Structure

```
frontend/woodheart-web/src/app/
├─ core/                       singletons, provided once
│   ├─ api/                    generated typed clients (from OpenAPI) + wrappers
│   ├─ auth/                   session store, guards, JWT + refresh interceptor
│   ├─ interceptors/           error, correlation-id, loading, retry
│   ├─ config/                 environment, feature flags, runtime config
│   └─ services/               seo, analytics, toast, currency, breakpoint
│
├─ shared/                     dumb, reusable, no business rules
│   ├─ ui/                     button, input, modal, drawer, badge, skeleton,
│   │                          price, rating, image, pagination, empty-state
│   ├─ pipes/                  bdtCurrency, banglaNumber, timeAgo, safeHtml
│   └─ directives/             lazyImg, clickOutside, infiniteScroll
│
├─ layouts/                    public-layout · account-layout · admin-layout · auth-layout
│
├─ features/
│   ├─ home/  catalog/  product/  collection/          public storefront
│   ├─ cart/  checkout/                                the money path
│   ├─ consultation/                                   booking wizard
│   ├─ account/     orders · bookings · addresses · wishlist · profile
│   ├─ auth/
│   └─ admin/       dashboard · products · inventory · orders · payments ·
│                   discounts · consultations · customers · notifications ·
│                   content · settings · reports
│
└─ styles/                     tokens.css, tailwind config, brand theme
```

### 9.2 Conventions

- **Standalone components only.** No `NgModule` anywhere.
- **Signals for state; `httpResource` / `resource()` for server data.** RxJS stays for genuine event streams (search typeahead debounce, websocket) rather than as the default idiom.
- **Zoneless change detection** from day one. Retrofitting it later is a slog.
- **Smart vs dumb split.** Route components fetch and orchestrate; `shared/ui` components take inputs and emit outputs, and never inject a service.
- **Every route lazy-loaded** via `loadComponent` / `loadChildren`. The admin bundle must never reach a public visitor.
- **Typed API client generated from the OpenAPI spec** (`openapi-typescript` or NSwag) as a CI step. Hand-written DTO interfaces drift from the backend within weeks — generated ones cannot.
- **Route-level SEO:** each public route sets title, meta description, canonical URL, OG tags and JSON-LD (`Product`, `Offer`, `BreadcrumbList`, `LocalBusiness`). This is direct revenue for a furniture brand, not a nicety.
- **Performance budget:** initial public JS ≤ 250 KB gzipped, LCP ≤ 2.5s on a simulated 4G connection. Enforced in CI via a bundle-budget check and Lighthouse CI.
- **Images:** `NgOptimizedImage`, AVIF/WebP with fallbacks, explicit width and height on every image, blur-up placeholders. A furniture site is 80% photography — this _is_ the performance work.
- **Accessibility:** keyboard-navigable, visible focus rings, labelled controls, `aria-live` on cart updates. Target WCAG 2.1 AA.

### 9.3 The three surfaces

| Surface            | Rendering       | Auth                        | Notes                                                                                                                     |
| ------------------ | --------------- | --------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| Public site        | SSR + prerender | Anonymous or optional login | SEO-critical. Product, category and collection pages prerendered where possible and revalidated on publish.               |
| Customer dashboard | CSR             | Customer role               | Orders, bookings, addresses, wishlist, reviews.                                                                           |
| Admin dashboard    | CSR             | Admin/Manager role          | Dense data tables, bulk actions, inline edit, drag-drop ordering, image upload with crop, rich text, chart-based reports. |

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
- **Keys:** `Guid` v7 (time-ordered, index-friendly) for entity ids; human-facing identifiers (`OrderNumber`, `BookingNumber`) are separate, sequential, and generated by a DB sequence.
- **Money:** `decimal(18,2)` + currency code, mapped as an owned type. `Money` is a value object with arithmetic that refuses to mix currencies.
- **Translations:** owned JSONB column `{ "en": "...", "bn": "..." }` for names and descriptions. Simpler than a translation table and Postgres indexes JSONB well.
- **Variant attributes:** JSONB, with a GIN index for faceted filtering.
- **Search:** Postgres `tsvector` + `pg_trgm` for product search with fuzzy matching. No Elasticsearch until the catalog exceeds ~10k SKUs.
- **Migrations:** EF Core migrations, one per logical change, reviewed like code. Seed data (divisions, districts, upazilas, categories, roles, notification templates, payment methods) lives in idempotent seeders, not in migrations.
- **Indexes to create deliberately:** product slug (unique), variant SKU (unique), order number (unique), `(customer_id, placed_at desc)`, `(status, placed_at)`, stock `(variant_id, warehouse_id)` unique, booking `(consultant_id, scheduled_at_utc)` unique-filtered, coupon code (unique, case-insensitive).
- **Backups:** nightly `pg_dump` to off-server storage, plus WAL archiving once order volume justifies it. **A restore is tested before go-live** — an untested backup is a rumour.

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
| **0 — Foundation**            | Skeleton that builds and deploys      | Solution + all four projects, Onion + arch tests wired, Docker Compose (Postgres + API + web), EF context, Identity, JWT, logging, error handling, OpenAPI, Angular workspace with layouts, Tailwind theme, CI pipeline.       |
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
| Node.js             | ⚠️ 22.12.0 — too old      | Angular 22 needs ≥ 22.22.3. `winget install OpenJS.NodeJS.LTS` → Node 24    |
| Angular CLI         | ⏳ pending Node           | `npx @angular/cli@22 new woodheart-web --ssr --style=scss --zoneless`      |

**Phase 0 — delivered**

| Item | Status |
| ---- | ------ |
| Solution, four projects, onion dependency direction | ✅ |
| `WoodHeart.ArchitectureTests` enforcing the layer rules | ✅ 6 tests |
| Domain building blocks — `Entity`, `AggregateRoot`, `ValueObject`, `Result<T>`, `Error` | ✅ |
| Bangladesh value objects — `Money`, `PhoneNumber`, `LocalizedText`, `Slug`, `EmailAddress` | ✅ 64 tests |
| Mediator pipeline — logging, validation, unit-of-work/transaction | ✅ |
| `WoodHeartDbContext` — snake_case, soft delete, audit stamping, `xmin` concurrency, outbox, post-commit domain events | ✅ |
| Initial migration — 11 tables | ✅ |
| ASP.NET Identity (phone as login handle), JWT + rotating refresh tokens | ✅ |
| RFC 9457 problem details with stable error codes | ✅ |
| Correlation ids, tiered rate limiting, health checks, Serilog, Scalar | ✅ |
| Walking skeleton (`/diagnostics/ping`, `/echo`) verified end to end | ✅ 9 integration tests |
| Docker Compose (Postgres + pgAdmin), production `Dockerfile.api` | ✅ |
| GitHub Actions CI — build, arch, unit, integration, migration verification, vulnerability audit | ✅ |
| Angular workspace | ⏳ blocked on Node |

**Total: 88 tests passing.**

**Build order for Phase 0**

1. `git init`, add `.gitignore`, `.editorconfig`, `Directory.Build.props`, `Directory.Packages.props` (central package management).
2. Create the solution and the four projects with the correct project references, so the dependency direction is right from the very first commit.
3. Add `WoodHeart.ArchitectureTests` **immediately** — the layer rules are only real if a build can fail on them.
4. `Domain.Common`: `Entity`, `AggregateRoot`, `ValueObject`, `IDomainEvent`, `Money`, `PhoneNumber`, `Result<T>`.
5. `Infrastructure`: `WoodHeartDbContext`, naming conventions, audit and domain-event interceptors, the first migration.
6. `Api`: Serilog, ProblemDetails handler, OpenAPI + Scalar, JWT auth, CORS, health checks, rate limiting, the mediator pipeline behaviours.
7. Docker Compose: Postgres + pgAdmin, so the whole team runs one command to get an environment.
8. `ng new woodheart-web --ssr --style=css --zoneless`, add Tailwind v4, layouts, the design tokens, the API client generation step.
9. GitHub Actions CI running build + tests + arch tests on every PR.

**Then say the word and I will start on Phase 0.** I would also recommend answering §16 questions 1, 2 and 8 first — they are the ones that shape Phase 2, which is where money starts arriving.
