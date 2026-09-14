---
id: feature-03
type: feature
parent: epic-17
wave: w15
status: active
extends: epic-15 F01 (ADR-025 §H — the T14 / T15 activation conditions)
---

# feature-03-invitation-e2e — The invitation e2e, and the test seam that must never ship

## Slice

NW-58r reduces, deliberately and with three independent reasons on the record, to
two small things: the invitation spec stops asserting something that is no longer
true, and a negative test makes the council's refusal of a test-only
authentication seam **enforceable rather than advisory**.

What it does **not** do is add a Playwright runner (NW-50, W18), a test-only
passcode seam (an authentication bypass shipped in every image, on a directory
`dev` and `demo` share, with nothing to execute it), or a `dev` mail-catcher (a
fourth apply that could error the single HCP run already carrying Service Bus,
ACS and Graph). N3b is walked by hand in the acceptance runbook.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | The spec tells the truth, and no test seam can be added quietly | w15 |

## Architecture decisions in force

- **ADR-025 §J.8–§J.9** — the seam refusal, the acceptable future mail-catcher shape, the bounds on any e2e directory credential, and **S-T23**.
- **ADR-016** (w15 clauses 20, 22) — no Playwright runner and no `dev` mail-catcher in w15; w14 clause 10 reaffirmed with its premise re-verified; N3b as a numbered acceptance step; the acceptance doc belongs to the final-integration task.
- **`none — ADR-014`** — a spec file is not CI YAML, so w15's zero-workflow-file assertion is untouched.

## Target repo

`raffa-web` (the spec) and `raffa-backend` (the negative test that runs in CI)
