---
id: us-01
type: user-story
parent: feature-05
wave: 13
status: active
---

# us-01-conversations — New chat, resume, and nobody else's chats

## Story

As **procurement**, I want my Ask conversations to be kept by Contigo —
resumable from another browser days later, with citation cards and actions
still clickable — and private to me inside my workspace, so that the
copilot is a place I return to, not a one-shot query box.

## Acceptance criteria

- [ ] AC-1 `conversation` / `conversation_message` are tenant tables under
      RLS keyed by tenant + user; another tenant cannot read a conversation
      with a guessed id (RLS integration test), and another user of the
      same workspace gets 404 / 403 on it.
- [ ] AC-2 `GET /api/conversations` lists the caller's last N (id, title ≤
      48 chars from the first question, scopeContractId, updatedAt);
      `POST /api/conversations` creates one (`{ scopeContractId? }`);
      `GET /api/conversations/{id}` returns it with its messages (role,
      markdown, citations, actions, kind, createdAt) — never the raw pack.
- [ ] AC-3 User identity is the API token subject when ADR-010 is in
      force; under the ADR-022 posture the `X-User-Id` header is required
      and treated as non-authoritative (documented in the endpoint, not
      trusted for anything but scoping).
- [ ] AC-4 `AddChatModule()` keeps working without a connection string
      (unit tests, local) and registers the DbContext when one is given.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| — | T01 is phase 1; T02 (API) needs T01 |

## Architecture decisions in force

- ADR-024 — conversations under RLS keyed by tenant + user
- ADR-009 — RLS policy on every tenant table; ADR-021 — `chat.sql` applied by CI
- ADR-022 / ADR-010 — header posture now, token subject later
- ADR-011 — message rows store no raw prompt / pack, only citations, actions and AI metadata

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | Conversations store: entities, `ChatDbContext`, RLS migration + `chat.sql`, `ConversationService` | M | phase-1 |
| T02 | Conversations API + `Program.cs` registration + RLS integration tests | M | phase-2 |

## Council decisions carried into this story

Tables `conversation(id, tenant_id, user_id, title, scope_contract_id,
created_at, updated_at)` and `conversation_message(id, tenant_id,
conversation_id, role, kind, markdown, citations_json, actions_json,
model_id, prompt_version, input_hash, created_at)`; RLS policy identical to
the other tenant tables (`app.tenant_id`). Connection string key
`ConnectionStrings:Chat` (same database as the other modules). Script
`backend/src/Contigo.Chat/Migrations/Scripts/chat.sql` added to the
fixed-order SCRIPTS list in `.github/workflows/backend.yml`.

## Open questions

- OQ-askv2-004 — unlimited retention in V2 (assumed)
- OQ-askv2-005 — `X-User-Id` until the API JWT lands (assumed)
