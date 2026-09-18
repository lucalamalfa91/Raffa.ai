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
   market records and Raffa's capability catalog). Portfolio and Renewals list
   the workspace's contracts; both default to **Ready**, with **To review** and
   **All** one click away. Portfolio headers filter by type (text / date /
   number / select).

**Ask** binds to a supplier/contract when the chat was opened from Contract 360
(`?scope=` → persisted `scopeContractId`) or when the question names a known
supplier. A bound chat shows a chip and a title `Supplier — Contract type`.
Citations show the document quote; a spanned clause offers **Open contract** and
**Open at this span** (viewer overlay). The rail searches and deletes the
caller's chats. The global Ask bar is hidden on `/ask` itself (that screen has
its own composer).

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
