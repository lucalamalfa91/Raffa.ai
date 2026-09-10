---
id: feature-02
type: feature
parent: epic-03
wave: R2
status: active
---

# feature-02-cancellation-alerts — Threshold alerts for cancellation deadlines

## Slice

Apply configurable threshold windows (365/270/180/120/90/60/30 days) and emit
renewal/cancellation deadline events that create alerts for owners, driven by a
daily schedule.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Configurable threshold scheduler + events | R2 |

## Architecture decisions in force

- ADR-002 (Renewals worker)
- ADR-003 (PostgreSQL)

## Target repo

`raffa-backend`
