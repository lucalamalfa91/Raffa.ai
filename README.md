# Contigo

AI-native procurement / contract-intelligence platform. Contigo knows what a
team bought, what they pay, when they need to act, and where they can save
money.

V1 is a **web-first modular monolith** (API + worker + Postgres + object
storage + queue) on Azure `dev` and `demo`. Scope is R0–R4 (ADR-001); full
CLM, e-sign, PO/invoice, and ERP replacement are out of scope.

- **Repository**: [`lucalamalfa91/contigo`](https://github.com/lucalamalfa91/contigo)
- **Owner**: `lucalamalfa91` — a personal GitHub **user** account. Contigo is
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

## Stack (locked by ADR)

| Layer | Decision |
|-------|----------|
| Backend | .NET 10 / ASP.NET Core modular monolith + worker (ADR-002) |
| Data | PostgreSQL Flexible Server + pgvector, EF Core, RLS tenancy (ADR-003, ADR-009) |
| Cloud | Azure North Europe, cheap SKUs (ADR-005, ADR-006) |
| IaC | Reusable Terraform modules + two env roots; HCP workspaces `contigo-dev` / `contigo-demo` (ADR-007) |
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
  GitHub `demo` Environment (ADR-016). First apply of workspace `contigo-demo`
  is a HCP UI / VCS run, same as `dev`.
- **Do not mix identities.** HCP Terraform uses the Azure app
  `contigo-hcp-dev` (`ARM_*` as HCP **Environment** variables). GitHub Actions
  deploy uses `contigo-sp-dev` / `contigo-sp-demo` via OIDC. Details:
  [`infra/README.md`](infra/README.md).
- **Demo fixture data** (so the Day-1 Savings screen is not empty on a
  fresh `contigo_demo`) is seeded by `.github/workflows/seed-demo-fixture.yml`
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
require the GitHub CLI (`gh`) authenticated against `lucalamalfa91/contigo`.

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
