---
id: feature-03
type: feature
parent: epic-02
wave: R1
status: active
---

# feature-03-portfolio-contract-360 — Portfolio list + Contract 360 API

## Slice

Expose the portfolio list/filter endpoints and the Contract 360 aggregate used by
the web client, reading validated structured data with authorization filters applied
server-side.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Portfolio list + filters (ADR-002/009) | R1 |
| us-02 | Contract 360 aggregate | R1 |

## Architecture decisions in force

- ADR-002 (Documents/Contracts API)
- ADR-009 (server-side tenancy filter)

## Target repo

`raffa-backend`
