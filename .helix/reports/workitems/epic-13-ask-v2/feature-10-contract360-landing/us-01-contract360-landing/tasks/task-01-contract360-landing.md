---
id: E13/F10/US01/T01
type: task
story: us-01-contract360-landing
wave: 13
status: live
target_repo: raffa-web
---

# task-01-contract360-landing — `?clause=` / `?page=` highlight with original wording + "Ask about it"

## Coding objective

Make Contract 360 the landing of every Ask citation, as in
`inputs/design/prototypes/Raffa V2 Prototype.html` — unpacked
`raffa-v2/app.jsx` `hl`, `citedOpened`, `back`, `backLabels`,
`clauses[].show`, `c360Chips`; `raffa-v2/markup.html` evidence block
"{{ hl.before }} **{{ hl.quote }}** {{ hl.after }}" and "← {{ backLabel }}";
`raffa-v2/screens-v2.md` §5 "Why". In
`web/src/routes/contracts/contract360/`: read `clause` and `page` query
params (and `location.state.from`); when `clause` matches a clause of the
360 payload, scroll to it, highlight it (`--color-accent-100` fill +
accent left bar) and render its original wording (`rawText`, with
`sourceSpan` emphasised when present) in the existing Clauses tab / list
without waiting for a click; when only `page` is given, highlight the
first clause with `sourcePage == page`; the back link reads "← Ask
Raffa" when `from === "ask"`, else the existing label. Add **Ask about
it** in the header (`btn-secondary`) navigating to
`/ask?scope=<contractId>` (new chat; the Ask route consumes the param in
F09/T04); show the supplier name in the header from `supplierName`
(fallback to the current label until the API field is regenerated in the
phase-4 client — read it defensively). Keep the tabs as they are today
(the no-tabs V2 layout is the feature's follow-up, R-WEB-06); do not
restyle the shell. Update `contract360ViewModel.ts` and tests.

## Parent story AC covered
- AC-1, AC-2, AC-3

## Files to create or modify
| Path | Change |
|------|--------|
| `web/src/routes/contracts/contract360/index.tsx`, `Contract360Header.tsx`, `contract360ViewModel.ts`, `contract360.css` | landing params, highlight, wording, back label, Ask about it |
| `web/src/routes/contracts/contract360/ClauseHighlight.tsx` | new |
| `web/tests/routes/contracts/contract360/*` | landing + highlight + Ask about it tests |

## Context the implementer needs
- **Design**: `inputs/design/prototypes/Raffa V2 Prototype.html`; unpacked anchors above; `raffa-v2/styles.css`; requirements R-EVD-02, R-WEB-06 (`inputs/requirements.md`).
- **Architecture decisions in force**: ADR-024 (citation landing, scoped chat), ADR-020 (amended: screen 5), ADR-019 (tokens).
- Gap G-360-LANDING.
- **Do not touch**: `web/src/routes/ask/**` (F09/T04 next phase), `routes/documents/**` (F09/T03 this phase), `web/src/api/**`, `web/openapi/**`, shell.

## Definition of done
- [ ] `npm test` in `web/` exit 0 — `/contracts/:id?clause=<id>` renders the clause highlighted with its `rawText` visible; `?page=12` highlights the first clause on page 12; `state.from === "ask"` → back label "← Ask Raffa"; "Ask about it" links to `/ask?scope=<id>`; header shows `supplierName` when present
- [ ] `npm run build` in `web/` exit 0

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | landing params, highlight, back label, Ask about it | `web/tests/routes/contracts/contract360/Contract360Route.test.tsx`, `contract360ViewModel.test.ts` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E13/F10/US01/T01
  prompt: reports/workitems/epic-13-ask-v2/feature-10-contract360-landing/us-01-contract360-landing/tasks/task-01-contract360-landing.md
  produces: [web-contract360-landing]
  depends_on: [web-rich-reply]
  effort: M
  layer: web
  status: live
```
