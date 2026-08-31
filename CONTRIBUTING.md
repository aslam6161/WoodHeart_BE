# How work moves through WoodHeart

This is the canonical copy. [WoodHeart_FE](https://github.com/aslam6161/WoodHeart_FE)
follows the same rules and links here rather than repeating them.

---

## The branches

| Branch | What it means | Who writes to it |
|---|---|---|
| `main` | **Deployable.** Whatever is here is running in production or about to be. | Nothing but a reviewed pull request from `develop`. |
| `develop` | **Integration.** Finished features waiting for the next release. | Reviewed pull requests from working branches. |
| `phase/<n>-<slug>` | The working branch for one roadmap phase. | You, directly. |

Nobody commits to `main` or `develop`. Both are protected, and the protection
is what makes the AI review and the test suite meaningful — an unenforced rule
is a suggestion.

### Working branch names

Lower-case, `<type>/<slug>`, checked by CI on every pull request:

| Type | For | Example |
|---|---|---|
| `phase/` | A roadmap phase from [PLAN.md §15](PLAN.md#15-delivery-roadmap) | `phase/1-catalog` |
| `feature/` | Something real that is not a whole phase | `feature/bulk-price-import` |
| `fix/` | A bug in `develop` or `main` | `fix/order-total-rounding` |
| `chore/` | Tooling, dependencies, CI | `chore/bump-npm-deps` |
| `docs/` | Documentation only | `docs/bkash-runbook` |
| `refactor/` | Behaviour unchanged, shape changed | `refactor/extract-pricing-service` |

The phase branches, ready to create as the roadmap reaches them:

```
phase/1-catalog                phase/4-consultation
phase/2-commerce-core          phase/5-bkash-notifications
phase/3-inventory-discounts    phase/6-growth-polish
```

---

## The loop

A phase branch is long-lived — it stays open for the whole phase. Each finished
feature inside it becomes its own pull request into `develop`. You do not wait
until the phase is over to merge.

```
                     ┌──────────────── merge back after each PR
                     ↓
  phase/1-catalog ───┴──► PR ──► develop ──► PR ──► main ──► CD ──► production
       ↑                    │                 │
   your commits       AI review +        AI review +
                      1 human approval   1 human approval
                      + green CI         + green CI
```

**1. Start the phase.**

```bash
git checkout develop && git pull
git checkout -b phase/1-catalog
git push -u origin phase/1-catalog
```

**2. Work. Push whenever.** CI runs on every push to a phase branch, so the
pull request opens with its checks already green instead of pending.

**3. A feature is finished — open the pull request.**

```bash
git push
gh pr create --base develop --fill
```

Small pull requests get real reviews; a two-thousand-line one gets a rubber
stamp. If a phase produces four reviewable features, that is four pull
requests, not one.

**4. Merge, then bring the phase branch back up to date.** Otherwise the next
pull request from it re-proposes changes `develop` already has.

```bash
git checkout develop && git pull
git checkout phase/1-catalog && git merge develop
```

**5. Release.** When `develop` is worth shipping, open `develop` → `main`. That
merge is what triggers the deployment.

```bash
gh pr create --base main --head develop --title "Release: phase 1 catalog"
```

---

## Every pull request gets two reviews

**The AI review** runs automatically within a couple of minutes of opening,
leaving inline comments. It knows this project's conventions — it is told to
look for a `decimal` that should be `Money`, a `DateTime.UtcNow` that should be
`IDateTimeProvider`, a helper that commits its caller's half-finished work. Pull
it back into a thread by mentioning `@claude` in a comment.

It **cannot approve and cannot merge.** It is a fast first pass that catches
the boring things, so the human review can spend its attention on whether the
design is right.

**The human review** is you, reading your own diff after the AI comments have
landed. That is genuinely worth doing and is not the same as writing it.

One honest caveat: **GitHub does not let you approve your own pull request.**
On a one-developer repository, a rule requiring one approval would make every
pull request permanently unmergeable — so it would get switched off, and a
protection rule you routinely switch off protects nothing. The protection
script therefore requires **zero** approvals by default and enforces what it
actually can: no direct pushes to `main` or `develop`, no force-pushes, all
checks green, all review threads resolved. The day a second person gets write
access, re-run it with `1` and the gate becomes real:

```bash
./deploy/github/protect-branches.sh aslam6161/WoodHeart_BE 1
```

Requires the `ANTHROPIC_API_KEY` secret:

```bash
gh secret set ANTHROPIC_API_KEY --repo aslam6161/WoodHeart_BE
gh secret set ANTHROPIC_API_KEY --repo aslam6161/WoodHeart_FE
```

Without it the review job logs a notice and passes, so the repository still
works for anyone who has not set it up.

---

## What CI checks

| Check | Backend | Frontend |
|---|---|---|
| Branch name | ✓ | ✓ |
| Build | `dotnet build -c Release` | `ng build --configuration production` |
| Unit + integration tests | `dotnet test` against real Postgres | Vitest |
| Architecture rules | NetArchTest — layering is a build failure | — |
| Migrations apply to an empty database | ✓ | — |
| Dependency audit | `dotnet list package --vulnerable` | `npm audit` |
| Bundle budgets | — | ✓ (600 kB warn / 700 kB error) |
| Docker image builds | ✓ | ✓ |

The image build runs on pull requests too. A Dockerfile only exercised on a
merge to `main` is a Dockerfile that breaks on the day you most need it.

---

## What CD does

Merging to `main` builds a Docker image and pushes it to GitHub Container
Registry:

| Repository | Image |
|---|---|
| `WoodHeart_BE` | `ghcr.io/aslam6161/woodheart-api` |
| `WoodHeart_FE` | `ghcr.io/aslam6161/woodheart-web` |

Each build is tagged three ways: `latest`, `sha-<short>`, and a timestamp.
**Pin `sha-` tags in production.** Rolling back then means naming a specific
sha, which is unambiguous in a way that "latest from last Tuesday" never is.

The deploy job stays skipped until the `DEPLOY_HOST` repository variable is
set — a grey check, not a red one, because a pipeline that fails on every merge
is one you stop reading. The setup steps are at the bottom of
[cd.yml](.github/workflows/cd.yml), and the stack it deploys is
[docker-compose.prod.yml](deploy/docker/docker-compose.prod.yml).

**Migrations do not run on application startup.** They are a deliberate step
before the rollout — startup migrations across two instances are a race
condition waiting to corrupt the schema.

---

## Commit messages

Imperative mood, explaining why rather than what. The diff already says what.

```
Snapshot the unit price onto order lines at placement

Joining live to Product meant a price change silently rewrote last
month's invoices.
```

Not `updated files` or `fix`.

---

## One-time setup

Branch protection cannot be configured from a workflow file. Run this once per
repository, after `gh auth login`:

```bash
./deploy/github/protect-branches.sh aslam6161/WoodHeart_BE
./deploy/github/protect-branches.sh aslam6161/WoodHeart_FE
```

It requires pull requests and passing checks on both `main` and `develop`, and
blocks force-pushes and deletion. Pass `1` as a second argument to also require
an approval, once there is somebody who can give one.

---

## The two-repository seam

A change that spans both repositories is two pull requests, and nothing
mechanically stops the shared contract types drifting apart. Three types are
shared by convention:

| Backend | Frontend |
|---|---|
| `GeneralResponse` / `GeneralResponse<T>` | `_models/generalResponse.ts` |
| `PagedList<T>`, `PaginationParams`, `PaginationHeader` | `_models/pagination.ts` |
| The `X-Pagination` response header | `_services/paginationHelper.ts` |

Change one and change the other **the same day**. Link the two pull requests to
each other in their descriptions, and merge the backend one first — a frontend
expecting a field the API does not send yet is a broken storefront; an API
sending a field nobody reads yet is harmless.
