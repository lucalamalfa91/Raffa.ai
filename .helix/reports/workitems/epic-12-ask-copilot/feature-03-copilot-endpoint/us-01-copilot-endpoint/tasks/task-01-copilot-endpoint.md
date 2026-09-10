---
id: E12/F03/US01/T01
type: task
story: us-01-copilot-endpoint
wave: 12
status: live
target_repo: raffa-backend
---

# task-01-copilot-endpoint — Domain gate, context pack, chat JSON

## Coding objective

Replace `ToStructuredNotWiredResponse` in `ChatEndpointExtensions`. Wire the
existing planner + `DeterministicQueryHandler` for structured facts
(renewals, spend) from **this tenant's** store.

Add a domain gate before RAG: greetings and off-domain (carbonara) do not
retrieve; legal questions refuse then offer a commercial analogue. Persona:
savings specialist, never lawyer.

Build a **context pack** for the `answer` role: tenant-scoped chunks (after
authz) **plus** `IBenchmarkService` numbers **plus**
`NegotiationStrategyCalculator` outputs when a quote/opportunity exists.
The model narrates those numbers. Insufficient evidence → spec §10.4 abstain.

API JSON (names may match existing types if you extend them):

```
{ canDetermine, answerMarkdown, citations[], actions[{ label, href }], domainRedirect? }
```

Actions are in-app routes only (`/contracts/:id`, `/renewals`, `/`,
`/quotes/...`). Do not invent hrefs the router does not have.

Chat must not reference the Documents project; mapping stays in `Raffa.Api`.
Do not write market rows into pgvector.

## Parent story AC covered

- AC-1 … AC-6

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Api/ChatEndpointExtensions.cs` | remove not-wired; new response shape |
| `backend/src/Raffa.Chat/` | domain gate, context pack, persona prompt |
| `backend/tests/Raffa.Chat.Tests/` | ciao / carbonara / legal / Allianz band |
| `backend/tests/Raffa.IntegrationTests/` | tenant isolation still holds |

## Context the implementer needs

- **Architecture**: ADR-023, ADR-011 amendment. Gaps G-NOT-WIRED, G-DOMAIN-GATE.
- **Do not touch**: `web/` (F05), storage `LoadAsync` (F04), Foundry SDK (F01).
- `FixtureAiGateway.AnswerAsync` must stop concatenating raw chunk text when
  used as the answer role for this path (prompt + pack, not echo).

## Definition of done

- [ ] Unit/API tests for A1–A5 in the brief (`ciao`, carbonara, Allianz band
      or abstain, legal refuse, no “not wired” on a 120-day renewal question).
- [ ] Cross-tenant RAG test still fails closed.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | domain gate + not-wired gone | `Raffa.Chat.Tests` / API tests |
| unit | calculator numbers unchanged in the pack | Chat or Quotes tests |
| integration | RLS / no cross-tenant evidence | existing Ask isolation tests |

## Open questions blocking this task

- none

## Wave-spec entry

```yaml
- id: E12/F03/US01/T01
  produces: [copilot-endpoint]
  depends_on: [foundry-gateway, fixture-catalog]
```
