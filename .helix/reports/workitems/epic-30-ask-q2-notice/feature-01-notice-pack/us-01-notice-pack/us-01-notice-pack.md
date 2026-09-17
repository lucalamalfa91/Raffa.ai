---
id: us-01
type: user-story
parent: feature-01
wave: w19
status: active
---

# us-01-notice-pack — The notice answer is a pack-backed date, not tenant RAG

## Story
As the **Ask engine**, I want a notice question answered from a structured pack
with a real calendar date (and days only when the clause cites N), so the answer
is deterministic and pack-backed.

## Acceptance criteria
- [ ] AC-1 notice/preavviso/disdetta → structured notice pack (endDate/cancellationDeadline/autoRenewal/renewalTermMonths + `WhenYouMustMove` + evidence).
- [ ] AC-2 the answer states the date; "N days" only if N is in the cited clause, never model-subtract.
- [ ] AC-3 scoped fixture answer = "18 Oct 2026" + "if missed renews for 12 months"; `autoRenewal=false` → contract ends on `endDate`; past deadline → "deadline passed N days ago" (N a pack value).

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-024 w19 (cl. 22); lock 8
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| feature (epic-27) lexicon routed notice; feature (epic-28) RAG filter | the pack + fallback |

## Architecture decisions in force
- ADR-024 w19 (cl. 22) — date, never model-subtract; days only if cited.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | notice-pack | M | phase-3 |

## Council decisions carried into this story
- structured notice pack; V1 = non-renewal/auto-renew notice only; no `cancellationNoticeDays` column.

## Open questions
- none
