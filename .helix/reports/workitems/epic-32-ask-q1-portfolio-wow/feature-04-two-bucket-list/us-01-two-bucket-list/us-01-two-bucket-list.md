---
id: us-01
type: user-story
parent: feature-04
wave: w19
status: active
---

# us-01-two-bucket-list — The Q1 answer splits two honest buckets, never one list

## Story
As a **user**, I want the Q1 answer to distinguish actionable-in-2026 from
locked-for-2026 rows with honest wording and clickable cards, so I am never told
a locked row can save 2026 money.

## Acceptance criteria
- [ ] AC-1 one `answer` carries two labelled groups: actionable-in-2026 (TODOs/Renewals + follow-up chip) vs impacts-2026-but-locked (360 link only, no 2026-save TODO).
- [ ] AC-2 each row: `{supplier} · {type}`, "X% above media" when a band exists, one-line why, `/contracts/{id}`.
- [ ] AC-3 price-in-line + worse-conditions listed with "prezzo ok, condizioni migliorabili", no invented %.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-024 w19 (cl. 20) + lock 2/3/9
- [ ] queued — head of W20

## Dependencies
| Depends on | Why |
|------------|-----|
| feature-03 (malposition-percent) + feature-02 (candidate-set) | the rows/buckets |

## Architecture decisions in force
- ADR-024 w19 (cl. 20) — two honest buckets; client renders server facts, never computes %.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | two-bucket-list | M | queued |

## Council decisions carried into this story
- two `.card` groups; follow-up chips only for actionable rows; Resume may drop chips (don't block).

## Open questions
- none
