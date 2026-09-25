# Raffa — next-waves input · Product Analytics

Status: **binding input** for a next-wave requirements document. Written
2026-09-25 by the founders, third file of this round after
`2026-09-25-security-hardening-and-certification.md` and
`2026-09-25-governance-and-legal.md`. Purpose: know whether a demo or a
pilot is actually working, before there is a paying customer to tell you.

IDs are new and stable (`ANA-nn`); they do not collide with any prior
prefix in this folder.

| | |
|---|---|
| Companion inputs | `2026-09-25-production-readiness-deferred.md` (SR-10, the *customer-facing* usage panel — this file is the founders'-facing counterpart, internal, ships first); `2026-09-25-governance-and-legal.md` (GOV-01/GOV-09 — this file's data is Raffa's own controller-role usage data, never contract content) |
| Verified today (2026-09-25) | No analytics SDK anywhere (`web/package.json` has none of PostHog/Mixpanel/Amplitude/GA/App Insights); `infra/modules/monitor` provisions a Log Analytics workspace for **infrastructure** telemetry (API latency, errors) only, per spec §15.1 — nothing captures a *product* event today (which screen, which action, how far a user got); no demo or pilot session has ever been instrumented |
| Product oracle | `inputs/product-spec.md` §15.1 (telemetry categories — this file is the "Product" row the spec names but nothing implements), §17.2 (product metrics: documents uploaded, queries per user, renewals acted on, savings realized — all listed, none measured) |

## 0. Why this is urgent now, not later

The founders' own priority for this round is landing the first customer
through a demo. A demo that goes well in the room and is then never opened
again is a failure you cannot see without this file. The two questions this
file exists to answer, in order: **did the prospect we demoed to ever come
back?** and **once a real workspace exists, did they get from upload to a
first "aha" (a citation, a renewal, a saving) or did they stall?** Neither
question has an answer today.

## 1. Binding instructions

1. **This is Raffa's own usage data, not customer contract content.** Per
   `GOV-09`, Raffa is controller for this category. Never log a question's
   text, a document's content, or anything `LoggingAiGateway` already
   deliberately excludes (ADR-011) — event names, screen names, durations,
   counts, ids. This file adds no new sensitive-data surface.
2. **Consent and disclosure are not optional.** Whatever this file ships
   is covered by `GOV-01`'s privacy policy (a "usage analytics" category
   must exist in the RoPA before this ships, not after) and, if any
   client-side tool sets a cookie, by `GOV-11`.
3. **Self-hosted or EU-hosted only.** A third-party analytics SaaS that
   stores event data outside the EU, or that itself trains on data it
   collects, contradicts `SR-01`'s data-residency non-goal in spirit even
   though usage events are not contract content — pick a tool and a region
   deliberately, do not default to whatever has the easiest npm install.
4. **Founders-facing first, customer-facing later.** This file builds the
   internal dashboard the founders read after every demo and pilot
   session. `SR-10` (deferred) builds the customer-safe subset an Admin
   can see. They share the event stream; they are not the same screen.
5. **Cheap and fast beats complete.** The first version should ship in
   days, not weeks — a handful of events and one dashboard, expanded once
   real usage exists to learn from.

## 2. Items

### ANA-01 — Pick and wire one analytics tool, EU-hosted, no cost until scale (must)

- **What it should be:** one client-side + one server-side event capture
  path into a single tool. Recommended: **PostHog** (self-hostable or its
  EU Cloud region, generous free tier, session replay available later if
  wanted, open source so it never becomes a second vendor lock-in
  question) — the founders confirm or pick an alternative, but the choice
  is made once, deliberately, per instruction 3.
- **Acceptance:** a page view and one custom event reach the tool's
  dashboard from `dev` within a day of setup; the tool's own data region
  is confirmed EU.
- **Seats (hint):** client-architect, software-architect, founders
  (tool choice).

### ANA-02 — The event taxonomy: what gets tracked, named once, added to as needed (must)

- **What it should be:** a short, stable event list, not an ever-growing
  ad hoc set. Minimum for the pilot funnel (spec §17.2's own list, made
  trackable):
  - `workspace_created`, `first_sign_in`
  - `document_uploaded`, `document_completed`, `document_failed`,
    `document_rejected`
  - `review_opened`, `field_accepted`, `field_corrected`,
    `contract_validated`
  - `ask_turn_sent` (with `intent` and `corpus` from the existing reply
    kind — never the question text), `citation_opened`,
    `ask_abstained`
  - `renewal_action_started`, `renewal_action_completed`
  - `savings_opportunity_viewed`, `savings_verified`
  - `quote_check_run`
  - `member_invited`, `member_accepted`
  - screen views for every top-level route
  Every event carries `workspaceId`, `userId` (hashed if the chosen tool
  does not already scope access), `role`, and a session id — never
  contract-derived text.
