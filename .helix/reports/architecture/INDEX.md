# Raffa V1 — Architecture Decision Records (INDEX)

Accepted ADRs promoted at council-close (2026-09-01/02), plus ADR-017 (OCR in V1). Source drafts live under
`reports/architecture/draft/<seat>/`; accepted copies below carry the canonical `ADR-NNN` number and
`Status: accepted`. Supporting (non-ADR) lane artefacts are listed separately and remain under their
seat's draft folder.

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-001 | V1 scope R0–R4 | product-owner | User-visible wave ladder; §1.2 non-goals out of scope; R3/R4 on fixture adapter, never a paid API for first `demo`. |
| ADR-002 | .NET solution shape | software-architect | Modular monolith: one project per bounded context + shared kernel + thin API/worker hosts. |
| ADR-003 | Relational store | software-architect | PostgreSQL Flexible Server + pgvector via EF Core/npgsql; RLS tenancy; single system of record. |
| ADR-004 | Foundry model roles | software-architect | Role-split (ocr/classify/extract/embed/answer) behind AI Gateway, config-selected cheapest IDs. |
| ADR-005 | Azure services + SKUs | cloud-architect | Container Apps (consumption) + Postgres Burstable + Storage + Service Bus Standard + Key Vault + Entra ID Free. |
| ADR-006 | Region | cloud-architect | North Europe (`northeurope`) for both `dev` and `demo` (West Europe ineligible for new customers on this tenant). |
| ADR-007 | Terraform layout | cloud-architect | Reusable modules + two env roots; remote state per env; no secrets in source. |
| ADR-008 | Foundry account shape | cloud-architect | One hub, two projects (`dev`/`demo`), one pay-as-you-go AI services account. |
| ADR-009 | Tenancy / RLS | security-architect | Postgres RLS on every tenant table; app passes `tenant_id`; RLS is the non-bypassable backstop. |
| ADR-010 | Entra ID / OIDC | security-architect | Per-env pair (public client + API registration), Authorization Code + PKCE, four registrations total. |
| ADR-011 | Key Vault + RAG isolation | security-architect | Per-env Key Vault + managed identity + OIDC federation; authz-before-retrieval; no-training; input-hash logging. |
| ADR-012 | Web stack | client-architect | React + TypeScript + Vite SPA, OIDC PKCE, static bundle on Static Web Apps free tier. |
| ADR-013 | Mobile stack | client-architect | React Native (Expo) + TypeScript, non-gating lane, no store release for R0–R4. |
| ADR-014 | Git flow | delivery-manager | Trunk-based, protected `main`, PR required; `main`→`dev` auto-deploy; tag + env approval for `demo`. |
| ADR-015 | CI → Azure auth | delivery-manager | OIDC federated credentials, per-env least-privilege service principals; no stored secrets. |
| ADR-016 | Promotion dev→demo | delivery-manager | Tag + `demo` GitHub Environment with required reviewers; code/artifacts only, never data. |
| ADR-017 | OCR in V1 | software-architect | Hybrid native-text + Azure AI Document Intelligence (`prebuilt-read`/`prebuilt-layout`) behind the AI Gateway; full document; not deferred. |

## Supporting artefacts (not ADRs)

| File (draft) | Seat | Purpose |
| --- | --- | --- |
| `draft/product-owner/scope-notes.md` | product-owner | Personas, non-goals, day-1 promise, acceptance hooks (seed for user stories). |
| `draft/software-architect/module-map.md` | software-architect | Module boundaries, entity ownership, dependency direction, worker responsibilities. |
| `draft/client-architect/api-consumption.md` | client-architect | One versioned OpenAPI contract → one generated TS client; OIDC; config-not-code. |
| `draft/delivery-manager/wave-calendar.md` | delivery-manager | Wave order + calendar (S0 → R0–R4, ~18 weeks) and environment plan. |

## Required-ADR coverage check

