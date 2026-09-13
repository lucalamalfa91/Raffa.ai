---
id: E15/F02/US01/T01
type: task
story: us-01-members-and-invite-ui
wave: w14
status: live
target_repo: raffa-web
---

# task-01-web-members-and-invites — the roster from the server, the honest invite result, and revoke / remove

## Context

**Closes: NW-04 (web), NW-58 (the members-screen half).**
Decision rows: `reports/architecture/waves/w14.md`, row **NW-04**
(client-architect) and row **NW-58** (client-architect + ux-ui-designer). ADRs in
force: **ADR-020 w14 design footer (screen 10)**, **ADR-019 w14 footer**,
**ADR-018 w14 footers + `:112-119`**, **ADR-012 w14 footer clauses 1 and 4**,
**ADR-001 w14 footer** (the domain restriction is deferred).

Today `memberStore.ts:57,77` reads and writes `sessionStorage`, so the table is
this session's optimistic echo of invites made in this tab; another Admin,
another browser or a reload sees a different roster than Postgres holds. And
`index.tsx:78` sets `sent` unconditionally on a 201, so
`InvitePane.tsx:101-105` renders `INVITATION_SENT_MESSAGE`
(`memberViewModel.ts:39`) when **no mail transport exists anywhere in the repo**
— the defect NW-58's must #1 names.

**This task owns `web/src/routes/workspace/members/**` in phase 4 and nothing
else.**

**Interface contract with its phase-4 sibling `E14/F03/US02/T01`** (which owns
`App.tsx`, `WorkspaceShellApp.tsx`, `AppShell.tsx` and the shell files): that
task adds `workspaceId: string` to `WorkspaceShellAppProps` and passes **both
`workspaceId` and `role`** into `<MembersRoute …>`, and **removes the
`<RequireRole role={role} allow="admin">` wrap** from the `workspace/members`
route so a Procurement member reaches this screen. **Consume those two props.**
Do **not** call `loadCurrentWorkspace()` yourself (`index.tsx:36`) and do **not**
edit `WorkspaceShellApp.tsx` or `RequireRole.tsx` — if the props are not there
at integration time, **HALT** and name that task.

- **Architecture decisions in force**: ADR-020 w14 design footer (screen 10),
  ADR-019 w14 footer (statuses, inline-not-dialog, the disabled-CTA rule),
  ADR-018 `:112-119` (list-surface states), ADR-012 w14 footer clause 4 (headers).
- **Do not touch**: `web/src/App.tsx`, `web/src/components/shell/**` (including
  `WorkspaceShellApp.tsx`, `AppShell.tsx`, `RequireRole.tsx`, `navItems.ts`,
  `workspaceRole.ts`, `useValidatedContractCount.ts`), `web/src/routes/signin/**`,
  `web/src/routes/invite/**`, `web/e2e/**` (all `E14/F03/US02/T01`'s, same phase
  or the integration task's); `web/openapi/raffa-api.v1.json`,
  `web/src/api/generated/schema.ts`, `web/src/api/client.ts` (phases 2 and 3 own
  them — **if a client method you need is missing, HALT and name it**);
  `backend/**`, `infra/**`, `.github/**`.

## Coding objective

**1. Delete `memberStore.ts` in full** — `loadWorkspaceMembers`,
`rememberInvitedMember`, `MEMBERS_KEY_PREFIX`, `WorkspaceMemberRow`.
`loadWorkspaceMembers` **writes on read** (`:70`) — a "load" that mutates
storage — and deleting it removes that surprise with it. Two invented facts go
with it and **neither is back-filled client-side**:
`lastActiveAt: new Date().toISOString()` (`:62-68` — "last active = now",
always, for whoever is looking; the server has no such column, so
`MembersTable` **drops the column**) and the hardcoded self-row `role: "Admin"`
(`:65`), the same class of client-side inference as `roleLabel`.

**2. `routes/workspace/members/index.tsx` — the roster is a server read.**
`MembersRoute` takes `workspaceId` and `role` as props (see the interface
contract above) and calls `apiClient.getWorkspaceMembers(workspaceId)` on mount.
The optimistic append after a successful invite (`:67-75`) becomes a
**re-read**. As a list surface (ADR-018 `:112-119`): a **skeleton** while
loading, and **error + Retry** that **never** renders the last known roster.

**3. The invite result is the server's fact.** `InvitePane`'s `sent` prop
(`InvitePane.tsx:17-18`) becomes the server's `mailDelivered` boolean; the
client **never** infers "sent" from a 201 (`index.tsx:78` sets `sent`
unconditionally today). Two strings, and only two:
- `true` → **"Invitation sent to {email}."** — the export's sentence plus the
  address, so a typo is catchable at the moment it is made.
- `false` → **"Invitation ready for {email}."** with the copyable single-use
  link, its expiry, and a **Copy link** button. The word "sent" appears
  **nowhere on the screen**. This copy is deliberately true for **both** of its
  causes (no transport configured, transport errored) and **never diagnoses the
  mailer**.
- **The sweep must catch `index.tsx:63` as well** — the *failure* path also
  claims a mail ("The invitation could not be sent."), not only
  `INVITATION_SENT_MESSAGE` (`memberViewModel.ts:39`).
- Compose the copyable link with **`new URL(acceptUrl, window.location.origin)`**,
  which accepts both a site-relative and an absolute `acceptUrl` — so **no client
  change is needed** when the transport wave makes it absolute.
- Do **not** add a third string or a `delivery` discriminator. The transport wave
  needs a **third value, not a second boolean**, and its failure copy is already
  pre-decided ("Invitation created, but the email could not be sent." +
  **"Try sending again"**, and **never** "invite again").

**4. The domain rule is demoted from a block to a warning.** A mismatch renders a
non-blocking warning under the field — **"{email} is outside {domain}. They will
get full {role} access to this workspace."** — with **submit enabled**. Format
errors (`memberViewModel.ts:71-72`) stay **blocking**: a malformed address is not
an address. Keep `validateInviteEmail` (`:69-82`) and `formatDomainError`
(`:58-61`) as the *source of the warning*, and comment that the "workspace
domain" is a proxy read off whoever is signed in (`index.tsx:45`, gap named at
`memberViewModel.ts:64-67`) — a **typo guard, never a safeguard**, since the
server has no domain rule and a direct API call bypasses it entirely.

