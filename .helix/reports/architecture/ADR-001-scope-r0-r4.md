# ADR-001 — V1 scope: R0–R4 wave slice and explicit out-of-scope boundary

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: product-owner (owner) + remaining Raffa council seats at council-close
- **Locked citations**: none — this ADR is the product scope elaboration of the
  locked WHAT (`inputs/product-spec.md`); no platform lock is added here.

## Context and problem statement

The product spec (§16) lays out five delivery waves R0–R4 and a V1 "done"
definition (§20). The engineering brief (§3, §11) says V1 in `dev`/`demo` must
cover the product Day-1 path and the R0–R4 backlog, but also names an explicit
non-goal set (spec §1.2) and a "production-only platform" out-of-scope (brief
§3). Without a crisp, user-visible slice per wave and a **named** out-of-scope
list, an implementer can silently pull §1.2 non-goals (or paid external
benchmark APIs) into an early wave, which would contradict the Day-1 promise and
Appendix C's "build the intelligence layer, not a UI around someone's API."

This ADR fixes the user-visible meaning of each wave and the hard V1 boundary so
the decomposer can slice work items against real product acceptance, not an
ambiguous backlog.

## Decision drivers

- Day-1 customer promise (spec §20): *"Raffa knows what we bought, what we pay,
  when we need to act, and where we can save money."* Every wave must visibly
  advance that promise, not just ship platform plumbing.
- Appendix C decision rule #10 and the benchmark-trust requirement (spec §10.4):
  an "insufficient market data" result is acceptable; a fabricated number is not.
- Brief §11: R3/R4 must not depend on a paid external benchmark API for the first
  `demo`; the benchmark is an interface + fixture adapter is enough (brief §10.2).
- Spec §1.2 non-goals are quoted verbatim and are not negotiable scope.

## Considered options

1. **Wave-by-wave user-visible ladder, with named out-of-scope list** — each R
   has a single "definition of success" sentence a stakeholder can verify, and
   §1.2 non-goals are carried forward as hard exclusions.
2. **R0–R4 as purely platform/infra increments** — ship auth/storage/extraction
   capacity without tying each wave to a user-visible outcome.
3. **Collapse R3/R4 into a paid-benchmark-driven demo** — treat benchmark
   integration as mandatory for `demo`, pulling a paid API into scope early.

## Decision outcome

