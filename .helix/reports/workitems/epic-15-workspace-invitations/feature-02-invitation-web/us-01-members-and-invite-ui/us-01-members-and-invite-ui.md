---
id: us-01
type: user-story
parent: feature-02
wave: w14
status: active
---

# us-01-members-and-invite-ui — a roster that survives a reload, and a screen that never says "sent" unless it was

## Story

As a **Workspace Admin**, I want the Members screen to show the roster the
server holds, tell me honestly whether the invitation was mailed or only
created, and let me revoke a pending invitation or remove a member — so that
nothing on the screen asserts a fact the system does not hold.

## Acceptance criteria

- [ ] AC-1 (**N3**) The Members table renders `getWorkspaceMembers(tenantId)`.
      Invite → the roster is **re-read**, not optimistically appended, and
      **survives `page.reload()`** — the one assertion that separates it from
      today's `sessionStorage` echo.
- [ ] AC-2 `memberStore.ts` is **deleted in full** (`loadWorkspaceMembers`,
      `rememberInvitedMember`, `MEMBERS_KEY_PREFIX`, `WorkspaceMemberRow`),
      including its `lastActiveAt: new Date().toISOString()` ("last active =
      now", always, for whoever is looking) and its hardcoded self-row
      `role: "Admin"`. **Neither invented fact is back-filled client-side**: if
      the server has no such column, `MembersTable` **drops the column**.
- [ ] AC-3 (**N3b-1**, UI half) The invite result renders the server's
      `mailDelivered`: `true` → "Invitation sent to {email}."; `false` →
      "Invitation ready for {email}." with the copyable single-use link, its
      expiry and a **Copy link** button, and the word "sent" **nowhere on the
      screen**. The client **never** infers "sent" from a 201.
- [ ] AC-4 The **failure** path stops claiming a mail too — `index.tsx:63`'s
      "The invitation could not be sent." is as wrong as
      `INVITATION_SENT_MESSAGE`.
- [ ] AC-5 A cross-domain address renders a **non-blocking warning** under the
      field — "{email} is outside {domain}. They will get full {role} access to
      this workspace." — with submit **enabled**. Format errors stay
      **blocking**: a malformed address is not an address.
- [ ] AC-6 Two destructive affordances in a fourth `Actions` column, both
      Admin-only and gated on the **server** role, both confirmed **inline in the
      row** (never a dialog), each re-reading the roster and with **no optimistic
      local removal**: **revoke** an `Invited` row (`DELETE …/invites/{id}`) and
      **remove** an `Active` one (`DELETE …/members/{membershipId}`).
- [ ] AC-7 Their consequence copy differs because they are different facts.
      Revoke says "Their link stops working. **They never had access to this
      workspace.**" and must **not** say "they lose access" — an invitation was
      never a grant.
- [ ] AC-8 (**N3b-7**, UI half) The **last Admin's Remove is a visibly disabled
      control with a `.hint`** — "This is the last Workspace Admin. Invite
      another Workspace Admin first." — never a hidden control and never a click
      that 409s. "Invite", not "promote": no role-change affordance exists in
      w14.
- [ ] AC-9 A **Procurement** member sees the roster **read-only**, and
      `Request access` is a real `mailto:` to the workspace's now-knowable Admin
      addresses. **A dead button is the one option not available.**
- [ ] AC-10 The roster is a list surface: skeleton, and error + Retry that
      **never** renders the last known roster.
- [ ] AC-11 `cd web && npm run build` and `npm test` exit 0.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| `workspace-roster-api` (E14/F04/US01/T01) | `getWorkspaceMembers(tenantId)` |
| `invitation-lifecycle-api` (E15/F01/US01/T01) | `mailDelivered`, `acceptUrl`, `revokeInvitation`, `removeMember` |

## Architecture decisions in force

- **ADR-020 (w14 design footer, screen 10)** — the two invite outcome strings,
  the domain warning, the status set, the two destructive affordances, the
  last-Admin control, the **email-primary** row, the **D8** role summaries, the
  Procurement variant.
- **ADR-019 (w14 footer)** — `Active` · `Invited` · `Expired`; `Expired` is
  *derived* from this system's existing "needs your decision" treatment, never a
  new colour; confirmation is **inline in the row**, because `--shadow-*` is
  "dialogs only" (`:78`) and the locked catalogue has no dialog.
- **ADR-018 `:112-119`** — the roster is a list surface: skeleton, error +
  Retry, and the error state **never** falls back to the last known roster.
- **ADR-012 (w14 footer clause 4)** — the client sends `X-User-Id` only on a
  route that already carries `{tenantId}`.
- **ADR-001 (w14 footer)** — the workspace-domain restriction is **deferred** to
  the wave that lands ADR-010.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | The roster from the server, the honest invite result, and revoke / remove | L | phase-4 |

## Council decisions carried into this story

- **The `false` copy is deliberately written to be true for both of its
  causes.** One boolean cannot separate "no transport configured" from
  "transport errored", so the copy states what is certainly true (the invitation
  exists, here is the link) and **never diagnoses the mailer**. In w14 the
  question is moot — `mailDelivered` is `false` by construction — but the string
  must not become a lie the day a transport ships.
- The delivery-**failure** copy is **pre-decided for the transport wave** so it
  is not re-opened: "Invitation created, but the email could not be sent." plus a
  **"Try sending again"** secondary, and **never** "invite again" — a repeat
  invite at the same role is rejected, which is the state everyone forgets. That
  wave needs a **third value, not a second boolean**.
- **The workspace-domain rule is a typo guard, never a safeguard**:
  `validateInviteEmail` (`memberViewModel.ts:69-82`) runs **client-side, before
  any request**, the server has no domain rule at all, and its "workspace
  domain" is a proxy read off whoever happens to be signed in (`index.tsx:45`;
  the code names the gap itself at `:64-67`). Far too imprecise to **gate** an
  invitation, and perfectly adequate to **inform** one. A hard block also makes
  **N3b unverifiable** whenever the second Entra account sits on another domain.
- **Two traps in the design-ref table are corrected and must not be copied into
  the screen**: there is **no member name** — `MembersTable.tsx:39` renders the
  **email** as the primary line and `:13-16` documents that a name is
  deliberately **never derived from the address**, so "aligning to
  `name · email · role · status`" would fabricate one; and the Admin role
  summary is **not** `markup.html:395`'s "Also uploads, deletes, manages
  members" but `memberViewModel.ts:30-33`'s **D8 / R-WEB-07** wording — copying
  the export **regresses an accepted decision**.
- Procurement read-only **resolves a contradiction inside the design oracle**
  rather than overriding it: ADR-018 `:107-108` and `ia-v2.md:13-15` say
  read-only, `markup.html:403` hides the grid outright. ADR-018's w14 design
  footer rules for **read-only**.
- `MEMBERS_TIP` (`memberViewModel.ts:36`) and its gating
  (`index.tsx:101-105`, `shell?.kbReady === false`) are **unchanged**. No task
  should "improve" it into an always-on banner.
- `memberViewModel`'s `getMemberStatusTag` (`:91-93`) and `memberRoleLabel`
  (`:84-88`) are pure and survive — which is why this swap is small.
  `MemberStatus` moves into the generated schema; `getMemberStatusTag`'s binary
  ternary gains `Expired`.

## Open questions

- **OQ-w14-005** — who may list and remove. **Answered**: any live member reads;
  only an Admin invites, revokes or removes; the last Admin cannot be removed.
