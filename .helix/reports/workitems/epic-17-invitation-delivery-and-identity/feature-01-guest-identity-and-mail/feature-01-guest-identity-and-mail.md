---
id: feature-01
type: feature
parent: epic-17
wave: w15
status: active
extends: epic-15 F01 (invitation lifecycle — its two deferred `must` clauses)
---

# feature-01-guest-identity-and-mail — The invite request provisions the identity and sends the mail

## Slice

One request, one transaction, in this order: provision the invitee's Entra B2B
guest through Microsoft Graph (`sendInvitationMessage: false` — Raffa.ai's own
mail is the channel), bind the returned object id into
`workspace_user.ExternalSubjectId`, write the membership and invitation rows,
send the mail with an **absolute** accept link, and return a 201 that names what
actually happened. A provisioning failure **aborts the invitation** — no row, no
mail, no token — and answers 502 with a machine-readable reason from a closed
set.

Both halves land in one task because they are one handler and one `Program.cs`
edit: NW-67 and NW-68 write the same four files, and separating them would force
two phases of the same request path for no reviewable gain.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Inviting a colleague creates their identity and mails them a working link | w15 |

## Architecture decisions in force

- **ADR-025 §J.1–§J.7** — `User.Invite.All` as a Graph **application** permission on the existing workload identity (not the Guest Inviter directory role); guest first, row second; the `oid` bind; the closed failure set; removal never deletes a guest; the new audit verbs; the per-tenant cap of 100 live invitations.
- **ADR-026 §1–§6** — `identityProvisioned`, `deliveryOutcome` with exactly `sent | mail_failed | no_transport`, the declared 502 and its `reason` set, replace-on-live-invitation.
- **ADR-002** (w15 footers) — the Graph adapter lives in `Raffa.Api/Infrastructure/`, never in the module; `Microsoft.Graph` → `Raffa.Api` only, enforced **package-scoped** in `SdkAllowListTests`.
- **ADR-011** (w15 §1, §4) — `acs-connection` is the one new secret and NW-67 adds none; the recipient address may appear in a tenant-scoped **audit row** and must never appear in the **application log**.
- **ADR-020** (w15 §4) — the invitation email is **surface 12**: plain text is the body of record.
- **ADR-005 / ADR-016** — the transport design is applied, not re-opened; the four `Invitations__*` keys arrive only through Terraform.

## Target repo

`raffa-backend`
