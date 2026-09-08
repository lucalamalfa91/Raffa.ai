# Open questions

Decisions this run needed and did not have. Every entry carries an **assumption
in force**, so the pipeline can keep moving. This file is not a halt.

Status: `open` · `answered` · `assumed-confirmed` · `assumed-wrong`

---

## How to use this file

**Agents**: never invent a locked decision. Add an entry, state the assumption,
build against it, and reference it from the ADR or task. An assumption you
record is a result; an assumption you absorb silently is a defect.

Mark a wave-spec task `status: gated` only when **no** assumption is defensible.

---

## Client-architect lane (independent)

- **OQ-client-001** — Azure Static Web Apps free tier is available and sufficient in the chosen region. **Status**: `assumed-confirmed`. **Assumption in force**: SWA free tier (TLS + CDN) hosts the static web bundle. Confirm SKU/region with cloud-architect at council-close. Ref: ADR-web-stack.
- **OQ-client-002** — Entra ID (OIDC) supports the Authorization Code + PKCE public-client flow for `dev`/`demo` tenants. **Status**: `assumed-confirmed`. **Assumption in force**: clients are public clients; no client secret in bundle. Confirm with security-architect. Ref: ADR-web-stack, ADR-mobile-stack, api-consumption.
- **OQ-client-003** — No BFF/API-proxy is required for V1; the SPA calls the API origin directly with CORS scoped to front-end origins. **Status**: `assumed-confirmed`. Ref: api-consumption.
- **OQ-client-004** — A mobile store release is genuinely out of scope for V1 `dev`/`demo` (no store-release dependency in spec §16/§20). **Status**: `assumed-confirmed`. Ref: ADR-mobile-stack.
- **OQ-client-005** — Mobile CI lanes can be configured as non-blocking in the council git flow/CI. **Status**: `assumed-confirmed`. Confirm with delivery-manager. Ref: ADR-mobile-stack.
- **OQ-client-006** — Expo/React Native supports OIDC Authorization Code + PKCE against Entra ID for public clients. **Status**: `assumed-confirmed`. Ref: ADR-mobile-stack.
- **OQ-client-007** — OpenAPI codegen tool and URL versioning scheme (e.g. `/v1/...`) are owned by software-architect; clients consume one generated TypeScript client. **Status**: `open` — resolve with software-architect at council-close. Ref: api-consumption.

## Delivery-manager lane (independent)

- **OQ-DM-001** — GitHub Environments with required reviewers are available on the `lucalamalfa91/contigo` public-repo plan; if not, `demo` promotion falls back to a protected tag + a PR to a `demo/*` pointer (still explicit/manual). **Status**: `assumed-confirmed`. Ref: ADR-git-flow, ADR-promotion-dev-demo.
- **OQ-DM-002** — The reviewers for the `demo` environment approval gate are product-owner + security-architect during V1; authority is council-owned. **Status**: `assumed-confirmed`. Ref: ADR-git-flow, ADR-promotion-dev-demo.
- **OQ-DM-003** — GitHub OIDC federation to the customer's Entra ID tenant is permitted on the org plan; fallback is a short-lived SP certificate in Key Vault (never a plaintext GitHub secret). **Status**: `assumed-confirmed`. Ref: ADR-ci-azure-auth.
- **OQ-DM-004** — OIDC subject-claim pinning for `demo` (environment + tag) is sufficient and the org does not allow unrestricted fork-triggered OIDC; deploy jobs run with `pull_request: false`. **Status**: `assumed-confirmed`. Ref: ADR-ci-azure-auth.
- **OQ-DM-005** — Tag naming `demo-v*` and environment name `demo` are council-owned and may be renamed; only the *mechanism* (tag + gated environment) is fixed. **Status**: `assumed-confirmed`. Ref: ADR-promotion-dev-demo.
- **OQ-DM-006** — No absolute start date exists in the brief; the wave calendar uses sequential weeks from an unspecified kickoff, not named calendar dates. **Status**: `assumed-confirmed`. Ref: wave-calendar.
- **OQ-DM-007** — Wave durations (~18 weeks R0–R4 + 2 weeks S0) are first-pass planning estimates owned downstream by the decomposer; this lane commits no hours/dates. **Status**: `assumed-confirmed`. Ref: wave-calendar.

## Security-architect lane (independent)

- **OQ-sec-001** — The chosen relational store (PostgreSQL + pgvector) supports `FORCE ROW LEVEL SECURITY` on the cheapest managed SKU that meets product constraints. **Status**: `assumed-confirmed`. **Assumption in force**: RLS is available; if the software-architect's SKU choice drops RLS, tenancy must force a SKU that keeps it (RLS is non-optional). Ref: ADR-tenancy.
- **OQ-sec-002** — Microsoft Foundry in the chosen region offers a no-training model endpoint (or an opt-out) for the contract-content models. **Status**: `assumed-confirmed`. **Assumption in force**: the AI Gateway selects a no-training endpoint; final model IDs confirmed jointly with software-architect + cloud-architect in the Foundry model-ID ADR. Ref: ADR-secrets-and-rag.

