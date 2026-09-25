# Raffa — next-waves input · Governance & Legal

Status: **binding input** for a next-wave requirements document. Written
2026-09-25 by the founders. The instruction, verbatim: *"i nostri clienti
dovranno essere tutelati e informati su quanto abbiamo intenzione di fare
con i loro dati … parola chiave trasparenza … però usa trappole (cavilli)
per far pendere la legge sempre dalla nostra parte su punti che potrebbero
essere controversi."*

IDs are new and stable (`GOV-nn`); they do not collide with `NW-*`, `CS-*`,
`SR-*` or `SEC-*`. This file specifies **legal content** (what a lawyer
drafts) paired with **the product surface that makes it real** (what an
engineer builds: consent capture, versioning, toggles, audit trail) — the
same split `SR-08`'s DPA item already used. No item here replaces a lawyer;
every item names where one is required.

| | |
|---|---|
| Companion inputs | `2026-09-25-security-hardening-and-certification.md` (SEC-25 ISMS, SEC-26 GDPR artefacts — this file supplies their customer-facing text); `2026-09-25-production-readiness-deferred.md` (SR-08 trust page/DPA template — this file is SR-08's content spec); `2026-09-25-saas-readiness-and-integrations.md` (SR-01 data-residency non-goal — this file's contracts must never contradict it) |
| Product oracles | `inputs/product-spec.md` §14.2 (AI privacy — no training on customer content), §14.3 (data lifecycle, export/deletion), ADR-030/ADR-031 (public GitHub issues, privacy scrub) |
| Verified today (2026-09-25) | No privacy policy, terms of service, cookie policy or DPA exists anywhere in the product or repo; `LICENSE` is MIT, copyright held by one founder personally, not a company entity; of ~180 commits on this checkout, 77 are authored "Claude" — the codebase is majority AI-authored, which is legally relevant to GOV-08 below |

## 0. The operating principle: asymmetric, never deceptive

The founders asked for the law to lean toward Raffa on genuinely
discretionary points. That is normal, professional contract drafting —
every SaaS vendor's terms favor the vendor on liability caps, IP in derived
data, governing law and fee changes, Vertice's included. This file does
exactly that. It does **not** do the other thing the word "cavilli" can
mean — a hidden or misleading term — for two concrete, non-moral reasons
that belong in a requirements file:

1. **It does not survive Italian law.** Articles 1341-1342 of the Codice
   Civile void a one-sided clause (limitation of liability, unilateral
   withdrawal, unilateral contract modification, arbitration) in a
   standard-form B2B contract unless the counterparty gives **specific,
   separate written approval** for that exact clause — a second signature
   or, in e-commerce practice, a second explicit click, not buried in a
   wall of text. GDPR Art. 5(1)(a) makes "transparent processing" a
   binding principle, not a preference; a hidden data-use term is a
   compliance finding waiting to happen, and SEC-26 (GDPR artefacts) would
   have to record it as a violation of its own RoPA. **A clause that does
   not survive scrutiny protects nothing** — it only creates the appearance
   of protection until the first dispute.
2. **It contradicts the product this company sells.** Every prior file in
   this round is built on "no evidence, no claim" and "nothing is touched
   without specific authorization" as Raffa's actual competitive edge over
   Vertice. A customer, a journalist or a regulator who finds one deceptive
   line in the terms does not just win that clause — they get to say Raffa
   lied about the one thing it promised. This is a commercial risk stated
   in plain terms, not a lecture.

**What this file does instead:** every item below takes Raffa's side on a
real, disclosed, defensible point, written to survive both an Italian judge
and a customer's own reading of it. Where Italian law specifically requires
the double-approval mechanism, the item says so as an engineering
requirement (a specific checkbox, not a buried paragraph), because that is
what makes the favorable term **actually enforceable** rather than
decorative.

## 1. Binding instructions

