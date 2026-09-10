---
id: E13/F05/US01/T02
type: task
story: us-01-conversations
wave: 13
status: live
target_repo: raffa-backend
---

# task-02-conversations-api — `/api/conversations` endpoints, host registration, RLS integration tests

## Coding objective

Expose the conversation store (`inputs/requirements.md` R-CONV-01…03, §6):
`GET /api/conversations` (caller's last N, default 5, query `take`),
`POST /api/conversations` (`{ scopeContractId? }` → 201 with the
conversation), `GET /api/conversations/{id}` (conversation + messages
ordered by `createdAt`; 404 when it belongs to another user of the same
tenant). Caller identity: the token subject when an authenticated
principal is present; otherwise the required `X-User-Id` header (ADR-022
posture — document it as non-authoritative in the endpoint summary and
in `backend/README.md`), missing header → 400. Tenant from `X-Tenant-Id`
exactly like `ChatEndpointExtensions`. Put the endpoints in a new
`ConversationsEndpointExtensions.cs` (`MapConversationsEndpoints()`);
the message-posting endpoint is **not** this task (F06/T01 adds
`POST /api/conversations/{id}/messages` to this file in phase 3). In
`Program.cs` (this task is its phase-2 writer): read
`ConnectionStrings:Chat`, call `AddChatModule(chatConnectionString)`,
map the new endpoints; add the `Chat` connection string to
`appsettings.Development.json` (same local database) and to the worker
only if the worker needs it (it does not). Integration tests:
`ConversationsCrossTenantIsolationTests` (RLS: tenant B cannot read tenant
A's conversation by id; user 2 of tenant A gets 404 on user 1's
conversation). Leave `POST /api/chat/query` untouched.

## Parent story AC covered
- AC-1, AC-2, AC-3

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/src/Raffa.Api/ConversationsEndpointExtensions.cs` | new (list, create, get) |
| `backend/src/Raffa.Api/Program.cs` | `AddChatModule(chatConnectionString)`, `MapConversationsEndpoints()` (phase-2 writer) |
| `backend/src/Raffa.Api/appsettings.Development.json` | `ConnectionStrings:Chat` |
| `backend/src/Raffa.Chat/Application/Conversations/*` | only if a query shape is missing (list for user, get with messages) |
| `backend/tests/Raffa.Api.Tests/ConversationsEndpointTests.cs` | new |
| `backend/tests/Raffa.IntegrationTests/ConversationsCrossTenantIsolationTests.cs` | new |
| `backend/tests/Raffa.Api.Tests/DeployableApiTests.cs` | connection string wiring if the test asserts registered modules |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (conversations per tenant + user), ADR-009 (RLS backstop), ADR-022 / ADR-010 (identity posture), ADR-011 (messages never carry the raw pack).
- Gap G-CONVERSATIONS. Header handling pattern: `ChatEndpointExtensions.PostChatQueryAsync` (`X-Tenant-Id` parse → 400).
- **Do not touch**: `ChatEndpointExtensions.cs` (F06), `Raffa.Chat/Infrastructure/ServiceCollectionExtensions.cs` (F08/T01 owns it this phase), `Raffa.Market` / `Raffa.Insights` registration (F06/T01 wires them in phase 3), OpenAPI json (documented by the phase-4 web task), `web/`.
- Terraform already injects the other module connection strings; the `Chat` string is the same database — extend `infra/modules/containerapps` **only if** a per-module secret is required by the existing pattern (check `ConnectionStrings__Renewals` wiring in `infra/modules/containerapps/main.tf`; if every module string is injected separately, add `ConnectionStrings__Chat` the same way).

## Definition of done
- [ ] `dotnet test backend/tests/Raffa.Api.Tests --filter ConversationsEndpointTests` exit 0 — 400 without `X-User-Id`, 201 create, list returns only the caller's, get 404 for another user
- [ ] `dotnet test backend/tests/Raffa.IntegrationTests --filter ConversationsCrossTenantIsolationTests` exit 0 (Postgres fixture) — RLS denies tenant B
- [ ] `dotnet test backend/Raffa.slnx` exit 0

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| API | endpoints + identity rules | `Raffa.Api.Tests/ConversationsEndpointTests.cs` |
| integration | RLS on conversations | `Raffa.IntegrationTests/ConversationsCrossTenantIsolationTests.cs` |

## Open questions blocking this task
- OQ-askv2-005 — `X-User-Id` until the API JWT (assumed)

## Wave-spec entry
```yaml
- id: E13/F05/US01/T02
  prompt: reports/workitems/epic-13-ask-v2/feature-05-conversations/us-01-conversations/tasks/task-02-conversations-api.md
  produces: [conversations-api]
  depends_on: [conversations-store]
  effort: M
  layer: backend
  status: live
```
