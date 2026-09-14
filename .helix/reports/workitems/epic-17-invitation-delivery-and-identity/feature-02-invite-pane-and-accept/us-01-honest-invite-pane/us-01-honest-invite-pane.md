---
id: us-01
type: user-story
parent: feature-02
wave: w15
status: active
---

# us-01-honest-invite-pane — The pane renders one server fact, and accept completes in one click

## Story

As a **workspace Admin**, I want the invite pane to tell me exactly what
happened — sent, or created but not emailed, or refused and why — so that I
never copy a link that cannot be used and never believe a mail went out that
did not. And as an **invited colleague**, I want clicking the link and signing in
to put me inside the workspace, without a second button.

## Acceptance criteria

- [ ] AC-1 `web/openapi/raffa-api.v1.json` declares `identityProvisioned` (non-nullable boolean), `deliveryOutcome` (non-nullable string with an `enum` of exactly `sent | mail_failed | no_transport`) and a **502 response** carrying `failureReason` with its closed reason set. The client is regenerated from it; nothing is hand-written as a DTO.
- [ ] AC-2 The pane branches on **one** server string with exactly **three** arms — no fourth, no second boolean, no client inference from a status code.
- [ ] AC-3 The link block renders only when the link is **usable**: `sent` → no link; `mail_failed` → link shown (it is the only way in); `no_transport` → link shown. A provisioning failure produces **no 201 at all**, so there is nothing to render a link for.
- [ ] AC-4 The three 201 strings are exactly: *"Invitation sent to {email}."* · *"Invitation created, but the email could not be sent."* (+ link block) · *"Invitation ready for {email}."* (+ link block).
- [ ] AC-5 The **"Try sending again" affordance does not ship.**
- [ ] AC-6 The 502's reason renders in the existing pre-creation error slot, each with the shared `.micro-meta` **"No invitation was created."**, plus a **fourth, catch-all row** so an unrecognised or proxy-mangled value can never put a raw wire enum on screen. `consent_missing` names **"a tenant administrator"**, not "you".
- [ ] AC-7 One sentence about identity — *"They will get a one-time code from Microsoft the first time they sign in."* — keyed to `identityProvisioned === true`, rendering **nothing at all** when provisioning is not configured. It says the identical thing whether the guest was created or already existed.
- [ ] AC-8 There is **no pre-send identity state** and **no fifth member status**.
- [ ] AC-9 On the accept screen, "Continue with Microsoft Entra ID" **awaits its own `loginPopup` promise and continues into the accept on resolution**. A visitor who arrives **already signed in** still reaches state 3 with its explicit "Join {workspace}" button. If the GET has not resolved when the popup does, the accept **waits for the offer**.
- [ ] AC-10 Popup-blocked keeps its current landing (state 5), and nothing else on the accept route changes — not the 8-phase union, not the fragment capture, not the storage rule.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-invite-provisions-and-mails (`E17/F01/US01/T01`) | the pane renders server facts that must already exist on the wire |
| us-02-spa-sends-the-token (`E18/F01/US02/T01`) | this task edits `client.ts`, which that task restructures from a synchronous header spread into one async token helper; and the accept binds to the server-verified identity that task supplies |

## Architecture decisions in force

- **ADR-020** (w15 §3) — every string and every state named above.
- **ADR-012** (w15 §7, §8, §13.1–§13.3) — the discriminant, the typed `failureReason`, `identityProvisioned`'s nullability, the auto-continue and its three reasons.
- **ADR-026** (w14 clause 3, w15 §8–§9) — `role` stays a bare wire string and its least-privilege parse is mandatory; the outcome field, by contrast, is a closed server vocabulary and therefore a literal union.
- **ADR-019** — no new row, token or component.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | The invite contract, the three-outcome pane, and accept in one click | M | phase-4 |

## Council decisions carried into this story

**The code names this wave as its owner.** `memberViewModel.ts:166-173` is a
discriminated union on the server boolean `mailDelivered` whose own doc comment
reads verbatim: *"not a third, invented state; **the transport wave that needs a
third value owns that change**, not this one"*.

**Four outcomes became three, and the mechanism is strictly stronger than the
rule it replaces.** A provisioning failure aborts the invitation, so no 201 can
carry "guest provisioning failed". The rule *the link renders only when it is
usable* now holds **by construction** rather than by a branch a task could get
wrong — and the task writes **three** arms and no fourth, because a dead branch
keyed to the link is exactly the one a later reader re-enables.

**The catch-all reason row is mandatory even though `tsc` will call it dead
code**: a literal union describes the contract, not the wire.

**`composeAcceptLink` needs no change at all.** `memberViewModel.ts:175-179` is
`new URL(acceptUrl, origin).toString()` and its own comment already states it
accepts a site-relative **and** an absolute value unchanged. Recorded so no task
is written for it.

**The auto-continue is deliberately not a `useEffect` on `accounts`**: it is the
same user gesture and the consent the button carries is consent to join; it fires
only on the transition this screen initiated, so a visitor already signed in
still gets state 3's explicit consent step; and an effect keyed on an MSAL
account array can fire for reasons that are not this sign-in.

## Open questions

- **OQ-w15-ca-02** — resolved by decision rather than by assertion: the accept binds to a server-verified identity and to the `oid` bound at invite time. The residual B2B walk is a numbered acceptance step.
- None blocking.
