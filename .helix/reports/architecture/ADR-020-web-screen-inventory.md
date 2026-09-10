# ADR-020 — Web screen inventory mapped to spec §16 (R0–R4) and §20

- **Status**: accepted
- **Date**: 2026-09-04
- **Deciders**: ux-ui-designer (draft), product-owner (concur), council-close
- **Locked citations**: "Every spec §16 row and §20 Day-1 step has at least one web story — 'API exists' is not enough" (brief §6); product-spec §16 (§ delivery ladder) and §20 (Definition of V1 done), quoted in web-integration-mandate §6.

## Context and problem statement

The pass exists because E02–E05 were decomposed `layer: backend` and no screen
was authored (brief §1). Decomposition is only allowed to finish when "every
spec §16 row has a screen (or a named non-goal against spec §1.2) and every §20
Day-1 step is reachable in the browser" (web-integration-mandate §6). This ADR
is that proof: a 1:1 screen inventory mapping every §16 definition-of-success
and every §20 definition-of-done step to a concrete screen in the Claude Design
prototype, with its empty/error/loading states in scope and any non-goal named.

The inventory's canonical form is `inputs/design/prototypes/screens.md`; the
clickable implementation is `inputs/design/prototypes/day1-demo.html`. This ADR
adopts that inventory and makes the mapping decomposable.

## Decision drivers

- **Closure of the "API-only" gap** — each §16 success criterion must resolve to
  a screen a user reaches, not an endpoint.
- **Traceability** — the decomposer needs one table that says "§16 R2 → routes
  /renewals, /contracts/:id (Renewal tab)".
- **No screen for a screen's sake** — anything that is genuinely out of V1 scope
  (spec §1.2) is named a non-goal rather than a phantom screen.

## Considered options

1. **One inventory ADR that is the §16/§20 traceability matrix** (chosen).
2. **Separate per-release ADRs** (R0–R4 each their own inventory).
3. **Fold the inventory into the IA ADR only**, without a §16/§20 traceability
   table.

## Decision outcome

**Chosen: Option 1** — a single screen-inventory ADR carrying the full §16 and
§20 traceability matrix, referencing the ten screens in `screens.md`. One
table keeps the "no success criterion without a screen" proof in one place for
the decomposer and the reviewer.

### Consequences

- **Good**: one source of truth for the decomposer and the day-after reviewer;
  explicitly closes the §16/§20 coverage question by naming a screen per row.
- **Bad**: the matrix is compact and must be re-checked if the prototype's
  screens change; it is traceability, not a pixel spec (the pixels live in the
  prototype files it cites).
- **Neutral**: empty/error/loading states are called in-scope but their exact
  copy lives in the prototype, not this ADR.

## Screen inventory (from screens.md)

1. **Sign-in → workspace** — R0.
2. **Members & roles** — R0 (invite; admin vs procurement).
3. **Upload → document status** — R0/R1 (processing, needs_review, failed).
4. **Portfolio** — R1 (columns, filters, attention strip).
5. **Contract 360** — R1 (header + 10 tabs).
6. **Review / correction** — R1 (confidence thresholds, evidence pane).
7. **Ask Raffa** — R1 (chat, citations, abstain).
8. **Renewal pipeline** — R2 (threshold strip, table, insight card + actions).
9. **Home / Savings** — R3 (6 KPIs, opportunities table).
10. **Quote check** — R4 (Extract → Assessment → Target → Negotiation).

## §16 traceability

| Release | Definition of success | Screen(s) |
|---|---|---|
| R0 — Foundation | Secure workspace can ingest documents | 1, 2, 3 |
| R1 — Contract Intelligence | Upload contracts + ask reliable questions | 3, 4, 5, 6, 7 |
| R2 — Renewals | Procurement doesn't miss material renewal windows | 8, 5 (Renewal tab), 4 (attention strip) |
| R3 — Savings | Quantifies credible savings opportunities | 9, 5 (Benchmark tab) |
| R4 — Quote Check | New proposal assessed in minutes | 10 (→ 9 outcome) |

## §20 traceability

