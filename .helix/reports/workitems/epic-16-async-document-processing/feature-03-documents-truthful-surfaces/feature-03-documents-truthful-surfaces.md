---
id: feature-03
type: feature
parent: epic-16
wave: w15
status: active
extends: epic-06 F05 (upload UI + status read-back), epic-13 F09 (web V2 documents screen)
---

# feature-03-documents-truthful-surfaces — Every number is the server's, and every wait says so

## Slice

The web half of the wave. The Documents screen renders the refusal that is now a
row, reads every number from the server instead of from the fetched page, and
stops polling honestly when nothing changes. The rail badge reads the endpoint
that has existed since epic-13 instead of a `sessionStorage` key whose writer has
no caller. Ask, Portfolio and Contract 360 gain the state they have never had:
**not ready yet**, told by the server, never inferred.

One task, because the council's own mechanisms braid these files together: the
counts, the refusal row and the local-upload row live in the same four files, the
three downstream surfaces share **one** poll budget with one sentence and one
label (ADR-018 w15 clause 6), and splitting them buys a single-writer conflict
or a compile failure for no benefit.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Documents, the rail and the downstream screens read the server | w15 |

## Architecture decisions in force

- **ADR-012** (w15 footer §4–§6, §10, §13.4–§13.8, §17–§18) — the provenance rule, the evidence hand-off, the counters, the upload deadline, the local refusal row, the third chip's server-side bucket, `isAttentionStatus`, `buildKbSummary`, the poll's no-change budget.
- **ADR-018** (w15 clauses 1–3, 6) — the IA states contract gains **not ready yet** and **nothing made it through**; what each Documents number counts; the rail-badge rule; one stopped-updates budget across five surfaces.
- **ADR-020** (w15 §1, §2, §6, §8) — screen 3's row, label, hint copy, deleted card, third chip and "Queued…"; screens 2, 5 and 6's new states; the stopped-updates notice.
- **ADR-019** (w15 clauses 1–2, 4–6) — **one** semantic row, `Rejected → .tag-outline` "Not added", derived from the shipped card. **No token, no component, no confidence row.**
- **ADR-027 §D7–§D9** — the server fields this feature consumes.
- **`none — ADR-013`** — mobile is a non-gating scaffold and no w15 item reaches it.

## Target repo

`raffa-web`
