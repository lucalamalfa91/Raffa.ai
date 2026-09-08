# Contigo — Screens (web V2, pilot path)

Pixel reference: `Contigo V2 Prototype.html` (unpacked: `markup.html`,
`styles.css`, logic `app.jsx`). Requirements: `inputs/requirements.md`.
Every screen keeps the Modernist system of ADR-019 (flat, 0 radius, one
accent, flush-left). Copy below is verbatim from `markup.html` / `app.jsx`
unless marked *req* (added by the requirements).

States each list/detail surface must ship (ADR-018 contract): loading =
skeleton rows, empty = h3 + one sentence + primary action, error = accent
left rule + h4 + plain endpoint name + Retry.

## 1. Sign-in → workspace → Ask — R0 (§16 R0, §20 "Create a workspace")

Hero "**Your contracts. Your savings. Nothing missed.**" with four pillars
(Contract Intelligence · Renewal Intelligence · Savings Intelligence · Quote
Check) and "Contract intelligence for procurement teams." Entra button →
workspace create ("A workspace is your tenant. Contracts uploaded here never
leave it.") or pick ("{{ seededCount }} validated contracts · CHF · eu-west")
→ **lands on Ask** (`enterWs → go('ask')`).
States: idle · signing (900 ms) · create · pick.

## 2. Ask Contigo — home (R1 §8.3–§8.4; requirements §5.2–§5.7)

- **Off** (no validated contract): "Ask needs at least one validated
  contract." + `askOffReason` ("Upload a contract first. Contigo extracts the
  facts, you sign off the weak ones, and Ask switches on." / "Your document
  is still processing or waiting for review. Ask only answers from facts
  that passed validation — so it never guesses.") + one CTA (`askOffCta`:
  "Upload a contract" / "Go to Documents").
