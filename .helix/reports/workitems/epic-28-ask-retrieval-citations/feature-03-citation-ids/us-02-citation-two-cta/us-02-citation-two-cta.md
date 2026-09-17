---
id: us-02
type: user-story
parent: feature-03
wave: w19
status: active
---

# us-02-citation-two-cta — The Q2 card offers two actions: 360 + viewer-at-span

## Story
As a **user** who asked about notice, I want a two-CTA card (open 360 and open
the viewer already at the notice span), so I do not search the document.

## Acceptance criteria
- [ ] AC-1 the notice card offers two actions: `.btn-primary` "Open contract" + `.btn-secondary` "Open at this span" (viewer route).
- [ ] AC-2 the viewer CTA opens the source page with the notice wording highlighted (W18 box).
- [ ] AC-3 neither CTA is omitted on the happy path; no `previewUrl`-only treatment.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-012 (cl. 50); ADR-018
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| us-01 (citation-ids-backend) | the card reads the real ids |

## Architecture decisions in force
- ADR-012 (cl. 50) — two-CTA card, never two nested buttons.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | citation-two-cta | S | phase-3 |

## Council decisions carried into this story
- one `reply.actions[]` of two; viewer at span = the notice clause; 360 is the other half.

## Open questions
- none
