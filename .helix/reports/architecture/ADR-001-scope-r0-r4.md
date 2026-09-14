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
