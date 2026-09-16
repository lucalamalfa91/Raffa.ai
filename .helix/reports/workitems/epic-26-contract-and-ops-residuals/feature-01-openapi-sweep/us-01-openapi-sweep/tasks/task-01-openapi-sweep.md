---
id: E26/F01/US01/T01
type: task
story: us-01-openapi-sweep
wave: w18
status: live
target_repo: raffa-backend
---

# task-01-openapi-sweep — Sweep stale OpenAPI/client gap prose (NW-30)

## Coding objective
Sweep the two stale claims in the hand-authored contract without changing a
shape: (a) `web/openapi/raffa-api.v1.json` — remove/adjust any prose implying
`GET /api/audit` is absent (it is CLOSED-ON-MAIN via w16 NW-08,
`raffa-api.v1.json:6486`, `Program.cs:377`) and record the conversations
`requestBody` as a documented generator limitation (the generator parses only
`responses`; bodies are hand-written in `client.ts`, as noted at
`raffa-api.v1.json:6`, conversations `:5339`, `:5680`); (b)
`web/src/api/client.ts` — sweep the same stale prose; confirm the hand-written
conversation bodies are present and correct (no re-wrap, no new method).
This is a documentation sweep only; no endpoint, wrapper or schema change.

## Parent story AC covered
- [x] AC-1 OpenAPI no longer claims audit absent; records requestBody limitation.
- [x] AC-2 client prose swept of stale claims.
- [x] AC-3 no new wrapper / shape change.

## Files to create or modify
| Path | Change |
|------|--------|
| web/openapi/raffa-api.v1.json | sweep stale audit/requestBody prose |
| web/src/api/client.ts | sweep stale prose; confirm hand-written bodies |

## Context the implementer needs

**Closes: NW-30**

- **Architecture decisions in force**: ADR-012 / ADR-026 (the requestBody is a documented generator limitation).
- **Do not touch**: schema.ts (no shape change); any endpoint.

## Definition of done
- [x] `grep` over `web/openapi/raffa-api.v1.json` and `web/src/api/client.ts` returns no stale "no `GET /api/audit`" claim
- [x] `dotnet build backend/Raffa.slnx` exits 0 (no code change expected; sanity)
- [x] `npm run typecheck` exits 0 (client prose change is comment-only) — no `typecheck` script exists in `web/package.json`; ran the equivalent `tsc --noEmit` directly (see Verification below)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| (grep) | no stale prose survives | `web/openapi/raffa-api.v1.json`, `web/src/api/client.ts` |

## Open questions blocking this task
- none

## Verification

Re-audit (2026-09-16): this task is a documentation sweep with a **NIL diff**
on both product files named in "Files to create or modify" — independent
re-reads (this turn) confirm both were already correct on `main` before this
task ran; nothing needed sweeping. Recorded here, on disk, per
`fan_out.require_delivery` — a verification-only turn with nothing committed
is not a delivery.

**AC-1 / AC-2 — no stale "audit absent" / "no requestBody" prose survives**
- `web/openapi/raffa-api.v1.json:6576-6661` — `GET /api/audit` is fully
  documented: 200 (array schema) / 400 (missing or non-GUID `X-Tenant-Id`) /
  403 (member, not Admin) / 404 (no live membership, never 403 — ADR-025
  Rule B1) response ladder, `ICallerContext` -> `WorkspaceRoleResolver`
  guard.
- `web/openapi/raffa-api.v1.json:6` (`info.description`) — the "Task
  E18/F02/US02/T01 (wave w16, NW-08 ...) adds `GET /api/audit` -- closing
  the gap this document's own provenance paragraph above named" sentence is
  present verbatim, and states the route "joins the documented-but-unwrapped
  set" (OQ-w16-003 ruled no web audit surface — `client.ts` correctly has no
  audit wrapper; that is not a gap).
- `web/openapi/raffa-api.v1.json:5429` (`createConversation`) — "the
  generator does not parse `requestBody` -- see this document's own
  'Codegen tool choice' paragraph" is present verbatim.
- `web/openapi/raffa-api.v1.json:5774` (`postConversationMessage`) —
  "hand-written client-side, see this document's 'Codegen tool choice'
  paragraph" is present.
- `backend/src/Raffa.Api/Program.cs:384-389` — `app.MapAuditEndpoints();`
  with the w16/NW-08 provenance comment; the route is live on this branch.
- `backend/src/Raffa.Api/ConversationsEndpointExtensions.cs:476` /
  `:483` — `CreateConversationRequest(string? ScopeContractId = null)` /
  `PostConversationMessageRequest(string? Question)`.
- `web/src/api/client.ts:1072-1074` (`CreateConversationRequest {
  scopeContractId?: string }`) and `:1117-1119` (`PostMessageRequest {
  question: string }`) are wire-compatible with the two backend records
  above — hand-written, not re-wrapped, no new method added.
- `git cat-file -t 992b7b9` → `commit`; `git merge-base --is-ancestor
  992b7b9 HEAD` → is an ancestor of this branch; `git show --stat 992b7b9`
  → `E18/F02/US02/T01: wip -- publish GET /api/audit in the OpenAPI
  contract and regenerate schema.ts`, touching `web/openapi/raffa-api.v1.json`
  + `web/src/api/generated/schema.ts` — the audit half was closed on
  wave w16, before this task existed.
- Broad case-insensitive sweep this turn over both files for stale
  audit-absence phrasing (`` no `GET /api/audit` ``, "does not exist", "not
  implemented", "no audit route", "stale", etc.) — every hit is an
  unrelated, legitimate 404/gap description for a *different* resource
  (workspace membership, invitation, conversation id, contract id, renewal
  action, strategy pack); zero survivors naming audit or requestBody.

**AC-3 — no new wrapper / shape change**: `git diff --stat
refs/helix/fork/wave/E26-F01-US01-T01..HEAD -- web/openapi/raffa-api.v1.json
web/src/api/client.ts` is empty before this commit — no line of either file
changed by this task.

**DoD commands, run this turn (not copied from a prior turn)**
- `dotnet build backend/Raffa.slnx` → `Build succeeded. 0 Warning(s) 0
  Error(s)`, exit 0.
- `web/package.json`'s `scripts` block has no `typecheck` entry (only
  `build` chains `tsc --noEmit`); ran the equivalent directly —
  `node web/node_modules/typescript/bin/tsc --noEmit -p web` → exit 0, no
  output.
- `reports/open-questions.md` grepped for `NW-30|E26/F01|openapi-sweep` —
  zero hits; this task has no open question and needed no assumption.

**Conclusion**: both halves of NW-30 (the audit-absent claim and the
requestBody gap) were already closed by wave w16's `E18/F02/US02/T01`
before this task ran. `web/openapi/raffa-api.v1.json` and
`web/src/api/client.ts` are correct as written; editing either now would
introduce a divergence from `Program.cs` /
`ConversationsEndpointExtensions.cs`, not remove one. This file is the
on-disk delivery artifact for `fan_out.require_delivery`.

## Wave-spec entry
```yaml
- id: E26/F01/US01/T01
  prompt: reports/workitems/epic-26-contract-and-ops-residuals/feature-01-openapi-sweep/us-01-openapi-sweep/tasks/task-01-openapi-sweep.md
  produces: [openapi-swept]
  depends_on: []
  effort: S
  layer: backend
  status: live
```
