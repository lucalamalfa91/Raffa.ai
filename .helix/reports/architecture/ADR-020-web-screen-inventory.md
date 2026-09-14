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

## Amendment (2026-09-13, wave w15 — a refusal becomes a row, three surfaces learn to say "not ready yet", and the first surface Raffa sends outside the browser)

Serves **NW-27**, **NW-61**, **NW-69** and **NW-68**. Written by ux-ui-designer,
owner of this ADR (`INDEX.md:53`). Every section above and **both w14 footers
stay in force and are not rewritten**; where this footer touches copy they
pre-decided, it says so and quotes them. States named here implement ADR-018's
w15 clause 1; treatments come from ADR-019's locked catalogue and its w15 row.

### 0. One copy rule for the whole wave: the export is pre-rebrand, the product is not

The product names itself **"Raffa.ai"** in every shipped user-facing string —
`SignInScreen.tsx:52` (the lockup `h1`), `askViewModel.ts:232`/`:258`,
`contracts/index.tsx:57`, `renewals/index.tsx:69`, `savings/index.tsx:61`,
`quotes/index.tsx:103`, `QuoteLinesTable.tsx:18`, `uploadPipeline.ts:42`. The V2
export says **"Raffa"** throughout (`screens-v2.md:26`, `:73`). This is not one
stale sentence, it is a **class**: `inputs/design/prototypes/raffa-v2/` predates
the rebrand, so **any task that retypes copy out of the export silently reverts
it**. Rule, binding every string below and every string a w15 task moves: **copy
already in `web/src` is moved character-for-character; copy taken from the export
is corrected to "Raffa.ai".** Named divergence, recorded once here instead of
six times.

### 1. Screen 3 (Documents) — a refused file is a row, not a card (NW-27)

**1.1 Why the session-only rule goes.** `screens-v2.md:75` marks the "Not added"
card *session-only, never counted*, and three shipped files assert that premise
by id (`documentTable.ts:74-79`, `UploadResultCard.tsx:14-16`,
`uploadPipeline.ts:57`), as does `requirements.md` **R-DOC-05 AC-1**. The premise
is stated by the code itself at `UploadResultCard.tsx:16` — *"a rejected file was
never stored, has no id"* — and **NW-27 removes exactly that premise**: with the
gate on the Worker the file **is** stored before the gate can run, and the
refusal arrives seconds or minutes later, **possibly with the browser closed**.
A card that vanishes on reload would then hide a durable server fact — the shape
ADR-012's w14 footer forbids — and would lose files against A15-1's own promise
that a dropped file survives a reload and a second browser. **Decision: a refused
file is a row with a terminal server status.** The supersession of R-DOC-05 AC-1
is **not this seat's to declare and was not declared here**: it is ratified by
product-owner (ADR-001 w15 clause 2) and recorded by software-architect
(ADR-024 w15 §3) — OQ-w15-D3. **What survives of the export's instinct is its
important half: *never counted*** (ADR-018 w15 clause 2).

**1.2 The wire value is `Rejected`; the screen says "Not added".** No new word
enters the product — the label and its treatment are lifted from the card being
retired (`UploadResultCard.tsx:24`), and the mapping is ADR-019's w15 row
(`.tag-outline`, and **not** `.tag-accent`, which is reserved for a Raffa-side
failure the user can retry). Two files, one of which the compiler will not ask
for: see ADR-019 w15 clause 2.

**1.3 The reason is the row's hint; the card is deleted.** The three refusal
sentences are requirement copy and move **verbatim** —
`uploadPipeline.ts:39-46` (`not_a_contract`, `no_readable_text`) and `:50-52`
(`getOversizedCopy`) — into the existing hint slot under the filename, the slot
`DocumentStatusTable.tsx:138` already uses. One deliberate edit: the
**`"Not added: "` lead-in is dropped**, because beside the tag the tag *is* the
lead-in; the lead-in is **embedded inside those two functions**
(`UploadResultCard.tsx:3-4` documents this), so dropping it is an edit to those
functions or to the server field that replaces them, never a render-time strip.
Per clause 0 the sentences keep "Raffa.ai". **`UploadResultCard.tsx` is deleted,
not kept beside the row**: `screens-v2.md:76` is "one row per file, per-file
outcome", and a card *and* a row for one file is two surfaces for one fact.

