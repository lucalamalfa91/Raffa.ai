# Raffa — next-waves input · ICP, Pricing & Go-to-Market

Status: **binding input**, fourth file of this round. Written 2026-09-25 by
the founders. Purpose: close the open question every other file in this
round pointed at and never answered — who is Raffa's customer, what do
they pay, and how do you get one in front of a demo. Unlike `CS-*`, `SR-*`,
`SEC-*`, `GOV-*` and `ANA-*`, most items here are **not engineering work**;
they are decisions and materials the founders own directly, with a small
engineering tail where a decision needs a product surface (a pricing page,
a signup flow, an analytics tag). Kept in `inputs/next/` anyway because the
engineering tail does feed the Helix process, and because every other file
in this round cites this one as an open dependency.

IDs are new and stable (`GTM-nn`).

| | |
|---|---|
| Companion inputs | Claude Doc "Raffa.ai vs Vertice — analisi competitiva e proposte" (2026-09-24) — ICP and pricing were left as open questions there; this file answers them; `2026-09-25-governance-and-legal.md` (GOV-05's liability cap is stated as "12 months' fees" — meaningless until GTM-02 sets a fee); `2026-09-25-production-readiness-deferred.md` (SR-10 usage panel is "a prerequisite the moment any pricing model is decided" — this file is that decision) |
| Verified today (2026-09-25) | No pricing, billing, plan or ICP documentation exists anywhere in the repository (confirmed by the earlier product-map audit); no public marketing site exists — the SPA itself has no unauthenticated landing page beyond sign-in; no sales collateral (pitch deck, one-pager, comparison page) exists on disk |

## 0. The founders' stated priority, restated as the frame for every decision below

**Near-term goal, in the founders' own words: find someone to be a
customer.** Every choice in this file is judged by one question — does it
help get a demo in front of a real prospect, and does it make that prospect
trust Raffa enough to say yes — not by whether it is the "correct" B2B SaaS
playbook in the abstract. A pricing model that is perfectly optimized for
unit economics at scale but takes three weeks to explain in a first meeting
is the wrong answer right now. A gorgeous marketing site with no product
behind it is also the wrong answer. This file picks the version of each
decision that gets to "yes" fastest without lying about what Raffa can
deliver today (the same honesty rule as every prior file in this round).

## 1. Binding instructions

1. **Every claim in any GTM material must be true today**, not aspirational
   — the same rule `SEC-27`'s "public claims allowed at each stage" and
   `GOV`'s §0 set for legal and security copy applies here: a demo script
   that promises a feature not yet built (`SR-11`'s Teams connector, for
   instance) is a trust failure the moment the prospect asks to see it.
   Aspirational items are named as roadmap, openly, in the same breath —
   which is itself a credibility move against Vertice's own "process vs
   profit" framing.
2. **ICP and pricing decisions here are provisional until the first three
   real conversations test them**, not fixed for a year. This file's
   default answers are the founders' best current call plus the
   competitive review's evidence, not a locked strategy.
3. **No feature is promised in a sales conversation that is not either
   shipped or explicitly on the roadmap the prospect can see.** The
   phased roadmap document (this round's next deliverable) becomes the
   thing a founder can literally show a skeptical prospect who asks "what
   happens after we sign."
4. **GTM materials never contradict SEC/GOV.** A claim like "your data is
   never touched by anyone" is only usable once `SEC-03` ships; until
   then, the honest version ("isolated per tenant at the database level,
   verified in our own code, full access controls shipping this quarter")
   is what goes in the deck.

## 2. Items

### GTM-01 — ICP: confirmed default, narrow on purpose (must — decision, no engineering)

- **Default (confirmed unless the founders override in §4):** Italian and
  DACH **mid-market companies without a structured procurement function**
  — 50 to 500 employees, a CFO, COO or operations lead who personally
  feels contract chaos as pain (missed renewals, no idea what they're
  paying, no time to negotiate), not a dedicated buyer with existing
  tooling. This is the space the competitive review identified as
  completely unserved by Vertice (which needs ≥$250k SaaS spend and a
  procurement team) and by every CLM vendor (which needs a legal team).
  Industries already modeled in the workspace picker (`manufacturing`,
  `food & beverage`, `financial services`, `software`) are the first
  targets, in that order — manufacturing and food & beverage because they
  have real non-SaaS spend (logistics, energy, packaging, services) that
  is Raffa's actual differentiation, not SaaS-only where Vertice already
  competes.
- **Why this default, not the alternative:** the competitive review's own
  analysis showed Vertice's price floor and integration-first onboarding
  leave this exact segment unserved; a demo to a company that already has
  a procurement team invites a direct Vertice comparison Raffa is not yet
  ready to win on data volume. A demo to a company with none invites no
  comparison at all — Raffa is simply better than a shared drive.
- **Acceptance:** the first outreach list (GTM-06) is built against this
  definition, not a broader one.
- **Owner:** founders (decision only).

### GTM-02 — Pricing: simple enough to say in one sentence, hybrid by design (must — decision + small engineering tail)

- **Default:** a low, flat monthly base fee sized to be a rounding error
  for a 50-500-person company (not a procurement-software-budget-line
  decision that needs three approvals) **plus** a percentage of
  **verified** savings (the `Savings verified` KPI already computed by the
  product) once a saving is actually realized — never on identified or
  in-progress opportunities, which is exactly the "Vertice measures
  against list prices few pay" criticism the competitive review flagged
  about Vertice's own guarantee. This is provisional, capacity-light, and
  says in one sentence: "a small monthly fee, and a small cut only of the
  money we actually save you, only after it's real."
- **First-customer variant, explicit:** the very first paying customer
  (the near-term goal) can be offered a **free or heavily discounted
  pilot period** (60-90 days) in exchange for being a reference and a case
  study — this is a deliberate, one-time exception to get to `yes` faster,
  never advertised as the standing price.
- **What needs engineering, later, not now:** nothing before a first
  customer signs — the fee can be a manually issued invoice for the first
  handful of customers. `SR-10`'s usage panel and any billing automation
  are explicitly Phase 2 work (see the roadmap document), not a blocker to
  closing the first deal.
- **Acceptance:** the one-sentence pricing explanation is usable, unedited,
  in a first sales call; the founders confirm the base-fee number and the
  savings-share percentage before the first quote goes out.
- **Owner:** founders (decision); software-architect only once billing
  automation is actually scheduled (not in this file's scope).

### GTM-03 — The demo itself: harden the existing pilot script, don't reinvent it (must)

- **Today:** `percorso-pilota-v1.md` already defines a strong 20-25 minute
  live demo script (upload → HITL → Ask structured → Ask RAG + abstain →
  a satellite screen). It was written for an internal/investor audience,
  not tuned for a skeptical first-time prospect who will ask hard
  questions mid-demo.
- **What it should be:** the same script, plus a **prepared answer sheet**
  for the five questions a real prospect asks that the current script
  does not anticipate: "where is my data stored" (SEC/GOV's honest
  today-answer, not the Phase-2 aspiration), "what if the AI gets
  something wrong" (the confidence system, human validation, `answer/
  v2.5.md`'s own doctrine), "how much does this cost" (GTM-02's one
  sentence), "can my team use this without IT" (yes today, guest invite;
  federation is roadmap), "what happens if I stop paying" (export before
  deletion, once `SR-08`/`SEC-05` ship — until then, the honest answer is
  simpler: "email us, we'll get you your data"). This answer sheet is the
  single highest-leverage artifact in this file for the founders' stated
  goal — it is what turns "impressed" into "trusts us."
- **Acceptance:** both founders can run the demo and answer all five
  questions from the sheet without notes, rehearsed at least twice against
  each other before the first real prospect meeting.
- **Owner:** founders.

### GTM-04 — A public page that exists before the first demo, not a full marketing site (must, small)

- **What it should be:** not a marketing site build-out — one page,
  outside the app, unauthenticated: what Raffa does (the four-pillar
  headline from the design brief — "Your contracts. Your savings. Nothing
  missed."), who it's for (GTM-01's ICP, in plain words), a "book a demo"
  action (a Calendly-style link is enough, no CRM needed yet), and — once
  ready — a link to the privacy policy (`GOV-01`) and the "Raffa vs
  Vertice" honest comparison the competitive review proposed. This exists
  so a prospect who got a demo can send the link to a colleague, and so a
  cold-outreach email has something to point to that is not "trust me,
  the product only exists behind a login."
- **Acceptance:** the page is live at a real domain, loads without
  authentication, and the "book a demo" action reaches the founders.
- **Owner:** founders (copy), client-architect (a static page, minimal
  build — reuses the design system, no new stack).

### GTM-05 — The comparison page: "Raffa vs Vertice", honest about the perimeter (should)

- **What it should be:** the competitive review's own positioning table,
  turned into one public page: what Vertice does that Raffa does not (and
  says so plainly — SaaS/cloud spend, expert buyers, a 250,000-contract
  dataset), what Raffa does that Vertice does not (any spend category,
  documents in Italian and German as the primary case, upload-first, no
  30,000-dollar floor). Never disparaging, always factual, cited the same
  way the competitive review itself was — this is also good SEO for
  anyone searching "Vertice alternative Italy."
- **Acceptance:** every claim on the page traces to the competitive
  review's own sourced findings; nothing overstates what Raffa ships today
  per instruction 1.
- **Owner:** product-owner, founders.

### GTM-06 — The first outreach list: name ten companies, not a market segment (must, no engineering)

- **What it should be:** GTM-01 defines a segment; this item makes it
  concrete. Ten named companies (or contacts) matching the ICP, reachable
  through an existing relationship (a founder's own network is worth
  more, this early, than any cold outbound tool) — manufacturing and food
  & beverage first per GTM-01's ordering. For each: who is the contact,
  what is the one sentence that would make *them specifically* say yes to
  a 20-minute call (their actual pain, not a generic pitch).
- **Acceptance:** the list exists with ten names and a one-line hook each;
  the first three calls are booked.
- **Owner:** founders.

### GTM-07 — A one-page leave-behind, not a deck (should)

- **What it should be:** a single PDF a prospect keeps after the meeting
  — the four pillars, the ICP-specific pain it solves, GTM-02's pricing
  sentence, and a QR/link to GTM-04's page. Not a 20-slide deck nobody
  reopens. Doubles as what a prospect forwards internally to get budget
  sign-off, so it must stand alone without a founder narrating it.
- **Acceptance:** a friendly outsider who was not in the demo can read the
  one-pager alone and correctly explain what Raffa does and costs.
- **Owner:** founders, product-owner.

### GTM-08 — First-customer success criteria, defined before they say yes (should)

- **What it should be:** a short, written definition of what "the pilot
  worked" means for the first customer specifically (e.g., "at least one
  real renewal caught before its deadline" or "at least one savings
  opportunity marked verified within 90 days") — agreed with the customer
  at signup, not invented after the fact. This becomes the case study's
  actual evidence and the trigger for GTM-02's pilot-to-paid conversion
  conversation.
- **Acceptance:** the criteria are written down and shared with the first
  customer before their data starts flowing.
- **Owner:** founders.

## 3. Order constraints

- GTM-01 and GTM-02 first — every other item quotes them.
- GTM-03 (demo hardening) and GTM-06 (the named list) can run in parallel,
  immediately — they are the two highest-leverage, lowest-effort items for
  the stated near-term goal.
- GTM-04 before GTM-05 and GTM-07 (they link to it).
- GTM-08 lands right before the first real signup, not earlier (no value
  defining success criteria with no prospect in the room yet).

## 4. Decisions requested (recorded here, answered by the founders)

- [ ] **ICP (GTM-01)**: confirm the default, or name a different first
  segment.
- [ ] **Pricing (GTM-02)**: confirm the base-fee number and the
  savings-share percentage; confirm the first-customer pilot discount.
- [ ] **The first ten names (GTM-06)**: who, specifically.

## 5. Acceptance walk

1. Both founders run GTM-03's demo end to end and answer all five prepared
   questions without hesitation.
2. GTM-04's page is live and the "book a demo" link works from a phone.
3. Three calls are booked from GTM-06's list within two weeks of this file
   being acted on.