**5. Two destructive affordances, not one.** A fourth **`Actions`** column the
export does not have (`markup.html:388` is three columns), Admin-only and gated
on the **server** role:
- an `Invited` row is **revoked** → `apiClient.revokeInvitation(workspaceId, id)`
- an `Active` row is **removed** → `apiClient.removeMember(workspaceId, membershipId)`

They take **different consequence copy** because they are different facts:
revoke says **"Their link stops working. They never had access to this
workspace."** and must **not** say "they lose access" — an invitation was never
a grant, and telling an Admin otherwise misrepresents what they just did.
Confirmation is **inline in the row**, never a dialog (`--shadow-*` is "dialogs
only", ADR-019 `:78`, and the locked catalogue has no dialog). The
self-removal variant reuses the **"You"** marker the table already renders
(`MembersTable.tsx:35,40`). Each action **re-reads the roster**; there is **no
optimistic local removal**, because the last-Admin 409 is a server rule.

**6. The last Admin's Remove is a visibly disabled control with a `.hint`** —
**"This is the last Workspace Admin. Invite another Workspace Admin first."** —
never a hidden control and never a click that 409s. **"Invite", not "promote"**:
no role-change affordance exists in w14.

**7. Status set is `Active` · `Invited` · `Expired`.** `MemberStatus` moves into
the generated schema type; `getMemberStatusTag` (`memberViewModel.ts:91-93`) is
a binary ternary today and gains `Expired`, *derived* from this system's
existing "needs your decision" treatment — **never a new colour**.
`memberRoleLabel` (`:84-88`) is pure and survives untouched.

**8. The Procurement variant.** A non-Admin member sees the roster
**read-only**: no `Actions` column, no invite pane, and `Request access` is a
real **`mailto:`** to the workspace's now-knowable Admin addresses (from the
roster this screen just read). **A dead button is the one option not
available.** This resolves a contradiction *inside* the design oracle rather
than overriding it: ADR-018 `:107-108` and `ia-v2.md:13-15` say read-only,
`markup.html:403` hides the grid outright, and ADR-018's w14 design footer rules
for read-only.

**9. Leave these exactly as they are**: `MEMBERS_TIP`
(`memberViewModel.ts:36`) and its gate (`index.tsx:101-105`,
`shell?.kbReady === false`) — do **not** "improve" it into an always-on banner;
the **email** as the row's primary line (`MembersTable.tsx:39`) and the reason a
name is never derived from it (`:13-16`); the **D8 / R-WEB-07** role summaries
(`memberViewModel.ts:30-33`) — **not** `markup.html:395`'s "Also uploads,
deletes, manages members", which would regress an accepted decision; and the
role order with Procurement first (`memberViewModel.ts:16`).

## Parent story AC covered

- AC-1 … AC-11

## Files to create or modify

| Path | Change |
|------|--------|
| `web/src/routes/workspace/members/index.tsx` | consume `workspaceId` + `role` props; roster from `getWorkspaceMembers`; re-read after invite / revoke / remove; skeleton + error/Retry; the failure copy at `:63` stops claiming a mail |
| `web/src/routes/workspace/members/MembersTable.tsx` | drop the `lastActiveAt` column; add the Admin-only `Actions` column with inline confirmation; the disabled last-Admin Remove + `.hint`; the read-only Procurement variant with a real `mailto:` |
| `web/src/routes/workspace/members/InvitePane.tsx` | the `sent` prop becomes the server's `mailDelivered`; the two strings; the copyable link + expiry + Copy button; the non-blocking domain warning |
| `web/src/routes/workspace/members/memberViewModel.ts` | `MemberStatus` from the generated schema; `getMemberStatusTag` gains `Expired`; the two outcome strings; `validateInviteEmail` becomes warning-vs-blocking |
| `web/src/routes/workspace/members/memberStore.ts` | **deleted** |
| `web/src/routes/workspace/members/members.test.tsx` | new/extended — the ACs below |

