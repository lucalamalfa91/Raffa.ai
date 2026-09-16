---
id: us-01
type: user-story
parent: feature-02
wave: w18
status: active
---

# us-01-savings-filters — Savings opportunities list is filterable

## Story
As an **operator**, I want to restrict the Savings opportunities list by
supplier / status / currency, so that I can focus on the opportunities that
matter without a new domain concept.

## Acceptance criteria
- [ ] AC-1 the Savings screen offers supplier / status / currency filter controls over the already-loaded opportunities.
- [ ] AC-2 the filter set is the council's pick (supplier / status / currency) — no invented category, Estimate is a sort not a filter.
- [ ] AC-3 clearing a filter restores the full list; filtering never writes to storage.

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours ADR-020
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| — | operates on the already-loaded `getSavingsOpportunities` / `getPortfolio` data |

## Architecture decisions in force
- ADR-020 — filter is presentation, never a client store; no invented category.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | savings-filters | S | phase-1 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Filter set: supplier / status / currency (product-owner's pick); "Estimate" stays a sort column, never a filter.

## Open questions
- none
