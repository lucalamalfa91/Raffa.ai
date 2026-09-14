---
id: us-01
type: user-story
parent: feature-01
wave: w15
status: active
---

# us-01-invite-provisions-and-mails — Inviting a colleague creates their identity and mails them a working link

## Story

As a **workspace Admin**, I want inviting a colleague to create their identity
and send them an email with a link that works, so that they can join without me
opening the Azure portal — and when something goes wrong, I want to be told
which part failed rather than handed a "ready" link nobody can use.

## Acceptance criteria

- [ ] AC-1 `POST /api/workspaces/{tenantId}/invites` provisions an Entra B2B guest for the invited address through Microsoft Graph with **`sendInvitationMessage: false`**, **before** any row is written, and binds the returned object id into `workspace_user.ExternalSubjectId`.
- [ ] AC-2 Inviting an address **already present** in the directory behaves identically, creates no duplicate guest, and is **indistinguishable in shape** from a fresh provision — the invite form is not a directory-enumeration oracle.
- [ ] AC-3 A provisioning failure **aborts the invitation**: no `workspace_invitation` row, no mail, no token. The response is **502** with a machine-readable `reason` from the closed set `consent_missing | provisioning_failed | directory_unavailable`, and one audit row is written.
- [ ] AC-4 The 201 carries **`identityProvisioned`** (non-nullable boolean; `false` means *not configured*, never *failed*) and **`deliveryOutcome`** (non-nullable string with an `enum` of exactly `sent | mail_failed | no_transport`), under the normative biconditional **`deliveryOutcome == "sent"` iff `mailDelivered == true`**, both computed in one place.
- [ ] AC-5 `AcsInvitationMailer` replaces `NullInvitationMailer` and sends a Raffa-branded mail to **exactly one recipient** — no CC, no BCC, no distribution list, no `Reply-To` at a Raffa-operated mailbox.
- [ ] AC-6 `acceptUrl` becomes **absolute** when `Invitations__AcceptUrlBase` is set, keeping the `#` fragment form. With mail enabled and the base absent, non-`https`, or not a well-formed absolute URI, **the API does not start** — it never falls back to the site-relative constant.
- [ ] AC-7 Inviting an address that already holds a **live** invitation **revokes and re-issues in one transaction**. The `alreadyMember` branch keeps its `Conflict`, and the unique-violation catch **stays** as the backstop for two concurrent invites.
- [ ] AC-8 A per-tenant cap of **100 live invitations** is enforced, audited as `workspace.invitation.cap_reached` when hit.
- [ ] AC-9 The application log carries the invitation id, the ACS operation id and a named outcome — **never the recipient address**, the rendered body, the accept URL, the token, its hash, or a raw ACS error payload. The tenant-scoped **audit row** may name the invited address.
- [ ] AC-10 The Graph **`inviteRedeemUrl` appears nowhere** — not in a response, a mail, a store, a log or an audit row.
- [ ] AC-11 `IGuestProvisioner` has a fourth **`NotConfigured`** value (default off) under which the invite behaves exactly as it does on `main` today — link-only, no directory write. The abort rule of AC-3 binds **`Failed`** alone.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-api-validates-the-token (`E18/F01/US01/T01`) | the accept must bind to a **server-verified** identity, not a client-supplied string. For a freshly provisioned B2B guest the directory UPN is the `…#EXT#@…` form and what MSAL surfaces is the provider's `preferred_username` — provider-defined and not assertable from this tree. Shipping an accept path built on `X-User-Id` would be rewritten by NW-05 in the same wave |

## Architecture decisions in force

- **ADR-025 §J.1–§J.7** — the permission, the ordering, the bind, the failure contract, removal, audit, the cap.
- **ADR-026 §1–§6** — the 201's shape and replace-on-live-invitation.
- **ADR-010** (w15 §2.3–§2.4) — never parse the `#EXT#` UPN for an authorization decision.
- **ADR-011** (w15 §1, §4) — the secret ledger and the log/audit split.
- **ADR-002** (w15 footers) — adapter in the host, `SdkAllowListTests` amended **package-scoped**.
- **ADR-020** (w15 §4) — the invitation email is surface 12; plain text is the body of record.
- **ADR-025 C9** — the accept link keeps its `#` fragment: a fragment is never transmitted to a server, which is what makes it safe in an **email** that passes through gateways, archivers and click-protection services that fetch and rewrite URLs server-side. Cloud-architect supplies the platform half: the SWA rewrites every non-asset path to `/index.html` and **logs the accept path**.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Guest provisioning, the real mailer, the absolute link and the 201's new shape | L | phase-3 |

## Council decisions carried into this story

**The single highest-value instruction in this wave** (ADR-025 §J.3b): bind the
Graph-returned guest object id into `workspace_user.ExternalSubjectId` **at
invite time**. Without it a B2B guest's UPN arrives mangled
(`luca_gmail.com#EXT#@…`), Rule D.3b's case-insensitive email equality fails,
and **A15-4 dies at its last step with the invitee signed in and locked out**.
The de-mangling "fix" is ambiguous for addresses containing `_` and must never be
a grant basis.

**Guest first, row second** — reached independently from two directions: from
ADR-026 §D4's partial unique index (a failed invite would otherwise hold the
address slot and block the retry) and from "an inert artefact beats a live
credential" (row-first leaves a live 256-bit token for an identity that cannot
sign in, which is A15-7's defect).

**`Invitations__AcceptUrlBase` is a security control, not a convenience.** It is
`https://` plus this environment's own SPA host, supplied by Terraform, and
**never** derived from `Host`, `Origin`, `X-Forwarded-Host` or any other request
value: a host-header-injected accept base means Raffa.ai mails a live invitation
token to an attacker-controlled origin and the invitee hands it over by clicking
a Raffa-branded mail.

**Two shipped comments become false this wave and are retired in the same edit**:
`NullInvitationMailer.cs:28-29` ("the 201 response body is the link's **only**
channel") and `infra/modules/identity/main.tf:41-42` (the latter belongs to the
Terraform task).

## Open questions

- **OQ-w15-005** — resolved in both halves: the existing per-environment workload managed identity calls Graph; **no secret, no app registration, no client credential**; the `azuread_app_role_assignment` **is** the consent. A guest provisioned from `dev` is visible to `demo` and that is **acceptable**, because directory presence is not a grant — proved by S-T19, not assumed.
- **OQ-w15-007** — resolved: removal **never** deletes or blocks the guest.
- **OQ-w15-sec-02** — resolved: connection string for w15; the `TokenCredential` migration is recorded as an ADR-011 amendment plus one role assignment when it lands.
- **OQ-w15-sec-03** — **assumption in force: the cap is 100.** The product-owner may change the number; the mechanism does not change.
- **OQ-w15-sec-01** — **assumption in force**: the `email` claim exists only if the optional claim is requested, and the design deliberately does not depend on it — the `oid` bind is the primary path. If `email` proves absent *and* the bind were missed, accept answers 403 and the Admin re-invites; **never a silent grant**.
