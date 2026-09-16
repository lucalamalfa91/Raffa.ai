---
id: feature-06
type: feature
parent: epic-22
wave: w17
status: active
extends: epic-19 F08
---

# feature-06-w17-integration — the wave is green, and a human can walk it on `dev`

## Slice

The last phase of w17: one task that builds and tests both trees, validates **both**
Terraform roots, proves the wave's CI-YAML delta is **exactly one added file**, proves
the `infra/` delta is exactly the four Terraform files ADR-016 w17 clause 37 names,
runs the browser paths that need no live Foundry, sweeps the READMEs whose public
surface this wave changed, and writes **`docs/waves/w17-acceptance.md`** — the
operator's per-item walk on `dev`, in the shape w14, w15 and w16 already use.

⚠ It also records what the wave **did not** deliver, as facts rather than
assumptions: the two outstanding HCP applies, the `demo` promotion outcome with the
tag actually read, the invitation walk that stays owed even if the promotion happens,
and the W18 remainder of NW-63 (bounding boxes, the widened gateway contract, the
phrase-edit write path).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | final-integration | w17 |

## Architecture decisions in force

- **ADR-016** w17 clause 41 — the final-integration list, inverted for a wave that
  **does** touch `infra/`; clause 42 — the known-gaps table.
- **ADR-014** w17 clause 1 — the CI-YAML set is exactly one added file; clause 5 —
  the wave record is `docs/waves/<w>-acceptance.md`, **never** the engine's generic
  `reports/execution/wave-close.md`; clause 6 — `backend.yml` stays closed.
- **ADR-005** w17 §19, §24 — the 20-file render measurement on `dev` runs **before**
  the first whole-tenant reprocess, never after.
- **ADR-016** w17 clause 44 — the acceptance walk's **first act** is confirming the
  `raffa-dev` apply, not running the console.

## Target repo

mixed — verification spans `backend/`, `web/`, `infra/` and `.github/`; the only file
this feature writes is `docs/waves/w17-acceptance.md` (plus standing README hygiene).
