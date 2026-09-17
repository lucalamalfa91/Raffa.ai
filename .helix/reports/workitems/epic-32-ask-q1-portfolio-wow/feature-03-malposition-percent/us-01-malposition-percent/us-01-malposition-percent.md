---
id: us-01
type: user-story
parent: feature-03
wave: w19
status: active
---

# us-01-malposition-percent — Mal-position % is a pure calculator, never a model float

## Story
As the **Ask engine**, I want the mal-position % computed by a pure calculator
(with the `actionableIn2026` flag), so the model never invents a percentile or a
2026 save.

## Acceptance criteria
- [ ] AC-1 `linePct = (unitPrice − media)/media × 100` (media = P50); listed iff any linePct > 0 or a worse-condition comparable fact; contract % = spend-weighted avg of positive linePcts.
- [ ] AC-2 `HasSufficientData=false` → omit the %; NumericGuard passes.
- [ ] AC-3 each listed row carries `actionableIn2026` + short why from stored facts, never model invention.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-024 w19 (cl. 19) + lock 1/3
- [ ] queued — head of W20

## Dependencies
| Depends on | Why |
|------------|-----|
| feature-02 (candidate-set-2026) | the % is computed per candidate |

## Architecture decisions in force
- ADR-024 w19 (cl. 19) — Appendix C rule 6 pure calculator.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | malposition-percent | M | queued |

## Council decisions carried into this story
- media = P50/median; never `"n/a"` currency; price-in-line + worse-conditions listed, no invented condition %.

## Open questions
- none
