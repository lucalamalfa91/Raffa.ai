---
id: feature-05
type: feature
parent: epic-27
wave: w19
status: active
---

# feature-05-supplier-resolution — Supplier/contract resolution (NW-80)

## Slice
`DomainGate.ExtractSupplierCandidate` matches exact then normalized/contains;
a multi-contract supplier uses the scoped id else the soonest deadline, and the
pack names the chosen contract — never silently merged. Resolution is host-side
(`Raffa.Api`); `Raffa.Insights` stays fenced.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | supplier-resolution | w19 |

## Architecture decisions in force
- ADR-024 w19 (cl. 14) — exact → normalized/contains; scoped id wins; named pack item; host composition.

## Target repo
`raffa-backend`
