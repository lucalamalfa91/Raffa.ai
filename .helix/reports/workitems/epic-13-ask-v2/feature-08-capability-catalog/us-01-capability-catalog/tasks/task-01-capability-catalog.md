---
id: E13/F08/US01/T01
type: task
story: us-01-capability-catalog
wave: 13
status: live
target_repo: raffa-backend
---

# task-01-capability-catalog — Catalog, routing table, `GET /api/capabilities`

## Coding objective

Teach Raffa its own product (`inputs/requirements.md` R-SYS-01…04). In
`backend/src/Raffa.Chat/Application/Capabilities/`: `Capability` record
(key, title, routePattern, description, exampleQuestions[], roleGate
`any|admin`, availability `always|needsValidatedContract|admin`,
howTo[]), `CapabilityCatalog` (static, version `capabilities-v2.0`) with
the entries: `ask` (`/ask`), `documents` (`/documents`),
`documents-attention` (`/documents?filter=attention`), `documents-review`
(`/documents?review={documentId}`), `portfolio` (`/contracts`),
`contract-360` (`/contracts/{contractId}`), `renewals` (`/renewals`,
`/renewals?select={contractId}`), `savings` (`/savings`), `quote-check`
(`/quotes`, `/quotes/{quoteId}`), `workspace-members`
(`/workspace/members`, admin). Descriptions and how-to steps come from
the V2 design: `inputs/design/prototypes/raffa-v2/ia-v2.md` (route map,
cross-links) and `raffa-v2/app.jsx` `ask()` capabilities branch ("I
answer from your validated contracts and route you to the right part of
Raffa: • Documents — upload contracts, review weak facts. • Portfolio —
every contract, spend, liability and risk in one table. • Renewals —
deadlines and the action for each. • Quote check — benchmark a new quote
against the market and your history."). `CapabilityRouting`: intent →
capability keys (benchmark / compare / competitor / "in linea" →
`quote-check` (+ `contract-360` when validated); unknown supplier →
`documents`; deadlines / notice / renew → `renewals`; saving / risparmio →
`savings`; how-to → the named capability; capability list → `renewals`,
`portfolio`, `quote-check`), and `ResolveActions(intents, RoutingContext
{ validatedContractCount, role, contractId?, quoteId?, documentId? })`
→ `CopilotAction(label, href, kind)` list with hrefs built **only** from
catalog patterns and known ids; when availability is
`needsValidatedContract` and the count is 0, replace with the Documents
action labelled "Upload a contract" and the copy "The portfolio lights up
from validated contracts. Upload one to start." (`raffa-v2/markup.html`).
`FeatureCitation.For(capability)` → citation item (`corpus: raffa`,
title, subtitle = route, snippet = description, href). Per-screen
suggestion chips (`chipsFor` in `app.jsx`) become `SuggestionsFor(screenKey,
supplierName?)`. Endpoint `GET /api/capabilities` (role-aware: admin-only
entries hidden for Procurement) in a new
`Raffa.Api/CapabilitiesEndpointExtensions.cs` — **not mapped** in
`Program.cs` here (F06/T01 maps it). Register the catalog in
`AddChatModule` (this task is the phase-2 writer of that file).

## Parent story AC covered
- AC-1, AC-2, AC-3, AC-4

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/src/Raffa.Chat/Application/Capabilities/Capability.cs`, `CapabilityCatalog.cs`, `CapabilityRouting.cs`, `CopilotAction.cs`, `FeatureCitation.cs`, `RoutingContext.cs` | new |
| `backend/src/Raffa.Chat/Infrastructure/ServiceCollectionExtensions.cs` | register catalog + routing (phase-2 writer) |
| `backend/src/Raffa.Api/CapabilitiesEndpointExtensions.cs` | new, unmapped |
| `backend/tests/Raffa.Chat.Tests/Capabilities/*` | catalog completeness, routing, availability, suggestions |
| `backend/tests/Raffa.Api.Tests/CapabilitiesEndpointTests.cs` | role filtering (uses a test host mapping the endpoint) |

## Context the implementer needs
- **Design**: `inputs/design/prototypes/Raffa V2 Prototype.html`; unpacked `raffa-v2/app.jsx` (`ask()` capabilities branch, `chipsFor`, `c360Chips`), `raffa-v2/markup.html` ("The portfolio lights up from validated contracts."), `raffa-v2/ia-v2.md` (route map + "Ask intents").
- **Architecture decisions in force**: ADR-024 (actions only from catalog hrefs), ADR-018 (amended: V2 routes), ADR-002 (Chat allow-list unchanged).
- Gap G-CAPABILITIES.
- **Do not touch**: `ChatEndpointExtensions.cs`, `AskRaffaQueryRouter`, `RagAnswerService` (F06), conversations files (F05), `Program.cs`, `web/`.

## Definition of done
- [ ] `dotnet test backend/tests/Raffa.Chat.Tests --filter Capabilities` exit 0 — every V2 route of `ia-v2.md` has an entry; benchmark intent → Quote check; unknown supplier → Documents; 0 validated contracts → upload action with the prototype copy; hrefs never contain `{`
- [ ] `dotnet test backend/tests/Raffa.Api.Tests --filter CapabilitiesEndpointTests` exit 0 — Procurement does not see `workspace-members`
- [ ] `dotnet build backend/Raffa.slnx` exit 0

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | catalog, routing, availability, suggestions, feature citations | `Raffa.Chat.Tests/Capabilities/*` |
| API | role-aware listing | `Raffa.Api.Tests/CapabilitiesEndpointTests.cs` |

## Open questions blocking this task
- OQ-askv2-003 — `/savings` route (assumed)

## Wave-spec entry
```yaml
- id: E13/F08/US01/T01
  prompt: reports/workitems/epic-13-ask-v2/feature-08-capability-catalog/us-01-capability-catalog/tasks/task-01-capability-catalog.md
  produces: [capability-catalog]
  depends_on: [conversations-store]
  effort: M
  layer: backend
  status: live
```
