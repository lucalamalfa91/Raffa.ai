---
id: feature-02
type: feature
parent: epic-01
wave: R0
status: active
---

# feature-02-azure-infrastructure — Terraform `dev`/`demo` infrastructure

## Slice

Provision two isolated Azure environments (`dev`, `demo`) in `westeurope` via HCP
Terraform: a reusable module library plus two thin env roots, instantiated with
Container Apps (consumption, scale-to-zero), PostgreSQL Flexible Server (Burstable)
with `pgvector`, Storage Account (blob + queue), Service Bus Standard, Key Vault,
Entra ID app registrations (OIDC), Container Registry (Basic), and the Azure AI
Foundry account shape (one hub, two projects) with Document Intelligence S0 on
the same PAYG AI services account (ADR-017, OCR in V1) — all tagged `project=raffa`,
`env=dev|demo`, never sharing a store between envs.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Terraform module library + version/provider pins (ADR-007) | R0 |
| us-02 | `dev` environment instantiation (ADR-005/006/007) | R0 |
| us-03 | `demo` environment instantiation (ADR-005/006/007) | R0 |
| us-04 | Entra app registrations + Key Vault (ADR-010/011) | R0 |
| us-05 | Foundry account: hub + two projects + Document Intelligence (ADR-008, ADR-017) | R0 |

## Architecture decisions in force

- ADR-005 — Azure services + SKUs
- ADR-006 — region `westeurope`
- ADR-007 — Terraform module layout + remote state + no secrets in source
- ADR-008 — Foundry account (one hub, two projects, one PAYG AI services account)
- ADR-010 — Entra OIDC (4 app registrations)
- ADR-011 — Key Vault + managed identity + no secrets
- ADR-017 — OCR in V1 (Document Intelligence S0 on the same AI services account)

## Target repo

`raffa-infra`
