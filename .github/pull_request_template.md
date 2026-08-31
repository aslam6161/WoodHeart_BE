## What this changes

<!-- One or two sentences. The diff says what; say why. -->

## Why

<!-- The problem, or the roadmap item. Link the PLAN.md section or issue. -->

## How to check it

<!-- The commands or the click-path a reviewer follows to see it work.
     "Ran the tests" is not this. -->

```bash

```

---

## Before requesting review

- [ ] Branch is named `<type>/<slug>` — see [CONTRIBUTING.md](../CONTRIBUTING.md)
- [ ] Targets `develop` (or is a `develop` → `main` release)
- [ ] `dotnet test` passes locally
- [ ] New behaviour has tests; a bug fix has a test that failed before the fix

## Conventions this touches

<!-- Tick what applies. Leave the rest. The AI review checks these too, but
     ticking them means you thought about them rather than were caught. -->

- [ ] Money is `Money`, never `decimal` or `double`
- [ ] Phone numbers are `PhoneNumber`, normalised to `+8801XXXXXXXXX`
- [ ] Time comes from `IDateTimeProvider`, never `DateTime.UtcNow`
- [ ] Only the service owning the use case commits, via `IUnitOfWork`
- [ ] Business failures are `GeneralResponse` values with a stable `ErrorCode`
- [ ] Stock changes are accompanied by a `StockMovement`
- [ ] Order lines snapshot their price at placement
- [ ] Controllers contain no business logic

## Schema and contract

- [ ] No migration in this change
- [ ] Migration included, and it applies to an **empty** database (CI checks this)
- [ ] Migration is backward compatible with the currently deployed API — the
      old code runs against the new schema for the length of the rollout
- [ ] No change to `GeneralResponse`, `PagedList<T>` or `X-Pagination`
- [ ] Contract changed — the matching [WoodHeart_FE](https://github.com/aslam6161/WoodHeart_FE)
      pull request is: <!-- link it -->

## Anything a reviewer should push back on

<!-- Shortcuts taken, alternatives rejected, things you are unsure about.
     Naming them here is how they get caught. -->
