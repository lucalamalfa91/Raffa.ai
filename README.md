# Raffa.ai

Contract intelligence for procurement. A workspace knows what the team bought,
what they pay, when they must act, and where they can save — from the contracts
they upload, not from the open web.

Web-first modular monolith (API + worker + Postgres + object storage + queue)
on Azure `dev` and `demo`. Scope is R0–R4 (ADR-001). Full CLM, e-sign, PO/invoice,
and ERP replacement are out of scope.

- **Repository**: [`lucalamalfa91/Raffa.ai`](https://github.com/lucalamalfa91/Raffa.ai)
- **Default branch**: `main` — trunk-based, protected (ADR-014)

## What you use

Sign-in lands on **Ask Raffa** (`/ask`). The rail is two-tier:

| Tier | Screen | Route |
|------|--------|--------|
| Primary | Ask Raffa (recent chats nested under it) | `/ask`, `/ask/:id` |
| Primary | Documents | `/documents` |
| From your contracts | Portfolio | `/contracts` |
| From your contracts | Renewals | `/renewals` |
| From your contracts | Savings | `/savings` |
| From your contracts | Quote check (new proposals, optional) | `/quotes` |

There is no Home item. Review is a state of Documents (`?review=`), not a rail
destination. Workspace & members lives in the rail footer (Admin). Portfolio,
Renewals, and Quote check stay dim until the workspace has at least one
**validated** contract (a linked document in `Completed`). Savings is always
reachable.

## How a workspace works

```
Upload in Documents → Worker processes → Review weak facts → Validated contract
        ↓                                         ↓
   Ask (citations)                    Portfolio · Renewals · Savings
```

1. **Documents** — drop PDF / Word / Excel / images. Size and format are
   decided on the request (413 / 415). Everything else runs on the Worker:
   classify, extract, embed. Non-contracts become a `Rejected` row ("Not added"),
   not a 422. Hung jobs retry: **Uploaded** after 3 minutes;
   **Processing** after 15 minutes of silence (job heartbeats). Admin
   **Delete all documents** removes the files and cascades the contracts they
   built, their renewal trackers, and Ask chats scoped to those contracts.
   The viewer opens as an in-page overlay; `/documents/:id/viewer` remains for
   deep links.
2. **Review** — Accept or Save in place (no remount). Accepting the extracted
   value officializes it (`human_accepted`). An extracted start date is always
   auto-accepted. Contract **status** is derived from official start/end dates
   at 100% confidence, not from the model's wording. "Mark as validated" signs
   the document `Completed`.
3. **Validated feeds the rest** — Ask answers from validated contracts (plus
   market records and Raffa's capability catalog). Portfolio lists the
   validated contracts only, soonest notice deadline first ("More columns"
   adds Start / Auto / Risk); Renewals defaults to **Ready**, with **To
   review** and **All** one click away.

**Savings** is a dashboard: verified money leads (with its share of spend),
then identified, in progress and savings potential; a portfolio-context strip
links into Portfolio and Renewals; the identified → in progress → verified
pipeline, "When you saved" (verified money per month, this year / last 90 days /
last verified saving), where the savings are (by supplier or lever) and the open
savings whose notice deadline is ahead. Pipeline stages and supplier bars filter
the opportunities table; each row offers Renewals (`?select=`) and a scoped Ask.
`/savings?contract=<id>` focuses one contract (Contract 360 "Track it in
Savings").

**Renewals** opens on a KPI strip (notice in 30 / 90 days, spend renewing inside
90 days, not started / in negotiation / closed — each a filter), a search and
status filter, and tick boxes for bulk actions (assign to me, show in Portfolio,
ask which to start first). The selected row's pane launches everything from one
registry (`web/src/routes/renewals/renewalActions.ts`): Ask Raffa bound to the
contract (plan, draft email, market check, why ranked), Contract 360 / Savings /
Portfolio / Quote check with the contract in context, and the operations not
built yet (reminder, calendar, send notice, export), which open Ask so it can
say so and file the request. A new capability is one registry entry.

Portfolio's `?ids=` narrowing takes `&from=renewals|savings` so the notice names
the screen that picked the contracts and links back to it.

**Ask** binds to a supplier/contract when the chat was opened from Contract 360
(`?scope=` → persisted `scopeContractId`) or when the question names a known
supplier. A bound chat shows a chip and a title `Supplier — Contract type`.
Citations show the document quote; a spanned clause offers **Open contract** and
**Open at this span** (viewer overlay). The rail searches and deletes the
caller's chats. The global Ask bar is hidden on `/ask` itself (that screen has
its own composer).

When Ask is asked for an operation it cannot perform yet (send an email, set
a reminder, export a file, raise a PO) it says so in one sentence, offers the
nearest alternative — a drafted negotiation email written from the contract's
own facts, or the screen that already has the answer — and can file the gap
as a GitHub issue for the team from a three-question card in the chat
(ADR-030). Beyond those five, Raffa recognises a missing feature by itself: a
capability investigator agent checks each new question against everything
Raffa can do, beside the answer so it adds no latency, and when the user asks
for something no screen or Ask ability does (a report for management, a slide
deck, …) a separate message after the answer offers to propose it as a new
feature through the same interview. Every such issue opens as
`awaiting-approval` and is built only after a maintainer approves it
(ADR-031).

The composer's **Web search** toggle (ADR-032, shown where the environment has
web research and usable once the workspace Admin switched it on) sends a
question to the public web and to the workspace's own data together, with the
procurement-only filters lifted and no per-question consent: only the
question's words are searched, and the web part of the reply is labelled
unverified. A plainly personal question ("dimmi la ricetta della carbonara")
gets Google and Perplexity links instead.

Quote check is only for a **new** supplier proposal. "Which of *my* contracts
are off-market?" stays in Ask (today that intent routes correctly, then
abstains — pack not built yet).

