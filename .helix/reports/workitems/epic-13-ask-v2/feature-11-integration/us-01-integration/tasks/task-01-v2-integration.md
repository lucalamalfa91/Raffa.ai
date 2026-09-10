---
id: E13/F11/US01/T01
type: task
story: us-01-integration
wave: 13
status: live
target_repo: raffa-backend
---

# task-01-v2-integration — CI jobs, golden set in CI, e2e V2 path, acceptance checklist, README sweep

## Coding objective

Close the wave the way `inputs/requirements.md` §10 and spec §20 demand.
(1) `.github/workflows/seed-market-intelligence.yml`: manual / callable,
input `target_environment` (`dev` | `demo`), same OIDC login, resource
group resolution and connection-string fetch as
`seed-demo-fixture.yml`, then runs the Worker command
`ingest-market --feed backend/fixtures/market-intelligence.mock.json`
(F02/T02) against that environment's database; prints the ingestion
summary; a second run must report zero inserted / updated.
(2) `.github/workflows/reprocess-tenant-documents.yml`: input
`target_environment` + `tenant_id`; lists the tenant's documents through
`GET /api/documents` and calls `POST /api/documents/{id}/reprocess` for
each with the deploy identity (or runs a `backend/scripts/reprocess_tenant.py`
helper if the API host is not reachable from CI — say which and why in
the README); afterwards verifies via SQL that no embedding of that tenant
starts with `%PDF` and reports contracts still without a supplier.
(3) `web/e2e/v2.spec.ts` (Playwright, next to `day1.spec.ts`): the V2 pilot
path against `dev` with the fixture-seeded workspace — A1 (drop a recipe
PDF, an unreadable PNG and a sample MSA together: two **Not added** cards,
one processed row), A3 ("ciao" → redirect prose, no abstain block), A4
("posso fare causa a Salesforce?" → refusal + Contract 360 action), A8
("cosa sai fare?" → feature cards with working links), A9 (a reply has a
citation card and an action; no guid / route line), A10 (reload
`/ask/<id>` resumes), A14 (`/` lands on `/ask`; rail two-tier; secondary
tier greyed before validation); A2 / A5 / A6 / A7 marked `test.skip` with
the reason "requires live Foundry" unless `E2E_LIVE_FOUNDRY=1`.
(4) Confirm the golden set runs in `backend.yml` through `dotnet test
Raffa.slnx` (no extra step) and fails the build on a guard
intervention; add the `AiEval` trait filter documentation. (5)
`docs/ask-v2-acceptance.md` (new root `docs/` folder): A1–A14 with the
exact questions (Italian and English), expected kinds, screens to check,
and the operator steps (seed market feed → reprocess tenant → promote
`demo-v*`). (6) README sweep: root `README.md` (V2 pilot path, new
workflows), `backend/README.md` (Market / Insights / Suppliers / Chat
modules, admission gate, conversations, AiEval, schema scripts list),
`web/README.md` (V2 routes, conversations, `X-User-Id`, e2e v2), `infra/README.md`
only if a connection string was added. This task is the phase-5 writer of
`Program.cs` and `backend.yml` if any last wiring is missing; prefer no
change.

## Parent story AC covered
- AC-1, AC-2, AC-3, AC-4, AC-5

## Files to create or modify
| Path | Change |
|------|--------|
| `.github/workflows/seed-market-intelligence.yml`, `.github/workflows/reprocess-tenant-documents.yml` | new |
| `backend/scripts/reprocess_tenant.py` (+ `tests/test_reprocess_tenant.py`) | new if needed |
| `web/e2e/v2.spec.ts`, `web/playwright.config.ts` | new spec; live-Foundry env switch |
| `docs/ask-v2-acceptance.md` | new |
| `.github/workflows/backend.yml` | only if the AiEval run needs a change (phase-5 writer) |
| `backend/src/Raffa.Api/Program.cs` | only if a mapping is still missing (phase-5 writer) |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (acceptance A1–A14), ADR-014 / 015 / 016 (CI, OIDC, promotion), ADR-021 / 022 (schema by CI; explicit seed jobs), ADR-011 (deploy identity, no secrets in workflows).
- **Design**: `inputs/design/prototypes/Raffa V2 Prototype.html` and `raffa-v2/ia-v2.md` "Pilot path" — the e2e follows it act by act; `raffa-v2/screens-v2.md` for the copy the e2e asserts.
- Gap G-INTEGRATION. Templates: `.github/workflows/seed-demo-fixture.yml`, `web/e2e/day1.spec.ts`, `reports/execution/demo-v-promotion-runbook.md`.
- **Do not touch**: application source under `backend/src` or `web/src` (if something is broken, HALT and name the task that owns it).

## Definition of done
- [ ] `python -m pytest tests/test_reprocess_tenant.py` exit 0 (if the helper exists); `actionlint` or `python scripts/ci_path_filters_verify.py` exit 0 on the new workflows
- [ ] `npx playwright test web/e2e/v2.spec.ts` exit 0 against `dev` (skipped cases listed with reasons)
- [ ] `dotnet test backend/Raffa.slnx` exit 0 (golden set included)
- [ ] `docs/ask-v2-acceptance.md` lists A1–A14; every README named above updated in the same commit

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| e2e | V2 pilot path on `dev` | `web/e2e/v2.spec.ts` |
| unit | helper script | `tests/test_reprocess_tenant.py` |
| CI | workflows lint | `scripts/ci_path_filters_verify.py` |

## Open questions blocking this task
- OQ-askv2-009 — live Foundry decides the e2e coverage (assumed)

## Wave-spec entry
```yaml
- id: E13/F11/US01/T01
  prompt: reports/workitems/epic-13-ask-v2/feature-11-integration/us-01-integration/tasks/task-01-v2-integration.md
  produces: [v2-integration]
  depends_on: [ask-golden-set, web-ask-v2, web-contract360-landing, supplier-extraction, market-index]
  effort: L
  layer: backend
  status: live
```
