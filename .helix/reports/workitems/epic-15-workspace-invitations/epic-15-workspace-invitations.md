---
id: epic-15
type: epic
wave: w14
status: active
extends: [epic-01 F05, epic-06 F04]
---

# epic-15-workspace-invitations — invite, accept, remove, re-invite

## Business capability

An Admin invites a colleague by email address. The invitation is a signed,
single-use, expiring link. The invitee clicks it, signs in, and **joins that
workspace** — not a new empty one. They stay a member until an Admin removes
them; re-adding them needs a **new** invitation. Nothing on the screen claims a
mail was sent unless the server says one left.

## Product coverage

| Source | Item |
|--------|------|
| `inputs/next/next-waves-todo.md` §1 | NW-58 (observed 2026-09-10) |
| spec §20 Day 1 | "Create a workspace and invite Procurement users" |
| `inputs/requirements.md` §5.1 / R-WEB-07 | workspace roles and the D8 role summaries |
| `inputs/design/prototypes/raffa-v2/screens-v2.md` §10 | Workspace & members |
| ADR-025 §C / §D | token, lifecycle, rejection contract, last-Admin guard |
| ADR-026 §D4 / §D5 / §D6 | `workspace_invitation`, endpoint contracts, mailer seam |

## Features

| ID | Title | Wave |
|----|-------|------|
| feature-01 | Invitation lifecycle API | w14 |
| feature-02 | Members and invitations on the web | w14 |

## Success looks like

**N3b-1…N3b-8** — an Admin invites an address; the invitation exists with a
copyable single-use accept link and the UI does **not** say "sent" (no
transport ships in w14, `mailDelivered: false`); the invitee opens the link,
signs in, and lands in **that** workspace; an unaccepted invitation grants
nothing; an Admin removes the member and their access is gone on the very next
request; re-joining needs a new invitation; the last Admin cannot be removed;
every gate is server-side from the membership row.

## Architecture decisions in force

- ADR-025 — workspace membership authorization and the invitation lifecycle (new; §C token, §D lifecycle, §G audit, §H tests)
- ADR-026 — §D4 `workspace_invitation`, §D5 endpoint contracts, §D6 `IInvitationMailer`
- ADR-009 — tenancy / RLS (w14 footer clause 4: the accept path's caller-supplied scope is the second and last named exception; clause 8: the new tenant table)
- ADR-011 — secrets and RAG (w14 footer: **no** Key Vault entry for the token; authz-before-retrieval)
- ADR-005 / ADR-006 — mail transport decided (ACS Email + Azure Managed Domain) and **deferred**; w14 Azure delta is zero
- ADR-018 (route footer) — `/invite/accept` is public, outside `AppShell`, signed-out reachable, fragment-carrying; the `BrowserRouter` hoist
- ADR-020 (design footer) — screen 10 invite outcomes; **new screen 11** = the accept flow in ten states
- ADR-019 — `Active` · `Invited` · `Expired` status treatments; inline (not dialog) confirmation
- ADR-001 (w14 footer) — the workspace-domain rule is a **warning**, not a block

## Out of scope

- **A real mail transport.** Decided (ACS Email + Azure Managed Domain, ADR-005
  w14 footer) and deferred to its own wave. w14 ships zero Azure resources,
  zero Terraform change, zero new config keys, zero new Key Vault secrets.
- **A server-held invitation key** — none, in w14 or after (ADR-025 §C).
- **Role change / promotion** — no affordance exists in w14; the last-Admin hint
  says "invite another Workspace Admin", not "promote".
- **A "claim this workspace" endpoint** — refused as a tenant-takeover
  primitive (ADR-025 §2.3). Backfill is an operator job (epic-14 F05).