## Context the implementer needs

- **Design oracle**: `inputs/design/prototypes/raffa-v2/screens-v2.md` §10
  (`:147-154`). **Anchors**: `markup.html:388` (the three-column table this task
  gives a fourth column), `markup.html:394-395` (invite roles, Procurement
  first), `markup.html:398` (the domain sentence, now a warning),
  `markup.html:399` + `app.jsx:170` ("Invitation sent." **unconditional — the
  defect, not the target**; the export sets it from a regex with no request at
  all), `markup.html:403` gated at `:384` (the non-Admin state),
  `markup.html:385` (`kbOff`, the tip), `app.jsx:133-134` (`status: Invited`,
  `tag-accent`).
- **Two anchors in the record's own design-ref table are superseded and must not
  be copied**: `name · email · role · status` (the backend stores **no** member
  name) and "Also uploads, deletes, manages members" (superseded by **D8 /
  R-WEB-07**).
- `web/package.json` has **no `lint` or `typecheck` script**: type checking is
  folded into `build` (`generate:api && tsc --noEmit && vite build`).
- The client sends **`X-User-Id` only** on `/api/workspaces/{tenantId}/members`
  and its DELETEs — the route already carries the tenant, and headers are
  attached per method in `client.ts`, never by a global wrapper.
- **N3's e2e** (invite → `page.reload()` → the roster still lists the invitee)
  belongs to `E14/F06/US01/T01`; `day1.spec.ts:134-147` already invites and
  asserts the email (`:146`) and "Invited" (`:147`), and **both pass on
  `sessionStorage` alone today — which is the defect**. The new assertion is one
  line longer and completely different in meaning: `await page.reload()` between
  the click (`:145`) and the assertions.
- **Do not touch**: `App.tsx`, `web/src/components/shell/**`,
  `web/src/routes/signin/**`, `web/src/routes/invite/**`, `web/e2e/**`, the three
  API-contract files, `backend/**`, `infra/**`, `.github/**`.

## Definition of done

- [ ] `cd web && npm ci` exits 0
- [ ] `cd web && npm run build` exits 0
- [ ] `cd web && npm test` exits 0
- [ ] `rg -n "memberStore" web/src` returns nothing
- [ ] `rg -n "lastActiveAt|INVITATION_SENT_MESSAGE" web/src` returns nothing
- [ ] `rg -ni "sent" web/src/routes/workspace/members` shows the word only in the
      `mailDelivered === true` branch
- [ ] `rg -n "sessionStorage|localStorage" web/src/routes/workspace/members`
      returns nothing

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit (vitest) | the roster renders from `getWorkspaceMembers`; invite triggers a **re-read**, not an append | `web/src/routes/workspace/members/members.test.tsx` |
| unit (vitest) | `mailDelivered: false` renders "Invitation ready for {email}." with the copyable link and the word "sent" nowhere; `true` renders "Invitation sent to {email}." | `web/src/routes/workspace/members/members.test.tsx` |
| unit (vitest) | a cross-domain address warns and leaves submit **enabled**; a malformed address still blocks | `web/src/routes/workspace/members/members.test.tsx` |
| unit (vitest) | revoke and remove render **different** consequence copy; both confirm inline; neither removes the row optimistically | `web/src/routes/workspace/members/members.test.tsx` |
| unit (vitest) | the last Admin's Remove is **disabled with a `.hint`**, not hidden and not clickable | `web/src/routes/workspace/members/members.test.tsx` |
| unit (vitest) | a Procurement member sees the roster read-only, no `Actions`, no invite pane, and a real `mailto:` Request access | `web/src/routes/workspace/members/members.test.tsx` |
| unit (vitest) | skeleton while loading; the error state renders Retry and **not** the last known roster | `web/src/routes/workspace/members/members.test.tsx` |

## Open questions blocking this task

- none. OQ-w14-005 is answered at the table (any live member reads; only an Admin
  invites, revokes or removes; the last Admin cannot be removed).

## Wave-spec entry
```yaml
- id: E15/F02/US01/T01
  prompt: reports/workitems/epic-15-workspace-invitations/feature-02-invitation-web/us-01-members-and-invite-ui/tasks/task-01-web-members-and-invites.md
  produces: [web-members-and-invites]
  depends_on: [workspace-roster-api, invitation-lifecycle-api]
  effort: L
  layer: frontend
  status: live
```
