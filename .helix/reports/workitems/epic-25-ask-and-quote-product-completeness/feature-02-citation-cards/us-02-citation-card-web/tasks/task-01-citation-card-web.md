---
id: E25/F02/US02/T01
type: task
story: us-02-citation-card-web
wave: w18
status: live
target_repo: raffa-web
---

# task-01-citation-card-web — Citation card: preview/deep-link or CTA, never an empty placeholder

## Coding objective
In `web/src/routes/ask/reply/CitationCard.tsx`, replace the always-present
"No page preview available" placeholder (`:42`) with the two-corpus treatment:
a `corpus === "tenant"` citation (with `previewUrl` set) renders the preview
`<img>` and opens the viewer deep-link on click (the `onOpen` already carries
the `href` mapped by `askViewModel.ts#buildTenantCitationHref` to
`?page=`); a `corpus === "raffa" | "market"` citation (no `previewUrl`) renders
a CTA card (a `.btn`-styled call-to-action label + the existing snippet) instead
of a preview slot. Keep the single native button interaction (ADR-019) and the
`replyTypes.ts`/`ReplyBody.tsx` contract unchanged — `previewUrl` stays optional.
Cite the design oracle `inputs/design/prototypes/raffa-v2/screens-v2.md` §2
(citation cards: title · page/section · snippet · preview · corpus badge).

## Parent story AC covered
- AC-1 tenant citation renders preview + opens viewer deep-link.
- AC-2 raffa/market citation renders a CTA card, no placeholder.
- AC-3 click → onOpen, native button.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/routes/ask/reply/CitationCard.tsx | two-corpus treatment (preview vs CTA) |
| web/src/routes/ask/reply/reply.css | CTA-card style (no new token, ADR-019) |

## Context the implementer needs

**Closes: NW-55**

- **Architecture decisions in force**: ADR-019 (native button, text + colour badges); ADR-024 (corpus division); ADR-018 (viewer deep-link).
- **Design anchor**: `inputs/design/prototypes/raffa-v2/screens-v2.md` §2.
- **Do not touch**: `replyTypes.ts` / `ReplyBody.tsx` (contract unchanged by rule); the backend `PackItem` (phase 1).

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test proving a tenant citation renders the preview and a raffa/market citation renders no empty placeholder

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | tenant → preview; raffa/market → CTA card, no placeholder | `web/src/routes/ask/reply/CitationCard.test.tsx` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E25/F02/US02/T01
  prompt: reports/workitems/epic-25-ask-and-quote-product-completeness/feature-02-citation-cards/us-02-citation-card-web/tasks/task-01-citation-card-web.md
  produces: [citation-card-treatments]
  depends_on: [citation-viewer-deeplink]
  effort: S
  layer: frontend
  status: live
```