**1.4 A `Rejected` row offers no next step.** The action cell is empty
(`getRowAction`, `documentTable.ts:117-134`, already returns `null` for a
processing row). There is nothing to review, ask or retry — re-dropping the same
file would be refused again. The Admin's existing `Delete`
(`DocumentStatusTable.tsx:184-208`) clears it, which needs no new endpoint and no
new copy, and product-owner's NW-27 row requires exactly that the row stay
"removable by the existing `DELETE`". **No "Dismiss" affordance**: a durable row
cannot be dismissed client-side without a client store standing in for the
server.

**1.5 Where the row is reachable: a third filter chip.** `Not added · K`, reading
`counts.rejected`, rendered **only while that count is > 0**; hint **"Files
Raffa.ai did not keep — never counted, never askable."**; the `all` hint becomes
"Everything **Raffa.ai keeps**, including validated documents." The full counting
contract, and the withdrawal of this seat's lane §A4, are ADR-018 w15 clause 2 —
this is its copy half.

**1.6 `stage: null` reads "Queued…", and the code already agrees.** Today
`DocumentStatusTable.tsx:155` renders `(item.stage ?? "Uploading") + "…"`, which
is true only in a synchronous product where the row and the upload are one act.
Once `POST /api/documents` returns at `Uploaded`, the file **is stored** and the
row is waiting for a Worker — a window the export never had (`app.jsx:46-48`
ticks to stage 1 after 700 ms) and one A15-2 deliberately makes long. A local
pre-201 row still reads **"Uploading…"**; a server row at `Uploaded` reads
**"Queued…"**; a `Processing` row reads its stage string, verbatim. **The six
stage strings are untouched** (`screens-v2.md:65-66` = `app.jsx:76` =
`documentTable.ts:56-63` = `DocumentProcessingStageMap.cs:20-39`) and
`getStagePercent(null)` stays 0%. **This costs nothing in the design system**,
and `documentTable.ts:76-78` says why in its own words: "`Uploaded` and
`Processing` both fold into `processing` — the row grid does not distinguish
'queued' from 'actively processing' visually, **only the stage text underneath
the tag does that**." Named divergence: the export cannot answer this because it
has no queue wait. A row stuck at "Uploading…" for a minute is
indistinguishable from a stuck upload — the exact defect A15-2 exists to catch.

**1.7 Screen 3's state inventory** becomes: onboarding empty · **queued** ·
uploading/processing (real stages) · needs_review · completed · failed (retry) ·
**not added (a row, terminal)** · attention-empty · list error.

### 2. Screens 2, 5 and 6 — "still processing" and "nothing made it through" (NW-61)

ADR-018 w15 clause 1 defines the two states; this is their copy. **Four new
strings enter the product in this clause and none has an oracle**, because the
export has no such state anywhere — each is derived from a shipped sentence and
listed in the exports owed below.

**2.1 Screen 2 (Ask) — the copy is right, the *predicate* is wrong, and a third
variant is forced.** `buildOffCopy(hasAnyDocument: boolean)`
(`askViewModel.ts:250-261`) is a **boolean ternary**, and `hasAnyDocument` is
`totalCount > 0` over documents **in any state** (`ask/index.tsx:99-105`). So a
tenant whose only document **failed** is told *"Your document is still processing
or waiting for review"* — **a fabricated fact the product ships today**, and
A15-3's exact prohibition. The h3 (`screens-v2.md:25`, "Ask needs at least one
validated contract.") is true in all three cases and is unchanged.

| Shown when | Sentence | CTA |
|---|---|---|
| Raffa.ai holds **no** document | "Upload a contract first. Raffa.ai extracts the facts, you sign off the weak ones, and Ask switches on." — **shipped, unchanged** (`:258`) | **Upload a contract** |
| ≥1 document is `Uploaded` · `Processing` · `NeedsReview` | "Your document is still processing or waiting for review. Ask only answers from facts that passed validation — so it never guesses." — **shipped, unchanged** (`:254`) | **Go to Documents** |
| ≥1 held, **none in flight, none validated** | **"Raffa.ai could not finish processing your documents. Ask only answers from facts that passed validation — so it never guesses."** — *new*; its second clause is the shipped one, verbatim | **Go to Documents** |

