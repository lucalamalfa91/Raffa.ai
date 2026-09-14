---
id: feature-03
type: feature
parent: epic-18
wave: w15
status: active
extends: epic-14 F02 (ADR-022's interim posture and its named retirement items)
---

# feature-03-interim-posture-retirement — The role headers and the unattributed actor go

## Slice

The two interim mechanisms ADR-022's w14 footer demoted and gave named
retirement items to. `X-Role` still flips catalog visibility through
`GET /api/capabilities` and is still a **published header parameter** in the
contract; and `"unattributed"` is still the actor nine service-layer sites write
to `CreatedBy` and to audit rows when no identity arrives.

Neither is a new decision — ADR-022's footer already governs both, and the w15
requirements record `seats: none` for NW-31 for exactly that reason. **Both
stories are queued to W16**; neither has a task in
`reports/plan/slices/w15.yaml`.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | The dual role headers are retired | **queued — W16** |
| us-02 | Every write names its actor | **queued — W16** |

## Architecture decisions in force

- **ADR-022** (w14 footer, w15 footer) — the interim mechanisms and their named retirements; the schedule is updated by NW-05.
- **ADR-011** — an audit row that cannot name its actor is a gap in the audit posture.
- **ADR-026** — the published contract must not carry a spoofable header parameter or a stale description.
- **ADR-016** (w15 clause 21) — `reprocess-tenant-documents.yml` is **out of service from w15** and **NW-31 owns it in W16**, so one wave opens that file once.

## Target repo

`raffa-backend`, plus `web/openapi/raffa-api.v1.json` and
`.github/workflows/reprocess-tenant-documents.yml`
