---
id: E28/F03/US02/T01
type: task
story: us-02-citation-two-cta
wave: w19
status: live
target_repo: raffa-web
---

# task-01-citation-two-cta — Two-CTA card: 360 + viewer-at-span (NW-83/NW-93)

## Coding objective
In `web/src/routes/ask/reply/CitationCard.tsx` + `askViewModel.ts`
(`mapConversationCitation` / `buildTenantCitationHref`), stop synthesising
`href` from a bare `?page=` and instead render a **two-CTA card** when a citation
carries a clause/span id: action 1 `.btn-primary` "Open contract" →
`/contracts/{contractId}`; action 2 `.btn-secondary` "Open at this span" → the
W18 viewer `/documents/{documentId}/viewer?page=<n>&clause=<clauseId>` (the id now
stamped by NW-83). Reuse `ActionRow`; never two nested buttons. When no clause id
exists fall back to the 360 `?clause=`/`?page=` half, never `previewUrl`-only.
Cite `screens-v2.md` §2 (citation card actions) as the anchor.

## Parent story AC covered
- AC-1 two actions (primary 360, secondary viewer-at-span).
- AC-2 viewer CTA opens the page with the wording highlighted.
- AC-3 neither CTA omitted on the happy path.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/routes/ask/reply/CitationCard.tsx | two-CTA rendering |
| web/src/routes/ask/askViewModel.ts | build viewer-at-span href from stamped ids |

## Context the implementer needs
- **Architecture decisions in force**: ADR-012 (cl. 50); ADR-018 (viewer route).
- **Design anchor**: `inputs/design/prototypes/raffa-v2/screens-v2.md` §2.
- **Do not touch**: the viewer route (w18); `ActionRow` (reuse).

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test that a citation with a clause id renders both CTAs and the viewer href carries `?page=`+`&clause=`

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | two-CTA + viewer href built from ids | `web/src/routes/ask/reply/CitationCard.test.tsx` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E28/F03/US02/T01
  prompt: reports/workitems/epic-28-ask-retrieval-citations/feature-03-citation-ids/us-02-citation-two-cta/tasks/task-01-citation-two-cta.md
  produces: [citation-two-cta]
  depends_on: [citation-ids]
  effort: S
  layer: frontend
  status: live
```
