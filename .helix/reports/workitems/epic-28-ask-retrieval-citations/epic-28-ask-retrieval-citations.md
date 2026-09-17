---
id: epic-28
type: epic
wave: w19
status: active
extends: [epic-13, epic-23, epic-25]
---

# epic-28-ask-retrieval-citations — RAG filter + priced-line parity + real citation ids

## Business capability
The Ask engine retrieves a contract's chunks (not the whole tenant), uses the
same benchmarked priced-lines as `/strategy`, and stamps real
`contractId`/`documentId`/`page`/span on every citation so the W18 viewer can
open at the span. These are the shared primitives (B) the flows cite.

## Product coverage
| Source | Item |
|--------|------|
| inputs/next/2026-09-16-ask-raffa-wow.md §2 | NW-81, NW-82, NW-83 (and NW-93's two-CTA halves) |
| ADR-024 w19 | clauses 15 (RAG), 16 (priced-lines), 17 (citation ids) |
| ADR-018 | viewer deep-link `/documents/:id/viewer?page&clause` |

## Features
| ID | Title | Wave |
|----|-------|------|
| feature-01 | priced-lines-parity | w19 |
| feature-02 | rag-contract-filter | w19 |
| feature-03 | citation-ids | w19 |

## Success looks like
A scoped Q2/Q3 retrieves only that contract (plus a labelled "similar types"
peer slice); a named-supplier strategy packs numbers equal to the 360
`/strategy`; the Q2 card offers both a 360 CTA and a viewer-at-span CTA.

## Architecture decisions in force
- ADR-024 w19 (cl. 15–17); ADR-018 (viewer deep-link); ADR-011 (market stays on `IMarketKnowledgeRetrieval`).

## Out of scope
- No cross-tenant vector shortcut; no new reply `kind`; no fabricated market number.