## Software-architect / council (OCR)

- **OQ-ocr-001** — OCR vs native document parse (brief §8, former CQ-008 sub-item). **Status**: `answered`. OCR is in V1: hybrid native-text + Azure AI Document Intelligence (`prebuilt-read` / `prebuilt-layout`) behind the AI Gateway; full document; no 2-page cap. Ref: ADR-017, ADR-004, ADR-005, ADR-008.

## Implementer — E01/F03/US02/T01 (per-folder workflows)

- **OQ-impl-001** — Task E01/F03/US02/T01's own "Files to create or modify" table names `workspace/contigo-infra/.github/workflows/*.yml`. ADR-014 itself fixes the product tree as `infra/`, `backend/`, `web/`, `mobile/`, `.helix/` at the repo root and explicitly rejects `workspace/<repo>/` as "not a stand-in"; `scripts/hcp_vcs_wiring.py` (already merged) also asserts HCP's trigger-prefix as root-relative `infra/`. **Status**: `assumed-confirmed`. **Assumption in force**: the four workflows were written to `.github/workflows/{infra,backend,web,mobile}.yml` at the worktree root — the only location GitHub Actions can actually discover them — not under `workspace/contigo-infra/`. Ref: ADR-014, task E01/F01/US01/T01 (root folder bootstrap).
- **OQ-impl-002** — Task E01/F03/US01/T01 (ci-azure-auth) left its composite action at `.helix/workspace/contigo-infra/.github/actions/azure-login/action.yml` (same wrong-prefix pattern as OQ-impl-001) instead of the repo root. **Status**: `assumed-confirmed`. **Assumption in force**: the action.yml content was copied verbatim (not rewritten) to `.github/actions/azure-login/action.yml` at the repo root, since `backend.yml`/`web.yml` reference it via `uses: ./.github/actions/azure-login`, which only resolves at the real repo root. The stale copy under `.helix/workspace/` was left in place (not this task's file scope to delete). Ref: ADR-014, ADR-015.
- **OQ-impl-003** — Whether `infra.yml`'s plan/apply job also needs an `azure/login` OIDC step. **Status**: `assumed-confirmed`. **Assumption in force**: no — `scripts/hcp_vcs_wiring.py` confirms both `contigo-dev`/`contigo-demo` HCP Terraform workspaces run `execution-mode=remote` (ADR-007's operating model: HCP itself runs plan/apply), so the GitHub Actions runner only ever talks to the HCP Terraform API (via `TF_API_TOKEN`, a non-Azure secret outside ADR-015's scope) — Azure credentials for the `azurerm`/`azuread` providers are supplied as that HCP workspace's own workspace variables, not via this workflow. Ref: ADR-007, ADR-015, `scripts/hcp_vcs_wiring.py`.
- **OQ-impl-004** — `web.yml` deploys to an Azure Static Web App resource; no `infra/modules/staticwebapp` exists yet (only `acr`, `containerapps`, `identity`, `keyvault`, `monitor`, `network`, `postgres`, `servicebus`, `storage` are provisioned). **Status**: `open` — needs a web-hosting infra task (feature-02-shaped) to provision the per-environment Static Web App and set `vars.AZURE_STATIC_WEB_APP_NAME` at the `dev`/`demo` GitHub Environment scope. `web.yml`'s deploy step fails fast with a named error if that variable is unset, rather than silently no-op-ing. Ref: ADR-012.
- **OQ-impl-005** — `backend.yml` builds `backend/src/Contigo.Api/Dockerfile` and `backend/src/Contigo.Worker/Dockerfile` via `az acr build`; neither Dockerfile exists yet anywhere in the repo. **Status**: `open` — needs a backend containerization task to add both Dockerfiles at those exact paths (the CI contract fixed here). Ref: ADR-002, ADR-005.
- **OQ-impl-006** — `infra.yml`/`backend.yml`/`web.yml` (not `mobile.yml`, which has no deploy target) declare `on.workflow_call` with a `target_environment` input, and their deploy/apply job's `environment:` and resource names key off it, so task E01/F03/US03/T01's `demo-promote.yml` can call `uses: ./.github/workflows/backend.yml` / `web.yml` / `infra.yml` with `target_environment: demo` per its own task text ("reuses the per-folder deploy jobs") instead of duplicating them. **Status**: `assumed-confirmed`. **Assumption in force**: this is additive only — direct `push`/`pull_request` triggers are unchanged and default to `dev`; us-03 still owns `demo-promote.yml`, the `demo` GitHub Environment/reviewers, and the `demo-v*` tag trigger untouched by this task. Ref: ADR-016, task E01/F03/US03/T01.
## Implementer / E01/F04/US02/T01 (EF Core + pgvector wiring)

- **OQ-impl-001** — ADR-004 fixes the embed-role model as "Foundry embedding model (e.g.
  `text-embedding-3-small` or `text-embedding-3-large`)... dimension fixed at schema time; small
  dimension preferred" but does not pin an exact integer. **Status**: `assumed-confirmed`.
  **Assumption in force**: `Embedding.Vector` is a fixed `vector(1536)` Postgres column
  (`Embedding.VectorDimensions` constant), matching `text-embedding-3-small`'s native output size
  — the smaller of the two named candidates, per ADR-004's "small dimension preferred for
  cost/size." If the council/AI Gateway later selects a different embed model with a different
  native dimension, this column width is a migration, not a redesign. Ref: ADR-003, ADR-004,
  `backend/src/Contigo.Documents.Contracts/Domain/Embedding.cs`.
