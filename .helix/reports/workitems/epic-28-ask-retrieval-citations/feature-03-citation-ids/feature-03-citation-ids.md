---
id: feature-03
type: feature
parent: epic-28
wave: w19
status: active
---

# feature-03-citation-ids — Citations carry real ids; two-CTA card (NW-83 + NW-93)

## Slice
`PackItem`/citation stamps real `contractId` + `documentId` + `page` + span/
clause so the W18 viewer opens at the span; the client renders a two-CTA card
(open 360 + open viewer at the span), never synthesising `href` from a bare
`page`.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | citation-ids-backend | w19 |
| us-02 | citation-two-cta | w19 |

## Architecture decisions in force
- ADR-024 w19 (cl. 17); ADR-012 (cl. 50); ADR-018 (viewer deep-link).

## Target repo
`raffa-backend` + `raffa-web`