| Definition of V1 done | Screen(s) |
|---|---|
| Create workspace + invite Procurement | 1, 2 |
| Upload portfolio; classify/extract/structure | 3 |
| Reliable questions with source evidence | 7 (→ 5 Clauses) |
| Renewal + cancellation deadlines | 4, 5, 8 |
| Contract/commercial risks | 5 (Risks, Overview) |
| Market benchmarks where available | 5 (Benchmark) + 10 |
| Prioritized savings opportunities | 9 |
| Quote → line-level assessment → target → strategy | 10 |
| Record outcome, track realized savings | 10 → 9 |

Every row resolves to a screen; **no §16/§20 step is a named non-goal** — the
entire Day-1 ladder is in scope and has a screen. Non-goals (spec §1.2) are the
platform-wide exclusions already carried in BACKLOG.md (CLM authoring,
e-signature, PO/invoice, supplier onboarding, sourcing/RFP, ERP replacement,
autonomous supplier comms, enterprise approval orchestration); none of them
maps to a §16/§20 success criterion, so none needs a screen.

## Empty / error / loading — in scope on the Day-1 path

Spec §7.1 encodes needs_review and failed (password-protected PDF) as
**first-class statuses, not edge cases**. Screens 3, 4, 7, 8, 9 must ship their
empty/error/loading states per ADR-018/019. Specifically named in the
prototype: upload failed + retry; portfolio no-match-for-filter and 503+retry;
Ask abstain and unknown-question fallback; renewal engine-unavailable; savings
benchmark-provider-unreachable with KPIs stale-labelled.

## Implications for the decomposition

- The decomposer must produce ≥1 `layer: web` story per screen above (screen 3
  may be two: upload UI + document-status read-back), each citing
  `inputs/design/prototypes/day1-demo.html` and `screens.md`.
- The final web-wave integration story walks the full Day-1 path (§20) in a
  browser on `demo`, matching the prototype — not `dotnet test`, not a Swagger
  page.
- Review/correct (screen 6) is a required screen, not optimisable away, because
  §7.3 (<80% require review) is a §16 R1 definition-of-success component.

## Assumptions

- The ten-screen prototype is complete enough to be the pixel reference for all
  of §20; any screen the implementer finds missing is a defect to raise, not a
  silent gap to fill with a divergent UI. (The export is 361KB and screen-complete
  per screens.md.)

## Amendment (2026-09-08, epic-12 / ADR-023)

Screen 7 (Ask Raffa) keeps the prototype chrome (ADR-019) but the **reply
body** is the savings-copilot contract in ADR-023: prose + inline `[n]` +
citation card with first-page preview + at least one in-app action. The red
“Cannot determine reliably” block is not the only UX for greetings or
off-domain questions — those are warm redirects. True insufficient-evidence
turns still abstain (spec §10.4). See ADR-023.

## Amendment (2026-09-08, epic-13 / ADR-024)

Screen inventory V2 is `inputs/design/prototypes/raffa-v2/screens-v2.md`
(authored from `Raffa V2 Prototype.html`): 1 Sign-in → Ask, 2 Ask Raffa
(home; off / new chat / conversation / abstain / redirect / refusal /
resumed), 3 Documents (onboarding, multi-file, **Not added**, attention
filter, real stages, validated hook), 4 Review as a state of Documents,
5 Contract 360 (citation landing with highlighted clause; answers band and
tracker as the P2 follow-up), 6 Portfolio, 7 Renewals, 8 Savings
(`/savings`), 9 Quote check, 10 Workspace & members. Every §16 row and §20
step still resolves to a screen (traceability table in `screens-v2.md`).
The reply body on screen 2 is the ADR-024 contract: markdown, human
citation cards with corpus badge and preview, in-app actions; the red
abstain block only for true insufficiency. This footer supersedes the
epic-12 amendment above. See ADR-024.

## Amendment (2026-09-10, wave w14 — screens 1 and 10 gain their real states; screen 11 is the invitation accept flow)

Serves **NW-24** and **NW-58**; carries the copy half of **NW-09** and the
screen-1 surface of **NW-14**. Written by ux-ui-designer, owner of this ADR
(`INDEX.md:53`). This footer **adds to** the epic-13 V2 inventory above and does
**not** supersede it: screens 2–9 are untouched.