- **OQ-impl-002** — ADR-003/ADR-009 write every table/column name in lowercase snake_case
  (`tenant_id`, `document`, `contract`, `embedding`, `clause`, ...), but neither ADR names an EF
  Core naming convention mechanism. **Status**: `assumed-confirmed`. **Assumption in force**: the
  Documents/Contracts `DbContext` calls `UseSnakeCaseNamingConvention()` (`EFCore.NamingConventions`
  package) so the physical schema matches the ADRs' own naming verbatim — without it, Npgsql/EF
  Core would emit quoted PascalCase identifiers instead. This convention is now load-bearing for
  every future migration in this module; us-03's RLS policies (`CREATE POLICY ... USING
  (tenant_id = ...)`) can rely on the lowercase column names existing as written. Ref: ADR-003,
  ADR-009, `backend/src/Contigo.Documents.Contracts/Infrastructure/DocumentsContractsDbContextOptions.cs`.

## Ask V2 lane (epic-13 / ADR-024, 2026-09-08)

Source: `inputs/requirements.md` §13. Every entry has an assumption in force; none gates a task.

- **OQ-askv2-001** — The mock market-intelligence record shape (`MarketDeal`, R-MKT-01) is Contigo's own normalized contract; the third-party API will be mapped onto it. **Status**: `assumed-confirmed`. **Assumption in force**: build `IMarketIntelligenceProvider` around `MarketDeal`; the live client maps into it. Ref: ADR-024.
- **OQ-askv2-002** — Admission threshold (0.6) and minimum readable text (200 chars) are right for the golden set. **Status**: `assumed-confirmed`. **Assumption in force**: both are configuration (`Documents:AdmissionThreshold`, `Documents:MinReadableChars`), tuned on `Contigo.AiEval`. Ref: R-DOC-03.
- **OQ-askv2-003** — Savings KPIs / opportunities are not a rail item in V2. **Status**: `assumed-confirmed`. **Assumption in force**: route `/savings` (renamed from Home), reached from Ask actions, Renewals and Contract 360. Ref: R-WEB-02, `contigo-v2/ia-v2.md`.
- **OQ-askv2-004** — Conversation retention. **Status**: `assumed-confirmed`. **Assumption in force**: unlimited in V2; deletion by the owner only. Ref: R-CONV-01.
- **OQ-askv2-005** — Per-user identity for conversations under the ADR-022 header posture. **Status**: `assumed-confirmed`. **Assumption in force**: `X-User-Id` = MSAL account username, non-authoritative; the task that lands the API JWT (ADR-010) replaces it with the token subject. Ref: R-CONV-03.
- **OQ-askv2-006** — Answer language. **Status**: `assumed-confirmed`. **Assumption in force**: follows the question's language; fixtures and golden set cover Italian and English. Ref: ADR-024.
- **OQ-askv2-007** — Upload processing stays synchronous in the request. **Status**: `assumed-confirmed`. **Assumption in force**: bounded by 50 MB / file and the OCR page budget; worker-queued upload is a later task. Ref: R-DOC-01, A7.
- **OQ-askv2-008** — A Quote admitted in Documents. **Status**: `assumed-confirmed`. **Assumption in force**: no automatic `Quote` record; the result card and Ask route to Quote check (`/quotes`) where the user uploads the quote. Ref: R-DOC-03 AC-4.
- **OQ-askv2-009** — Live Foundry on `dev` / `demo`. **Status**: `assumed-confirmed`. **Assumption in force**: acceptance A2–A8 runs against the Foundry-backed gateway (Container Apps inject `AiGateway__*`); CI proves the same paths on the fixture gateway. Ref: R-AI-01.
