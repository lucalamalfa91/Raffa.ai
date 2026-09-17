---
id: us-01
type: user-story
parent: feature-02
wave: w19
status: active
---

# us-01-candidate-set-2026 — Only Active + validated contracts impacting 2026 are ranked

## Story
As the **Ask engine**, I want the Q1 candidate set to be Active + validated
contracts that impact 2026 costs (option 3), so processing shells and ended
contracts never rank.

## Acceptance criteria
- [ ] AC-1 the set is validated **and** `Status="Active"` **and** option-3 (impacts 2026 costs); expired/inactive/ended-2025 out; 2025-2028 term in; start-2026-06-01 in.
- [ ] AC-2 a processing shell is never listed; no unbounded N+1 (batch + bound N×lines).

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-024 w19 (cl. 18) + lock 2
- [ ] queued — head of W20

## Dependencies
| Depends on | Why |
|------------|-----|
| — | `PortfolioFilter.Status` and `CountValidatedContractsAsync` already exist |

## Architecture decisions in force
- ADR-024 w19 (cl. 18) — option 3 candidate set.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | candidate-set-2026 | M | queued |

## Council decisions carried into this story
- option 3 candidate set; actionability flag is NW-88; narration split is NW-89.

## Open questions
- none
