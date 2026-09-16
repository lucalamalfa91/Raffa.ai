---
id: us-02
type: user-story
parent: feature-02
wave: w18
status: active
---

# us-02-citation-card-web — The citation card renders preview or CTA, never a placeholder

## Story
As a **user**, I want a tenant citation to open the page it came from (with a
real preview), and a Raffa/market citation to offer a clear CTA, so the card is
never an empty placeholder.

## Acceptance criteria
- [ ] AC-1 a tenant citation with `previewUrl` renders the preview and opens the viewer deep-link on click.
- [ ] AC-2 a Raffa/market citation without `previewUrl` renders a CTA card (no "No page preview available" placeholder).
- [ ] AC-3 the card's one interaction stays "click → onOpen" (native button, ADR-019).

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] honours ADR-024/ADR-018 and ADR-019
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| us-01 (citation-card-backend) | the card reads the `previewUrl`/`href` the backend now sets |

## Architecture decisions in force
- ADR-019 — every interactive control is a native button; corpus badge carries text + colour.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | citation-card-web | S | phase-3 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Two-corpus treatment: tenant → preview + viewer deep-link; raffa/market → CTA card, no preview slot.

## Open questions
- none
