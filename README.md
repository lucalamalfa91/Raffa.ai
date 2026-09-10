# Raffa.ai

AI-native procurement / contract-intelligence platform. Raffa knows what a
team bought, what they pay, when they need to act, and where they can save
money.

V1 is a **web-first modular monolith** (API + worker + Postgres + object
storage + queue) on Azure `dev` and `demo`. Scope is R0–R4 (ADR-001); full
CLM, e-sign, PO/invoice, and ERP replacement are out of scope.

- **Repository**: [`lucalamalfa91/raffa`](https://github.com/lucalamalfa91/raffa)
- **Owner**: `lucalamalfa91` — a personal GitHub **user** account. Raffa is
  not a GitHub organization (ADR-014).
- **Visibility**: public
- **Default branch**: `main` — trunk-based, protected (ADR-014)

## Folder layout

One monorepo, four product domains plus the Helix run artefact — not four
separate remotes (ADR-014). Each domain folder carries its own README; keep
those in sync when the public surface of that folder changes (see
`.helix/skills/readme-hygiene.md`).

| Folder | Contents | README |
|--------|----------|--------|
| `infra/` | Terraform for Azure `dev` and `demo` (HCP Terraform remote state) | [`infra/README.md`](infra/README.md) |
| `backend/` | .NET 10 modular-monolith API + worker | [`backend/README.md`](backend/README.md) |
| `web/` | React + TypeScript SPA (Vite, MSAL PKCE) | [`web/README.md`](web/README.md) |
| `mobile/` | React Native (Expo) — **non-gating** lane, no store release for R0–R4 | [`mobile/README.md`](mobile/README.md) |
| `.helix/` | Helix process artefact — ADRs, work items, slices, delivery process | [`.helix/README.md`](.helix/README.md) |
| `docs/` | Architecture diagrams and acceptance checklists (Ask Raffa V2 data flow: [`docs/architecture/ask-raffa-v2-data-flow.md`](docs/architecture/ask-raffa-v2-data-flow.md)) | — |

## Stack (locked by ADR)

| Layer | Decision |
|-------|----------|
| Backend | .NET 10 / ASP.NET Core modular monolith + worker (ADR-002) |
| Data | PostgreSQL Flexible Server + pgvector, EF Core, RLS tenancy (ADR-003, ADR-009) |
| Cloud | Azure North Europe, cheap SKUs (ADR-005, ADR-006) |
| IaC | Reusable Terraform modules + two env roots; HCP workspaces `raffa-dev` / `raffa-demo` (ADR-007) |
| Auth | Entra ID, Authorization Code + PKCE; per-env public client + API registration (ADR-010) |
| Web | React + TypeScript + Vite on Azure Static Web Apps; config at runtime, not build time (ADR-012) |
| Mobile | Expo + TypeScript; `continue-on-error` in CI (ADR-013) |
| CI → Azure | GitHub OIDC federated credentials; no stored client secrets (ADR-015) |
| Promotion | Merge to `main` → `dev`; tag + GitHub Environment approval → `demo` (ADR-016) |

## Environments and who applies what

- **`dev`** — auto-deploy on merge to `main` (backend/web). Infra apply is
  owned by **HCP Terraform VCS**, not by a CLI `terraform apply` from GitHub
  Actions (`.github/workflows/infra.yml` apply job is a pointer to the HCP UI).
- **`demo`** — tagged promotion (`demo-v*`) with required reviewers on the
  GitHub `demo` Environment (ADR-016). First apply of workspace `raffa-demo`
  is a HCP UI / VCS run, same as `dev`.
- **Do not mix identities.** HCP Terraform uses the Azure app
  `raffa-hcp-dev` (`ARM_*` as HCP **Environment** variables). GitHub Actions
  deploy uses `raffa-sp-dev` / `raffa-sp-demo` via OIDC. Details:
  [`infra/README.md`](infra/README.md).
- **Demo fixture data** (so the Day-1 Savings screen is not empty on a
  fresh `raffa_demo`) is seeded by `.github/workflows/seed-demo-fixture.yml`
  — an explicit, manually-dispatched/callable job against `dev` or `demo`,
  reusing the same per-env deploy identity above, never a side effect of an
  ordinary `dev` push. Details: [`backend/README.md`](backend/README.md)
  "Demo fixture seed".

## Branching and protection

Trunk-based: every change lands on `main` through a required pull request.
`main` is protected — PR required (including for admins), status checks must
pass, no force-pushes, no branch deletion.

Helix execution fan-out works on `wave/<task-id>` branches, merges them into
`integration` at phase barriers, then opens a PR `integration` → `main`.
Operators do not hand-merge at barriers. See
`.helix/reports/architecture/ADR-014-git-flow.md`.

## Local verification of repo shape

These scripts make repo identity and branch protection reproducible. Both
require the GitHub CLI (`gh`) authenticated against `lucalamalfa91/raffa`.

```bash
# owner/repo/visibility/description/default-branch + folder layout + secret scan
python scripts/verify_github_repos.py

# applies (idempotently) and confirms `main` branch protection
python scripts/apply_github_branch_protection.py
```

Both exit `0` when the repository already matches — or has just been
brought to match — the required shape, and non-zero with a report of what
does not.

Per-domain how-to (run, test, plan, deploy) lives in that folder's README.
Architecture decisions live under `.helix/reports/architecture/`.

## Ask Raffa V2 — the pilot path and its operator jobs (epic-13, ADR-024)

V2 makes **Ask Raffa the home of the product**: sign-in lands on `/ask`, the
rail is two-tier, uploads happen only in Documents (non-contracts are refused
at the door), and every answer cites one of three sources — your validated
contracts, the market-intelligence feed, or Raffa's own capability catalog —
or abstains. The buyer journey it has to survive is one flow:

> Sign in → **Documents** (drop one or more contracts; non-contracts are
> refused) → review weak facts → **Ask** (contract vs market, renewal
> strategy) → **new chat** (portfolio strategy) → follow a citation into
> **Contract 360**.

**Acceptance checklist**: [`docs/ask-v2-acceptance.md`](docs/ask-v2-acceptance.md)
— A1–A14 as a runbook, one exact command or click-path and one observable pass
condition per row, plus the gaps that currently shape what a row can prove.
**Data flow**: [`docs/architecture/ask-raffa-v2-data-flow.md`](docs/architecture/ask-raffa-v2-data-flow.md).

### The two V2 operator jobs

Both are `workflow_dispatch` (and `workflow_call`) only — never a side effect
of a push, exactly like `seed-demo-fixture.yml` (ADR-021 / ADR-022). Both reuse
the same per-environment OIDC deploy identity and the same `postgres-connection`
Key Vault secret, so neither needs a new Azure role assignment (ADR-011,
ADR-015).

| Workflow | Inputs | What it does |
|----------|--------|--------------|
| [`.github/workflows/seed-market-intelligence.yml`](.github/workflows/seed-market-intelligence.yml) | `target_environment` (`dev` \| `demo`) | Runs the Worker's `ingest-market` command against that environment's database with the checked-in mock feed (`backend/fixtures/market-intelligence.mock.json`), prints the ingestion summary, **re-runs it and fails unless the second pass reports `0 inserted, 0 updated`** (R-MKT-03 AC-1), then verifies `market_record` / `market_embedding` landed and still carry no `tenant_id`. |
| [`.github/workflows/reprocess-tenant-documents.yml`](.github/workflows/reprocess-tenant-documents.yml) | `target_environment`, `tenant_id` | Lists that tenant's documents through `GET /api/documents`, calls `POST /api/documents/{id}/reprocess` for each, then verifies by SQL that **no embedding of that tenant starts with `%PDF`** (R-DOC-07 AC-1) and reports the contracts that still have no supplier (R-SUP-03). |

Order for a fresh environment: deploy (schema apply, ADR-021) → **seed-demo-fixture**
→ **seed-market-intelligence** → **reprocess-tenant-documents** → walk
`docs/ask-v2-acceptance.md`. The same order applies to `demo` after a `demo-v*`
promotion.

### Proving it in a browser

`web/e2e/v2.spec.ts` (Playwright) walks A1, A3, A4, A8, A9, A10 and A14 against
a deployed environment, and A2 / A5 / A6 / A7 as well when `E2E_LIVE_FOUNDRY=1`.
Unconfigured it reports every row as skipped with the reason and exits `0`. See
[`web/README.md`](web/README.md) "End-to-end (Ask Raffa V2 pilot path)". The
V1 walk, `web/e2e/day1.spec.ts`, is red against the V2 shell by design; this
suite replaces it.

The AI golden set (A13) needs no CI step of its own:
`backend/tests/Raffa.AiEval` is a member of `Raffa.slnx`, so
`.github/workflows/backend.yml`'s existing `dotnet test Raffa.slnx` runs it
and a guard intervention fails the build. See
[`backend/README.md`](backend/README.md) "Ask Raffa V2 — operator jobs, golden
set and acceptance".
