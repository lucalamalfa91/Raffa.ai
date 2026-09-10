---
id: F11
type: feature
parent: epic-13
wave: 13
status: active
---

# feature-11-integration — CI jobs, golden set in CI, e2e V2 path, demo acceptance

## Slice

Closes the wave the way spec §20 closes V1: the V2 pilot path proven in a
browser against `dev`, not `dotnet test`. Adds the operator jobs the
earlier features assume — `seed-market-intelligence.yml` (idempotent
ingestion of the mock feed into `raffa_dev` / `raffa_demo`) and
`reprocess-tenant-documents.yml` (re-OCR / re-embed + supplier back-fill
for existing tenant documents) — the Playwright `web/e2e/v2.spec.ts`
walking `inputs/requirements.md` §10 A1…A10 and A14, the golden set
running in CI on the fixture gateway, the demo acceptance checklist
(`docs/ask-v2-acceptance.md`) for A2–A8 on live Foundry, and the README
sweep for every public surface the wave changed.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | V2 final integration — the pilot path on `dev`, ready for `demo` | 13 |

## Architecture decisions in force

- ADR-024 — acceptance A1–A14
- ADR-014 / ADR-015 / ADR-016 — CI on `main`, OIDC identities, tag promotion to `demo`
- ADR-021 / ADR-022 — schema by CI, fixture seed as an explicit job

## Target repo

mixed — `raffa-backend` (workflows, scripts), `raffa-web` (e2e)