- **New chat**: "What do you want to know?" (`askHello`) + scope line
  `askScope` ("Answers only from N validated contracts (Salesforce, …) ·
  cites or abstains") + "Structured questions run on validated fields,
  legal questions retrieve clauses — every answer cites its page or says it
  cannot answer." Input placeholder "Ask Contigo — spend, dates, clauses,
  liability…"; two suggestion chips.
- **Conversation**: header `convTitle` with **+ New chat**; turns You (17px
  heading weight) / Contigo (14px); Contigo turns render *req* markdown
  body, citation cards (title · page/section · snippet · preview · corpus
  badge), action buttons, follow-ups. Prototype chips `[n] doc · p.N §S`
  become cards (R-WEB-04); the `m.route` line is **not** rendered.
- **Abstain**: accent-left block "Cannot determine reliably." + reason — only
  for true insufficiency. **Redirect** (greeting / off-domain / needs
  document) and **refusal** (legal): warm prose + one CTA (*req*).
- **Thinking**: "Authorising scope → detecting intent → retrieving evidence"
  (V1 copy retained until the reply streams).
- Rail: last 5 conversations under Ask Contigo, resume by click, active one
  in accent.
States: off · empty · thinking · answered · abstain · redirect · refusal ·
resumed · transport error.

## 3. Documents — R0/R1 (§7.1 statuses; requirements §5.1)

- **Onboarding empty** (`docsEmpty`): "First your contracts. Then your
  questions." Three steps: 01 · Upload "Drop your contracts" · 02 · Review ·
  03 · Ask "Answers only from validated facts, with the page that proves
  them." Dropzone: "Upload contracts" / "or drop files anywhere in this box"
  + sample file. Strip: PDF · DOCX · XLSX (*req*: · PNG · JPG), 50 MB / file.
- **List** (`docsList`): summary `kbSummary` ("N documents · M askable · K
  waiting for your review"); filter **Needs your attention** (default) /
  **All documents · N** with hint "Completed documents are hidden — they are
  already askable." / "Everything, including validated documents."; rows
  Document · Supplier · Type · Status · action (**Review N fields** /
  **Ask about it** / **Retry upload**); processing rows show the stage
  (Uploading · Classifying · OCR / text · Sections & tables · Extracting
  facts · Validating schema) and a percentage.
- **Attention empty**: "Nothing needs you right now." + "Every uploaded
  document is validated and askable. Completed documents are tucked away —
  switch to All documents."
- **Validated hook** (`justValidated`): "*X* is now askable." → "Ask: when
  does it expire?"
- *req* **Not added** card per rejected file: "Not added: this looks like a
  recipe, not a contract. Contigo only keeps contracts, order forms, quotes
  and the documents around them. Drop the signed agreement or the
  supplier's proposal." (session-only, never counted).
- *req* multi-file drop: one row per file, per-file outcome.
States: onboarding empty · uploading/processing (real stages) ·
needs_review · completed · failed (retry) · rejected (not added) ·
attention-empty · list error.

## 4. Review — a state of Documents (§7.3; requirements R-WEB-05)

"← Documents" back link; title `reviewTitle` ("N facts below 80% — you
decide" / "All weak facts decided"); sub "Contigo will not use these for
renewals or Ask until you accept or correct them. Everything else was
extracted above threshold." / "Mark the contract as validated to make it
askable." Field list with confidence tags (Accepted · Flagged · Review),
Accept / Correct, evidence pane (`sel`: doc · page · § · before / **quote** /
after), "Show all 41 extracted facts" toggle, confidence tip (">95% accept,
80–95% flag, <80% you decide. Below 80% Contigo will not use the fact for
renewals or Ask."), **Mark as validated** (disabled while weak facts are
open). *req*: the supplier fact is a critical field here (R-SUP-01).
States: weak facts open · all decided · correcting · validated.

## 5. Contract 360 — no tabs (§8.2; requirements R-EVD-02, R-WEB-06 P2)

Header: supplier, name, "{{ type }} · {{ spend }} / year · {{ docCount }}
documents · {{ status }}". **Answers band**: *Where you can save* (estimate
+ lever), *When you must move* (notice deadline, days left, auto-renews
clause), *What to do* (action + rationale + primary button + "Assign to a
colleague"). **Why**: 2–3 clauses (type · normalized · page § · risk ·
confidence), click → original wording highlighted (`hl`; citation landing
from Ask, `citedOpened`). **Details ▾** ("All terms, documents and open
facts ▾"): key terms table, documents in family, facts still to decide.
Tracker after "Start negotiation now": status In negotiation, target,
"close by {{ cancel }}", 4-step checklist (Notify · Request revised pricing
· Counter with the market benchmark · Sign or send non-renewal notice),
**Track it in Renewals →**, Undo. Back label follows the origin (Ask
Contigo / Documents / Portfolio / Renewals).
V2 acceptance requires the citation landing (highlighted clause with the
original wording); the full answers-band layout is feature F10 (P2).
States: open · in negotiation · assigned · not uploaded (empty).

## 6. Portfolio — R1 (§8.1)

Greyed until validated contracts ("The portfolio lights up from validated
contracts. Upload one to start." + "Upload a contract"). Summary
`pfSummary` ("N validated contracts · CHF X annual · K notice deadlines
within 45 days"); table Supplier · contract · Type · Annual spend · Ends ·
Notice by · Risk (+ "More columns"); urgent rows (≤ 45 days) accent-tinted.
Rows open Contract 360.

## 7. Renewals — R2 (§9.1–§9.3)

Empty: "No renewal dates yet" — "Renewals are computed from validated end
dates and notice periods. Upload a contract to start." List sorted by
priority (`score`), columns Supplier · contract · Renews in · Notice by ·
Priority · Status; insight card for the selected row (facts + recommended
action + rationale); actions **Start negotiation** / **Assign**; "Open
contract →". Status shared with the Contract 360 tracker (`racts`).

## 8. Savings — `/savings` (R3 §10.1; prototype `home` block)

KPIs: Contracts analyzed · Upcoming renewals · Savings identified (with
meta lines); opportunities table (Supplier · Action · Estimate · Status),
rows open Contract 360. Not a rail item in V2 (requirements A3); reached
from Ask actions, Renewals and Contract 360.

## 9. Quote check — R4 (§11–§12)

Empty: "Drop a supplier proposal; Contigo normalises the lines and compares
them with the market and with what you already pay." + **Upload a quote**;
after load: Supplier quote lines (Quoted · Market band · Position), "Target
and negotiation levers are one step further — shown only if you want them."
Reached from the rail or from Ask routing (benchmark intent).

## 10. Workspace & members — R0 (§3.1)

Members table (name · email · role · status), invite form (email must match
the workspace domain; role Admin "Also uploads, deletes, manages members" /
Procurement "Asks, reviews, triages renewals"), tip "invite the team once
the first contract is validated — there is nothing for them to ask before
that." Procurement sees "Only a Workspace Admin can invite members. Admins:
…" (request access).

## §16 / §20 traceability (V2)

| Spec | Screen(s) |
|---|---|
| R0 secure workspace ingests documents | 1, 3, 10 |
| R1 upload + reliable questions with evidence | 3, 4, 2, 5 |
| R2 renewal windows never missed | 7, 5, 6 |
| R3 credible savings | 8, 5, 2 (portfolio strategy) |
| R4 quote assessed in minutes | 9 (+ 2 routing) |
| §20 "reliable questions with source evidence" | 2 → 5 |
| §20 "prioritized savings opportunities" | 8, 2 |
