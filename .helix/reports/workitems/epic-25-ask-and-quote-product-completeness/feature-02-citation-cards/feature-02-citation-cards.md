---
id: feature-02
type: feature
parent: epic-25
wave: w18
status: active
---

# feature-02-citation-cards — Citation cards: tenant deep-link or CTA (NW-55)

## Slice
End the empty "No page preview available" placeholder: a tenant citation
deep-links to the viewer with a real page preview; a Raffa/market citation
renders a CTA card with no fabricated preview slot. The backend divides the
pack by `corpus` (real `previewUrl` only for tenant pages) and the card renders
the two-corpus treatment.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | citation-card-backend | w18 |
| us-02 | citation-card-web | w18 |

## Architecture decisions in force
- ADR-024 / ADR-012 / ADR-018 — citation deep-link to the viewer; `corpus` divides the card.

## Target repo
`raffa-backend` + `raffa-web` (mixed)