1. **Every legal document ships with a plain-language summary next to the
   full text**, not instead of it — a short "what this means" box (à la
   GDPR's layered-notice best practice), reusable component across privacy
   policy, DPA and ToS.
2. **No legal document may promise less than the product's own security
   and trust doctrine, or more than it can prove.** The ToS's liability
   language must not disclaim responsibility for a breach that SEC-03's
   "no standing human access" control exists specifically to prevent —
   doing so would be Raffa's own lawyer contradicting Raffa's own security
   architect in writing. Liability limits apply to ordinary commercial
   risk (service interruption, an AI extraction error the confidence
   system already flagged), never as a shield for Raffa's own negligence
   or a broken promise from another file in this round.
3. **A clause requiring specific approval under art. 1341-1342 c.c. is
   built as its own explicit UI action** (its own checkbox or initial,
   named in the acceptance flow, logged with a timestamp) — never inferred
   from "I accept the terms" alone. GOV-05 lists which clauses need this.
4. **The data-flywheel clause (GOV-04) is the one place this file must be
   most conservative, not most aggressive** — it is the point most likely
   to become "controversial" in the founders' own sense, and it is also
   the one that only works at all if it survives a regulator's reading of
   it (see §0.1). Recommendation default: **opt-in**, visible and
   one-click reversible in the Savings dashboard, not opt-out — this is
   the founders' own "trasparenza" instruction applied to the hardest
   case, and it is also the safer legal position until GOV-10's
   anonymization standard is confirmed to remove the data from GDPR's
   scope entirely. The founders may choose opt-out once GOV-10 confirms
   true anonymization; that choice is recorded in §5, not assumed here.
5. **A real lawyer, not this file, produces the final legal text.** Every
   GOV item is a content and requirements brief for that lawyer plus the
   product surface around it — never a document to publish as-is.
6. **Out of scope for Helix (flagged, not built):** founders' equity and
   IP-assignment agreement between the two founders and any contractor;
   moving the company's IP (including the MIT-licensed code) into a proper
   corporate entity if not already there; D&O and cyber-liability /
   tech-E&O insurance sized to the liability caps GOV-05 sets. These are
   real and urgent but are not product backlog — see §6.

## 2. Items

### Track A — Customer-facing transparency (the half that protects the customer)

#### GOV-01 — Privacy policy (GDPR Art. 13/14) with a versioned, product-served text (must)

- **Content brief for the lawyer:** what personal data Raffa processes and
  why (contract signatories' and counterparties' names found inside
  uploaded documents; the customer's own users' account data; usage
  telemetry) — mapped exactly to `SEC-26`'s Records of Processing
  Activities, no daylight between the two documents; legal basis per
  category; retention periods matching `SEC-05`'s erasure receipts;
  data-subject rights and how to exercise them; international transfer
  basis for any non-EU sub-processor (see GOV-03); the no-training
  commitment (spec §14.2) stated as a **customer-facing promise**, not
  only an internal control.
- **Product surface:** the policy text is served and versioned like
  `CS-05`'s help articles (an API-served, dated document, not a static
  page frozen at launch); every version bump is timestamped; a banner
  shows existing users a summary of what changed, in the product, on next
  sign-in, never only by email.
- **Acceptance:** the live policy on `/privacy` matches the RoPA's
  categories one-for-one; a version history is visible; changing a
  category in the RoPA without a matching policy update fails a manual
  review checklist (automating this fully is a later item, not this one).
- **Seats (hint):** product-owner, security-architect, founders + external
  counsel (drafting).

#### GOV-02 — First-upload data-use disclosure, short and honest (must)