This answers **OQ-w15-ca-04**, routed here by client-architect, and it honours
their binding constraint exactly: the third state is **not** collapsed into the
"still processing" sentence. **A tenant holding only refused files gets the first
row, not the third** — because ADR-018 w15 clause 2 excludes `Rejected` from what
Raffa.ai holds, so "Upload a contract first" is *true* for them, and they were
already told per file, on the row, why each was refused. That is the counting
rule doing real work rather than being bookkeeping. Fixed grammatical number is
the product's existing practice on this surface (the shipped sentence says "Your
document" for any count), so **no pluralisation logic is introduced**.
`buildOffCopy`'s signature widens from `boolean` to the three-way state — the
anchor a task must cite.

**2.2 Screen 6 (Portfolio) — a third variant and *no* new string.** Today one
zero state (`contracts/index.tsx:130-139`), verbatim from `markup.html`'s `kbOff`
and `screens-v2.md:116-117`. The h3 **"Nothing to triage yet"** is true in all
three cases and never changes.

| Shown when | Sentence | CTA |
|---|---|---|
| 0 validated, nothing held | "The portfolio lights up from validated contracts. Upload one to start." — shipped, unchanged | **Upload a contract** |
| 0 validated, ≥1 in flight | "Your documents are still being processed. The portfolio lights up from validated contracts." | **Go to Documents** |
| 0 validated, ≥1 held, none in flight | "The portfolio lights up from validated contracts." — **the shipped sentence minus its "Upload one to start." clause** | **Go to Documents** |

Dropping that clause is the whole fix: it is the one part of the sentence that
tells a user to do what they have already done (ADR-018 w15 clause 1, rule 2).

**2.3 Screen 5 (Contract 360) — a fifth state, because the alternative is an
empty contract.** `screens-v2.md:112` lists `open · in negotiation · assigned ·
not uploaded (empty)`; the code has `loading | not-found | error | ready`
(`contract360/index.tsx:21-25`, `:140-157`). Once NW-27 creates a `contractId`
before extraction finishes, `ready` renders an aggregate with no clauses, no
spend and no dates — an **empty contract**, indistinguishable from a contract
whose extraction genuinely found nothing. Both readings of ADR-018's new state
apply, driven by ADR-027 §D8's `readiness` and **never inferred from an empty
clause array**:

| `readiness` | h3 | Sentence | CTA |
|---|---|---|---|
| in flight | **"This contract is still being prepared."** | **"Raffa.ai is still extracting the facts. It will open here once they pass validation."** | `.btn-secondary` **Go to Documents** |
| terminal, nothing extracted | **"This contract has no validated facts yet."** | **"Raffa.ai could not finish processing its documents. Open Documents to see what happened to each one."** | `.btn-secondary` **Go to Documents** |

The second reading shares its opening clause with 2.1's new sentence by design —
one new idea, two surfaces. Two smaller consequences of the same rule: **Details ▾
› "documents in family"** (`screens-v2.md:103-104`) renders a not-yet-validated
document **with its row status tag** rather than omitting it silently — a family
that hides its own in-flight members is the same lie one level down — and the
back label and all other chrome are unchanged.

**2.4 What NW-61 does not change**: the optimistic local row, the stage
rendering, the progress bar, the six stage strings, the attention filter's
default, `justValidated`'s hook. The poll predicate, the request deadline and the
read-back contract are **client-architect's**.

### 3. Screen 10 — the invite pane's outcome set (NW-69), corrected against what the backend seats ruled

**3.1 One boolean cannot carry what NW-67 and NW-68 produce.** Today two states
from one flag (`InvitePane.tsx:116-120`, `:122-140`), typed as a binary union at
`memberViewModel.ts:171-173`, whose own doc comment reads *"not a third, invented
state; **the transport wave that needs a third value owns that change**, not this
one."* **This is that wave.** 10.1 already reserved the third value; 10.3 named
the same defect shape for `MemberStatus`.

