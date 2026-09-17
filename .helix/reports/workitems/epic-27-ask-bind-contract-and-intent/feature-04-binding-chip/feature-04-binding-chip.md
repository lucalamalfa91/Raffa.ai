---
id: feature-04
type: feature
parent: epic-27
wave: w19
status: active
---

# feature-04-binding-chip — Persistent contract-binding chip (NW-78)

## Slice
While `scopeContractId` is set, the Ask thread shows a chip
`{supplierName} · {type}` linking `/contracts/{id}`, surviving resume; it is
built from the conversation's persisted `scopeContractId`, never the transient
`?scope=` query.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | binding-chip | w19 |

## Architecture decisions in force
- ADR-012 (cl. 49) + ADR-020 (37.2) — `.tag-neutral` chip, never a guid.

## Target repo
`raffa-web`
