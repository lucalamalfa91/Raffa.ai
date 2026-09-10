---
id: E13/F05/US01/T01
type: task
story: us-01-conversations
wave: 13
status: live
target_repo: raffa-backend
---

# task-01-conversations-store — Conversation entities, `ChatDbContext`, RLS, `ConversationService`

## Coding objective

Give `Raffa.Chat` its own persistence for conversations (HITL D5,
`inputs/requirements.md` R-CONV-01, §7). Add `Domain/Conversations/`
`Conversation` (id, tenant, userId, title ≤ 48 chars, scopeContractId?,
createdAt, updatedAt) and `ConversationMessage` (id, tenant,
conversationId, role `you|raffa`, kind `answer|abstain|redirect|refusal`,
markdown, citationsJson, actionsJson, modelId?, promptVersion?, inputHash?,
createdAt) as `TenantScopedEntity`-style classes (copy the pattern of
`Raffa.Documents.Contracts.Domain.TenantScopedEntity`; Chat may not
reference Documents). Add `Infrastructure/ChatDbContext` (+ design-time
factory, EF configurations, `TenantRlsConnectionInterceptor` wiring exactly
like `DocumentsContractsDbContext`), an EF migration that creates both
tables with the RLS policy on `app.tenant_id` (`FORCE ROW LEVEL SECURITY`,
same SQL shape as `AddTenantRowLevelSecurity` in Documents), and the
checked-in idempotent script `Migrations/Scripts/chat.sql` generated with
`dotnet ef migrations script --idempotent` (ADR-021) plus a
`ChatMigrationScriptTests` mirroring `DocumentsContractsMigrationScriptTests`.
Add the script to the fixed-order `SCRIPTS` arrays ("Apply schema" and
"Verify schema applied") in `.github/workflows/backend.yml`. Implement
`Application/Conversations/ConversationService` (create, list recent for
tenant + user, get with messages, append message) writing audit rows
`conversation.created` / `conversation.message.appended` through
`IAuditWriter`. Change `AddChatModule()` to an overload
`AddChatModule(string? chatConnectionString = null)`: without a string it
registers what it registers today (tests and local keep working); with one
it also registers `ChatDbContext` + `ConversationService`. Do not add the
HTTP endpoints (T02) and do not edit `Program.cs`.

## Parent story AC covered
- AC-1 (RLS policy in place; the cross-tenant integration test lands in T02), AC-4

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/src/Raffa.Chat/Domain/Conversations/Conversation.cs`, `ConversationMessage.cs`, `TenantScopedEntity.cs` | new |
| `backend/src/Raffa.Chat/Infrastructure/ChatDbContext.cs`, `ChatDbContextFactory.cs`, `ChatDbContextOptions.cs`, `Configurations/*` | new |
| `backend/src/Raffa.Chat/Migrations/*` + `Migrations/Scripts/chat.sql` | new (EF migration + idempotent script) |
| `backend/src/Raffa.Chat/Application/Conversations/ConversationService.cs` (+ result records) | new |
| `backend/src/Raffa.Chat/Infrastructure/ServiceCollectionExtensions.cs` | overload with optional connection string |
| `backend/src/Raffa.Chat/Raffa.Chat.csproj` | EF Core / Npgsql packages as the other modules |
| `backend/tests/Raffa.Chat.Tests/Conversations/*` | service tests (in-memory / sqlite as the module's convention), migration script test |
| `.github/workflows/backend.yml` | add `chat.sql` to both SCRIPTS arrays (this task is the phase-1 writer of this file) |

## Context the implementer needs
- **Architecture decisions in force**: ADR-024 (conversations under RLS keyed by tenant + user), ADR-009 (RLS on every tenant table; `SET app.tenant_id`), ADR-021 (checked-in idempotent SQL applied by CI; no `MigrateAsync`), ADR-011 (no raw prompt / pack in rows), ADR-002 (Chat allow-list `[SharedKernel, AiGateway]` — copy patterns, do not reference Documents).
- Gap G-CONVERSATIONS. Reference implementation to mirror: `backend/src/Raffa.Documents.Contracts/Infrastructure/*`, `Migrations/Scripts/documents-contracts.sql`, `backend/README.md` "Schema apply".
- **Do not touch**: `Program.cs` (F04/T01 owns it this phase), `ChatEndpointExtensions.cs`, `AskRaffaQueryRouter` / `RagAnswerService` (F06), `web/`.

## Definition of done
- [ ] `dotnet test backend/tests/Raffa.Chat.Tests` exit 0 — create / list / get / append; title truncation at 48; migration script test green
- [ ] `dotnet ef migrations script --idempotent` output equals the checked-in `chat.sql` (the script test proves it)
- [ ] `dotnet build backend/Raffa.slnx` exit 0; `AddChatModule()` without arguments still resolves `AskRaffaQueryRouter` and `RagAnswerService`
- [ ] `grep -c "chat.sql" .github/workflows/backend.yml` = 2

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | service behaviour, audit rows, title rule | `Raffa.Chat.Tests/Conversations/ConversationServiceTests.cs` |
| unit | idempotent script matches the model | `Raffa.Chat.Tests/ChatMigrationScriptTests.cs` |

## Open questions blocking this task
- OQ-askv2-004 — unlimited retention (assumed)

## Wave-spec entry
```yaml
- id: E13/F05/US01/T01
  prompt: reports/workitems/epic-13-ask-v2/feature-05-conversations/us-01-conversations/tasks/task-01-conversations-store.md
  produces: [conversations-store]
  depends_on: []
  effort: M
  layer: backend
  status: live
```