- **What it should be:** one short, plain-language card shown once, at
  the first document upload in a workspace (reusing the onboarding hero
  slot from `percorso-pilota-v1.md`'s "Carica → Elabora → Chiedi"):
  what happens to this file (extraction, storage location, who can see
  it), what never happens (no training on its content, no human reads it
  without an audited, authorized reason — GOV-02 is where `SEC-03`'s
  break-glass guarantee becomes a customer promise the customer actually
  reads), and a link to the full privacy policy and DPA. Never a wall of
  text; never buried in a settings page nobody opens.
- **Acceptance:** the card appears once per workspace on the first upload,
  is dismissable, and is reachable again from Workspace & members.
- **Seats (hint):** product-owner, ux-ui-designer, client-architect.

#### GOV-03 — Live sub-processor registry (must)

- **What it should be:** a public page listing every sub-processor
  (Microsoft Azure incl. Foundry/OpenAI and Document Intelligence, Azure
  Communication Services, GitHub for the feature-request loop, and any
  future connector provider from `SR-04`/`SR-11`/`SR-12`), each with its
  role, data category touched, and region. Sourced from the same asset
  inventory `SEC-25` already requires internally — this page is that
  inventory's public face, not a second list to keep in sync by hand.
  Admins who opted into email notice (a checkbox, not a default) get
  notified before a new sub-processor is added, with an objection window
  before it goes live for their workspace, matching Art. 28(2) GDPR's
  sub-processor consent requirement.
- **Acceptance:** the page lists every sub-processor Terraform actually
  provisions, with none missing and none stale after a new connector
  ships; a test compares the page's list against the infra inventory and
  fails on drift.
- **Seats (hint):** cloud-architect, product-owner, security-architect.

#### GOV-04 — The data-flywheel clause: consent, default, and the toggle (must)

- **Why this is the load-bearing item:** two turns ago the founders said
  Raffa needs customer data to make its AI more efficient and proactive.
  The answer given then stands: Raffa never trains a shared model on raw
  contract text; it learns from **aggregated, anonymized** structured
  facts and negotiation outcomes (spec §12.3's own "data flywheel"). This
  item is where that becomes a real, disclosed, defensible contract term
  instead of an internal intention.
- **Content brief for the lawyer:** a specific clause (separate from the
  general "we may improve our services" boilerplate every ToS has, which
  is too vague to rely on for this) stating precisely what is contributed
  (which structured fields, never raw document text or names), the
  anonymization method and minimum group size **once GOV-10 sets it**,
  that no output can be traced back to a contributing workspace, and that
  the resulting benchmark data belongs to Raffa outright once anonymized
  (this is the actual IP asset — say so plainly, it is also the thing
  that makes it defensible: Raffa owns anonymized derived data, never the
  customer's own contract text).
- **Product surface:** an Admin-visible, workspace-level toggle in
  Workspace & members ("Contribute anonymized data to Raffa's market
  benchmarks: On/Off"), defaulting per instruction 4 above, changeable at
  any time, with the current setting shown next to the Savings
  dashboard's benchmark figures ("your workspace contributes to this
  average" / "your workspace does not contribute"). A change of default
  for existing customers requires the same notice-and-reconfirm flow as
  any other material ToS change (GOV-05).
- **Acceptance:** turning the toggle off on `dev` stops that workspace's
  future structured facts from entering the anonymization pipeline
  (retroactive removal of already-anonymized aggregate data is not
  technically meaningful once true anonymization holds — GOV-10 decides
  whether this needs stating); the toggle's current state is visible
  wherever benchmark data derived from customer contributions is shown.
- **Seats (hint):** product-owner (owns this call with the founders),
  security-architect, software-architect, founders + counsel.

### Track B — Asymmetric but enforceable commercial terms

#### GOV-05 — Master Subscription Agreement / Terms of Service (must)

- **Content brief for the lawyer**, point by point, each marked for
  whether Italian law requires the art. 1341-1342 double-approval
  mechanism:
  - **IP split** (no special approval needed — this is descriptive, not
    one-sided): the customer retains ownership of the documents they
    upload and the facts extracted from them; Raffa owns the platform,
    the software, and — per GOV-04 — the anonymized aggregate data once
    it qualifies under GOV-10's standard.
  - **Liability cap** (**needs double approval**): aggregate liability
    capped at fees paid in the preceding 12 months; excludes indirect and
    consequential damages; **explicit carve-out, never capped**: a data
    breach caused by Raffa's failure to operate the controls `SEC-03`
    through `SEC-17` describe, gross negligence, and willful misconduct.
    This carve-out is not generosity — it is what keeps the cap
    enforceable at all: an Italian court is far more likely to strike an
    unlimited liability shield than a capped one with honest carve-outs,
    and a cap that tried to cover a broken security promise would be the
    exact "cavillo" §0 warns against.
  - **No warranty on AI-generated accuracy beyond the confidence system**
    (no special approval needed — this describes the product truthfully):
    "Raffa provides confidence-scored, cited information; validation of
    critical facts before a consequential decision remains the customer's
    responsibility" — this mirrors the `answer/v2.5.md` persona and the
    Review flow's own >90% bar exactly, so it is accurate, not evasive.
  - **Auto-renewal and fee changes** (**needs double approval** for the
    auto-renewal mechanism specifically): default auto-renews for a term
    equal to the original, cancellable with notice stated in the signup
    flow itself (never only in the full text); fee increases on renewal
    with a stated notice period, disclosed at signup, never silently
    applied mid-term.
  - **Unilateral ToS modification** (**needs double approval**): Raffa may
    update terms with notice; continued use after the notice period is
    acceptance; a **material** change (price mechanism, liability, data
    use) requires an affirmative re-click, not silent continuation —
    stricter than the law requires, because it is also what keeps GOV-04's
    flywheel default changes honest.
  - **Governing law and venue** (no special approval needed for the choice
    of law itself, though the accompanying arbitration/exclusive-venue
    clause, if any, does): Italian law, courts of the company's registered
    seat.
  - **Assignment** (no special approval needed): Raffa may assign the
    agreement in a merger, acquisition or financing without consent;
    the customer may not assign without Raffa's consent. Flagged for the
    founders' own benefit: a customer contract that blocks assignment on
    acquisition is a real diligence problem in a future raise or exit.
  - **Termination and data return**: for cause with a cure period; on
    termination the customer can export via `SR-08`'s endpoint for a
    stated window before deletion proceeds per `SEC-05`'s erasure receipt.
  - **Acceptable use**: the customer warrants they have the right to
    upload what they upload (protects Raffa when a customer uploads a
    third party's confidential contract without authorization); no
    reverse engineering; no use of the platform to build a competing
    product; the customer, not Raffa, is the data controller for personal
    data inside their own uploaded documents and bears the lawful-basis
    responsibility for having put it there.
  - **Feedback and feature-request IP** (no special approval needed):
    anything submitted through Ask's feature-request flow (ADR-030/031)
    is licensed to Raffa perpetually, worldwide and irrevocably to build,
    use and own — needed precisely because that flow turns customer words
    into a **public** GitHub issue.
  - Confidentiality (mutual), force majeure, dispute resolution.
- **Product surface:** signup and renewal flows capture each
  double-approval clause as its own logged checkbox/initial, timestamped,
  tied to the ToS version in force (reuses GOV-01's versioning); a
  material-change re-click flow (banner + explicit action, not a passive
  "by continuing to use...").
- **Acceptance:** a `dev` signup walkthrough shows each flagged clause as
  its own explicit action, not bundled into "I accept the terms"; the ToS
  version and the specific clauses accepted are stored per account, per
  version, queryable for a legal dispute or an audit.
- **Seats (hint):** product-owner, client-architect (the acceptance flow
  and its audit trail), founders + external counsel (drafting).

#### GOV-06 — The controversial-points register (must)

- **What it should be:** not code — a short internal document (private,
  alongside `SEC-25`'s ISMS repository, never in the public repo) listing
  every point where Raffa's interest and a customer's reasonable
  expectation could diverge (liability for an AI extraction error, the
  data-flywheel default, fee changes, a sub-processor change, breach
  liability, what happens to data after termination), the position taken,
  and the one-sentence reason it is still defensible and honestly stated.
  This is the concrete artifact that proves §0's principle was actually
  applied, item by item, rather than asserted once at the top of this
  file. It is also exactly what an investor's or an enterprise customer's
  legal diligence will ask for, so it pays for itself twice.
- **Acceptance:** every `must`-marked clause in GOV-05 has a row; the
  register is reviewed whenever GOV-05's terms change.
- **Seats (hint):** founders, product-owner, security-architect.

### Track C — AI and sector-specific legal

#### GOV-07 — EU AI Act classification and Article 50 transparency (must)

- **Why now:** the Act's transparency obligations for AI systems
  interacting with natural persons are in force; its high-risk-system
  obligations phase in over 2026-2027 depending on category. Raffa's
  extraction/classification and negotiation-recommendation features are
  **not obviously** high-risk under Annex III (procurement decision
  support is not a listed category as of this file's writing), but "not
  obviously" is not the same as "confirmed" — this needs an actual EU AI
  Act lawyer's opinion, not an assumption baked into the product.
- **Content brief for the lawyer:** a formal classification memo
  (limited-risk vs. the Annex III boundary) for each AI-driven feature
  (extraction, classification, Ask's `answer`/`analyst`/`research` roles,
  the `capability-investigator`, the negotiation-strategy calculator's AI
  narration); Article 50's transparency duty — a user must be told
  clearly they are interacting with an AI system — already partly true
  via Ask's citations and abstain behaviour, but this item asks for an
  **explicit, unambiguous** statement at first contact (folds into GOV-02)
  rather than relying on the product's tone to imply it.
- **Acceptance:** the classification memo exists, dated, covering every
  AI-driven feature listed above; GOV-02's disclosure card states plainly
  that the user is talking to an AI system.
- **Seats (hint):** founders + AI Act counsel, security-architect,
  product-owner.

#### GOV-08 — Copyright status of AI-authored code and the public-repo decision (must)

- **Today (evidence):** 77 of roughly 180 commits on this checkout are
  authored "Claude"; the `LICENSE` is MIT, copyright held by one founder
  personally rather than a company entity, and the repository is public
  (the same fact `SR`'s decision list already flagged: "chiudere il
  repository o dichiarare open-core"). Copyright protection for
  substantially AI-generated output is unsettled and varies by
  jurisdiction (the US Copyright Office's position on works without
  meaningful human authorship is restrictive; EU/Italian treatment is
  less tested for this specific question). If the code is not reliably
  protectable as a copyrighted work, the MIT license is nearly moot —
  there may be little exclusive right to license in the first place, and
  the trade secret argument (which depends on the code being kept
  confidential) directly conflicts with keeping the repository public.
- **Content brief for the lawyer:** an opinion on (a) the copyright
  status of the codebase given its authorship mix, (b) whether trade
  secret protection is a stronger fallback and what confidentiality
  practices it requires (which is not compatible with a public MIT repo
  as-is), (c) whether assigning IP to a proper corporate entity (not a
  founder personally) is needed regardless of the answer to (a) — this
  last point is true either way and should not wait on the rest.
- **Product/process surface:** going forward, commit messages and PR
  descriptions that record the human decision behind an AI-authored change
  (already this session's own practice — ADRs, requirements files, review)
  strengthen any human-authorship argument; this is a documentation habit
  to keep deliberately, not new tooling.
- **Acceptance:** the opinion exists; the repository visibility decision
  (already queued in the SR file) is made with this opinion in hand, not
  before it.
- **Seats (hint):** founders + IP counsel.

#### GOV-09 — Controller/processor roles, stated correctly per data category (must)

- **What it should be:** a short, precise statement — feeding directly
  into `SEC-26`'s RoPA and GOV-01's privacy policy — of who is controller
  and who is processor for each data category: Raffa is **processor** for
  the personal data found inside a customer's own uploaded contracts (the
  customer is controller, per GOV-05's acceptable-use warranty); Raffa is
  **controller** for its own product-usage data (accounts, support
  tickets from `CS-03`, audit logs) and for the anonymized aggregate
  benchmark data once GOV-10 confirms it has left GDPR's scope entirely
  (anonymized data has no controller under GDPR because it is no longer
  personal data — say so precisely, not loosely).
- **Acceptance:** the statement matches, field for field, what the DPA
  (SR-08) and the RoPA (SEC-26) already say; no category is left
  ambiguous.
- **Seats (hint):** security-architect, founders + counsel.

#### GOV-10 — The anonymization standard: the gate that makes GOV-04 possible (must)

- **Why this is the single most load-bearing item in this file:** GOV-04's
  entire premise — that Raffa can learn from customer data without it
  being "customer data" anymore — depends on this item, not on intention.
  GDPR Recital 26 tests anonymization by whether re-identification is
  reasonably likely using any means, not by whether Raffa itself intends
  to re-identify. A weak standard (e.g., stripping only the supplier's
  name) does not clear that bar and leaves GOV-04's data still personal
  data, still inside GDPR, still requiring the stricter opt-in-only
  posture.
- **Content brief for the lawyer + technical spec for the architect,
  together, not separately:**
  - a **minimum group size** before any aggregate figure is shown or
    contributed (the benchmark service's existing 5-sample "insufficient
    market data" floor, per `product-spec.md` §10.4, was set for
    statistical confidence, not for anonymization — this item asks
    whether it is also sufficient for anonymization, or must be higher,
    with the lawyer's input on the legal test and the architect's input
    on what a real attacker could still infer from a 5-record aggregate
    in a narrow category/geography/size-band combination);
  - **suppression of any dimension combination that narrows a group below
    the threshold** (e.g., "insurance, Switzerland, 20,000+ employees" may
    have only one real contributor — the aggregate must suppress or widen
    the bucket rather than publish a number that is effectively one
    company's price);
  - a documented, versioned **anonymization pipeline** (not a one-off
    script) that only ever reads already-validated structured facts, never
    raw document text (consistent with `product-spec.md`'s "AI is not the
    database" and this session's own "we learn from facts, not from
    blobs" answer);
  - a re-identification risk test run before each pipeline change, not
    only at launch.
- **Acceptance:** the pipeline's suppression rule is demonstrated on
  `dev` — a category/geography/size combination with fewer than the
  threshold contributors never surfaces a number; the lawyer's written
  opinion states the threshold clears GDPR's anonymization test.
- **Seats (hint):** security-architect (owner, with the lawyer), software-
  architect (the pipeline), product-owner.

### Track D — Lower-priority, still real

#### GOV-11 — Cookie / tracking consent, ready for when analytics arrives (could)

- **What it should be:** no product analytics or marketing cookies exist
  today (README/package.json confirm no analytics SDK); this item is a
  placeholder spec so that whenever one is added, a consent banner and
  policy ship in the same wave, never after the fact.
- **Acceptance:** N/A until a cookie-setting feature is proposed; this
  item blocks that feature's acceptance until a banner exists.
- **Seats (hint):** product-owner, client-architect.

#### GOV-12 — Sanctions and denied-party screening for new workspaces (should)

- **What it should be:** given the SR-file's ambition to sell into
  regulated sectors (financial services already appears as a workspace
  industry option) and any eventual EU/UK/US customer base, a lightweight
  screening step against EU/OFAC sanctions lists at workspace creation —
  not a KYC programme, a basic denied-party check, refusable manually by
  an Admin (founder) review before activation for any flagged case.
- **Acceptance:** workspace creation for a sanctioned entity name is
  flagged for manual review before activation.
- **Seats (hint):** security-architect, founders.

## 3. Order constraints

- GOV-10 (anonymization standard) before GOV-04's default is finalized —
  the opt-in/opt-out decision in instruction 4 is provisional until GOV-10
  reports.
- GOV-09 (controller/processor roles) before GOV-01 and before `SR-08`'s
  DPA are finalized — both need it as their factual basis.
- GOV-05's double-approval clauses depend on GOV-06 existing first (the
  register is where "does this need double approval" gets decided,
  clause by clause, before the signup flow is built).
- GOV-07 and GOV-08 can run in parallel with everything else; both are
  lawyer-led and do not block engineering work.
- If the wave cap is reached: GOV-01, GOV-04, GOV-05, GOV-09, GOV-10 are
  never pushed past the next wave — they are the ones a customer or a
  regulator would ask about first. GOV-11 and GOV-12 move first if
  something must give.

## 4. Cancels / touches

- Cancels nothing.
- Supplies the content spec for `SR-08`'s DPA template and trust page
  (deferred file) — when that item is un-deferred, it should read GOV-01,
  GOV-03, GOV-05 and GOV-09 first rather than starting from a blank page.
- Extends `SEC-25` (ISMS: GOV-06's register lives beside it, GOV-03's
  registry feeds from its asset inventory) and `SEC-26` (GDPR: GOV-01,
  GOV-09, GOV-10 are its customer-facing and technical counterparts).
- Touches ADR-030/031 (adds the feedback-IP clause, GOV-05, that makes the
  public-issue flow's implicit assumption explicit in the contract).
- Touches the SR file's open decision on the public repository (GOV-08's
  opinion should land before that decision, not after).

## 5. Decisions requested (recorded here, answered by the founders)

- [ ] **GOV-04's default**: opt-in (this file's recommendation, consistent
  with "trasparenza") or opt-out-once-GOV-10-confirms-true-anonymization.
- [ ] **Liability cap ceiling**: 12 months' fees (this file's suggestion,
  standard SaaS practice) or a different multiple.
- [ ] **Governing law and venue**: Italian law confirmed; which court seat.
- [ ] **GOV-08's repository decision**: hold until the IP-counsel opinion,
  or proceed with the SR file's "close it now" default in the meantime.

## 6. Non-Helix legal work to run in parallel (flagged, not built)

- Founders' equity and IP-assignment agreement (each founder and any
  contractor formally assigns IP to the company entity — GOV-08 depends
  on this existing regardless of the copyright-status answer).
- Confirm the company's IP, including this codebase, sits in a proper
  corporate entity, not personal ownership (today's `LICENSE` copyright
  line is a person, not a company).
- Cyber-liability / technology E&O insurance, sized once GOV-05's
  liability cap is set — a cap the company cannot actually pay if breached
  is not a real protection for either side.

## 7. Acceptance walk (for `docs/waves/<wave>-acceptance.md`)

1. Sign up a new workspace on `dev`: each double-approval clause (auto-
   renewal, liability cap, unilateral modification) appears as its own
   explicit, separately logged action, not bundled into one checkbox.
2. Upload the first document: the GOV-02 disclosure card appears once,
   states the AI-system fact (GOV-07) and the no-standing-access promise
   (SEC-03) in plain language, and is reachable again later.
3. Toggle the data-flywheel setting off in Workspace & members: the
   Savings dashboard reflects "does not contribute" immediately.
4. Open `/privacy`: the categories match the RoPA line for line; the
   version history is visible.
5. Open the sub-processor page: it matches the live Terraform inventory;
   adding a new connector in a later wave without updating this page fails
   the drift test.
6. Attempt to surface a benchmark figure for a suppressed (too-small)
   category/geography combination: it does not appear.
7. The controversial-points register (GOV-06) has a row for every `must`
   clause in GOV-05, each with its stated reason.
