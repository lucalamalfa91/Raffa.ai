---
id: us-01
type: user-story
parent: feature-08
wave: 13
status: active
---

# us-01-capability-catalog — Raffa routes me to the right screen with the right citation

## Story

As **procurement**, I want to ask "cosa sai fare?" or "come faccio a
rivedere i campi deboli?" and get the module I need with a button that
opens it, and I want every other answer to end with the right in-app
action, so that Ask is the front door of Raffa and never sends me to a
page that does not exist.

## Acceptance criteria

- [ ] AC-1 `GET /api/capabilities` returns the versioned catalog: key,
      title, routePattern, description, exampleQuestions[], roleGate,
      availability (`always` | `needsValidatedContract` | `admin`),
      howTo[] — entries for Ask Raffa, Documents (upload / attention /
      review), Portfolio, Contract 360, Renewals, Savings, Quote check,
      Workspace & members.
- [ ] AC-2 A routing table maps planner intents to catalog keys
      (benchmark → Quote check, unknown supplier → Documents upload,
      deadlines → Renewals, savings → Savings, how-to → the named module);
      `ResolveActions(intent, context)` returns `{ label, href }` with
      hrefs built only from catalog patterns and known object ids
      (`/contracts/{id}`, `/quotes/{id}`, `/documents?review={id}`,
      `/renewals?select={id}`).
- [ ] AC-3 An availability condition not met (no validated contract)
      replaces the action with the Documents upload action and the copy
      "The portfolio lights up from validated contracts."
- [ ] AC-4 Feature citation items (`corpus: raffa`, title = capability,
      snippet = description, href = route) are produced for capability
      answers.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-conversations | `AddChatModule` file ownership order (T01 phase 1 → this phase 2) |

## Architecture decisions in force

- ADR-024 — capability catalog; actions only from catalog hrefs
- ADR-018 (amended) — V2 route map (`raffa-v2/ia-v2.md`)

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | Capability catalog, routing table, `CapabilitiesEndpointExtensions.cs` (mapped by F06) | M | phase-2 |

## Council decisions carried into this story

Catalog lives in `backend/src/Raffa.Chat/Application/Capabilities/`
(`CapabilityCatalog`, `CapabilityRouting`), version string
`capabilities-v2.0`. Prototype oracle: `raffa-v2/app.jsx` `ask()`
capabilities branch ("I answer from your validated contracts and route
you to the right part of Raffa: • Documents … • Portfolio … • Renewals …
• Quote check …") and `chipsFor` per screen; empty-state copy from
`markup.html` ("The portfolio lights up from validated contracts. Upload
one to start.").

## Open questions

- OQ-askv2-003 — Savings at `/savings`, not in the rail (assumed)
