---
id: us-01
type: user-story
parent: feature-05
wave: w19
status: active
---

# us-01-q1-follow-up-todos — The Q1 follow-up writes Renewals TODOs for the actionable row

## Story
As a **user**, I want a Q1 follow-up on an actionable supplier to name the top 3
points and persist all to Renewals, so the strategy is durable.

## Acceptance criteria
- [ ] AC-1 resolves X (NW-80); multiple X spelled out, never merged; top-3 in chat; persist-all (no cap at 3).
- [ ] AC-2 action "Open in Renewals →" `/renewals?select={id}`.
- [ ] AC-3 a locked-for-2026 row never gets a 2026-save TODO / 2026-save promise.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-024 w19 (cl. 21) + lock 7/9
- [ ] queued — head of W20

## Dependencies
| Depends on | Why |
|------------|-----|
| epic-31 ranker + epic-29 todos | persisted via the shared ranker + upsert |

## Architecture decisions in force
- ADR-024 w19 (cl. 21) — persist todos, server-injected action.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | q1-follow-up-todos | M | queued |

## Council decisions carried into this story
- persist-all for actionable rows; locked rows → 360 only.

## Open questions
- none
