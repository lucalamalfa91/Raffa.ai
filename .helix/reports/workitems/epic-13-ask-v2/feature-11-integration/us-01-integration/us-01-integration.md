---
id: us-01
type: user-story
parent: feature-11
wave: 13
status: active
---

# us-01-integration — V2 final integration: the pilot path on `dev`, ready for `demo`

## Story

As the **operator**, I want the whole V2 pilot path proven in a browser
against `dev` and the operator jobs that make `demo` honest (market feed
seeded, existing documents re-processed and supplier-named) checked in and
runnable, so that promoting `demo-v*` shows a working product, not a set
of green unit tests.

## Acceptance criteria

- [ ] AC-1 `.github/workflows/seed-market-intelligence.yml` (manual /
      callable, per environment, same OIDC identity model as
      `seed-demo-fixture.yml`) ingests `backend/fixtures/market-intelligence.mock.json`
      idempotently; a second run reports zero changes.
- [ ] AC-2 `.github/workflows/reprocess-tenant-documents.yml` re-OCRs /
      re-embeds every document of a given tenant and back-fills suppliers;
      afterwards no embedding starts with `%PDF` and no contract of that
      tenant lacks a supplier when the document names one.
- [ ] AC-3 `web/e2e/v2.spec.ts` (Playwright) walks
      `inputs/requirements.md` §10 A1, A3, A4, A8, A9, A10, A14 against `dev`
      with the fixture-seeded workspace (and A2, A5–A7 when live Foundry is
      available, skipped with a named reason otherwise).
- [ ] AC-4 The golden set (`Contigo.AiEval`) runs in `backend.yml` on the
      fixture gateway and fails the build on any numeric-guard intervention
      or kind mismatch.
- [ ] AC-5 `docs/ask-v2-acceptance.md` lists A1–A14 with the exact
      questions, expected kinds and the screens to check on `demo`; every
      README whose public surface changed in e13 is current (root,
      `backend/`, `web/`, `infra/` if workflows changed).

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| every e13 story | final integration depends on every leaf artifact of the wave |

## Architecture decisions in force

- ADR-024 — acceptance A1–A14
- ADR-014 / ADR-015 / ADR-016 — CI on `main`, OIDC identities, tag promotion
- ADR-021 / ADR-022 — schema by CI; seeds as explicit jobs, never a side effect of a push

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | CI jobs, golden set in CI, e2e V2 path, acceptance checklist, README sweep | L | phase-5 |

## Council decisions carried into this story

Reuse `.github/workflows/seed-demo-fixture.yml` as the template (OIDC
login, resource-group resolution, connection string fetch per ADR-021);
the reprocess job calls the API `POST /api/documents/{id}/reprocess` per
document with the deploy identity's `X-Tenant-Id`, or a `backend/scripts/`
Python helper when the API is not the right tool. Playwright config stays
`web/playwright.config.ts`; the V2 spec sits next to `day1.spec.ts`.

## Open questions

- OQ-askv2-009 — live Foundry availability decides which e2e cases run (assumed)
