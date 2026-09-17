---
id: E27/F05/US01/T01
type: task
story: us-01-supplier-resolution
wave: w19
status: live
target_repo: raffa-backend
---

# task-01-supplier-resolution — Supplier/contract resolution beyond exact-capitalized (NW-80)

## Coding objective
In `backend/src/Raffa.Chat/Application/Gate/DomainGate.cs`
`ExtractSupplierCandidate`, extend the match from exact `Supplier.Name` equality
to: exact, then `Raffa.Suppliers.Products` normalized/contains (via the existing
`SupplierNameNormalizer` / `ISupplierNameLookup` the host already uses), so
"astercloud GmbH" and "AsterCloud GmbH" both resolve. In
`backend/src/Raffa.Api/AskCopilotService.cs` `BuildInDomainReplyAsync`, replace
the `FirstOrDefault` supplier match with: scoped id wins; else the soonest
cancellation-deadline/renewal (the `RenewalPipelineBuilder` sort); and add a
**named pack item** ("Using {Type} CT-01 (renews …). Ask if you meant another.")
so multiple contracts are never silently merged. Unknown supplier stays
`NeedsDocument` + upload.

## Parent story AC covered
- AC-1 exact → normalized/contains.
- AC-2 scoped id / soonest; named pack item.
- AC-3 unknown → `NeedsDocument`.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.Chat/Application/Gate/DomainGate.cs | normalized/contains supplier match |
| backend/src/Raffa.Api/AskCopilotService.cs | scoped-id / soonest resolution + named pack item |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 w19 (cl. 14) — host composition; never merge.
- **Do not touch**: `Raffa.Insights` (stay fenced `[SharedKernel, Benchmark]`); the planner.

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.Chat.Tests` + `Raffa.Api.Tests` exit 0, with tests: lowercase/suffix resolves, multi-contract picks soonest + names the item, unknown stays NeedsDocument

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | normalized/contains match | `backend/tests/Raffa.Chat.Tests` |
| integration | soonest-of-many + named item | `backend/tests/Raffa.Api.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E27/F05/US01/T01
  prompt: reports/workitems/epic-27-ask-bind-contract-and-intent/feature-05-supplier-resolution/us-01-supplier-resolution/tasks/task-01-supplier-resolution.md
  produces: [supplier-resolution]
  depends_on: [engine-scope]
  effort: M
  layer: backend
  status: live
```
