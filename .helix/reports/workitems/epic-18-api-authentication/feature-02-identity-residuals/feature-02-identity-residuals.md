---
id: feature-02
type: feature
parent: epic-18
wave: w15
status: active
extends: epic-13 F05 (conversations), epic-01 F05 (audit read)
---

# feature-02-identity-residuals — Conversations and the audit read follow the token subject

## Slice

Two routes that ADR-010's w14 footer names by id and that NW-05 unblocks without
finishing: conversation rows must be keyed by the token subject rather than by an
un-normalized header string, and `GET /api/audit` must become reachable by a real
workspace Admin through **membership**, not through a claim.

**Both stories are queued to W16.** They are decomposed here with full evidence
so the next intake picks them up without re-auditing; neither has a task in
`reports/plan/slices/w15.yaml`.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Conversation `user_id` is the token subject | **queued — W16** |
| us-02 | `GET /api/audit` works for a real Admin | **queued — W16** |

## Architecture decisions in force

- **ADR-010** (w14 footer, w15 footer) — the token subject is the identity; a `tenant_id` or `roles` claim is **never** the authorization source.
- **ADR-024 / `OQ-askv2-005`** — already names the conversation-keying swap by id.
- **ADR-026** — a route that is not in the published contract cannot be reached by a generated client.
- **ADR-011** — who may read a tenant's audit trail, and the scoping of that read.

## Target repo

`raffa-backend`