Flows in detail: [`docs/architecture/product-flow.md`](docs/architecture/product-flow.md).
Ask engine, store, and Foundry: [`docs/architecture/ask-raffa-v2-data-flow.md`](docs/architecture/ask-raffa-v2-data-flow.md).

## Repo layout

| Folder | What a developer runs |
|--------|------------------------|
| `web/` | React + TypeScript SPA (Vite, MSAL PKCE). [`web/README.md`](web/README.md) |
| `backend/` | .NET 10 API (`Raffa.Api`) + Worker (`Raffa.Worker`) + Postgres. [`backend/README.md`](backend/README.md) |
| `infra/` | Terraform for Azure `dev` / `demo`. [`infra/README.md`](infra/README.md) |
| `mobile/` | Expo lane, non-gating for R0–R4. [`mobile/README.md`](mobile/README.md) |
| `docs/` | Product and Ask data-flow diagrams (this folder) |
| `.helix/` | Delivery process artefact — not the product |

## Stack

| Layer | Decision |
|-------|----------|
| Backend | .NET 10 / ASP.NET Core modular monolith + worker (ADR-002) |
| Data | PostgreSQL Flexible Server + pgvector, EF Core, RLS tenancy (ADR-003, ADR-009) |
| Cloud | Azure North Europe (ADR-005, ADR-006) |
| IaC | Terraform; HCP workspaces `raffa-dev` / `raffa-demo` (ADR-007) |
| Auth | Entra ID, Authorization Code + PKCE (ADR-010) |
| Web | React + TypeScript + Vite on Azure Static Web Apps; config at runtime (ADR-012) |
| CI → Azure | GitHub OIDC; no stored client secrets (ADR-015) |
| Promotion | Merge to `main` → `dev`; tag + GitHub Environment approval → `demo` (ADR-016) |

Local how-to lives in the folder READMEs. Architecture decisions live under
`.helix/reports/architecture/`.

## Environments

- **`dev`** — auto-deploy on merge to `main` (backend/web). Infra apply is HCP
  Terraform VCS, not a GitHub Actions `terraform apply`.
- **`demo`** — tagged promotion (`demo-v*`) with required reviewers (ADR-016).
- Identities and fixture/seed jobs: [`infra/README.md`](infra/README.md),
  [`backend/README.md`](backend/README.md). Operator-only workflows (never a
  side effect of a push): `seed-demo-fixture.yml`, `seed-market-intelligence.yml`,
  `verify-tenant-corpus.yml`, `backfill-workspace-membership.yml`.

## Branching

Trunk-based: every change lands on `main` through a required pull request.
No force-pushes, no branch deletion.

```bash
python scripts/verify_github_repos.py
python scripts/apply_github_branch_protection.py
```