All fourteen required topics from the council-protocol brief are covered by ADR-001 through ADR-016
(scope/R0–R4, git flow, Azure SKUs, region, Terraform layout, .NET solution, web, mobile, Foundry
models, CI→Azure auth, promotion, relational store, tenancy/RLS, Key Vault+RAG). ADR-017 closes the
CQ-008 sub-item "OCR vs native document parse": OCR is in V1, not deferred. No required topic
remains unaddressed.

## Web delta (wave 6+, appended 2026-09-05)

ADR-001…017 are unchanged. New accepted ADRs from `raffa-web-design`:

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-018 | Web information architecture | ux-ui-designer | Left-rail Day-1 sitemap; Admin vs Procurement; cites `inputs/design/prototypes/ia.md`. |
| ADR-019 | Web design system | ux-ui-designer | Adopt Claude Design Modernist export verbatim; no forked tokens. |
| ADR-020 | Web screen inventory | ux-ui-designer | 1:1 §16/§20 → ten screens in `prototypes/screens.md` + `day1-demo.html`. |

## Schema-apply (wave 9 / e09, appended 2026-09-05)

ADR-001…020 are unchanged. New accepted ADR from `raffa-schema-design`:

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-021 | Schema apply on Azure Postgres | software-architect | CI applies checked-in idempotent EF SQL after container update; no `MigrateAsync` in the API; Terraform injects Savings/Quotes connection strings. |

## Demo-readiness (wave 10 / e10, appended 2026-09-05)

ADR-001…021 are unchanged. New accepted ADR from `raffa-readiness-design`:

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-022 | Day-1 demo auth + fixture seed | security-architect | First `demo-v*` may keep `X-Tenant-Id` on the API; savings numbers come from a seeded fixture on `raffa_demo`; ADR-010 remains the post-Day-1 host target. |

## Ask savings copilot (wave 12 / e12, appended 2026-09-08)

ADR-001, 004, 011, 018, 020 keep their original Decision and gain an
**amendment footer**. New accepted ADR from `raffa-ask-design`:

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-023 | Ask Raffa savings copilot | product-owner + software-architect | Foundry-hosted copilot over tenant RAG + `IBenchmarkService` (fixture now); narrates P25–P75 / negotiation numbers; no legal advice; rich `/ask` reply. |

## Ask Raffa V2 (wave 13 / e13, appended 2026-09-08)

ADR-001…022 keep their original Decision; ADR-001, 004, 011, 018, 020 gain
an **epic-13 amendment footer** (superseding their epic-12 footers).
**ADR-023 is superseded by ADR-024** (HITL 2026-09-08, `inputs/requirements.md`
§0 D4; epic-12 / e12 never launched). New accepted ADR from
`raffa-ask-process.yaml` (Ask V2):

| ADR | Topic | Seat | One-line decision |
| --- | --- | --- | --- |
| ADR-024 | Ask Raffa V2 | product-owner + software-architect + security-architect + ux-ui-designer | Documents-only intake with an admission gate before persistence (non-contracts refused, never stored); server-side conversations under RLS; three sources of truth (validated contracts, market-intelligence feed with its own index — mock now, API later —, capability catalog); structured no-tools `answer` role with grounding + numeric guards; deterministic strategies (contract vs market, renewal strategy, portfolio criticality); V2 IA with `inputs/design/prototypes/Raffa V2 Prototype.html` (unpacked `raffa-v2/`) as the pixel reference. |

## Live Foundry (appended 2026-09-09)

ADR-001…024 keep their original Decision. ADR-004, ADR-005, ADR-008 and
ADR-017 gain an **amendment footer** dated 2026-09-09: the shared Azure AI
Services account `aisvc-raffa` (kind `AIServices`, account-native Foundry
projects, no hub), its per-environment projects, model deployments and RBAC
are created by Terraform (`infra/modules/foundry`; owned by the `dev` root,
attached by `demo`; two-phase wiring behind `ai_gateway_wired`); confirmed
per-environment model ids (dev: gpt-5.4-nano + text-embedding-3-small; demo:
gpt-5.4 / gpt-5.4-nano + text-embedding-3-large at 1536 dimensions); OCR is
Document Intelligence `prebuilt-read` for every PDF and image with the page
map from `pages[].spans`, native parsing only for DOCX/XLSX. No new ADR.
