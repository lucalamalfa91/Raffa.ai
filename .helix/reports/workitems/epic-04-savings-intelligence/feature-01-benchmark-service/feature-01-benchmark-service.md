---
id: feature-01
type: feature
parent: epic-04
wave: R3
status: active
---

# feature-01-benchmark-service — Benchmark interface + fixture adapter

## Slice

Define the normalized Benchmark Service interface (getBenchmark with P25/P50/P75,
metric, confidence, provenance, comparison dimensions) and implement the internal
fixture adapter used for the first `demo`.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Benchmark interface + normalized contract | R3 |
| us-02 | Fixture adapter + provenance/confidence | R3 |

## Architecture decisions in force

- ADR-001 (fixture adapter, no paid API)
- ADR-002 (Benchmark context)

## Target repo

`raffa-backend`
