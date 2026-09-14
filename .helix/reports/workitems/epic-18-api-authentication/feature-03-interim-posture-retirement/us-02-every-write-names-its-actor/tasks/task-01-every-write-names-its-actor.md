---
id: E18/F03/US02/T01
type: task
story: us-02-every-write-names-its-actor
wave: w15
status: queued
target_repo: raffa-backend
---

# task-01-every-write-names-its-actor — Thread the resolved actor into nine services; delete the constant

> **Queued for W16.** No entry in `reports/plan/slices/w15.yaml`; not fanned out
> this wave.

## Coding objective

Delete `"unattributed"` and give every write a real actor. `ResolveActor`
(`DocumentsEndpointExtensions.cs:569-573`) returns the constant whenever the
header is missing or blank — **it never rejects** — and it feeds four write paths
(`:128` validate, `:230` upload, `:460` reprocess, `:514` delete). Nine
service-layer sites hardcode the same constant and take **no actor from the
request at all**; those are the rows that survive NW-05 untouched unless this
task threads the identity through. Collapse the three divergent absent-identity
behaviours — 401 on every `ICallerIdentity` consumer, 400 on conversations, a
silent `"unattributed"` audit row on nine paths — to a single **401**.

## Parent story AC covered

- AC-1 … AC-4 (all of them — this is the story's only task)

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Api/DocumentsEndpointExtensions.cs` | delete `UnattributedActor` (`:71`) and `ResolveActor` (`:569-573`); the four write paths take the resolved identity |
| `backend/src/Raffa.Documents.Contracts/Application/DocumentUploadService.cs` | `:48` default removed; `:106` `CreatedBy` and `:136` audit take the actor |
| `backend/src/Raffa.Documents.Contracts/Application/ContractCorrectionService.cs` | `:118` default removed; `:306`, `:332`, `:346`, `:360` take the actor |
| `backend/src/Raffa.Renewals/Application/RenewalActionService.cs` | `:57` default removed; `:138` takes the actor |
| `backend/src/Raffa.Savings/Application/SavingsOpportunityService.cs` | `:95` default removed; `:165`, `:316` take the actor |
| `backend/src/Raffa.Quotes/Application/QuoteUploadService.cs` | `:38` default removed; `:135` takes the actor |
| `backend/src/Raffa.Quotes/Application/Outcome/NegotiationOutcomeService.cs` | `:88` default removed; `:190` takes the actor |
| `backend/src/Raffa.Quotes/Application/Normalization/SkuMappingService.cs` | `:90` default removed; `:205` takes the actor |
| `backend/src/Raffa.Chat/Application/RagAnswerService.cs` | `:67` default removed; `:140` takes the actor |
| `backend/src/Raffa.Api/AskCopilotService.cs` | `:103` default removed; `:989` takes the actor |
| `backend/src/Raffa.Api/ConversationsEndpointExtensions.cs` | the 400 at `:396-398` becomes the same 401 as everywhere else |
| `backend/tests/Raffa.Api.Tests/`, `backend/tests/Raffa.Audit.Tests/` | the tests below |

Passata 2 cwd is the per-task git worktree of the product clone.

## Context the implementer needs

`Closes: NW-32`.

Not at the w15 table — queued. Evidence:
`reports/context/waves/w15-requirements.md` §2, NW-32 row. The w15 decision that
motivates it is `reports/architecture/waves/w15.md` **NW-05**, delivery-manager
cell.

- **Architecture decisions in force**: **ADR-011** (every write names its actor);
  **ADR-022** (the last interim fallback retires).
- **The real defect is the inconsistency, not the string.** One absent header
  produces three different outcomes across the API. Collapsing them is the point;
  deleting the constant is how.
- **Nine of the ten sites take no actor parameter today**, so this is a signature
  change through nine services, not a find-and-replace on one constant.

## Definition of done

- [ ] `dotnet build backend/Raffa.slnx` exit 0
- [ ] `dotnet test backend/Raffa.slnx` exit 0
- [ ] `grep -rn "unattributed" backend/src/` returns **no match**
- [ ] `dotnet test backend/tests/Raffa.Audit.Tests` exit 0 — no audit row can be written without a named actor
- [ ] `dotnet test backend/tests/Raffa.Api.Tests` exit 0 — an absent identity is **401** on conversations as well as everywhere else

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | no `CreatedBy` / `CorrectedBy` / audit row accepts a placeholder actor | `backend/tests/Raffa.Audit.Tests/` |
| API | one absent-identity behaviour — 401 — across documents, conversations and every write path | `backend/tests/Raffa.Api.Tests/` |

## Open questions blocking this task

- Whether historic `"unattributed"` rows are backfilled. **Assumption in force**: left and labelled, never rewritten. Not blocking.

## Wave-spec entry

**None — `status: queued`.** When W16 schedules it:

```yaml
- id: E18/F03/US02/T01
  prompt: reports/workitems/epic-18-api-authentication/feature-03-interim-posture-retirement/us-02-every-write-names-its-actor/tasks/task-01-every-write-names-its-actor.md
  produces: [actor-on-every-write]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
