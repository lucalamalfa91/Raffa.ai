---
id: F05
type: feature
parent: epic-13
wave: 13
status: active
---

# feature-05-conversations — Server-side conversations under RLS

## Slice

HITL decision D5: conversations live server-side, per user and per
workspace, resumable from any device. `Conversation` /
`ConversationMessage` in `Contigo.Chat` with their own DbContext, RLS
policy and `chat.sql`; messages keep role, markdown, citations, actions,
kind and AI metadata (never the raw pack). API: list the caller's recent
conversations, create (optionally scoped to a contract), get with
messages; the message-posting endpoint is wired by F06 on top of this
store (`inputs/requirements.md` R-CONV-01…03, §6).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | New chat, resume, and nobody else's chats | 13 |

## Architecture decisions in force

- ADR-024 — conversations under RLS keyed by tenant + user
- ADR-009 — RLS on every tenant table; ADR-021 — `chat.sql` applied by CI
- ADR-022 / ADR-010 — `X-User-Id` non-authoritative until the API JWT (OQ-askv2-005)

## Target repo

`contigo-backend`