- **Acceptance:** a full pilot walkthrough on `dev` (the acceptance walk
  already written into `CS-*`/`SR-*` files) produces every event above,
  once each, in the right order.
- **Seats (hint):** product-owner (owns the taxonomy), software-architect,
  client-architect.

### ANA-03 — The founders' dashboard: one screen, the pilot funnel (must)

- **What it should be:** a single dashboard (in the analytics tool itself,
  not a custom-built Raffa screen — do not build product to watch product)
  showing, per workspace: time from `workspace_created` to first
  `document_uploaded`, to first `contract_validated`, to first
  `ask_turn_sent`, to first `citation_opened` — the funnel a demo prospect
  is supposed to walk, instrumented so the founders see exactly where a
  real prospect actually stalls after the meeting ends. A second view:
  workspaces with zero activity in the last 7 days (the ones about to go
  cold, worth a founder's phone call).
- **Acceptance:** the dashboard is bookmarked and checked; it correctly
  shows a `dev` walkthrough's funnel with no manual data-pulling.
- **Seats (hint):** product-owner, founders.

### ANA-04 — Post-demo silent tracking: did they ever come back? (must)

- **What it should be:** every prospect workspace created from a demo
  (tagged at creation — a simple `source: demo` property) is watched for
  return visits after the meeting ends. This is the single most important
  number for the founders' stated near-term goal: not "did the demo go
  well in the room" (unmeasurable, subjective) but "did they open Raffa
  again without us in the room." A weekly digest (email or a Slack/Teams
  webhook if `SR-11` exists yet, otherwise a plain scheduled email to the
  founders) listing every demo workspace's post-meeting activity.
- **Acceptance:** creating a workspace on `dev` tagged `source: demo` and
  returning to it a day later shows up in the digest.
- **Seats (hint):** software-architect, product-owner.

### ANA-05 — AI cost and usage per workspace, founder-visible (should)

- **What it should be:** internal cost telemetry already required by spec
  §15.1/§15.2 exists at the infrastructure level; this item makes it
  queryable per workspace so a founder can see, before it becomes a
  surprise invoice, which pilot is burning the most model spend relative
  to the value it is getting (ties to `SEC-07`'s per-tenant AI budget —
  this is the visibility half, SEC-07 is the enforcement half).
- **Acceptance:** a founder can see, per workspace, AI calls and estimated
  cost for the current period on `dev`.
- **Seats (hint):** cloud-architect, software-architect.

### ANA-06 — Error and friction signals, not just happy-path events (should)

- **What it should be:** alongside the happy-path funnel, track the
  moments that predict churn before it happens: an upload that fails
  twice for the same workspace, an Ask turn that abstains three times in
  a row, a review queue untouched for 48 hours with documents waiting.
  These feed the same weekly digest as ANA-04 and, once `CS-10`'s mascot
  ships, are exactly its reactive trigger list — this item and `CS-10`
  should share one detector implementation, not two.
- **Acceptance:** forcing each friction condition on `dev` produces the
  corresponding signal in the founders' view.
- **Seats (hint):** software-architect, client-architect.

## 3. Order constraints

- ANA-01 before everything else (nothing to wire events into otherwise).
- ANA-02 and ANA-03 together, first wave — this is the minimum that
  answers "is the demo working."
- ANA-04 depends only on ANA-01/02 and is cheap; ship it in the same wave
  as ANA-03, it is the item that most directly serves the founders'
  stated immediate goal.
- ANA-05 and ANA-06 can follow once a real pilot exists to observe.

## 4. Cancels / touches

- Cancels nothing. Supplies the technical seam `SR-10` (deferred) later
  reads from for the customer-facing subset. Shares its detector logic
  with `CS-10`'s reactive mascot triggers once that item builds.

## 5. Acceptance walk

1. Create a `dev` workspace tagged `source: demo`; walk the full pilot
   script (upload → review → validate → ask → citation → renewal action);
   every event in ANA-02 appears once, in order, on the dashboard.
2. Leave the workspace untouched for a day; it appears in ANA-04's digest
   as returned or not, correctly.
3. Force a document to fail upload twice; the friction signal fires.
