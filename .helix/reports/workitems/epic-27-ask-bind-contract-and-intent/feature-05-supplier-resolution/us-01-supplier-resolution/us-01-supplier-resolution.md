---
id: us-01
type: user-story
parent: feature-05
wave: w19
status: active
---

# us-01-supplier-resolution — "AsterCloud GmbH" resolves without `NeedsDocument`

## Story
As the **Ask engine**, I want supplier/contract resolution to match beyond
exact capitalization and to never silently merge multiple contracts, so a
lowercased / suffix form still resolves and the soonest contract is named.

## Acceptance criteria
- [ ] AC-1 exact then normalized/contains match ("AsterCloud GmbH" → AsterCloud; lowercase "salesforce" too).
- [ ] AC-2 multi-contract: scoped id wins, else the soonest cancellation deadline/renewal, and the pack names the chosen contract ("Using {Type} CT-01 …").
- [ ] AC-3 unknown supplier stays `NeedsDocument` + upload.

## Definition of done
- [ ] every AC verified by a named test
- [ ] honours ADR-024 w19 (cl. 14)
- [ ] no unresolved, unassumed open question

## Dependencies
| Depends on | Why |
|------------|-----|
| feature-02 (engine-scope) | scoped-id-wins needs the scope id threaded first |

## Architecture decisions in force
- ADR-024 w19 (cl. 14) — host composition; `Raffa.Insights` fenced.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | supplier-resolution | M | phase-2 |

## Council decisions carried into this story
- resolution host-side via `ISupplierNameLookup`/normalizer; never merge; named pack item.

## Open questions
- none
