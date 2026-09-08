---
id: F08
type: feature
parent: epic-13
wave: 13
status: active
---

# feature-08-capability-catalog — Ask knows every Contigo screen

## Slice

A versioned, machine-readable catalog of Contigo capabilities (key, title,
route pattern, description, example questions, role gate, availability
condition, how-to steps) in `Contigo.Chat`, exposed at
`GET /api/capabilities`, with a routing table the planner uses so every
action href comes from the catalog (never model-authored) and so "what can
you do" / "come faccio a…" turns answer with feature cards
(`corpus: contigo`) and working deep links. Availability-aware: greyed
modules become the upload action with the prototype's copy
(`inputs/requirements.md` R-SYS-01…04; prototype oracle `app.jsx` → `ask()`
capabilities branch and `chipsFor`).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Contigo routes me to the right screen with the right citation | 13 |

## Architecture decisions in force

- ADR-024 — capability catalog, actions only from catalog hrefs
- ADR-018 (amended) — V2 route map is the catalog's truth

## Target repo

`contigo-backend`
