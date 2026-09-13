---
wave: w14
source: inputs/next/<file>.md
source_sha256: <sha256 of the raw file>
design_sources: [inputs/design/<path>, …]        # or []
baseline: <short sha> (<branch>)                  # git -C .. rev-parse --short HEAD
generated: <YYYY-MM-DDTHH:MMZ>
previous_wave: e13
caps: { max_tasks: 20, max_phases: 5 }
focus: "<operator focus or none>"
---

# Wave <w> — normalized requirements

Written by `next-intake` from the raw file above. The raw file stays the
human's; this file is the process oracle for the council and the decomposer.
Items keep the source ids when the raw file has them (`NW-01`), otherwise
they get `<W>-NN`. Every claim about "today" cites a file in `../`.

## 1. Oracles in force

- Product: `inputs/product-spec.md` §…, `inputs/requirements.md` §…,
  `inputs/percorso-pilota-v1.md` §…
- Locked: `reports/context/locked-decisions.md` (cite, never re-open)
- ADRs touched by this wave: ADR-NNN (…), …
- Design: `inputs/design/<path>` (anchors: …) | none
- Last wave: `reports/execution/wave-close*.md` — undelivered / salvage tags: …
- Carry-over: queued tasks from `BACKLOG.md` (`status: queued`): …

## 2. Items

| ID | Title | Kind | Priority | Area | Status today | Seats | ADR touchpoints | Design refs | Acceptance |
|---|---|---|---|---|---|---|---|---|---|
| <W>-01 (NW-01) | … | bug / change / feature / design / infra / ops / carry-over | must / should / could | web / backend / infra / ai / auth / ci | OPEN / PARTIAL / CLOSED-ON-MAIN / DEFERRED | software-architect, security-architect | ADR-010 | — | N1, N5 |

### <W>-01 — <title>

- **Source**: NW-01, `inputs/next/<file>.md` §1 (raw wording quoted below)
- **Raw**: "<verbatim or tight paraphrase, original language>"
- **Today (evidence)**: `../backend/src/…/X.cs:120` does …; `../web/src/…`
  reads …; `GET /api/…` returns …
- **Gap**: what is missing or wrong, in one or two sentences
- **Seats**: software-architect (new endpoint + membership table),
  security-architect (token subject → membership) | none
- **ADR touchpoints**: amend ADR-010 (…) | new ADR (…) | none
- **Design refs**: `inputs/design/…` anchor "…" | none
- **Acceptance**: N1 …; N5 … (observable on `dev`)
- **Proposed epic**: epic-14-<slug> (extends epic-01 F05, epic-06)
- **Task sketch**: 1–3 bullets naming files and endpoints (optional)

(repeat per item)

## 3. Seat roster for this wave

| Seat | Involved | Items |
|---|---|---|
| product-owner | yes / no | <W>-03 |
| software-architect | … | … |
| cloud-architect | … | … |
| security-architect | … | … |
| client-architect | … | … |
| ux-ui-designer | … | … |
| delivery-manager | … | … |

## 4. Proposed epics (append-only, next free numbers)

| Epic | Slug | Theme | Items | Extends |
|---|---|---|---|---|
| epic-14 | … | … | <W>-01…<W>-08 | epic-01 F05, epic-06 |

## 5. Selection for this wave (cap <max_tasks> tasks / <max_phases> phases)

- **In wave** (priority order): <W>-01, …
- **Queued** (decomposed, not in this wave): …
- **Out**: CLOSED-ON-MAIN: …; DEFERRED: …; out of scope (ADR-001 §1.2): …
- **Order constraints**: e.g. "<W>-05 (JWT) before <W>-01 (membership from `sub`)"

## 6. Superseded work items

| Existing item | Superseded by | Why |
|---|---|---|
| none | | |

## 7. Open questions and assumptions in force

- OQ-<w>-001 — … **Assumption**: … (also appended to `reports/open-questions.md`)
