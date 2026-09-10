---
id: us-01
type: user-story
parent: feature-02
wave: 12
status: active
---

# us-01-fixture-catalog — Representative worldwide mock

## Story

As **procurement**, I want Raffa to compare my Allianz-class commercial
terms to a labelled representative market band, so I know if I am above
P75 without pretending the numbers are a live paid feed.

## Acceptance criteria

- [ ] AC-1 The fixture catalog includes insurance / enterprise software /
      facilities-class rows with P25–P75, including a name usable as
      “Allianz” in conversation.
- [ ] AC-2 Provenance remains `fixture` / representative; thin samples still
      abstain (spec §10.4).
- [ ] AC-3 No paid Tropic/Vendr client is added.

## Definition of done

- [ ] AC verified by named tests
- [ ] honours ADR-001 / ADR-023

## Dependencies

| Depends on | Why |
|------------|-----|
| — | Parallel with F01 (different files) |

## Architecture decisions in force

- ADR-001 — Internal Dataset first
- ADR-023 — market corpus is `IBenchmarkService`

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | Expand FixtureBenchmarkAdapter | M | phase-1 |

## Open questions

- none