**Chosen: Option 1.** Each wave is defined by the user-visible outcome it unlocks
(spec §16's "definition of success" verbatim intent), and the §1.2 non-goals stay
out of V1 regardless of wave, with R3/R4 benchmark work gated on the **interface +
fixture adapter**, never a paid external API for the first `demo`.

### Consequences

- **Good**: each wave has a stakeholder-checkable acceptance sentence; the
  decomposer and reviewer can reject a task that expands past §1.2 or that hard-wires
  a paid benchmark into an early slice.
- **Bad**: "quantifies credible savings" (R3) and "assessable in minutes" (R4) are
  delivered against fixture/limited data, so the numbers shown in the first `demo`
  are *representative*, not live market truth — which must be labelled as such in
  the UX.
- **Neutral**: R1–R4 share the same R0 foundation; deferring a wave does not defer
  the R0 platform slice (platform is always the first technical slice per brief §11).

## Pros and cons of the options

### Option 1 — user-visible ladder + named exclusions
- Good: maps directly to spec §16/§20; prevents silent scope creep; keeps R3/R4
  off paid APIs.
- Bad: requires discipline to label fixture-driven numbers as "insufficient/representative
  data", not live market positions.

### Option 2 — platform-only waves
- Good: simpler to sequence infra.
- Bad: does not satisfy "Done when" §13.4 (product Day-1 path on `demo`) and has no
  stakeholder-verifiable outcome per wave.

### Option 3 — paid-benchmark-driven demo
- Good: richer R3/R4 demo.
- Bad: violates brief §11 (no paid external benchmark dependency for first demo) and
  Appendix C's final test.

## Implications for the decomposition

- Every work item must carry a `release` tag in {R0, R1, R2, R3, R4} and an
  acceptance criterion written as a user-visible outcome, not an infra capability.
- Any work item that implements a §1.2 non-goal (full CLM/authoring, e-signature,
  PO/invoice management, supplier onboarding, full sourcing/RFP, ERP replacement,
  autonomous supplier comms, complex enterprise approval orchestration) is
  **out of scope** and must not appear in the V1 backlog.
- Benchmark-related work in R3/R4 must go through the Benchmark Service interface
  with the fixture adapter as the first adapter; a paid external API may only be
  introduced as a later, council-justified adapter — never a hard dependency of the
  first `demo`.
- The first R3/R4 demo must render a benchmark result as either confident (with
  P25/P50/P75 + provenance + confidence) or an explicit "insufficient market data"
  outcome; never a bare precise-looking number without provenance.
- R1 extraction includes **OCR in V1** (ADR-017): native text when sufficient,
  Azure AI Document Intelligence for scanned/image/low-text pages and layout,
  full document (no 2-page cap), behind the AI Gateway. A native-PDF-only
  extraction slice is not a complete R1.

## Assumptions

- Fixture adapter data is acceptable to demonstrate the R3/R4 user flow; live market
  data is a later, council-justified adapter. (See reports/open-questions.md.)
- "Definition of success" sentences in spec §16 are the canonical wave gates and are
  not re-negotiated here.

## Amendment (2026-09-08, epic-12 / ADR-023)

Ask Raffa uses this ADR’s **Internal Dataset** as the **market corpus** for
commercial comparison (in line / below / above P25–P75). The fixture catalog
must be expanded into a labelled *representative* worldwide mock (including
insurance names such as Allianz) so the copilot has something to narrate.
A paid Tropic/Vendr (or similar) adapter remains a later implementation of
`IBenchmarkService` — never a hard dependency of the first `demo`, and never
a second RAG index of other tenants’ contracts. See ADR-023.

## Amendment (2026-09-08, epic-13 / ADR-024)

The **Internal Dataset** is now the **mock market-intelligence feed**
(`inputs/requirements.md` R-MKT-01…05): a checked-in, labelled
*representative* dataset of how companies close contracts (price bands,
discounts, uplift caps, notice periods, negotiated clauses) behind
`IMarketIntelligenceProvider`, projected into `IBenchmarkService` rows and a
shared read-only market index. The paid third-party API stays a later
provider behind the same seam — never a hard dependency of the first V2
`demo`, never another tenant's contracts. This footer supersedes the
epic-12 amendment above. See ADR-024.

## Amendment (2026-09-10, wave w14)

Wave w14 ("workspace is real") fixes the scope boundary of the **invitation
lifecycle** and of the **workspace profile**. The body above is unchanged: the
R0–R4 ladder, the §1.2 non-goal list quoted verbatim, and the benchmark
interface + fixture-adapter rule all stand. The epic-13 footer above stays in
force — this footer addresses a different subject and supersedes nothing.
Items served: NW-58, NW-24, and one constraint on NW-09.

**1 — The invitation lifecycle is in scope, at R0.** Token, role and expiry;
accept; sign in; **join that workspace**; Admin removal; re-invite after
removal. This is R0 work under spec §16 ("auth, workspace, multi-tenancy,
**roles**") and it is the second half of the §20 Day-1 sentence "create a
workspace and **invite Procurement users**". A wave that ships an invite an
invitee cannot act on has not delivered §20.

**2 — A real mail transport is deferred, and is *not* a §1.2 non-goal.** Spec
§13.4 ranks "email notifications" **P1 / V1** and §16.1 ranks "Email alerts"
**P1**; §1.2's eight non-goals do not mention email or invitations.
`inputs/percorso-pilota-v1.md` excludes it from the pilot three times — §2
"Non è onboarding: **email**", §5 "**Niente email nel pilota**", §7 under the
heading "Fuori da questo pilota **anche se restano nello spec V1 pieno**".
Deferral is therefore a **priority** ruling inside accepted scope, not a scope
reduction: a later wave picks the transport up without re-litigating this ADR,
and the §1.2 list is not extended by it. For w14 the delivery mechanism is a
**copyable single-use accept link** in the Members UI, behind an
`IInvitationMailer` seam so the transport is a later drop-in.

**3 — The screen must not assert a fact the system does not hold.** A general
acceptance rule, not a w14 preference, and it outlives the deferral in clause 2:
the UI must not say "Invitation sent." while no mail leaves the system, and it
must not print a workspace currency or region the system never captured (see
clause 5). Where the fact is missing, the copy shrinks; it is never invented.

**4 — An unaccepted invitation grants nothing.** `Invited` is not membership:
before the link is opened the invitee sees no workspace. This *changes shipped
behaviour* — the R0 task that wrote a membership row at invite time is
superseded by this wave (see `reports/architecture/waves/w14.md`).

**5 — The workspace profile is `name` + `industry` + `country`.** Industry and
country are **closed lists** (`inputs/design/prototypes/raffa-v2/markup.html:55-56`),
not free text. **Currency and business region are derived from `country` and
stored; the user is never asked for them** — the prototype's create form has no
currency control, and `CHF · eu-west` appears only as pick-row *output*
(`markup.html:63`). No other workspace attribute is in scope for V1.

**6 — A workspace currency is a display default and never overrides a
contract's own extracted currency.** Converting or re-tagging a validated
contract amount by workspace currency would contradict spec §2 ("AI is not the
database"; canonical facts live in structured storage). Cross-currency
normalisation would need an FX source and is a separate, later, council-
justified item — it is not part of the workspace profile.

**7 — The workspace-domain restriction on an invitee's address is deferred**
to the wave that lands ADR-010 (real token claims). It is prototype helper
text (`screens-v2.md:150`), not a spec or §20 requirement, and enforcing it in
w14 would make w14's own acceptance unverifiable — that acceptance invites a
second Entra account, which in practice sits on a different domain. It stays
as helper text, not as a server-side block.

Deciders: product-owner (owner of this ADR), with cloud-architect co-deciding
clause 2 (no Azure resource, no Terraform module, no per-env secret is added by
NW-58 in w14) and software-architect + security-architect owning the mechanism
of clauses 1 and 4 in the separate invitation-lifecycle ADR of this wave.

## Amendment (2026-09-13, wave w15)

Wave w15 ("upload feels instant, and inviting a colleague works end to end")
fixes the scope boundary of **asynchronous document processing** and of
**guest provisioning + invitation mail**. The body above is unchanged: the
R0–R4 ladder, the §1.2 non-goal list quoted verbatim, and the benchmark
interface + fixture-adapter rule all stand. The epic-12, epic-13 and w14
footers stay in force. This footer **supersedes nothing**; it *completes*
clauses 1 and 2 of the w14 footer and *resolves* its clause 7.
Items served: NW-27, NW-61, NW-67, NW-68, with one constraint each on NW-69
and NW-05.

**1 — The asynchronous pipeline is restored as the product rule, and this is a
correction, not an expansion.** Spec §7.1's heading is literally "Asynchronous
pipeline" (`inputs/product-spec.md:286`); `uploaded` is defined there as
"Document stored; **processing not started**" (`:296`); §13.3 lists OCR/parsing
and AI extraction as **background jobs** (`:609-611`); §16.1 ranks "Async queue
/ worker" **P0** (`:745`); `inputs/percorso-pilota-v1.md:53-54` says "job
async" and "**Può lasciare la pagina**". `POST /api/documents` returns once the
file is stored. **No new scope authority is granted by this clause** — an
undelivered P0 backlog line is being delivered late. `inputs/requirements.md`
A7 (`:827`) and `OQ-askv2-007` ("upload stays synchronous") are **superseded**
and are `assumed-wrong` from this wave on (OQ-w15-003, with software-architect).

**2 — The admission gate splits, and a refusal is a fact the user is entitled
to keep.** Format and size (a `.zip` renamed `.pdf`; an oversized file) are
refused **in the request**, before anything is stored. Content classification
("this is not a contract") **moves to the Worker**, because it needs OCR and a
live model and cannot hold an interactive budget. A refused file therefore
becomes a **persistent, visible, terminal refusal record**, not a card that
dies with the browser session:

- **Raffa does not keep the content of a file it refused.** The *record* of the
  refusal persists; the uploaded content does not. The mechanism is
  software-architect's and security-architect's; the promise is this ADR's.
- It is **never askable** and never enters Ask's corpus. ADR-024's grounding
  rule is untouched: a refusal is not "validated with zero facts", it is
  outside the corpus entirely. No task may relax this to make a count add up.
- It is **never counted** in "All documents", never in the "waiting for your
  review" queue, and it never inflates an askable count.
- It needs **no new removal capability** — the existing `DELETE
  /api/documents/{id}` (Admin) applies.

This clause **supersedes two accepted requirement clauses, deliberately and on
the record**, because `inputs/**` is never edited by this process:
`inputs/requirements.md` **R-DOC-05 AC-1** (`:196-197`, "`DocumentProcessing
Status` gains no `Rejected` value (rejected files do not exist server-side)")
is superseded entirely; **R-DOC-04**'s clause "shown for the current session
only (they are not stored)" (`:188-190`) is superseded. **R-DOC-04's "never
counted in 'documents' or 'askable'" clause is NOT superseded — it stands, and
clause 2 above restates it.** The design oracle's matching *session-only, never
counted* rule (`screens-v2.md:75`) is overridden only in its *session-only*
half. This ratifies OQ-w15-D3 (ux-ui-designer) and answers OQ-w15-004.

Reason, stated so a later wave does not quietly reverse it: *session-only* was
coherent only while refusal was synchronous — the person who dropped the file
saw the card in the same interaction. Once classification is asynchronous, a
refusal that arrives after the browser closes is shown to **nobody**, which
contradicts this wave's own acceptance ("every document reaches a terminal
state with the browser closed") and leaves the user believing Raffa holds a
document it silently discarded. Async classification **plus** a session-only
refusal is the one combination that loses a user's file without telling them,
and it is not authorised.

**3 — "Still processing" outranks "empty".** Where a screen cannot distinguish
*nothing exists* from *not ready yet*, it must say *not ready yet*. An empty
Contract 360 during processing asserts "this contract has no clauses" when the
truth is "Raffa has not read them yet" — a fabricated fact, against spec §2
("AI is not the database … canonical facts live in structured storage"). Two
"not ready" states exist and must not collapse into one: **still processing**
(transient) and **waiting for your review** (needs a human) — the pilot's
minute 6–11 is exactly that human act (`percorso-pilota-v1.md:122`).
**Askable = validated, not merely `completed`**: `percorso-pilota-v1.md:55-56`
unlocks *Mark as validated* only when the weak fields are decided, and that is
the number the rail and the picker were already made to agree on in w14.
Screens are **gated, never blocked** — the pilot says the user "può lasciare la
pagina" (`:54`).

**4 — Inviting a colleague provisions a guest identity, and never anything
more.** Completing w14 clause 1 ("a wave that ships an invite an invitee cannot
act on has not delivered §20"): w14 shipped a copyable link the invitee still
could not act on, and papered over it with an operator's Azure-portal step. An
Azure-portal step the customer must perform is not a product. Three fences,
all product scope:

- **Raffa creates a guest; it never deletes or blocks one** (answers
  OQ-w15-007). A directory object is the **customer's**, not Raffa's, and the
  same person may belong to other workspaces — removing a member from
  workspace A must not disable a human being in workspace B. Raffa's scope is
  the **workspace roster**, not the identity lifecycle. No directory-deletion
  path is built. Membership removal stays immediate (ADR-025).
- **One guest per invited address, idempotent.** Re-inviting, or inviting
  someone already in the tenant, creates nothing new and never duplicates a
  person in the customer's directory.
- **No directory reads beyond what provisioning needs.** Raffa does not list,
  search, enumerate or sync the customer's directory.

This is not §1.2's "supplier onboarding": the invitee is a **colleague**
joining the customer's own workspace (§20 Day 1, "invite **Procurement
users**"; §3.1's role table is entirely internal), and nothing here is
autonomous — an Admin clicks Invite. `percorso-pilota-v1.md` draws the same
line in the customer's own words, listing outreach/RFQ/CLM/signature as
"**Non-goal prodotto**" (`:153`) and keeping email/Entra in a separate
demo-scope row (`:152`).

**5 — w14 clause 7 is resolved: the workspace-domain restriction is NOT
enforced.** Clause 7 deferred it "to the wave that lands ADR-010 (real token
claims)". **NW-05 lands ADR-010 in this wave**, so clause 7 fires now — and
this wave's own acceptance invites a personal (off-domain) address and requires
it to succeed. A B2B *guest* is by definition an identity from outside the
tenant's verified domain: an item whose purpose is inviting an external address
cannot ship a block on external addresses. It remains **prototype helper text**
(`screens-v2.md:149-150`), never a server-side block. **A deferral does not
convert into an obligation when its trigger fires.** If the restriction is ever
wanted it returns as its own item with its own justification.

**6 — The mail-transport deferral of clause 2 ends; scope never changed.**
Clause 2 already ruled the transport "deferred, and is *not* a §1.2 non-goal …
a later wave picks the transport up without re-litigating this ADR". **W15 is
that later wave**, so this footer records the **end of a deferral**, not a
scope change, and the table does not re-argue it (answers OQ-w15-002). The
pilot document never excluded mail from the *product*: §7's heading is "Fuori
da questo pilota **anche se restano nello spec V1 pieno**" (`:142`) and its
table separates "non servono a mostrare il processo" (`:152`, email) from
"**Non-goal prodotto**" (`:153`). Conditions:

- **Exactly one mail type ships: the invitation.** Renewal alerts, digests and
  completion notices stay P1 for later waves. A notification framework is not
  authorised by this footer.
- **The pilot script is unchanged and gains no inbox step**
  (`percorso-pilota-v1.md:130` "non mostrare email"; `:110`).
- **`mailDelivered` is a server fact.** "Invitation sent." may appear only on a
  send the server confirmed — never a client optimism, never a default `true`.
  This is w14 clause 3 inverted, not weakened. `mailDelivered: false` must stay
  fully functional: the invitation exists and the copyable link still works.
  The failure copy is already pre-decided in ADR-020's w14 footer and is not
  reopened.
- **A mail containing a site-relative accept link is a mail that does not
  work.** If the absolute accept-URL configuration cannot be delivered through
  Terraform in this wave, **NW-68 does not ship**. This is the one item this
  seat would rather cut than narrow.
- Note on priority, recorded to stop a recurring misreading: §13.4's `P1 / V1`
  is the **top** integration tier while §16.1's `P1` is the **second** backlog
  tier — two scales with opposite senses. Separately, §16.1's line is "Email
  **alerts**", a renewal notification this footer fences *out*. NW-68 draws its
  weight from §20's Day-1 sentence "create a workspace and **invite Procurement
  users**", not from §16.1.

**7 — The general rule under all six clauses.** w14 clause 3 said *the screen
must not assert a fact the system does not hold*. This footer adds its mirror,
and both halves bind equally:

> **The system must not silently drop a fact the user is entitled to.**

It is why a refused file must survive the session (clause 2), why a processing
document may not render as an empty contract (clause 3), why an invite may not
present a "ready" link that cannot be used (clause 4), and why "Invitation
sent." may only follow a confirmed send (clause 6).

**Not decided here** (recorded so no task reads this footer as authority):
queue transport, worker scaling, cost shape, which identity calls Graph,
whether Terraform manages the directory permission, the `Rejected` copy and its
placement in the IA, and the token/claims mechanism. Those belong to
cloud-architect, security-architect, software-architect and ux-ui-designer.
One position only is taken: **admin consent for guest provisioning must be a
one-time operator act, never a runtime prompt shown to an Admin using the
product.**

Deciders: product-owner (owner of this ADR), with software-architect
co-deciding clauses 1 and 2 (the pipeline and the gate's mechanism),
ux-ui-designer co-deciding clauses 2 and 3 (the refusal and not-ready states,
whose copy and IA placement are theirs), security-architect co-deciding clause
4's fences and clause 5, cloud-architect co-deciding clause 6's transport and
cost shape, and delivery-manager co-deciding clause 6's per-environment
delivery.

### Addendum to this footer (2026-09-13, wave w15, later table round)

Same wave, same footer, appended after reconciling with the other seats' files.
Clauses 1–7 above are unchanged and nothing in them is withdrawn. Four rulings
this seat owed the table, one of which withdraws part of its own lane draft.
Recorded here because a decomposer reads **files**, not a transcript.

**8 — A15-6's acceptance wording is corrected: no "Try sending again" ships,
and this seat's own lane draft is withdrawn.** The intake worded A15-6 as "mail
transport failing → ADR-020 failure copy **+ 'Try sending again'**"
(`w15-requirements.md:542`), and this seat's lane draft went further still with
**A15-6b**'s "plus 'Try sending again'" and **A15-6c — re-send is real**. **Both
are withdrawn.** ADR-020's w15 §3.4 rules the affordance out and is right on the
mechanism: the server stores the invitation token **only as a SHA-256 hash**, so
it cannot re-send the original link — any retry is necessarily a **re-issue**
that mints a new token and kills the link the Admin is looking at — and
ADR-020's own w14 footer `:284-287` already forbids telling the Admin to invite
again from this pane. *An affordance with no legal mechanism is not shipped*,
and that precedent is ADR-020's own. A15-6 therefore reads, and this is the
wording a reviewer must hold NW-68 to:

> **A15-6** — with the transport failing, or with
> `Invitations__Mail__Enabled = false`, an invite still **succeeds**: the pane
> shows ADR-020 10.1's pre-decided failure sentence, the response carries
> `mailDelivered: false`, **the invitation exists and the copyable link still
> works end to end** (the invitee can still join), and no log line or audit row
> carries the mail body, the accept URL or the token. **No re-send affordance is
> expected, and its absence is not a defect.**

**No capability is lost, which is why this is a correction and not a
reduction**: an Admin who needs a fresh link **revokes and re-invites** — two
actions that already exist and are already audited — and security-architect's
§J.2b makes that a single-transaction replacement. What is declined is a
one-click button, never the ability. If a later wave wants the button it ships
on §J.2b's mechanism, with copy that says *replace*, never *retry*.

**9 — OQ-w15-D2 is not overruled, and the ground is a role boundary rather than
a tolerance.** ux-ui-designer asked whether a `Rejected` row that only an Admin
can remove is acceptable for a Procurement user. **It is; the assumption in
force stands.** Clause 7 is the reason: the record of a file Raffa refused and
did not keep is a fact the user is entitled to, and it now carries its reason on
the row instead of in a card that vanished on reload. Two bounds make it cheap —
the `Not added` chip renders **only while `counts.rejected > 0`**, so a tenant
that never has a refusal sees no change at all, and refusals sit in **their own
filter**, so they never dilute "All documents" or the attention default.

**The reason this is not fixed by giving Procurement a delete button**: `DELETE
/api/documents/{id}` is Admin-only, and widening it is a change to the **role
model** (spec §3.1), not a tidy-up of a list. No oracle asks for it, no item in
this wave carries it, and a task that quietly broadened a role to clear a chip
would be the largest unreviewed change in the wave. Revisit only if a pilot user
complains, and then as its **own item** with its own justification.

**10 — The live-invitation cap is 100 (OQ-w15-sec-03 left the number to this
seat).** Confirmed as proposed; the mechanism and its
`workspace.invitation.cap_reached` audit row are security-architect's and are
not re-opened. 100 is deliberately two orders of magnitude above an honest pilot
workspace — §20 Day 1 invites "Procurement **users**" and §3.1's role table is
entirely internal — so it never fires for a real user and bounds only the
directory-spam shape NW-67 newly creates. **It is a safety bound, not a licensed
quota**: no screen advertises it, and a tenant that reaches it has a support
conversation, not a paywall.

**11 — If the 20-task cap binds, the two `should`s yield before either
`must`.** §5's in-wave order is **dependency**-driven, which places NW-69 and
NW-58r (both `should`) ahead of NW-05 and NW-06 (both `must`). Dependency order
is not priority, and the cap must not silently convert one into the other:
**NW-05 and NW-06 are not the tail of this wave.** The release valves, in this
order and no other: (1) **NW-10** — the badge is *always absent* today
(`rememberDocument` has zero production call sites), so deferring it regresses
nothing and its head-of-W16 slot is already recorded; (2) **NW-58r** reduces to
the runbook walk the table already ruled (OQ-w15-006); (3) **NW-69** narrows to
the outcome states NW-67 and NW-68 actually produce. **Never narrowed**: NW-27's
durability (A15-2 *is* the item), NW-67's failure contract (A15-7 exists
precisely because a "ready" link that cannot be used is the defect), and NW-68's
absolute accept link (clause 6 — cut the item before shipping a broken link).

**12 — What `demo` actually shows this wave, and why that is a pass rather than
a gap.** Both new capabilities ship behind per-environment flags that default
**false** on `demo` (`Invitations__Mail__Enabled`,
`Invitations__GuestProvisioning__Enabled`). This clause exists because clause 8
ruled what a disabled *mail* flag means for an invite and nothing ruled the
*provisioning* flag — and provisioning is the path that **aborts**.

- **Flag-off is not failure, and `demo` loses nothing w14 delivered.**
  ADR-026 §2 gives `IGuestProvisioner` a fourth value, `NotConfigured`, under
  which "the invite behaves exactly as it does on `main` today — link-only, no
  directory write — instead of either lying or failing" (`ADR-026:536-539`).
  That is the ruling this seat needed, and it comes from software-architect's
  mechanism rather than from this seat's authority: §3's "a failure leaves
  nothing behind" binds **`Failed`**, never `NotConfigured`. An invite on
  `demo` therefore still succeeds and its copyable link still works — the same
  guarantee clause 8 already gives for `Invitations__Mail__Enabled = false`.
- **A15-4, A15-5 and A15-7 are `dev` acceptance and are not walkable on `demo`
  this wave.** With both flags off there is no mail and no guest to observe. A
  reviewer must not read that as NW-67 or NW-68 undelivered, and the stories
  must carry the environment on their acceptance rows.
- **No task turns either flag on for `demo`.** Doing it to make acceptance pass
  would add an inbox step to the pilot script, against clause 6. `demo` with
  mail is its own item, in a later wave, with its own justification.

**And the pilot path is not demonstrable until w14 reaches `demo`.**
delivery-manager's finding — `demo` stands at `demo-v3` of 2026-09-04 with no
`demo-v4`, a whole wave behind (`ADR-016:416-421`) — is a **product** fact, not
only a delivery one: `inputs/percorso-pilota-v1.md` is the client demo path,
and the workspace-and-invite half of it has never existed on the environment
the demo is run from. Cutting `demo-v4` on the current `main` at the w15 gate,
before the wave starts, is therefore **ratified by this seat as a product
requirement**, and w15 promoting as `demo-v5` is endorsed.

Deciders on this addendum: product-owner (owner), with ux-ui-designer's ADR-020
w15 §3.4 governing clause 8's affordance and their OQ-w15-D2 raising clause 9,
security-architect owning clause 10's mechanism and audit verb, and — on
clause 12 — software-architect's ADR-026 §2 supplying the `NotConfigured`
semantics and delivery-manager's ADR-016 w15 footer the promotion facts.

## Amendment (2026-09-14, wave w16)

Wave w16 ("nothing the product knows lives only in a browser tab") fixes the
scope boundary of **server-side state for facts the product already stores**.
The body above is unchanged: the R0–R4 ladder, the §1.2 non-goal list quoted
verbatim, and the benchmark interface + fixture-adapter rule all stand. The
epic-12, epic-13, w14 and w15 footers — including the w15 addendum's clauses
8–12 — stay in force. This footer **supersedes nothing**. Items served: NW-07,
NW-12, NW-13, NW-21, with acceptance and roster rulings on NW-32 and NW-08 that
this seat owns by wording even though §3 does not seat it on their mechanism.

**0 — Zero new capability, and that is the reason all four are in scope.** Every
item exposes a fact already stored under RLS — `renewal_action`,
`negotiation_outcome`, `RealizedSavings`, `conversation` — or replaces a
`sessionStorage` mirror of one. None of §1.2's eight non-goals is touched; no
paid market API enters any environment (NW-52 stays DEFERRED). The items are
in-wave on **ADR-012 §1's w14 rule** ("a client store never stands in for a
missing GET"), not on a product expansion. **None of these four surfaces is on
the pilot script**: all are satellites (`percorso-pilota-v1.md:105-109`), the
20–25 minute script (`:118-126`) reaches them only as a citation landing, Quote
check is explicitly non-hero (`:109`, `:147`) and "Home savings come demo" is
explicitly out (`:148`). **Consequence, binding on the decomposer: no item in
this footer may claim pilot-script priority, and none grows a new screen.**

**1 — Pre-w15 conversation rows are recorded as retired: never remapped, never
deleted. A16-1 is reworded because as written it can neither fail nor pass.**
NW-07's keying seam closed in w15 (`Raffa.Api/Infrastructure/CallerContext.cs`
resolves identity; absent identity is 401, never 400), so what remains is a
**data disposition**, and this seat reduces it from *migrate* to *record*.
Rows created before w15 are keyed by the MSAL username; a returning caller is
keyed by `oid`; those rows are unreachable. They stay in place because a blind
email→`oid` remap needs a directory lookup the product has no batch path for
(`Raffa.Chat/Migrations/` holds only `Initial` and the RLS policy), and a
*wrong* remap would hand one user's threads to another — a breach of
`inputs/requirements.md` R-CONV-01 AC-1 (`:241-242`), strictly worse than the
loss it repairs. Deleting is equally refused: the rows are the customer's, the
same ground as w15 clause 4. Against **w15 clause 7** ("the system must not
silently drop a fact the user is entitled to") this is satisfied, not waived:
the drop is **recorded and bounded, not silent**. One standing constraint falls
out of it, to preserve rather than to build: **no surface may count a
conversation it cannot open** — the list filters on the same key, so an orphan
never appears as a phantom row, and no task may regress that.

A16-1's published wording ("sign in as the same user with different UPN
casing") is **vacuous**: the key is `oid`, which is invariant under UPN casing,
and `Infrastructure/CallerIdentity.cs:60-68` records **not** lower-casing as
deliberate because `oid` is an opaque, case-sensitive object id. The acceptance
this wave is held to, on `dev`:

> 1. User A creates a conversation, reloads → still readable by A.
> 2. User B, a member of the same workspace, can neither list nor read it
>    (R-CONV-01 AC-1).
> 3. A pre-w15 conversation is not listed and not counted anywhere; opening it
>    is a clean **404** — never a 500, never another user's thread.
> 4. The disposition is recorded in the PR description and in
>    `reports/open-questions.md`.

**The promoted task carries an acceptance line that must not be implemented.**
`epic-18/.../task-01-conversation-user-is-the-subject.md:69` requires "a
conversation created under `User@Example.com` is readable by the same subject
presenting `user@example.com`". Satisfying it forces an implementer to
**re-introduce the exact normalization ADR-010 rejected** — it is not merely
vacuous, it is actively harmful. Raw §0.4 forbids rewriting the task body, so
the corrected wording lives here and in `reports/architecture/waves/w16.md`, and
**the decomposer must carry clauses 1–4 above into the story row and the slice
rather than DoD line 69**. Clause 2 of that acceptance is **never narrowed**:
per-user isolation is a tenancy promise, not a wave preference. `OQ-askv2-005`
retires with this item; ADR-024 is software-architect's to amend.

**2 — NW-12 is the read-back and nothing more; and an existing AC is
re-pointed, not cancelled.** W16 renders **the recorded outcome of the quote in
front of you** — *did my record stick, does a second browser agree*. A
cross-quote history, levers analytics or an Ask-citable list is **NW-57 (W18)**,
and the design oracle draws the line itself
(`inputs/design/prototypes/raffa-v2/screens-v2.md:139-145`, "Target and
negotiation levers are one step further"). It lands on the existing `/quotes`
surface; no new screen.

The honest finding this seat owes the record: **`E08/F03/US01` AC-4 is today
discharged by a session store.** That story is `status: active`, its AC-4 is
unchecked, and the code standing in for it —
`web/src/routes/quotes/quoteOutcomeStore.ts` — cites that AC in its own header
while its `sumRealizedSavings` has **zero production callers**; shipped UI copy
admits it ("Saved to **this browser's** … record for this session"). AC-4 is
therefore **not satisfied**; it is stood in for by exactly the pattern ADR-012
§1 forbids. **Ruling: AC-4 is re-pointed to NW-12 + NW-21 as its honest
server-side implementation — not cancelled, no status banner, no `superseded:`
line, the story stays `active`.** Two wording corrections inside it are this
seat's: (a) the surface is **Savings**, not "Home" — Home savings are out of the
pilot (`percorso-pilota-v1.md:148`); (b) what updates is the realized **count**,
not a money tile (clause 4). This *refines* `w16-requirements.md` §6's "none":
**no work item is cancelled this wave.**

> **A16-6** — on `dev`: upload a quote → record an outcome → reload **and** a
> second browser → the outcome renders with its server-computed values; clearing
> `sessionStorage` changes nothing; no production module imports
> `quoteOutcomeStore`.

**3 — The four named negotiation steps are canonical, the row stores the key and
never the label, and ticks are per contract.** Ratifies OQ-w16-006's product
half (the module and the table are software-architect's).

- **Canonical set**: *Notify · Request revised pricing · Counter with the market
  benchmark · Sign or send non-renewal notice* (`screens-v2.md:106-108`). Four,
  no more; a fifth step is a product change with its own item.
- **Keyed by stable step key, never by array index.** The reason is this seat's
  rather than the architect's: two of the four labels are **parameterized**
  (`contract360ViewModel.ts:211-218` — the tracker header is literally
  "close by {{ cancel }}"). The row stores the **key**; the rendered label stays
  client-side. Freezing a supplier name or a cancellation deadline into a
  persisted row makes the record assert a fact that later changes — **w14
  clause 3**.
- **Per contract, one live negotiation per contract**, matching
  `renewal_action`'s unique `(tenant_id, contract_id)` and the design, where the
  tracker appears after "Start negotiation now" and its status is "shared with
  the Contract 360 tracker (`racts`)" (`screens-v2.md:105,130`). **Not per
  renewal cycle**: no cycle entity exists, and per-cycle tick *history* would be
  a new capability — out for V1, and asked for by no oracle.

> **A16-7** — on `dev`: tick 2 of 4 on contract X → reload **and** second
> browser → the same 2 ticked; contract Y unaffected; the step keys the API
> returns match the four names; no
> `sessionStorage["raffa.contract360.steps.*"]` is a source of truth.

**4 — "The Savings KPI moves" means the status move. The realized-amount gap is
an unmet AC of an existing story, not a new capability — and an outcome may be
*resolved* to an opportunity but never *guessed*.** Rules OQ-w16-005, which is
this seat's alone.

- **A16-8 closes on the status move.** The intake's assumption is ratified, but
  its stated ground (the design column is "Estimate") is not the load-bearing
  one. The real ground: **no surface renders a realized money figure.** The
  Savings KPI row builds exactly three cells — Contracts analyzed · Upcoming
  renewals · Savings **identified** — matching `screens-v2.md:134-135`, which
  has no realized tile; realized appears only as a **count**. The realized
  amount is already stored and already on the wire per opportunity
  (`RealizedSavings`, exposed as `realizedAmount`), so **no fact is dropped from
  the row** and w15 clause 7 is not engaged. The estimate-summed `Realized`
  range in `SavingsKpiCalculator` is therefore a latent **API-shape** defect
  that reaches no screen.
- **Correction to OQ-w16-005, on the record.** It frames the amount fix as "an
  ADR-001 capability change and the head of W17". It is **neither a capability
  change nor a new item**: it is an unmet AC of an existing, still-`active`
  story — `E04/F03/US01`, whose AC-1 names "realized" and whose implementing
  code documents the gap and names the record task that did ship. It needs **no
  ADR-001 amendment and no new epic**; it needs a **priority ruling: head of
  W17**, recorded here so it is not lost a third time. Spec `:447` defines the
  tile as "**Verified** negotiated/implemented savings".
- **Fence binding this wave, at zero cost**: **no w16 task may render
  `SavingsKpiSummary.Realized` as a money amount.** The only realized figure a
  w16 surface may show is the per-opportunity `realizedAmount`, which is
  correct. Ground: **w14 clause 3**. The defect is already live rather than
  dormant — `PATCH /api/savings/{id}` is routed and already flips status and
  stores the amount — so **NW-21 widens that path, it does not create it.**
- **May an outcome be resolved to an opportunity rather than naming one? Yes,
  but it must never guess.** The link is explicit, or derived from a fact the
  system holds (the quote's contract / supplier). With **no unambiguous
  opportunity the outcome is recorded unlinked**, `savingsPropagated` stays
  `null`, and nothing moves — an honest omission, never a nearest-match. Ground:
  this ADR's own driver (a fabricated number is not acceptable) and
  `NegotiationOutcome.cs:127-131`, which already states that no derivable link
  exists. This fence is **never narrowed**.

> **A16-8** — on `dev`: record an outcome linked to an opportunity → its status
> is `Realized` and its `realizedAmount` equals the outcome's `realizedSaving`;
> the Savings realized **count** increments; a second browser agrees; an outcome
> with no unambiguous opportunity is still recorded, is visibly unlinked, and
> moves no KPI.

**5 — Two rulings on items this seat does not sit on, because both are acceptance
wording rather than mechanism.**

- **A16-4 (NW-32) is ratified as the intake reworded it.** The original could not
  fail — an unsigned POST is 401 today, before the item runs — so "a signed POST
  on each of the nine paths writes an audit row naming the caller's resolved
  subject" is the acceptance. Two notes for the decomposer: the promoted task's
  DoD line "absent identity is 401 on conversations" is **already true** and is
  not evidence of delivery; the grep DoD is the real gate and must be read with
  the "outside comments the council allows" caveat, since three doc-comment hits
  survive by design. No claim is made on the mechanism.
- **OQ-w16-003 is ruled `no`: there is no web audit surface in W16.** Raw §6
  A16-2 reads "Admin opens audit **or calls** `GET /api/audit`", audit appears
  nowhere in the pilot path (§5 lists no audit surface; §7 keeps Swagger and
  admin chrome out), and a screen would seat ux-ui-designer and add ADR-018 /
  ADR-020 work the 5-phase cap has no room for. **ux-ui-designer stays
  unseated**; ADR-020's screen inventory is unchanged. If an audit screen is
  wanted it returns as its own item.

**6 — Priority, and the release valves in this order and no other.** All nine
in-wave items are `should` except W16-01 (`could`), so **no must/should
inversion exists and w15 clause 11 does not fire this wave**. If the 20-task or
5-phase cap binds:

1. **W16-01** (`could`, intake-originated) → head of W17's `could` tier, never
   displacing an item already queued for W17.
2. **NW-12 narrows** to the read-back on the existing `/quotes` surface — never
   a new screen, never NW-57's history.
3. **NW-13** — least user-visible of the four and absent from the pilot script.

**Never narrowed**, as a priority ruling and not a claim on mechanism: **NW-11**
— it is the single shared `racts` fact across Renewals, Contract 360 and Savings
(`screens-v2.md:130`; `app.jsx:109,166`), so narrowing it re-creates the
split-brain this wave exists to kill; **NW-07's clause 2**; **NW-21's fence**.

**Not decided here** (recorded so no task reads this footer as authority): the
routes and their shapes, which module owns the step-tick table, the link column
and its migration, the stores' retirement mechanics, the contract file's
one-writer schedule, and anything in NW-08 / NW-31 / NW-32 beyond the wording
above. Those are software-architect's, client-architect's, security-architect's
and delivery-manager's.

Deciders: product-owner (owner of this ADR), with software-architect co-deciding
clause 1's disposition mechanism and clause 3's module and table choice,
client-architect co-deciding clauses 2 and 3 where a store retires under
ADR-012 §1, and security-architect cited on clause 1's single comparison rule
for the identity column. `reports/architecture/waves/w16.md` carries the
per-item rows and the votes.

### Clauses 7–8, appended at the same table (wave w16, second round)

Clauses 0–6 above are unchanged. Two questions reached this seat **after** they
were written — `OQ-w16-sa-01` (software-architect, naming product-owner) and
delivery-manager's **D4** proposal to re-word A16-3 once OQ-w16-004's premise was
withdrawn. Acceptance wording and the honest recording of a deferral are this
seat's, so both are ruled here rather than settled inside another seat's draft.

**7 — ADR-028 §D5 clause 2 (deterministic supplier-name resolution) ships in
w16. Rules `OQ-w16-sa-01`.** The assumption in force is ratified, with the
reason corrected.

- It is **already sanctioned by clause 4**: a lookup against the
  `(tenant_id, normalized_name)` unique index that links only on exactly one
  match and otherwise declines **is** "resolved, never guessed". It adds no
  capability — it makes an already-built propagation reachable — and touches no
  §1.2 non-goal, no schema, no contract, no screen.
- **Without it NW-21 does not change what the user sees.** Software-architect's
  decision is zero client change, and the one production call site builds six
  fields and omits `savingsOpportunityId` (`web/src/routes/quotes/index.tsx:274-282`).
  Clause 1 alone therefore closes A16-8 **by `curl` and by nothing else**, leaving
  the product in precisely the state the item names: a recorded outcome that never
  moves Savings. A wave item that closes only on a request no shipped surface
  makes is not delivered in this product — acceptance is proven **on `dev`**
  (w15 clause 12's posture), not in a test client.
- **Fence 1 — the acceptance walk drives clause 1.** A16-8's deterministic close
  is the **explicit-id** path. Clause 2 is a reachability improvement whose
  *decline* is a pass, not a failure: software-architect's named risk (the pilot
  corpus may hold no quote whose free-text supplier resolves) is accepted openly
  rather than hidden, and no acceptance step may depend on the resolver firing.
- **Fence 2 — an unlinked outcome must never read as a realized saving.** When
  `savingsPropagated` is `null` the outcome is recorded and **nothing moved**; no
  surface may present that as success, and no total may absorb it. This is a
  *must not*, so it grows no screen and seats no designer — and the copy that
  today claims the record is kept in "this browser's … record for this session"
  retires with `quoteOutcomeStore` under clause 2's NW-12 work anyway.
- **If the council later drops §D5 clause 2**, NW-21 is recorded as **API-only
  closed with its UI path still open**, and that residual becomes an item at the
  head of W17. It is never left implicit. `SupplierResolver` is never called on
  this path (software-architect's, cited not decided here): recording an outcome
  must not mint a supplier row.

**8 — A16-3's workflow half is re-worded, and what w16 does not restore is
recorded rather than dropped.** Acceptance wording only; this seat claims nothing
about the mechanism, the file, or the identity plane.

The published A16-3 offers two closes — "the workflow authenticates **or** is
replaced by an enqueue that needs no API auth" — and the table took **neither**:
OQ-w16-004's premise was withdrawn by three seats, and delivery-manager's
disposition deletes the workflow's API steps. An acceptance whose both branches
were declined must be re-stated honestly, not quietly narrowed. Held to, on `dev`:

> **A16-3** — no `X-Role` / `X-Workspace-Role` / `X-User-Id` survives in
> `.github/`, in `web/openapi/raffa-api.v1.json` or in the capabilities endpoint;
> the Admin catalog rows come from membership; **an Admin resubmits a document on
> deployed `dev` through the product's own path and the Worker drives it to a
> terminal state**; the surviving verification job runs green on that tenant.

- **Nothing the product *does* is being removed.** The deleted CI path fails 401
  at its first API step today, before this wave runs; what is retired is a stale
  record, which is the defect class NW-31 exists to kill. The in-product path
  (`POST /api/documents/{id}/reprocess`, membership-derived Admin, 202) is a
  **stronger** close than the CI job ever was: a real Admin with a real token,
  which is what the acceptance was trying to prove.
- **Named so no one reads it as delivered**: w16 does **not** restore the
  **bulk, whole-tenant** reprocess. It is an operator affordance, not a new
  capability; it is deferred to **W17** with its shape already designed
  (delivery-manager's D5), and it must appear in this wave's known-gaps record so
  a `demo` walker does not read its absence as a w16 regression. Under w15
  clause 7 a loss is acceptable only when **recorded and bounded**: this one is
  both. No `must` is postponed behind a `could` — NW-31 is `should` and clause 6's
  "nothing queued past W17" holds.
- **Never narrowed — the grep half.** "No `X-Role` anywhere" is what makes the
  retirement auditable; a replacement file still *named* `reprocess-*` while never
  reprocessing would re-create the stale record this item deletes, so
  delivery-manager's primary (replace the file, do not keep the name) is ratified
  and their own named fallback refused, on product-record grounds.

Deciders on clauses 7–8: product-owner (owner), with software-architect's
ADR-028 §D5 supplying clause 7's mechanism and raising `OQ-w16-sa-01`, and
delivery-manager's w16 draft D3–D5 supplying clause 8's disposition and the W17
successor. Neither clause amends a decision above; both are additive.

## Amendment (2026-09-15, wave w17)

Wave w17 ("the product officializes what it knows, shows the page it read it
from, and answers where you can save") fixes the scope boundary of **what the
product may assert on screen once a fact has been decided**. The body above is
unchanged: the R0–R4 ladder, the §1.2 non-goal list quoted verbatim, and the
benchmark-interface + fixture-adapter rule all stand. The epic-12, epic-13, w14,
w15 and w16 footers — including w16's clauses 7–8 — stay in force. **This footer
supersedes nothing in this ADR.** What it supersedes are **five product-oracle
lines and one Appendix C rule**, on the record only (`inputs/**` is never
edited), listed in clauses 2 and 6. Items served: NW-72, NW-71, NW-20, NW-22,
NW-62, NW-64, NW-66, plus rulings on NW-75, NW-23 and NW-25, which are not at
this table. Baseline `d3d2d24`.

**0 — Zero new capability; three w16 forward promises discharged; and, unlike
w16, this wave stands on the pilot script.**

- Every in-wave item **renders or decides a fact the product already extracts
  and stores** under RLS — `extraction_evidence` confidences, `ContractClause`
  rows, `RealizedSavings` rows, the persisted notice/term columns. None of
  §1.2's eight non-goals is touched and **NW-52 stays DEFERRED**: the market
  band in NW-22 and NW-62 comes from `IBenchmarkService`'s existing fixture /
  market-feed adapter and this tenant's own corpus, **labelled representative**
  (this ADR `:47-50`, `:90-93`), never a paid market API, in any environment.
- **w16 clause 4** ruled the realized-amount gap "a priority ruling: head of
  W17". **Discharged** — NW-72 is row 1 of the wave (`w17-requirements.md:752`).
- **w16 clause 8** deferred the bulk whole-tenant reprocess to W17 "with its
  shape already designed". **Discharged** — NW-73 is row 2, in wave, and §5
  records it as deliberately not cut.
- **w16 clause 6, valve 1** sent **W16-01** to "the head of W17's `could` tier".
  **It is not owed, and that is verified rather than assumed**: W16-01 shipped in
  w16 (`w17-requirements.md:700` and `:784` — `d3d2d24`, PR #129). A valve
  promise that silently evaporates between waves is exactly the drop w15 clause 7
  forbids, so it is checked and closed here rather than left to lapse.
- **Unlike w16 clause 0, this wave is on the pilot script**, and that changes a
  priority rather than a scope. `percorso-pilota-v1.md:121-122` is the upload →
  HITL act of the 20–25 minute demo, and NW-71 rewrites the very rule that act
  demonstrates. The consequence is clause 2's fixture fence, and it is the one
  thing in this wave that can break the demo while every test stays green.

**1 — NW-72. The w16 money fence is lifted and *replaced*, not deleted; the
story is completed, not cancelled.** Rules the product half of **OQ-w17-006**.

- **In scope as an unmet acceptance criterion, not as a new promise.** This
  ratifies w16 clause 4's own correction: it is "neither a capability change nor
  a new item". `E04/F03/US01` AC-1 stays **`active`** — **no status banner, no
  `superseded:` line** — and NW-72 discharges its realized half. This is the
  whole of `w17-requirements.md` §6's "no work item is cancelled this wave", and
  this seat confirms it: **nothing is cancelled in w17.**
- **The fence itself.** w16 clause 4 bound that wave with "no w16 task may render
  `SavingsKpiSummary.Realized` as a money amount". W17 lifts it **and replaces
  it**, because its ground — **w14 clause 3**, never assert a fact the system
  does not hold — is untouched. The replacement, binding on the decomposer:
  1. A realized money figure is read from **`RealizedSavings` rows**, the
     audit-tracked record, **grouped by currency and never summed across
     currencies**.
  2. The **estimate-summed `Realized` range** in `SavingsKpiCalculator` remains a
     defect **to fix, never to render**. Lifting the fence does not promote it.
  3. An outcome with `savingsPropagated: null` is recorded, visibly unlinked,
     and **enters no total**. This restates w16 clause 7 fence 2, which is
     **never narrowed**.
- **The slot must be designed, not assumed.** `screens-v2.md:134-135` carries no
  realized tile at all — the same anchor that justified w16's fence. The cell is
  **ux-ui-designer's**; this seat constrains only its meaning: it is labelled
  **Verified** (spec `:447`, "**Verified** negotiated/implemented savings") and
  **never implies a single currency**.

> **A17-S1** — on `dev`: record an outcome that realizes an opportunity → the
> Savings KPI band shows a money figure for realized, **one line per currency**,
> labelled Verified; reload **and** a second browser agree; an unlinked outcome
> moves no figure; the pre-negotiation estimate appears nowhere in that cell.

**2 — NW-71. One bar at 90 %, for every field, including the five critical ones
— and a percentage on screen is floored, never rounded up across it.**

- **A codification, not an expansion.** One threshold, **90 %**, every field, the
  five critical fields included, **no always-review list**. No §1.2 non-goal
  moves and no capability is added; the pilot loop keeps its shape and only the
  number changes.
- **Superseded on the record — five oracle lines, not one.** `w17-requirements.md`
  §6 names `product-spec.md:333-335` and `percorso-pilota-v1.md:46`. Three more
  carry the same retired bands and are superseded with them:
  `percorso-pilota-v1.md:32` (the first-field tooltip, ">95% / 80–95% / <80%"),
  **`:55`** (the review queue defined as "solo campi **<80%**") and **`:122`**
  (the demo script's "due campi <80%"). `product-spec.md:341`'s *stricter
  validation for critical fields* is superseded **in its threshold half only**.
- **Not superseded, and both matter.** (a) The **criticality** of contract value,
  cancellation, termination, renewal date and price uplift — they keep every
  other privilege they hold (clause 5's always-shown rule; `screens-v2.md:92`);
  only their *threshold* goes. (b) `percorso-pilota-v1.md:65` (`needs_review` =
  "almeno un campo sotto soglia") and `:55`'s unlock rule ("**Mark as validated**
  only when the weak fields are decided") are **threshold-relative** and survive
  verbatim, re-parameterized by the new bar.
- **The consequential-use fence moves with the bar.** Spec `:337` and
  `screens-v2.md:90` fence consequential use below 80 %; the fence becomes **"not
  accepted — whether by the rule or by a human"**. The unusable set therefore
  *grows* for a `needs_review` document, and after "Mark as validated" nothing
  changes. This is a tightening, and it is deliberate.
- **Display corollary, this seat's addition to OQ-w17-002.** That question
  reconciles server and web at the boundary but says nothing about the *label*.
  The operator stash compares a **rounded** percentage, so `0.895` would render
  "90 % · Review" — the screen printing the threshold number beside the word that
  denies it. **Rule: a percentage displayed beside a decision is floored, never
  rounded up across the bar** (`0.895` → "89 % · Review"), **or omitted.** A
  rendering that rounds *up* to the bar asserts a reliability the fact does not
  hold — w14 clause 3 again.
- **⚠ Correction to ADR-019's w14 footer, clause 6 (`ADR-019:224-230`).** It
  defers the three confidence rows on the ground that `semantics.ts:9-13` "cites
  a HITL decision of 2026-09-10 (≥ 90 % is auto-accepted…)". **On `d3d2d24` it
  does not**: that docstring carries spec §7.3's *old* bands verbatim and
  `isConfidenceBlocking` is still `< 80`. The staleness the footer asserts is
  real; the evidence it cites is not on `main` — the 90 % comment exists only in
  the uncommitted operator stash. **W17 is the wave that puts the ruling on the
  record**, and no seat may treat it as already decided. Recorded here rather
  than by editing ADR-019, which is ux-ui-designer's.
- **The fixture fence — OQ-w17-po-01, and it can break the demo while CI stays
  green.** At a single bar of 90 %, a seeded field at exactly `0.9` flips from
  *Flagged* to **Accepted**. `percorso-pilota-v1.md:122` requires **two fields
  below the bar** on the live-uploaded MSA or the pilot's HITL act has nothing to
  review. **Ruling: the fixture changes, never the threshold.** No task may soften
  the 90 % rule to keep a seed interesting.

> **A17-1…A17-4** — on `dev`: upload a document with fields on both sides of the
> bar → every field ≥ 0.90 is **already accepted** on first open of Review, with
> no click, **including a critical one**; every field below is queued; reload
> **and** a second browser agree (a server decision, not session state); one
> audit row per document names the auto-accepted **fields, never their values**;
> and no screen shows a percentage that contradicts its own decision state.

**3 — NW-20. "Activity" is the provenance timeline of events already persisted,
and an empty array is not a decision.**

- Both `benchmark` and `activity` are spec §8.2 (`:367`) tab names, so neither is
  a new capability; what is missing is a **definition**, which is this seat's.
- **Activity for V1** is drawn **only** from events the system already persists:
  uploaded / processing completed / `document.validated` / `document.reprocessed`,
  a contract correction, a renewal action started or assigned, a negotiation step
  ticked, a savings outcome recorded. Each entry carries **when**, **actor**
  (ADR-011's convention, `system:<component>` for pipeline events) and **what
  changed** — and **never the value of an extracted fact**.
- **Out**: supplier communication or outreach (§1.2 non-goal, and
  `percorso-pilota-v1.md:153`), notes and comments, and anything that would need
  a new table.
- **The honest alternative, if that projection does not fit the wave**: **remove
  `activity` from the payload and delete the memberless record type**, rather
  than ship an array that can never fill. A named tab that never lights up is w14
  clause 3 inverted. **What is not acceptable is leaving an unconditional `[]`
  with no decision.** `benchmark` has no such option — NW-62 consumes it.

**4 — NW-22 and NW-62. A market claim has exactly two honest shapes, and "Not yet
available" is not one of them.** Rules the product half of **OQ-w17-004**.

- This ADR `:94-96` binds unchanged: a confident answer **with provenance**, or
  an explicit insufficient-data result — "never a bare precise-looking number
  without provenance". Spec `:538` says the same ("do not present a provider
  percentile without provenance/confidence") and `:496` explains why. Therefore a
  market position or savings band may render as **either**:
  1. a **representative** position — above / in line with / below market —
     carrying its **adapter, sample size and as-of date**; or
  2. the explicit **"insufficient market data"** result.
  **Never a percentile alone, never "market" unqualified, never "Not
  determined".**
- **A fixture-derived figure is never presented as live market truth.** This
  ADR's own "Bad" consequence (`:57-60`) is the reason this seat sits on NW-62 at
  all.
- **The steady state must stop reading as a defect.** "Not yet available" /
  "Not determined" (`contract360ViewModel.ts:150-152`) is a placeholder, not an
  answer. A missing fact is **named**, together with the way to obtain it (a link
  into Review); thin market data **says so**. Spec §8.4 `:392` is the fence, and
  the fence is a labelled answer, not silence.
- **OQ-w17-004, product half**: geography is the **workspace country** (the w14
  profile field) used as the contract's market and **labelled representative**. A
  per-contract geography field is a new capability plus a migration and is **not
  this wave**; a paid lookup is §1.2.

> **N16** — on `dev`: a renewal row with a validated price, and a contract with a
> validated end date and a processed document, each return one of the two shapes
> above; `benchmark` is non-empty or carries the explicit insufficient-data
> marker; every `activity` entry traces to a persisted event, or there is no
> `activity` member at all; **no screen shows a market claim without its
> source**; and neither 360 answer reads "Not yet available" while the facts it
> needs are in the file.

**5 — NW-64. A field extraction did not recover is shown empty and fillable, not
hidden.**

- Review's promise is that you **see** the facts and decide them
  (`percorso-pilota-v1.md:55`, `screens-v2.md:81-93`). Hiding a field because
  extraction returned nothing asserts a completeness the system does not have —
  w14 clause 3, and the exact inverse of the fence clause 2 tightens.
- **Always shown, recovered or not**, mapping spec `:341`'s five critical fields
  onto the Review model: **`annualSpend`, `totalContractValue`,
  `cancellationDeadline`, `endDate`, `renewalTermMonths`** — plus `supplier`,
  `type`, `status`, `currency`, `autoRenewal`, which are already required. Empty,
  **fillable** rows. Other optional fields keep today's behaviour.
- **Two of the five critical fields have no correctable field at all** —
  *termination* and *price uplift*. They are **not invented this wave**: that is
  a schema and extraction change, not a rendering fix. Recorded here as a known
  gap and a W18 candidate, so their absence is bounded rather than silent.

> **N18** — on `dev`: a document where OCR misses end date and cancellation
> deadline → both appear as **empty fillable rows**; filling one persists across
> a reload **and** a second browser; "Mark as validated" still reflects the
> decided set; the test that asserts today's hiding behaviour is **rewritten,
> not deleted**.

**6 — NW-66. Confidence never appears on Contract 360; the Why row carries
leverage instead — and Appendix C `:954` is superseded in its rendering half
only.**

- **A deliberate divergence from the design oracle, recorded as one.**
  `screens-v2.md:101` puts **confidence** on the Why row. W17 removes it. The
  reason is clause 2: after NW-71 confidence is a **decision**, not a score, and
  360 becomes the surface of *officialized* facts (NW-65) — so a percentage there
  re-opens a decision Review has already closed.
- **One rule binds NW-65 and NW-66, stated once so they cannot drift:
  confidence lives in Review; Contract 360 never renders it.**
- **The Appendix C question, which this seat raises rather than waits to be
  caught on.** `product-spec.md:954` reads "**Never show a consequential
  extracted fact without source evidence and confidence metadata**", and a Why
  clause is consequential. It is therefore superseded **in its rendering half,
  on Contract 360 only**, by NW-71 + NW-65 + NW-66 together: every fact 360
  displays is **officialized** (auto-accepted at ≥ 90 % or signed by a human) and
  carries **page · section** evidence, which is a *stronger* reliability signal
  than a score the user cannot act on. The rule stands **verbatim everywhere
  else** — in **Review**, where the confidence *is* what the user decides on, and
  in **Ask**, where a consequential answer still carries source and confidence.
  **The storage half is never touched**: spec `:127` (every critical fact
  preserves document, page/section and confidence) stands, confidence stays
  persisted on `extraction_evidence`, and it remains the input to the decision.
  A rule that is met by a better signal is superseded honestly, not ignored.
- **Leverage vocabulary — this seat's words.** The raw enum
  (`ContractRiskLevel` = Low/Medium/High/Critical) **never reaches the screen**.
  The row reads **"Push to change"** (Critical, High) · **"Worth raising"**
  (Medium) · **"Standard terms"** (Low). Never colour-only (ADR-019), never a
  percentage. The original quote leaves the row.

> **N20** — on `dev`: a Why row shows type, normalized value, page · section and
> exactly one leverage tag in those words; **no percentage and no raw enum string
> anywhere on Contract 360**; the full quote is one click away and is never
> rendered untruncated in the row.

**7 — Priority, the release valves, and the two items that leave the live set.**

- **NW-75 is ruled OUT for V1** — the ruling `w17-requirements.md:767-768`
  explicitly reserves for this council ("if the council rules it **OUT**, it
  leaves the live set entirely and is recorded, not queued"). Four grounds:
  1. **No estimate exists to show.** The oracle's table is Supplier · Action ·
     **Estimate** (`screens-v2.md:135`), and a tracked action holds no
     system-held estimate until an opportunity exists. The row would either
     invent a number — against `E04/F03/US01` AC-3's "never fabricated
     precision" and w14 clause 3 — or ship an undefined blank column. This is
     why w16 retired `buildTrackedOpportunityRow` in the first place.
  2. **Two homes already exist** — Renewals (`screens-v2.md:127-130`) and the
     360 tracker (`:105-108`). A third surface adds no user-visible outcome
     (this ADR `:84-85`).
  3. **The pilot puts this surface out of the demo**
     (`percorso-pilota-v1.md:148`, "Home savings come demo" explicitly out).
     NW-72 is already this wave's Savings work, and it closes a real AC.
  4. **It is `could`, and it is overflow.** OUT removes it from the live set
     instead of parking it, which frees W18's head for NW-63's deferred half.
  **Re-entry condition, so this is a ruling and not a refusal**: it returns as a
  **new item** once a tracked action can carry a system-held estimate — i.e.
  after NW-62's benchmark join (OQ-w17-004). Not before, and never as a
  pseudo-row. **No task, no story, no ADR-020 change.**
- **NW-23 and NW-25 are queued to W18 — queuing, not demotion.** Both are
  `could` and both sit last in the raw file's overflow order. **OQ-w17-007's
  product half travels with NW-23**: Portfolio filters by **supplier category**
  (spec §8.1 `:358` lists Category among the required filters) while
  `screens-v2.md:119`'s "Type" column stays the *document* type — two dimensions,
  never merged. **NW-25, recorded so W18 does not re-litigate it**: the filters
  are the oracle's own columns, **supplier** and **status**; Estimate is a
  **sort**, not a filter, because a range control over a representative band
  implies a precision the band does not have.
- **OQ-w17-001 — the NW-63 split is ratified, on one condition.** A reduced
  `must` is honest; a silently deferred one is not. The split holds **only**
  because `w17-requirements.md:811-813` already makes the remainder the **head of
  W18, ahead of this run's four overflow items**. If the council instead rejects
  the split it must cut **two `should` items** and say which — it may not absorb
  the difference by narrowing a `must`.
- **Priority this seat defends**: NW-72 head (w16 clause 4 already ruled it
  there); **NW-71 before NW-64 / NW-65 / NW-66**, which is a single-writer
  constraint and not a preference; and **no `must` is demoted or queued past the
  next wave** — NW-71, NW-62 and NW-63 all stay in wave.
- **If the cap binds further, the cut order is the raw file's and no other**:
  NW-75 (now OUT), NW-23, NW-25, NW-74 — **NW-74 keeps `should` while queued**.
  **Never narrowed**: NW-71's single bar, NW-72's currency grouping, NW-62's
  provenance rule, and clause 5's always-shown critical fields.

**Not decided here** (recorded so no task reads this footer as authority): the
per-field decision's storage shape and its migration, the payload shapes of
`benchmark` / `activity` / the 360 answers, the rasteriser and the viewer, the
confidence-row treatment and the risk→label table, the screens and their copy,
the console's identity and its Terraform grant, and the contract-file schedule.
Those are software-architect's, ux-ui-designer's, client-architect's,
security-architect's, cloud-architect's and delivery-manager's.

Deciders: product-owner (owner of this ADR), with software-architect co-deciding
clauses 1, 3 and 4 on mechanism and OQ-w17-003/004/006, ux-ui-designer owning the
ADR-019 / ADR-020 half of clauses 1, 2, 5 and 6, client-architect co-deciding the
read-back in clauses 1 and 5, and delivery-manager cited on clause 2's fixture
fence. `reports/architecture/waves/w17.md` carries the per-item rows and the
votes.

### Clauses 8–9, appended at the same table (wave w17)

Clauses 0–7 above are unchanged. Two questions reached this seat **at the
table** rather than in its lane — ux-ui-designer's three-state finding and
delivery-manager's `demo` question, which that seat records as declining to
defer a fourth time. Both are acceptance wording and priority, so both are ruled
here rather than left in another seat's draft. An unrecorded table ruling is not
a ruling.

**8 — "Accepted by the rule" and "accepted by you" are different facts and must
never render alike.** Raised by ux-ui-designer against clause 2.

- After NW-71 a field is in one of **three** states — **auto-accepted**,
  **accepted by a human**, **pending a decision** — and they carry different
  authority: one is the product's judgement, one is the user's signature, one is
  nobody's. Painting the first two identically tells the user they signed
  something they did not. That is **w15 clause 7** (the system must not silently
  drop a fact the user is entitled to) and **w14 clause 3** in the same breath.
- **Ruling: the three states stay distinguishable on every surface that shows an
  acceptance**, in words and not by colour alone (ADR-019). "Accepted
  automatically · NN %" and "Accepted by you" are the *product's* distinction;
  the exact strings and treatment are **ux-ui-designer's** (ADR-019 / ADR-020).
- **Consequence this seat does own**: the wire must be able to carry the
  distinction. A **boolean** collapses two of the three and makes the rule
  unimplementable — but the shape is **software-architect's** and is not decided
  here; what is decided is that the contract may not make the three states
  indistinguishable.
- **A decision is server-computed and read-only to the client** (cited from
  security-architect, not owned): a client-supplied decision is refused, never
  persisted. A display band must never become an authorization or a record.

> **A17-5** — on `dev`: a document with fields on both sides of the bar → a
> field the rule accepted and a field the user accepted are **told apart on
> screen without hovering**; reload and a second browser agree; nothing on screen
> claims the user decided a field the rule decided.

**9 — `demo` is not dormant, and the promotion is not deferred a fourth time.**
Answers delivery-manager's direct ask. Priority ruling only; the mechanism,
the tag and the ordering stay ADR-016's and that seat's.

- **`demo` cannot be declared dormant**, because the pilot path is the
  product's own client-facing acceptance surface and `percorso-pilota-v1.md` is
  run from `demo`. w15 clause 12 already ratified the `demo-v4` cut **as a
  product requirement** on exactly that ground.
- **What changes with w17, and why this is no longer a satellite question.**
  w15's and w16's gaps were the workspace/invite half and satellite surfaces.
  **w17's two flagship items are pilot-script surfaces** — the Review HITL act
  (`percorso-pilota-v1.md:121-122`) and the Contract 360 answers. Shipping w17 to
  `dev` alone therefore leaves `demo` behind on the **hero loop**, not on
  satellites. That is a materially worse state than the one this question was
  deferred in three times.
- **Ruling**: the `demo` backlog is cleared by an **operator promotion decision
  at the w17 HITL gate**. **No wave task promotes** and this clause mints none —
  promotion is a human-gated act (ADR-016). **If that decision is not taken at
  the w17 gate, then W18 opens with the promotion as its head item, ahead of
  every feature**, including this run's overflow. A deferral that never costs a
  slot is what let this reach a fourth wave.
- **Not decided here**: the tag, the approval chain, the seed/flag posture per
  environment and whether w17's two flags default `false` on `demo` — all
  delivery-manager's and cloud-architect's.

Deciders on clauses 8–9: product-owner (owner), with ux-ui-designer raising
clause 8 and owning its copy and treatment, software-architect owning the wire
shape clause 8 constrains, security-architect cited on the read-only rule, and
delivery-manager raising clause 9 and owning its mechanism. Neither clause
amends a decision above; both are additive.

### Clause 10, appended at the same table (wave w17)

Clauses 0–9 above are unchanged. **OQ-w17-ux-04** was raised by ux-ui-designer
at this table **against clause 1 and owed back to this seat**, and it is the
last open question of the w17 table. It is acceptance wording, so it is ruled
here rather than left in another seat's draft.

**10 — the realized-money KPI cell is labelled "Savings verified", and the
screen word is not the domain word.**

- **Correction to this seat's own clause 1, on the record.** Clause 1 requires
  the cell to be "labelled **Verified** (spec `:447`)". Re-read first-hand,
  `product-spec.md:447` is a **two-column** row: the **label** column reads
  **"Savings Realized"**, and "**Verified** negotiated/implemented savings" is
  its **meaning** column. Clause 1 cited the meaning as though it were the
  label. What clause 1 actually carries is the **evidence standard** that word
  states — not the bare string — and this clause says so before anyone
  implements the narrower reading.
- **Ruling: "Savings verified".** ux-ui-designer's proposal is adopted whole,
  for its own structural reason and for one product reason it did not have to
  make:
  1. **Parallelism** (ux-ui-designer's, adopted): the band's other three labels
     are noun phrases naming a thing counted — "Contracts analyzed", "Upcoming
     renewals", "Savings identified" (`savingsViewModel.ts:85-87`). In a row of
     **equal** cells (`design-system.md:45`) a bare "Verified" would be the only
     cell naming a **status**, and would read as a filter or a state rather than
     as money.
  2. **"Realized" is the word attached to the defect.** The bucket that sums
     estimates today is called `Realized` (`SavingsKpiCalculator.cs:86`,
     `:103-104`). NW-72's point is the **evidence** difference between the two
     money cells — a pre-negotiation estimate versus a recorded outcome — not a
     lifecycle step. Labelling the repaired cell with the defective bucket's own
     word leaves the screen reading exactly as it did before the fix; "verified"
     states the standard the number now meets, which is the half of spec `:447`
     that matters.
- **Fence — the screen word and the domain word are allowed to differ, and
  nothing is renamed to "align" them.** `RealizedSavings`
  (`RealizedSavings.cs:26`), `realizedAmount`
  (`SavingsEndpointExtensions.cs:169-171`) and the domain bucket keep their
  names; software-architect's **declared** break on `SavingsKpiSummary.Realized`
  (ADR-028 w17 footer) stays the **only** rename this wave, and no task may
  widen it into a vocabulary sweep on the strength of this label.
- **Deliberate divergence from the spec's label column, recorded as one.** It
  joins the divergence already ratified for this cell — `screens-v2.md:134-135`
  carries no realized-money slot at all (ADR-020 w17 §17). The spec's **meaning**
  column is honoured verbatim; its label column is not.
- **Unchanged by this clause**: the whole of clause 1 — money only from
  `RealizedSavings` rows, **grouped by currency, never summed across them**; an
  outcome with `savingsPropagated: null` enters no total; absent → the existing
  `—` idiom, never a fabricated `0`; provenance in the cell's `meta`.

> **A17-S1 gains one check**: the cell is labelled **"Savings verified"**, and
> the estimate's word never labels it; the pre-negotiation estimate appears
> nowhere in that cell.

Deciders on clause 10: product-owner (owner and ruler), ux-ui-designer (raised
it, owns the treatment and the `meta` copy), software-architect (cited on the
declared wire break the fence bounds). The clause **amends no decision above**:
it corrects a citation inside clause 1 and fixes the string clause 1 left
ambiguous. Clause 1's substance is untouched.