**Numbering — read this first.** Every "screen N" below is the **ADR-024 V2
numbering** of the epic-13 footer at `:135-149` (1 Sign-in → Ask … 10 Workspace
& members), **not** the `## Screen inventory` body at `:57-66`, where 2 is
"Members & roles". The body is unchanged and remains the R0 record. **Screen 11
is the next free number in the V2 list.**

Pixel oracle: `inputs/design/prototypes/raffa-v2/` — `screens-v2.md`,
`ia-v2.md`, `markup.html`, `app.jsx` — the newest export and the one the w14
requirements name. Where the export and the requirements differ the
**requirements win and the difference is named**, per `ia-v2.md:125-136`.

### Screen 1 — Sign-in → workspace

**1.1 Create form: three fields, the export's copy.** `markup.html:52-57` is
`Create your workspace` / "A workspace is your tenant. Contracts uploaded here
never leave it." / **Company** · **Industry** · **Country** / `Create
workspace`. Adopted whole, in that order, replacing today's single field
labelled "Workspace name" (`WorkspacePickerScreen.tsx:189-212`, label `:192`).

- The **label** is "Company"; the **field** is `name` (ADR-001 w14 clause 5,
  ADR-003 w14). A task must not rename the field to match the label.
- The tenancy sentence at `:53` is kept. It is the only place in the product
  that tells a user their contracts stay in their tenant, and it is the copy
  that earns the first upload.
- **Country is required** (it derives currency); **Industry is optional**.
- Both lists ship **verbatim from the export**: Industry is exactly five
  options ending in `Other` (`markup.html:55`) — `Other` **is** the "not
  specified" case, so no empty sentinel option is added; Country is exactly
  Switzerland · Italy · Germany · Austria (`:56`).

**1.2 Currency is shown, never asked.** The form asks Country and never asks
currency, yet the pick row prints `CHF` (`markup.html:63`) — the only consistent
reading is derivation, and over the export's own four-country list the
derivation is **total**, so w14 needs no "unknown country" branch and no
currency control. The derived value is echoed under the Country select as a
`.micro-meta` line — **"Amounts are shown in CHF."** — so a wrong guess is
catchable *before* the workspace exists. A workspace currency is a **display
default and never overrides a contract's own extracted currency** (ADR-001 w14
clause 6).

**1.3 The pick-row meta line is a segment list that degrades.** Anchor
`markup.html:63`, `{{ seededCount }} validated contracts · CHF · eu-west`.
Render up to three segments joined by ` · `; **a null segment is dropped**,
never rendered as an empty gap, a dash or a placeholder:

| # | Segment | Copy | When null |
|---|---|---|---|
| 1 | count | `N validated contracts` / `1 validated contract` / `No validated contracts yet` | never null |
| 2 | currency | the ISO code — `CHF` | omit the segment |
| 3 | region | the **country name** — `Switzerland` | omit the segment |

Today the row renders **two** segments: `WorkspacePickerScreen.tsx:173-174`
prints the count and appends `currencyRegion` as one pre-joined string.
Splitting it into two real fields is part of this item, not a refactor a task
may skip. Singular/plural is not a new pattern — `app.jsx:160` already
implements it for `askScope`; w14 applies the export's own rule to a second
surface, because `app.jsx:138` hardcodes `seededCount: 3` and therefore never
exercises 0 or 1, while **every pre-existing workspace on `dev` will render the
zero form on day one**.

**1.4 The role tag renders the server's role.** `markup.html:62` is a two-cell
grid whose second cell (`:64`) is `<span class="tag tag-accent">{{ roleLabel
}}</span>`, already implemented at `WorkspacePickerScreen.tsx:177`. It is a
**frozen field**: `:83` hardcodes `roleLabel: "Workspace Admin"` and
`workspaceStore.ts:56-64` types it as that **string literal**, so the type system
itself asserts "every workspace you can see is one where you are Admin". The
moment NW-58 lands, an invited Procurement member signs in and screen 1 tells
them they are a Workspace Admin — a false claim on the product's first screen
after sign-in, produced by this wave's own feature. The tag therefore renders
the **server role from NW-01's row**, using the existing vocabulary (ADR-019 w14
clause 5) with **pass-through for unmodelled roles**; no new string is invented.

**1.5 State inventory.** `screens-v2.md:21` lists idle · signing · create ·
pick. w14 makes screen 1 the product's **first network read** and the inventory
grows accordingly, per ADR-018 `:112-119`:

| State | Copy / treatment |
|---|---|
| loading | skeleton rows in the workspace list |
| error | 2px accent left rule + h4 + the plain endpoint name + secondary **Retry**. **Never a cached list** |
| empty | the create form *is* the empty state — no separate "you have no workspaces" screen |
| resolving (app gate) | the shell's own resolving state, which **must not flash the sign-in screen** — that reads as a logout on every reload |
| interstitial | **an unrecorded divergence, recorded here**: `WorkspacePickerScreen.tsx:100-137` renders "You're in {name}" with Continue / Switch workspace / Sign out. The export has no such state — `app.jsx:139` is `enterWs:()=>this.go('ask')`, straight to Ask. Whether it survives for a single-membership user is **NW-03's routing rule (client-architect, ADR-018 `:125-126`)**; this inventory records the real state set either way |

**1.6 "Validated contracts" — one number, five surfaces.** `:173` renders the
literal `contracts`, dropping "validated"; the string is fixed in the same task
that makes the number real, or the picker claims a portfolio the user does not
have. One server field feeds **five** surfaces: this pick row, screen 2's
`askScope` (`app.jsx:160`), screen 6's `pfSummary` (`screens-v2.md:118-120`) and
the rail's **Portfolio** and **Renewals** badges (`ia-v2.md:30-31`, both defined
as "number of validated contracts"). **The rail's Documents badge is a different
number** — `N to review` / `N docs` (`ia-v2.md:25`) counts *documents* — and no
task may force it to agree; that surface is NW-10, W16. Copy rule everywhere:
**"validated contracts", never "knowledge base"** (the `kb*` identifiers in
`web/src` are internal symbols and no rename is asked for).

### Screen 10 — Workspace & members

**10.1 The invite result is the server's fact, in two strings.** Today
`memberViewModel.ts:39` is `INVITATION_SENT_MESSAGE = "Invitation sent."`,
rendered at `InvitePane.tsx:101-105` whenever `index.tsx:78` sets `sent` on a
201 — a claim about a **mail**, made from a **database write**. (The export is
weaker still: `app.jsx:170` sets `inviteSent` from a regex, with no request at
all.) The pane now renders the outcome the server reports in `mailDelivered`
(ADR-026 §D6):

| `mailDelivered` | Copy | Extra affordance |
|---|---|---|
| `true` | **"Invitation sent to {email}."** | — |
| `false` | **"Invitation ready for {email}."** + `.micro-meta` "Send them this link. It expires {date} and can be used once." | the accept link in a readonly `.input` + `.btn-secondary` **"Copy link"** |

The `true` copy keeps the export's sentence and adds the address, so a typo is
catchable at the moment it is made. In the `false` state the word "sent" appears
**nowhere on the screen** — including in the failure path, where `index.tsx:63`
today says "The invitation could not be sent."; a sweep of this pane must catch
that string too.

**The `false` copy is deliberately written to be true for both of its causes.**
One boolean cannot distinguish "no transport is configured" from "the transport
errored", and in w14 the question is moot — `mailDelivered` is `false` by
construction until a transport ships — but the copy must not become a lie the
day one does. It therefore says what is certainly true (the invitation exists,
here is the link) and never diagnoses the mailer. **Pre-decided for the
transport wave, so it is not re-opened**: if that wave wants to tell an Admin
that delivery *failed* — "Invitation created, but the email could not be sent."
plus a **"Try sending again"** secondary — it needs a **third value, not a
second boolean**, and the copy is settled here. That copy must **not** tell the
Admin to invite again: the invitation already exists, and re-inviting the same
email at the same role is exactly what fails today
(`WorkspaceMembershipService.cs:93-96`).

**10.2 The workspace-domain rule is a warning, not a gate.**
`screens-v2.md:150` states "email must match the workspace domain", and
`validateInviteEmail` (`memberViewModel.ts:69-82`) enforces it **client-side,
before any request**, using the signed-in Admin's own email domain as the
tenant's (`index.tsx:45`; the code names this proxy as a gap at `:64-67`).
**ADR-001's w14 footer defers the restriction to the wave that lands ADR-010**,
so in w14 it must not block. It is not deleted either — the design rule's intent
is worth keeping — it is **demoted to a non-blocking warning** beneath the email
field:

> {email} is outside {domain}. They will get full {role} access to this
> workspace.

The submit stays enabled. Format errors (`:71-72`, "An email is required." /
"Enter a valid email address.") remain **blocking** and are unchanged — a
malformed address is not an address. The distinction is the point: a
**client-side check is a typo guard, never a safeguard** (the server has no
domain rule; a direct API call bypasses it entirely), and a proxy that reads the
tenant's domain off whoever happens to be signed in is far too imprecise to
**gate** an invitation while being perfectly adequate to **inform** one. This
also unblocks NW-58's own acceptance, which a hard block would make unverifiable
whenever the second test account sits on another domain.

**10.3 Status set: `Active` · `Invited` · `Expired`.** Treatments are ADR-019's
w14 clause 1. Two anchors for the decomposer: `getMemberStatusTag`
(`memberViewModel.ts:91-93`) is a **binary ternary** and `MemberStatus`
(`memberStore.ts:14`) a **binary union** — neither can express a third case, and
`memberStore.ts` is deleted by NW-04, so the union moves rather than being
edited in place. An `Expired` row carries the row action **"Send a new
invitation"**, and the row needs a date to be actionable. **A removed member is
not a row**: this table answers "who can get in", not "who ever could"; the
audit trail is `/api/audit` (NW-08, W15).

**10.4 Two destructive affordances, not one.** An `Invited` row is **revoked**
and an `Active` row is **removed** — different endpoints, different consequences,
and therefore different copy. Both are Admin-only, rendered in a fourth
`Actions` column that the export does not have (`markup.html:388` is three
columns: Member · Role · Status), as `.btn-ghost` per row, gated on the
**server** role and never on a client-declared one. Both confirm **inline in the
row** (ADR-019 w14 clause 4), two buttons plus one consequence line:

| Action | Question | Consequence line |
|---|---|---|
| revoke (`Invited`) | **Revoke the invitation for {email}?** | Their link stops working. They never had access to this workspace. |
| remove (`Active`) | **Remove {email}?** | They lose access immediately. To bring them back you will need to send a new invitation. |
| remove (self) | **Remove yourself from {workspace}?** | You will lose access immediately and will need a new invitation to return. |

Revoke must **not** say "they lose access" — an invitation was never a grant
(ADR-025), and telling an Admin otherwise misrepresents what they just did. The
self case needs no new data: the table already marks your own row "You"
(`MembersTable.tsx:35,40`); on success the app returns to `/signin`, because it
must not sit in a shell for a tenant the user no longer belongs to.

**10.5 The last Admin's `Remove` is a visible disabled control.** Per ADR-019
w14 clause 2, with the `.hint`:

> This is the last Workspace Admin. Invite another Workspace Admin first.

"Invite", not "promote" — there is no role-change affordance in w14 and the copy
must not point at one.

**10.6 The member row shows an email, and the role summaries are D8's.** Two
traps of the same shape:

- **There is no member name.** `MembersTable.tsx:39` renders the **email** as
  the primary line and `:13-16` documents why — the backend stores no name for a
  member, so the email is primary and **never a name derived from the address**.
  A task "aligning the table to `name · email · role · status`" would fabricate
  a name out of the local part. The primary line **stays the email**; the only
  secondary line is the honest "You". NW-04's payload may carry `name?`, and the
  row renders it **only when the server actually has one**.
- **The Admin role summary is not the export's sentence.**
  `memberViewModel.ts:30-33` ships "Procurement: Asks, **uploads**, reviews,
  triages renewals" / "Admin: Also **deletes documents and manages members**",
  because decision **D8 / R-WEB-07** moved upload to Procurement (`:23-29`;
  `ia-v2.md:129`). `markup.html:395` still reads "Also uploads, deletes, manages
  members". **Copying the export's string regresses an accepted decision.**

Unchanged and safe to keep: role order and default (`memberViewModel.ts:16` —
Procurement first, least privilege by default, matching `markup.html:394-395`),
the tenant header line (`formatWorkspaceLine`, `:41-44`, which already refuses
the export's invented "HF-CH-001" of `markup.html:383`), and `MEMBERS_TIP`
(`:36` = `markup.html:385`) with its existing `kbReady === false` gate
(`index.tsx:101-105`) — no task should "improve" the tip into an always-on
banner.

**10.7 Procurement sees the roster, read-only.** See the ADR-018 w14 design
footer: `:107-108` already grants Procurement a read-only state on this surface
and the export contradicts it by hiding the table (`markup.html:403`, the whole
grid gated at `:384`). The roster renders **without the `Actions` column and
without the invite pane**, with the explanation block beneath it. The route
guard that makes this reachable is client-architect's.

**10.8 Loading and error.** The roster is a list surface, so ADR-018 `:112-119`
binds it: skeleton rows while loading, and 2px accent left rule + h4 + the plain
endpoint name + secondary **Retry** on failure — **never the last known roster**.
There is no empty state: you are always in your own roster.

### Screen 11 — Invitation accept (`/invite/accept`) — **new**

The route is added to the IA by the client-architect's ADR-018 w14 footer;
**this ADR owns its states and its copy**. Nothing resembling this screen exists
anywhere in the V2 export. It renders outside `AppShell` and is reachable signed
out.

| # | State | Copy | CTA |
|---|---|---|---|
| 1 | loading | skeleton rows | — |
| 2 | valid, signed out | kicker "Invitation" · h2 **"Join {workspace}"** · "You have been invited as {role}." | **"Continue with Microsoft Entra ID"** (`markup.html:44`, verbatim) |
| 3 | valid, signed in | the same block | **"Join {workspace}"** |
| 4 | accepting | the CTA is `disabled` with its label in progress | — |
| 5 | **no token present** | h2 **"Open your invitation link again"** · "Signing in takes you away from this page, so the invitation link has to be opened once more. It is still valid." | — |
| 6 | wrong signed-in account | "This invitation was sent to a different address. You are signed in as {current email}." | `.btn-secondary` "Sign out and use a different account" |
| 7 | expired | "This invitation has expired." + "Ask a Workspace Admin to send you a new one." | none |
| 8 | already accepted | "You have already joined {workspace}." | primary "Go to {workspace}" |
| 9 | revoked / unknown token | "This invitation is no longer valid." + the same "ask an Admin" sentence | none |
| 10 | transport error | 2px accent left rule + h4 + the plain endpoint name + secondary **Retry** | **Retry** |

Five of these are the reason the screen is specified here rather than left to a
task:

1. **State 5 is a normal outcome, not an error.** The token lives in memory for
   one mount and the address bar is cleared (ADR-025 Rule C10; ADR-012 w14),
   and MSAL is redirect-only — so a signed-out invitee who signs in **loses the
   token by design**, and so does anyone who reloads. The copy must therefore
   never say "invalid" or "expired": that sends a user back to their Admin for a
   replacement they do not need, and since invitations are single-use the
   Admin's "fix" costs a real one.
2. **State 6 must not echo the invited address.** ADR-025 makes the mismatch a
   403 "whose reason never echoes the address", and the pre-accept payload does
   not carry it — so the screen literally cannot render it, and must not be
   written as though it could. It is reached **after** an accept attempt, not
   before one.
3. **The pre-accept block shows the workspace name and the offered role, and
   nothing else** — no contract count, no member list, no supplier names. The
   designer's instinct is to add "3 validated contracts" for warmth; that would
   leak tenant data to anyone holding a URL.
4. **States 7 and 9 are worded identically in their first sentence** on purpose,
   so the screen never tells a probing visitor which case they hit.
5. **State 10 exists because ADR-018 `:112-119` requires it** of every surface:
   a token lookup that 503s is an error, while expired and revoked are terminal
   *informational* states that do not satisfy that contract.

On success the invitee lands on **`/ask` in that workspace** (`app.jsx:139`;
`ia-v2.md:46`) — and per 1.4, the pick row must not then tell them they are an
Admin. The signed-in-as kicker has an existing idiom to reuse rather than
invent: the accent `h6` "Signed in as {email}" of `markup.html:51,60`, already
implemented at `WorkspacePickerScreen.tsx:148`.

### Design exports still owed (ADR-020 `:121-124` — a defect to raise, not a silent gap)

None is blocking: every state above is decided from ADR-019's locked catalogue
and the V2 export's own idiom, so the wave proceeds. But the Claude Design
round-trip is lost until the operator exports (1) the accept screen in its ten
states; (2) screen 10's members table with the fourth `Actions` column, the
inline confirms and the last-Admin disabled state; (3) the invite pane in its
`mailDelivered: false` form — readonly link field, Copy link, expiry meta — and
the domain **warning**; (4) screen 10 in the Procurement read-only variant;
(5) screen 1's create form with the derived-currency line and the pick row in
its zero / singular / null-profile forms, including the role tag for a
non-Admin; and (6) a member row in the `Expired` status.

## Amendment (2026-09-10, wave w14 — the empty picker's sentence, and the one affordance re-issue has)

Serves **NW-01** (the picker's empty state) and **NW-58**. Written by
ux-ui-designer, owner of this ADR. This is the **second** w14 footer here; it
supersedes nothing in the first **except 10.3's row-action wording**, which
clause 2 below corrects and names. Screens 2–9 and screen 11's ten states are
untouched.

### 1. The picker's empty-list sentence — the copy, and the one word removed

Client-architect's ADR-012 w14 clause 3 (`:232-265`) traced a signed-out invitee
to the create-a-workspace form and handed this seat the copy. Adopted, with one
change. The string is:

> **Invited to a workspace? Open the invitation link your workspace admin
> shared with you.**

Not *"Open your invitation link **again**"*. Both reasons come from the same
property that makes the sentence safe:

- **"again" presumes a history this screen cannot have.** Per 1.5 above, *the
  create form **is** the empty state* — so this line sits under the create form
  of **every** user whose list is empty, including a first-time Admin on the
  pilot path who never held an invitation. The sentence is unconditional
  precisely because the picker must not know whether an invitation exists
  (`:422-425`); a screen that cannot know one exists cannot claim one was
  opened. On the accept screen the presumption is **earned** — state 5 is
  reachable only by someone who demonstrably held a token — which is why
  **state 5 keeps "again" and the picker does not**. The two strings differ on
  purpose: a task that reuses state 5's `h2` here ships the presumption.
- **"your invitation link" does not tell a stranded invitee where to look.** In
  w14 no mail leaves the system (`mailDelivered: false`, 10.1), so the link
  reached them because a **person** pasted it; naming the sender is the
  actionable half. "shared with you" is deliberately channel-neutral — it stays
  true the day a transport ships, and it never claims the product sent anything
  (ADR-001 w14 clause 3).

**Treatment**: a `.micro-meta` line beneath the create form's CTA — **not** a
`.hint`. ADR-019 w14 clause 2 reserves `.hint` for the visible reason on a
**gated control**, and nothing here is gated: this is a second path, not a
blocked one. One line of copy inside the NW-01 picker task, per client-architect.
No component, no token, no endpoint, no new task.

**A rule, because the sentence's safety is a property a later task can delete.**
It stays **unconditional**. The "improvement" it invites is to show it only to
users who actually hold an invitation — which requires the picker to ask the
server *"does this signed-in address hold one?"*, and that is an
invitation-probing oracle for any authenticated user: exactly what ADR-025
closes by wording states 7 and 9 identically (`:426-427`), and what
`GET /api/invites` refuses by demanding the token and returning "nothing else,
ever" (ADR-026 `:228-230`, `:244-246`). Showing it to everyone is both the
cheaper and the safer design; **conditioning it requires a leak.**

### 2. `Send a new invitation` has no legal path — and re-issue is one need, not two

**The defect is in 10.3 above, written by this seat.** An `Expired` row is given
the action "Send a new invitation". The accepted schema rejects it: ADR-026 D4
`:209-211` ships a partial unique index `(tenant_id, lower(email)) WHERE
accepted_at IS NULL AND revoked_at IS NULL`. That predicate excludes **accepted**
and **revoked** rows — and a lapsed invitation is neither (`accepted_at` and
`revoked_at` are both still null, `:203-204`), so it keeps occupying the slot and
the new insert violates the index. Widening the predicate to exclude expiry is
not available either: a partial-index predicate must be immutable, and "expired"
is a comparison against the clock (`expires_at`, "evaluated against `IClock`",
`:202`). The ADR's stated intent — *"so re-invites cannot accumulate valid
links"* — is about **valid** links and does not cover the lapsed case; the
predicate does. The one action an `Expired` row offers fails at the database.

**The same gap has a second face, and the pilot hits it first: an invitation
link cannot be shown twice.** `acceptUrl` appears on exactly one response in the
whole contract — the invite 201 (ADR-026 `:227`). `GET /api/invites` returns
`{ workspaceName, role, expiresAt }`, "nothing else, ever", and only to whoever
already holds the token (`:228-230`). Storage is a SHA-256 hash (ADR-025 Rule C2
`:214`, T10 `:635`). So the plaintext exists for the life of one HTTP response
and the invite pane that renders it: an Admin who navigates away from screen 10
can never see that link again and **the server cannot produce it** — by design,
and correctly. Nor can they re-invite: in w14 the live row holds the unique slot,
and `InviteAsync`'s own contract already "fails cleanly" on a repeat of the same
role today (`WorkspaceMembershipService.cs:52-54`).

So *"the invitee lost the link"* and *"the invitation lapsed"* are **one need —
re-issue** — and w14 models only **invite** and **revoke**. The recovery exists
(revoke, then invite) and **nothing on screen names it**; 10.1 above forbids the
failure copy from saying "invite again", which is right for that case and leaves
this one unspoken.

**Decision — one affordance, on both rows, meaning revoke-then-invite:**

| Row | Action | Confirm question | Consequence line |
|---|---|---|---|
| `Expired` | **Send a new invitation** | Send a new invitation to {email}? | They get a new link, valid until {date}. The expired one stays dead. |
| `Invited` | **Send a new invitation**, secondary to revoke | Replace the invitation for {email}? | **The link you already shared stops working.** They get a new one, valid until {date}. |

The `Invited` consequence line is why this is specified here rather than left to
a task: re-issuing against a **live** invitation silently kills a link that may
be sitting in someone's inbox, and the Admin must be told **before** the click.
Same rule as 10.4 — say what the person on the other end experiences.

**Prevention, in a string that already exists.** 10.1's `false`-state
`.micro-meta` becomes: **"Send them this link. It expires {date}, can be used
once, and is not shown again."** Three facts, delivered at the only moment the
Admin holds the only copy. This replaces the two-fact version in 10.1.

**Mechanism is not this seat's.** Whether re-issue is DELETE-then-POST from the
client over the two endpoints ADR-026 D5 already defines (`:234`, `:226`) or one
server-side operation is software-architect's, and the audit trail of a replaced
invitation is security-architect's §G. It needs **no new endpoint** on the
accepted shape and **no new component** — it is 10.4's row-action + inline-confirm
pattern. If those seats instead widen the index, the screens above are unchanged;
only the number of calls behind one button moves.

### 3. What this footer does not change

No token, no component, no confidence row — ADR-019 needs no second footer, since
`.micro-meta` and `.hint` are used exactly as its w14 clause 3 already permits.
Screen 11 is untouched. **`waves/w14.md` is not edited**: its `:361` carries
10.3's action as this seat wrote it, and it is corrected here through the NW-58
row that already names ADR-020 as governing — the product-owner's principle,
which binds this seat too. **The decomposer must read this footer to build that
row.** The exports owed above gain one item: (7) the re-issue confirm on an
`Invited` and an `Expired` row.
