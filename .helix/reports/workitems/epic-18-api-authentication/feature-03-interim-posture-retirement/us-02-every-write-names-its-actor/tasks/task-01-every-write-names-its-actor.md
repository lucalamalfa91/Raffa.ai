---
id: E18/F03/US02/T01
type: task
story: us-02-every-write-names-its-actor
wave: w15
status: live
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

---

## Wave w16 addendum (2026-09-15) — promoted to `live`, phase 1

**Appended by `next-decomposer`; the body above is unchanged (raw §0.4).** Where
this section and the body disagree, **this section wins** — it carries the w16
council's rulings, which were taken against the tree at `f0b3436` and correct
several premises the body was written under.

**Scheduled**: `reports/plan/slices/w16.yaml`, **phase 1**, `effort: L` (not `M`
— nine service types across five modules). Artifact `actor-on-every-write`;
`E19/F01/US01/T01`, `E19/F02/US01/T01` and `E19/F04/US01/T01` all depend on it,
which is why it runs first.

**Decision row**: `reports/architecture/waves/w16.md`, **NW-32**
(software-architect + security-architect).

### The framing changed, and it changes what "done" means

The body says the defect is "the inconsistency, not the string", and asks to
collapse three divergent absent-identity behaviours to one 401. **Two of the
three no longer exist.** Since PR #117 every endpoint reaching the nine services
sits behind `ICallerContext` and returns **401 first** — verified gate by gate.
So `"unattributed"` is **not** written on an identity-absent branch: it is an
**unconditional hardcode on every call, signed or not**.

> **The defect is a falsified audit trail, not an authentication bypass.** An
> authenticated caller's writes are attributed to nobody. That is a live ADR-011
> violation (w16 clause 15), and it is why the item stays `should` rather than
> being promoted.

- **Body row "the 400 at `:396-398` becomes the same 401" — VOID.** No such branch
  exists; absent identity on conversations is already 401
  (`backend/src/Raffa.Api/Infrastructure/CallerContext.cs:117-120`). **This task
  does not edit `backend/src/Raffa.Api/ConversationsEndpointExtensions.cs`** —
  its stale doc paragraph at `:23-37` belongs to `E18/F02/US01/T01` in phase 3.
- **DoD box "an absent identity is 401 on conversations" is already true and is
  not evidence of delivery** (ADR-001 w16 clause 5). **The grep box is the real
  gate**, read with the caveat below.
- The body's line numbers predate w15. **Re-verified on `f0b3436`**: exactly
  **10** declarations of `private const string UnattributedActor = "unattributed"`
  and **14** runtime write sites across **9** service types —
  `Raffa.Api/AskCopilotService.cs:103`; `Raffa.Api/DocumentsEndpointExtensions.cs:81`
  (**declaration only, already dead** — the `ResolveActor` helper it fed was
  deleted in w15); `Raffa.Chat/Application/RagAnswerService.cs:67`;
  `Raffa.Documents.Contracts/Application/ContractCorrectionService.cs:118`;
  `Raffa.Documents.Contracts/Application/DocumentUploadService.cs:59`;
  `Raffa.Quotes/Application/QuoteUploadService.cs:38`;
  `Raffa.Quotes/Application/Normalization/SkuMappingService.cs:90`;
  `Raffa.Quotes/Application/Outcome/NegotiationOutcomeService.cs:88`;
  `Raffa.Renewals/Application/RenewalActionService.cs:57`;
  `Raffa.Savings/Application/SavingsOpportunityService.cs:95`.

### The shape (ADR-002 w16 clause 4, ADR-011 w16 clauses 15–17)

- **A required `string` actor, positionally after the entity id**, on all nine
  service methods — the shape the three already-correct document paths use
  (`DocumentsEndpointExtensions.cs:147`, `:512`, `:576`, all passing
  `caller.Identity!`). **No default value**, so the placeholder cannot return by
  omission.
- **No new type and no ambient accessor.** An ambient actor lets the two
  caller-less sites compile and silently write nothing — the same defect with a
  new name.
- **The two caller-less sites** — `Raffa.Chat/Application/RagAnswerService.cs:140`
  and `Raffa.Savings/Application/SavingsOpportunityService.cs:165` — take a
  reserved **`system:<component>`** principal. The convention is **already live**
  at `backend/src/Raffa.Api/NegotiationOutcomePropagationService.cs:97`
  (`"system:negotiation-outcome-propagation"`). Invent nothing. It can never
  collide with a token subject: the string carries a `:`, which an Entra object
  GUID cannot, and **the reserved prefix is never accepted as a resolved token
  subject**. *A reserved actor is a fact; `"unattributed"` is a lie.*
- `AuditEvent.Actor` stays `required string`
  (`backend/src/Raffa.Audit/Domain/AuditEvent.cs:27`). **The column was never the
  problem** — the placeholder exists precisely because null is impossible.
- **`backend/tests/Raffa.Chat.Tests/RagAnswerServiceTests.cs:70` is REWRITTEN to
  assert the resolved actor, never deleted** (S16-10). It is the only evidence
  the behaviour changed.
- **One stale record this creates, closed inside this task's own file**: the doc
  comment at `NegotiationOutcomePropagationService.cs:94-96` calls that string an
  "interim actor placeholder (ADR-010 is not wired in yet)". ADR-010 **was**
  wired in w15 and the value is now permanent and correct. No new task, no new
  writer (ADR-011 w16 clause 16c).
- **The append-only consequence — do not propose a cleanup.**
  `backend/src/Raffa.Audit/Migrations/Scripts/audit.sql:76-88` installs a trigger
  rejecting UPDATE and DELETE on `audit_event`, so the `"unattributed"` rows
  already written are **permanent and uncorrectable**. That is correct for an
  audit trail. **The trigger is never dropped — not for a backfill, not inside a
  migration, not temporarily.** This item stops the bleeding; it does not clean
  history.

### Named tests this task carries

| Id | What it must prove |
|---|---|
| **S-T28** | a **signed** POST on each of the nine paths writes an audit row whose `Actor` equals the caller's resolved subject; `grep -r unattributed backend/src` returns nothing outside the comments the council allows; **no test asserts the literal** |
| **S-T29** | the two caller-less sites write the reserved `system:<component>` principal, and that string is **rejected as a token subject** — the two namespaces provably cannot collide |

### Reworded acceptance (A16-4, ADR-001 w16 clause 5)

> A **signed** POST on each of the nine paths writes an audit row naming the
> caller's resolved subject; `grep -r unattributed ../backend/src` returns
> nothing outside comments the council allows; no test asserts the literal.

### Single writer, phase 1

This task owns `backend/src/Raffa.Api/AskCopilotService.cs`,
`backend/src/Raffa.Api/DocumentsEndpointExtensions.cs`,
`backend/src/Raffa.Api/NegotiationOutcomePropagationService.cs` and the nine
service files. **Do not touch**
`backend/src/Raffa.Api/ContractsEndpointExtensions.cs`, anything new under
`backend/src/Raffa.Documents.Contracts/Domain` or `.../Infrastructure`, or
`backend/src/Raffa.Worker/**` — those belong to the two sibling tasks of this
phase. Do not open `web/**` or `.github/**`.
