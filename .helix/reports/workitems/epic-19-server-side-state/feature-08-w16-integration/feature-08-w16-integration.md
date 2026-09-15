---
id: feature-08
type: feature
parent: epic-19
wave: w16
status: active
extends: epic-16 F04
---

# feature-08-w16-integration — The wave is green, swept and walkable on `dev`

## Slice

The single task of this feature depends on every leaf artifact of w16. It builds
and tests both trees, asserts the wave's three negatives (zero `infra/` delta,
exactly two `.github/workflows` files changed, the **paired** retirement grep),
sweeps the stale prose the wave's own changes create, and writes
`docs/waves/w16-acceptance.md` — the per-item manual walk on `dev` an operator
follows, with its closing known-gaps table.

`backend.yml`'s build+test job is unfiltered, so the Postgres/Testcontainers
suites gate here. A `127.0.0.1:5432 refused` is a **fixture gap, never a flake to
re-run** (ADR-014 w15 clause 3).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | final-integration | w16 |

## Architecture decisions in force

- **ADR-016** w16 clause 34 — the final-integration list: build *and* test both
  trees; **no `terraform fmt/validate`** — assert the negative instead; the
  inverted CI-drift assertion; the paired retirement grep; write the acceptance
  doc in the w14/w15 shape; the README sweep including **`web/README.md:1166`**,
  the stale `documentStore.ts` paragraph NW-10 left behind and for which the
  intake deliberately created no task — *this is that task*.
- **ADR-016** w16 clause 35 — the known-gaps set: **closes** w15's reprocess row,
  **inherits** four (`demo` flags, Postgres-only suites, e2e-not-in-CI, the
  verified-domain guest refusal), **adds** two (the bulk whole-tenant reprocess
  deferral to W17, and the baseline's pending infra apply stated as a fact).
- **ADR-014** w16 clauses 1–2 — the CI-YAML set is **exactly two files**; an
  unplanned `.github/workflows/**` diff is a defect and this task fails on it.
  **One** `integration → main` PR; **no `demo-v*` tag is cut**.
- **ADR-012** §12 — the Playwright case is acceptance-runbook evidence, **not a
  CI gate**; no workflow runs Playwright (NW-50 is W18) and it must not be
  presented as one.

## Target repo

mixed — `raffa-backend` + `raffa-web` + docs (no `infra/`, no new workflow)
