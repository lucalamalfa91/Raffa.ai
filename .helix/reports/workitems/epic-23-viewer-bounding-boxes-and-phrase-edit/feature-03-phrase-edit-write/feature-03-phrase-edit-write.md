---
id: feature-03
type: feature
parent: epic-23
wave: w18
status: active
---

# feature-03-phrase-edit-write — Phrase-edit write path (proposal vs override)

## Slice
An edited OCR phrase writes an override **beside** the proposal on the
`extraction_evidence` row family (the ADR-003 w18 override slot feature-02
added), never rewriting the proposal in place. A new write endpoint exposes it;
a second browser reads back the override, and a reprocess cannot silently
revert it (ADR-027).

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | phrase-edit-write | w18 |

## Architecture decisions in force
- ADR-029 clause 1 — phrase edit is an override, never an in-place rewrite.
- ADR-027 — re-derivation never overrides a human correction.
- ADR-012 §3 / single-writer — the endpoint lands on `ContractsEndpointExtensions.cs` one phase after feature-02.

## Target repo
`raffa-backend`
