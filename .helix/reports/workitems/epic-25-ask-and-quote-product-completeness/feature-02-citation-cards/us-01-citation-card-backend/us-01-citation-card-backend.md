---
id: us-01
type: user-story
parent: feature-02
wave: w18
status: active
---

# us-01-citation-card-backend — Tenant citations carry a real viewer preview and deep-link

## Story
As the **Ask engine**, I want a tenant citation to carry a real page preview
and a viewer deep-link (while Raffa/market citations carry a CTA, never a
fabricated preview) so the card never shows an empty placeholder.

## Acceptance criteria
- [ ] AC-1 a tenant (`corpus=tenant`) clause citation carries a real `previewUrl` and an `href` deep-linking to the viewer (`/documents/:id/viewer?page&clause`).
- [ ] AC-2 a `corpus=raffa`/`market` citation carries a CTA `href`, never a page-preview `previewUrl`.
- [ ] AC-3 `PackItem` divides by `corpus`: `PreviewUrl` is set only for tenant pages.

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] honours ADR-024 (corpus division), ADR-018 (viewer route)
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| — | `PackItem` already carries `Corpus`/`PreviewUrl`/`Href`; only the values are wrong |

## Architecture decisions in force
- ADR-024 / ADR-012 / ADR-018 — tenant citation deep-link; `corpus` decides the card.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | citation-card-backend | M | phase-1 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Tenant clause citation deep-links to `/documents/:documentId/viewer?page&clause`; `corpus=raffa` never carries a page-preview slot.

## Open questions
- none
