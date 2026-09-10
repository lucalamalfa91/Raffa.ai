# Raffa — Screens (web V1, Day-1 path)

Prototype: `Raffa Day-1.dc.html` (Claude Design) · standalone export `Raffa Day-1 Prototype.html`.
Spec references: §7.1/7.3 (status, confidence), §8.1–8.4 (portfolio, 360, Ask, evidence), §9.1–9.3 (renewals), §10.1 (KPIs), §11.1–11.3 (quote), §12.1–12.2 (negotiation), **§16** (release mapping R0–R4), **§20** (Definition of V1 done).

Tweaks available in the prototype: `role` (admin | procurement), `dataState` (populated | empty | loading | error), `uploadOutcome` (needs_review | completed | failed).

---

## 1. Sign-in → workspace — R0 (§16 R0, §20 "Create a workspace")
Left: statement panel (accent-100 ground with grid, north-star sentence, 4 V1 jobs). Right: "Continue with Microsoft Entra ID" → redirect state → workspace list (name, contract count, currency/region, role tag) + "Create a new workspace".
States: idle · redirecting (spinner) · workspace picker.

## 2. Members & roles — R0 (§3.1, §20 "invite Procurement users")
Table Member / Role / Status / Last active. Right pane: invite form — email, role radio (Workspace Admin vs Procurement with permission summaries), Send invitation.
States: sent (row appended, "Invited" tag) · validation error (non-tenant domain) · **non-admin**: "You don't manage this workspace" + Request access.

## 3. Upload → document status — R0/R1 (§7.1 statuses)
Dropzone (drag-and-drop, "Choose from computer" file picker, sample file), formats/size/sources strip. Processing pipeline list (6 stages, current pulsing). Result card by outcome. Document table: Document / Type / Supplier / Status / Uploaded, rows open Contract 360.
States: idle · uploading/processing · **needs_review** · completed · **failed** (password-protected PDF, retry).

## 4. Portfolio — R1 (§8.1 columns + filters)
Header + filter chips (Supplier, Category, Renewal period, Spend, Status, Risk, Auto-renewal). **Attention strip**: Deadlines < 45 d · Need review · Failed/processing · High risk (click = filter). Table (fixed widths, min 1000px, scrolls): Attention · Supplier · Contract · Annual spend · Start · End · Renewal · Cancel by · Auto · Status. Rows sorted by severity → deadline → score; critical rows tinted + red bar.
States: populated · loading (skeleton) · empty (first upload CTA) · error (503 + retry) · no match for filter.

## 5. Contract 360 — R1 (§8.2 header + 10 tabs)
Header: supplier kicker, contract name, type/status tags, doc count; 6-cell fact row (annual spend, TCV, start→end, renewal + days, cancellation deadline + notice, risk + priority). Tab bar.
- **Overview**: recommended-action block (big statement + rationale + Open in renewals / Why this score) with 3 driver numbers; "Needs your attention" (fields < 95%) and "Top risks".
- **Commercials / Products / Clauses / Obligations / Risks / Documents / Benchmark / Renewal / Activity**: one template — h6 title + summary line right + `.table` (Term · Value · Source · Confidence pattern). Benchmark adds a P25/P50/P75 ladder; Renewal adds priority-score component table.

## 6. Review / correction — R1 (§7.3 thresholds, §4.1 correction history)
Header + "Mark as validated" (disabled until all < 80% fields decided). Progress line + legend. 4-column list: Field (critical marker) · Extracted value + source · Confidence tag · Decision (Accept / Correct or result). Right pane: evidence page with highlighted passage, correction form, model/prompt version.
States: pending · accepted · corrected (value shown) · auto-accepted · blocked CTA.

## 7. Ask Raffa — R1 (§8.3 routing, §8.4 evidence)
Global Ask bar on every screen (contextual suggestions; Enter → this screen; ⌘K). Chat with route line ("Structured query…", "Clause retrieval…"), numbered citation chips (doc · page · §) opening Contract 360 › Clauses. **Abstain** block: "Cannot determine reliably" + reason. Right rail: suggested questions.
States: empty · thinking (authorise → intent → retrieve) · answered · abstain · unknown question fallback.

## 8. Renewal pipeline — R2 (§9.1 thresholds, §9.2 score, §9.3 insight card)
Header + summary. **Threshold strip** 0–30 … 270–365 d with counts (click = filter). Table: Score · Supplier · contract · Annual spend · Renews in · Cancel by · Status. Insight card (§9.3 fields: spend, cancellation deadline, uplift, market position, potential savings, owner; recommended action + rationale) with actions Start negotiation / Assign to me / Snooze → confirmation + link to Contract 360.
States: populated · empty · loading · error (engine unavailable) · no renewals in window.

## 9. Home — Savings — R3 (§10.1 KPIs)
6 KPI cells: Annual spend analyzed · Savings identified · Savings realized · Savings in progress · Contracts analyzed · Upcoming renewals. Opportunities table: Opportunity · Type · Current spend · Estimated savings · Confidence · Owner · Status · Realized. Rows open Contract 360 › Benchmark or Quote check.
States: populated · empty · loading · error (benchmark provider unreachable; KPIs stale-labelled).

## 10. Quote check — R4 (§11.1 workflow, §11.2 output, §11.3 guardrails, §12.1–12.2)
Stepper Extract → Assessment → Target → Negotiation.
- Extract: line table with benchmark match; **unmatched SKU** block with manual mapping select + recalculate (assessment blocked until resolved).
- Assessment: 4 numbers (quote, market range, assessment, potential saving), line-level P25/P50/P75 table with confidence, provenance card.
- Target: price ladder (range, target, quote), opening/acceptable/walk-away table, editable target.
- Negotiation: levers with evidence and impact; outcome form → recorded outcome (original, target, final, realized, duration, levers) → Home Savings Realized updates.
States: unmapped/blocked · mapped · no outcome · outcome recorded.

---

## §20 coverage
| Definition of V1 done | Screen |
|---|---|
| Create a workspace and invite Procurement users | 1, 2 |
| Upload a portfolio of contracts; classify/extract/structure | 3 |
| Ask reliable questions with source evidence | 7 |
| See renewal and cancellation deadlines | 4, 5, 8 |
| See contract/commercial risks | 5 |
| See market benchmarks where available | 5 (Benchmark), 10 |
| See prioritized savings opportunities | 9 |
| Upload quote → line-level assessment → target → strategy | 10 |
| Record outcome, track realized savings | 10 → 9 |
