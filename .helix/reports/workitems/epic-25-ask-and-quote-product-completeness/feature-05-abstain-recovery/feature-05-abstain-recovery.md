---
id: feature-05
type: feature
parent: epic-25
wave: w18
status: active
---

# feature-05-abstain-recovery — Ask never dead-ends: abstain carries a recovery action (NW-59)

## Slice
An abstain/error reply carries a recover action so Ask never dead-ends: the
backend emits a server-selected recovery action (from the catalog) on every
abstain path, and the web renders it as a secondary action — never primary.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | abstain-recovery-backend | w18 |
| us-02 | abstain-recovery-web | w18 |

## Architecture decisions in force
- ADR-024 (reply kinds; actions from the catalog only), ADR-020 (copy/actions).

## Target repo
`raffa-backend` + `raffa-web` (mixed)
