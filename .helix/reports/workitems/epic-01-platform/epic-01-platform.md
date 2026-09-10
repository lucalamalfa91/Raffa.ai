---
id: epic-01
type: epic
wave: R0
status: active
---

# epic-01-platform — Platform foundation (R0)

## Business capability

Deliver the secure multi-tenant foundation every later wave builds on: the
existing GitHub repository [`lucalamalfa91/raffa`](https://github.com/lucalamalfa91/raffa),
HCP Terraform provisioning of two
isolated Azure environments (`dev`/`demo`), trunk-based CI/CD with an explicit
`demo` promotion gate, a deployable ASP.NET Core modular-monolith API + background
worker, PostgreSQL+pgvector with tenant RLS, Entra OIDC auth, workspace/roles,
document upload into tenant-scoped object storage, and an audit baseline — so that
**a secure workspace can ingest documents** (spec §16 R0).

## Product coverage

| Source | Item |
|--------|------|
| spec §16 | R0 — Foundation (definition of success) |
| spec §3.2 | multi-tenancy, `tenant_id`, app+DB isolation |
| spec §14.1 | auth, secrets, audit, TLS |
| ADR-001 | V1 scope R0–R4 (R0 row) |
| ADR-002 | .NET modular monolith + worker |
| ADR-003 | PostgreSQL + pgvector |
| ADR-005 | Azure services + SKUs |
| ADR-006 | region `westeurope` |
| ADR-007 | Terraform layout |
| ADR-008 | Foundry account shape |
| ADR-009 | tenancy / RLS |
| ADR-010 | Entra OIDC registrations |
| ADR-011 | Key Vault + managed identity |
| ADR-012 | web stack (SPA skeleton) |
| ADR-013 | mobile stack (non-gating scaffold) |
| ADR-014 | git flow |
| ADR-015 | CI → Azure auth |
| ADR-016 | promotion dev→demo |
| ADR-017 | OCR in V1 (Document Intelligence endpoint on the AI services account) |

## Features

| ID | Title | Wave |
|----|-------|------|
| feature-01 | platform-bootstrap (lucalamalfa91/raffa + HCP + git flow) | R0 |
| feature-02 | azure-infrastructure (Terraform dev/demo) | R0 |
| feature-03 | ci-cd-delivery (CI auth + pipelines + promotion) | R0 |
| feature-04 | backend-foundation (monolith + store + RLS + deployable API) | R0 |
| feature-05 | identity-workspace (OIDC + workspace + roles) | R0 |
| feature-06 | document-ingestion (upload + storage + audit) | R0 |
| feature-07 | web-client-foundation (OIDC SPA) | R0 |
| feature-08 | mobile-scaffold (non-blocking) | R0 |
| feature-09 | r0-integration | R0 |

## Success looks like

A reviewer can, on `dev`: authenticate as a workspace admin, create a workspace,
invite a Procurement user, upload a contract PDF, and see the document stored and
an audit event recorded — all enforced by DB-level RLS so one tenant cannot read
another tenant's rows, and the same path is repeatable on `demo` after an explicit
tag + environment approval.

## Architecture decisions in force

- ADR-001, ADR-002, ADR-003, ADR-005, ADR-006, ADR-007, ADR-008, ADR-009, ADR-010, ADR-011, ADR-012, ADR-013, ADR-014, ADR-015, ADR-016, ADR-017

## Out of scope

- Any R1–R4 product capability (extraction, renewals, savings, quote check) beyond the scaffolding that later waves complete.
- Production environment, production HA, multi-region, dedicated-per-tenant DB.
- Mobile store release (mobile is a non-gating scaffold only).