**3.2 A provisioning failure is a *blocking* error, not a fourth success state —
and this corrects this seat's lane draft and client-architect's cell.** My lane
§D2 designed a state "Invitation created, but {email} cannot sign in yet" with
the link suppressed, and client-architect's NW-69 cell lists
"guest provisioning failed" as one of four 201 outcomes. **Neither can occur**:
software-architect's NW-67 row rules that a provisioning failure **aborts the
invitation — no invitation row, no mail, no token** — and returns **502**, and
security-architect concurs from the credential side ("an inert artefact beats a
live credential"). So there is nothing to show a link *for*. **My reasoning
reached "suppress the link"; their mechanism reached "there is no link", which is
strictly stronger, and this seat adopts it.** The pane's rule survives verbatim —
**a link renders only when it is usable** — and is now satisfied **by
construction** rather than by a branch. The 201's outcome enum therefore carries
**three** values, and 10.1 gains **one** new value, not two.

**3.3 The three success outcomes** (an invitation exists; the guest is
provisioned or provisioning is not configured):

| # | Outcome | Copy | Link block |
|---|---|---|---|
| 1 | mail **sent** | "Invitation sent to {email}." — 10.1, **unchanged** | no |
| 2 | mail **failed** | "Invitation created, but the email could not be sent." — **10.1's pre-decided third value, verbatim** (`:282-284`) | **yes** |
| 3 | **no transport configured** | "Invitation ready for {email}." — 10.1, **unchanged**, + the expiry `.micro-meta` (`memberViewModel.ts:194-196`) | yes |

The word **"sent"** appears in outcome 1 only. Blocking pre-creation failures
(format, 400, 409) are unchanged (`InvitePane.tsx:110-114`).

**3.4 "Try sending again" does not ship, and this ADR has already made this
ruling once.** 10.1 pre-decided the *sentence* for outcome 2 **and** a
`.btn-secondary` "Try sending again". The sentence ships; **the affordance does
not, because no mechanism can perform it.** Software-architect's NW-68 row
establishes that the server **cannot re-send the original link** — the token is
stored only as a SHA-256 hash — so any retry is necessarily a **re-issue**, which
mints a new token and kills the link the Admin is looking at; and this ADR's own
w14 footer `:284-287` already forbids telling the Admin to invite again from this
pane ("re-inviting the same email at the same role is exactly what fails today").
A button that silently invalidates a link the Admin may already have copied,
under a label that says "try again", would be misleading copy authored by this
seat. **The link block *is* the remedy** — and cloud-architect's deliverability
caveat (a managed domain sends from `…azurecomm.net`, which corporate filters
treat harshly) makes outcome 2 **common rather than rare**, which is exactly why
it must be fully functional. The only re-issue path stays the roster row's
**"Send a new invitation"** with its inline confirm and its "The link you already
shared stops working." consequence line (`:539-546`). This applies the precedent
of this footer's own predecessor, §2 above: *an affordance with no legal
mechanism is not shipped*. **This withdraws the fallback branch my lane §8.2 had
already corrected once, and closes it.**

**3.5 The blocking 502: copy for a closed reason set.** Software-architect's
closed set is `consent_missing` | `provisioning_failed` | `directory_unavailable`
and routes its copy here. Rendered in the existing pre-creation error slot
(`InvitePane.tsx:110-114`), each with the shared `.micro-meta` **"No invitation
was created."** so the Admin never has to wonder whether a half-invitation
exists:

| `reason` | Sentence |
|---|---|
| `consent_missing` | "Raffa.ai is not allowed to add guests to your company directory yet. A tenant administrator has to approve that permission." |
| `provisioning_failed` | "Your company directory would not add {email}. Check the address, or ask a tenant administrator." |
| `directory_unavailable` | "Your company directory could not be reached. Try again in a few minutes." |
| *anything else* | "Raffa.ai could not create this invitation." |

**The wire `reason` is never rendered** — the fourth row exists so that an
unrecognised value, a proxy-mangled body or a later addition to the set can never
put a raw enum on screen. **No new affordance**: the invite form is still filled
and is itself the retry, so nothing is added for a path where retrying may be
futile. Each sentence names **what someone must do next** (ADR-019 w14 clause 2),
and `consent_missing` says "a tenant administrator" rather than "you" because the
Admin of a Raffa.ai workspace is usually **not** the Entra tenant admin — telling
them to fix a permission they do not hold is worse than telling them nothing.

**3.6 One sentence about identity, on three surfaces, keyed to a server fact.**

> **"They will get a one-time code from Microsoft the first time they sign in."**

It renders as a `.micro-meta` on outcomes 1–3 **only while the 201's
`identityProvisioned` is true**, on screen 11 state 2 (`:397`), and in the second
person in the email (§4). With NW-67 the invitee's first sign-in triggers an
Entra **email one-time passcode** — a second, unexpected message from Microsoft —
and the raw file names that exact abandonment ("today the first signal is
Microsoft's error inside the popup, where Raffa cannot intervene"). It satisfies
security-architect's **no-directory-enumeration-oracle** rule, because it says
the identical thing whether the guest was created or already existed. When
provisioning is **not configured** it does not render at all: the pane then says
nothing about identity, which is w14's behaviour and leaks no configuration fact.
**There is deliberately no pre-send identity state** — with NW-67 the product
creates the account, so a warning that the invitee "has no account yet" would be
false the instant it rendered. The domain warning (10.2) is untouched.

**3.7 No new member status.** The roster keeps `Active · Invited · Expired`
(10.3, ADR-019 w14 clause 1). A fifth status is refused: the roster answers "who
can get in", and a delivery or provisioning outcome is a **pane fact about the
last action**, not a permanent property of a row.

**3.8 Vocabulary mapping.** Client-architect asked that if this seat's vocabulary
differs from the wire values, the mapping live in `memberViewModel.ts` and be
named here. It does not differ: three outcomes, one per row of 3.3, plus
`identityProvisioned` for 3.6. The pane branches on **one server string** and
renders **one server boolean**; it infers neither.

### 4. Surface 12 — the invitation email (NW-68)

**4.1 The pane's delivery copy is confirmed unchanged** — 10.1 pre-decided it
"so it is not re-opened" (`:280-287`) and this seat re-opens nothing. What
changes is the outcome *set* (§3), not the words. The absolute accept link needs
**no copy change**: `composeAcceptLink` (`memberViewModel.ts:175-179`) already
accepts a site-relative or an absolute `acceptUrl` unchanged, by design —
recorded so no task is written for it.

**4.2 The email has no design owner in any export, and that is how a task invents
one.** `inputs/design/` holds screens, `markup.html`, `styles.css` and `app.jsx`
— **nothing that leaves the browser**. Left to the implementing task, the first
message Raffa.ai ever sends to a person outside the tenant would be branded HTML
nobody reviewed. It is therefore **surface 12 of this inventory**: not a screen,
but a user-visible surface with a state set and an owner. (If a second message
ever ships, a later wave may lift it into its own ADR; one message does not earn
one.)

**Form.** **Plain text is the body of record**; an HTML part may be added but
must carry the same words in the same order and read correctly when stripped. One
column, left-aligned, **no images, no logo file, no web font, no external
stylesheet**; any colour is an inline token *value* (ADR-019 w15 clause 3c). The
accept link appears as a **plain visible absolute URL** as well as an anchor, so
a forwarded or text-only copy is still usable.

**Copy — deliberately the same words as the screen the link opens.**

> **Subject**: You've been invited to {workspace} on Raffa.ai
>
> **Join {workspace}**
>
> You have been invited as {role}.
>
> [ Open your invitation ]   ← the absolute accept link
>
> Signing in uses your email address — Microsoft will send you a one-time code.
>
> This link expires {date} and can be used once.
>
> If you were not expecting this, you can ignore this message.

Lines 2 and 3 are **screen 11 state 2 verbatim** (`:397`): the mail and the
landing page must say the same thing or the invitee thinks they clicked the wrong
link. Per clause 0 the subject carries **"Raffa.ai"**, the product's own name in
every shipped string.

**Three rules, each closing a real failure.** (1) **The mail carries the
workspace name, the offered role and the expiry — and nothing else.** No
validated-contract count, no member list, no supplier names: the leak guard of
`:422-425`, and stronger, because a mail can be forwarded by anyone, forever.
This is the copy half of security-architect's one-recipient rule. (2) **The
one-time-code line is not decoration** — it is the same sentence as 3.6, in the
second person, and it is what turns a suspicious second email into an expected
step. (3) **The mail never claims an account was created.** It says signing in
uses their email address; "we created an account for you" is both alarming and,
from the invitee's side, untrue — nothing exists for them to manage.

**Anchors**: `IInvitationMailer.cs:16-33`; `NullInvitationMailer.cs:16-38`, which
deliberately never interpolates the URL and whose `:28-29` comment ("the 201
response body is the link's **only** channel") **becomes false this wave and is
retired in the same edit**.

### 5. What this footer does not change, and what is still owed

Screens 1, 4, 7, 8, 9 and 11 (beyond 3.6's `.micro-meta`); both w14 footers;
10.2–10.8; the six stage strings; the confidence vocabulary; every error and
loading treatment. **Design exports owed** — `:438-449` and `:571` gain four, and
**none blocks w15**, because every state above is decided from ADR-019's locked
catalogue and the export's own idiom: (8) the invite pane in its **three** outcome
states plus the blocking-error state; (9) a Documents row reading **Not added**
with its reason hint and empty action cell, a row reading **Queued…**, and the
**third filter chip**; (10) Portfolio and Contract 360 in their two new zero
states; (11) **the invitation email**. The Claude Design round-trip stays lost
until the operator exports them.

## Amendment (2026-09-14, wave w15 — re-entry round: the refusals that never become rows, the reason the durable row has no wire for, and the copy for a poll that stops)

Continues the **2026-09-13 w15 footer** above (`:573-930`, sections 0–5), every
clause of which stays in force; section numbering continues from it. Serves
**NW-27** and **NW-61**. Written by ux-ui-designer, owner of this ADR
(`INDEX.md:53`). Two of the three sections below **correct this seat's own §1.3**
rather than a peer's; the third answers **OQ-w15-ca-05**, which client-architect
routed here at this round.

### 6. A refusal has four producers, three of them never become rows, and §1.3 sent all three sentences to a slot only one can reach

**6.1 The correction.** §1.3 moved "the three refusal sentences" verbatim into
"the existing hint slot ... `DocumentStatusTable.tsx:138`". That slot is inside
the **server-row** branch (`:112-145`). Read against `runUploadBatch` — the
finding is client-architect's (ADR-012 §13.4) and is re-verified here at source —
only one of the four producers ever has a server row:

| Producer | Site | Stored? | Surface |
|---|---|---|---|
| content gate `not_a_contract` / `no_readable_text` | `uploadPipeline.ts:117-120` (422); moves to the Worker under NW-27 | **yes** | server row, `Rejected`, hint at `DocumentStatusTable.tsx:138` |
| oversize, refused **in the browser with no HTTP call at all** | `:105-108` → `getOversizedCopy` (`:48-52`) | never | **local row** |
| **413 / 415**, refused in-request by product-owner's split gate | `:122-125` | never | **local row** |
| transport / server failure | `:127-132` | n/a | unchanged (`failed`, "Retry upload") |

**Decision: the local row is adopted, the card is still deleted, and §1.3's
routing splits accordingly.** `LocalUploadEntry.phase` (`uploadPipeline.ts:69-75`)
gains `"rejected"`, and the two pre-storage refusals render as a **local row** in
the grid that already renders local entries (`DocumentStatusTable.tsx:86-110`),
whose hint condition at `:90` widens from `phase === "failed" && errorMessage` to
any entry carrying a message. One file is then **one row on every path** — which
was §1.3's own stated ground for deleting the card, and it is only *true* once
these two have a row. Keeping the card for them was the alternative offered and
is **rejected**: it would leave screen 3 with a card *and* a row grid for the same
class of outcome, which is exactly what §1.3 removed, and the card's premise
doc-comment (`UploadResultCard.tsx:9-18`) is retired as a whole this wave.

**6.2 What the user reads, on both surfaces.** The tag is the same
`.tag-outline` **Not added** on the local row and on the server row (ADR-019 w15
round-3 clause 4) — deliberately **indistinguishable**, because the user's fact is
identical ("Raffa.ai did not keep this file") and the difference between the two
is ours, not theirs. §1.3's lead-in rule applies to both: the tag *is* the
lead-in, so `"Not added: "` is dropped at source in `getRejectionReasonCopy`
(`:39-46`) and `getOversizedCopy` (`:50-52`).

**One further edit, licensed by provenance rather than taste**: the oversize
sentence also loses its interpolated `${fileName}`, because the row's filename
cell (`:89`) already carries it — the card repeated it (`:25` beside `:30`) and a
row must not. It may be edited where the other two may not:
`getRejectionReasonCopy`'s two sentences are **requirements copy**
(`inputs/requirements.md:186` quotes the `not_a_contract` sentence), whereas the
oversize sentence is **app-authored** — the requirement fixes only the limit
("batch ≤ 20 files, ≤ 50 MB per file", `:148`) and `uploadPipeline.ts:48-49` says
so in its own comment. The row's hint becomes **"This file is larger than 50 MB,
the most Raffa.ai accepts."**

**6.3 The finding: one sentence on screen 3 is authored by the API, and it
survives this wave.** `:122-125` renders
`result.error ?? "Not added: this file could not be added."` — `result.error` is
whatever the API's error body happens to carry, and the fallback appears in no
oracle. It is the only user-facing sentence on this screen that nobody designed,
and it is not going away: product-owner's NW-27 row keeps **format/size
in-request**, so 413/415 is the one refusal path the Worker never sees. Two
designed sentences replace it, selected by status code:

| Code | The row's hint |
|---|---|
| **413** | **"This file is too large for Raffa.ai to accept. Try a smaller file, or split it into parts."** |
| **415** | **"Raffa.ai cannot open this file type. Try the original PDF, or a clear scan of the signed pages."** |

The 413 sentence deliberately **does not restate 50 MB**: the browser already
refuses at 50 MB (`isOversized`, `:105`), so a 413 is the platform refusing a file
the client thought was fine, and quoting a limit the client cannot vouch for is
the fabricated-fact class A15-3 forbids. The 415 sentence is derived from the
shipped `no_readable_text` sentence's second clause ("Try a clearer scan or the
original PDF", `:44`), so no new idiom enters the product. **`result.error` is
never rendered.** The rule this generalises is the one NW-67 already uses for a
failed invitation: **a server field may *select* the sentence a user reads; it may
never *supply* it.** One convention for the wave, not two.

**6.4 The local row's shape.** No next-step control (the `Uploading…`
`.micro-meta` at `:105` must not render for `phase === "rejected"`), no stage bar
(`:97` already gates on `phase === "uploading"`), an empty delete cell (`:108`
already renders one), and **no Dismiss** — a row a reload clears needs no second
way to clear it, and §1.4 already refused Dismiss on the durable row for a
different reason. It is never counted and never filterable: no id, never in
`documents`, never in `counts`, and the `Not added · K` chip reads the **server**
bucket (§1.5; ADR-012 §13.5b). A local refusal appears **only** in the default
list, above the server rows, exactly where the card was. R-DOC-04's *session-only*
rule therefore survives precisely where it is **true** — the half of the export's
instinct §1.1 kept.

### 7. The durable "Not added" row has no wire for its reason, so after a reload it says nothing

**Verified, not assumed.** ADR-027 §D6 point 2 persists the rejection reason on
the `document` row ("three nullable columns, and **this** is the migration",
`ADR-027:217-218`), but §D7's response shape (`:246-256`) and §11's contract-change
list (`:467`, "add `counts`, add `Rejected` to the `processingStatus`") add **no
reason to `items[]`**. The 422 body that carries the sentence today exists only in
the tab that uploaded the file — and NW-27's premise is that the refusal can
arrive **with the browser closed** (§1.1). So on the very reload §1.1 exists to
survive, the row renders a terminal **Not added** tag above an **empty hint**:
product-owner's "persistent, visible, terminal record" satisfied in letter and
lost in substance, because the user is told a file was refused and never told why.

**Decision (the copy side, which is this seat's): the sentence stays here and the
server sends a code.** The item carries a **rejection reason from the closed set
the code already fixes** — `AdmissionDecision.cs:23-32`, `NotAContract |
NoReadableText`, the only two, cited by §D6 itself — and the client maps it
through `getRejectionReasonCopy`, whose two sentences are requirements copy and
must not be re-authored server-side. **Ask to software-architect** (their file,
their edit): §D7's item shape gains that field, or names the surface it intends to
leave blank.

**Assumption in force if it does not land before the gate**: the row renders the
tag and **no hint** — silence, never a guessed reason, and never a client store
remembering this session's 422 so the row "still looks right" on this machine.
That last is the shape ADR-012's w14 clause 7 deleted and the reason
`documentStore.ts` is being removed one item over. A blank hint is a gap a user
can ask about; a remembered one is a lie the next browser tells.

### 8. OQ-w15-ca-05 — the sentence and the label for a poll that has stopped

ADR-012 §17 gives the Documents poll a **no-change budget** (five minutes) with an
explicit resume, because ADR-027 §C6 withdrew the guarantee that every row reaches
a terminal status. The stopped state's copy is this seat's, and here it is.

**8.1 It is neither an empty state nor an error state.** Rows are on screen and
each is exactly what the server last said; nothing has failed. It is therefore a
**list-level notice, not a row state**: no tag changes, no row moves, the progress
bar keeps rendering the server's stage (`:151`, `:155`), and the chips keep the
server's last numbers. It renders as one `.hint` line **below the grid** with the
resume control beside it as `.btn-secondary` — the pair `:100-103` already ships —
so no component and no token is added (ADR-019 w15 round-3 clause 5).

**8.2 The copy — two strings, and the same two everywhere.**

| Element | String |
|---|---|
| Notice | **"Nothing has changed for five minutes, so this page stopped checking for updates."** |
| Resume control | **"Check again"** |

Three things the sentence deliberately does **not** say. It does not say Raffa.ai
is still working on the file — in §C6's stranded case nothing is, and saying so is
the fabricated fact A15-3 forbids. It does not say anything failed — nothing has,
and `Failed` is a status this notice may never imply (client-architect's first
prohibition, in copy form). It does not name a cause: the only fact the client
owns is **what the page did**, and the actor is therefore *the page*, never
Raffa.ai — "Raffa.ai stopped checking" would report a service outage that has not
happened.

**8.3 Where else it binds — the half §17 could not see from inside one hook.**
§17's budget lives in `useDocumentsList.ts`. But NW-61's client ruling makes
**Ask** re-read `listDocuments({ pageSize: 1 })` on the same 2 s cadence while its
gate is off, where that fetch is a **one-shot `useEffect` today**
(`ask/index.tsx:100-105`, verified) — a *second*, brand-new recurring re-read this
wave introduces. The same stranded row leaves an Ask tab polling forever under
**not ready yet**, and it is worse there than on Documents: that state's whole
promise is *we will let you in when it is ready*, and Ask has no row grid to show
the user that nothing is moving. Portfolio's third variant (§2) inherits the shape.

**The rule is stated at IA level so no surface rediscovers it** (ADR-018 w15
round-3 clause 6): *a "not ready yet" state that depends on a repeating re-read
stops on the same budget and offers the same resume; a surface that cannot offer
the resume must not claim it is waiting.* On screens 2, 5 and 6 the stopped state
keeps its heading and sentence and adds **"Check again"** as a `.btn-secondary`
**beside** the existing CTA; the block's one primary action ("Go to Documents")
is unchanged. One notice string and one label, verbatim on all four surfaces: a
stopped update is one fact, and four wordings of it would be four facts.

**8.4 Screen 3's state inventory (§1.7) gains `updates paused`** — a **list**
state, not a row state, and the only state in this ADR that describes the
*client's* behaviour rather than a document's. Recorded as such so no task adds it
to `RowStatus` or to `semantics.ts`.

### 9. What this footer does not change, and what is owed

Sections 0–5 above, both w14 footers and every other screen; the six stage
strings, the confidence vocabulary, and every error and loading treatment.
**Design exports owed** — the four at `:923-930` gain two, and neither blocks
w15, because both are assembled from ADR-019's locked catalogue and this screen's
own idiom: (12) a Documents list with a **local "Not added" row** above the server
rows; (13) the **updates paused** notice with its resume control, on screen 3 and
in the empty block of screens 2, 5 and 6.
`HITL_CLAUDE_DESIGN: export screen 3 with a local "Not added" row above the server rows and with the paused-updates notice, and a tier empty block carrying a secondary "Check again" beside its primary CTA.`
