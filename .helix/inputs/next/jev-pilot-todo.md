# Raffa — next-waves input · Jev (TypeSafe AI) evaluation

Status: **binding input** for a Jev evaluation wave. Written 2026-09-26 after
a manual audit of every `IAiGateway` call site on this checkout (18 sites
across `Raffa.AiGateway`, `Raffa.Documents.Contracts`, `Raffa.Chat`,
`Raffa.Market`, `Raffa.Api`) crossed against TypeSafe AI's public Jev
material and third-party benchmarks (2026-09-15 launch onward). IDs continue
from `next-waves-todo.md` / `w18-todo.md` (last used: NW-97).

| | |
|---|---|
| What is Jev | TypeSafe AI "System 1" model: outputs typed decisions (discrete choice / calibrated score / structured schema) + confidence, not autoregressive text. Not a chat/completions API — a different call shape from Foundry. |
| Where it could fit | `IAiGateway` roles that are narrow, fixed-taxonomy, structured-output decisions: `Classify`, and the classification-shaped subset of `Analyze`. Never `Extract`, `Embed`, `Answer`, `Ocr`, `Research` — see §3. |
| Why not a drop-in | ADR-004 makes a **model** swap a config change; Jev is not a model swap, it is a **different vendor shape** behind the same role — needs a new `IAiGateway` implementation (or role-split composite), not a config edit. |
| Key risk (public data) | Independent phishing-classification benchmark: Jev asked one question scored **62.6%** vs Claude Haiku 4.5's **81.3%** on the same 2,000-item set. Reached 95.0% only after decomposing into 5 atomic questions with weights fitted on 1,000 labelled examples — a per-question-type calibration exercise, not a config toggle. TypeSafe's own docs: thresholds do not transfer between question types. |
| Oracle for the call-site map | This file's §3/§4; full detail in the 2026-09-26 audit transcript (not persisted elsewhere — re-derive from `IAiGateway.cs`, `AiGatewayModelOptions.cs`, and the modules listed in §3/§4 if this file is consumed after further drift). |

## 0. Binding instructions

1. **No production `IAiGateway` implementation change lands from this file
   alone.** Every item here builds evaluation infrastructure or produces a
   decision record. The swap itself is a *future* item, gated on NW-100's
   scorecard clearing all three bars (time, quality, tokens) — the
   stakeholder rule is that a partial win does not qualify.
2. **Shadow mode only, one flow.** NW-98 is scoped to
   `CapabilityInvestigator` (§1) because it is the only call site where a
   quality regression is low-blast-radius (already fail-open, async,
   non-blocking — `CapabilityInvestigatorAgent.cs`, ADR-031) *and* the
   `analyst` role it uses resolves to the full `Answer` deployment in
   `demo` (not the `nano` tier), so the published Jev-vs-frontier-model
   deltas actually apply. Do not extend shadow mode to another call site in
   this wave.
3. **Data governance gates the pilot, not just engineering.** Jev is a new
   external subprocessor (TypeSafe AI). NW-99 must close — DPA / data
   residency / what leaves the tenant boundary — before NW-98 sends a
   single live tenant question to it. Fixture/synthetic traffic only until
   then.
4. **§3 stays out.** Every call site in §3 is excluded from this wave with
   a written reason. Pulling one in requires a new council pass, not a
   silent extension of NW-98's pattern.

## 1. Head of this wave

### NW-98 — Shadow-mode Jev evaluation harness for CapabilityInvestigator (must)

- **Status:** OPEN — new capability, additive only.
- **Today:** `CapabilityInvestigator.InvestigateAsync`
  (`backend/src/Raffa.Chat/Application/Gaps/CapabilityInvestigator.cs`) calls
  `IAiGateway.AnalyzeAsync` (`analyst` role) once per chat turn, timeout-bounded,
  fail-open, to produce a `GapVerdict` (`None`/`Supported`/`KnownGap`/`Gap`) +
  confidence (ADR-031). Single implementation today: `FoundryAiGateway`
  (`backend/src/Raffa.AiGateway/Foundry/FoundryAiGateway.cs`), wrapped by
  `LoggingAiGateway` (audit: model id/version, prompt version, input hash —
  never raw content).
- **Must:**
  1. A `JevAnalyzeClient` (new, alongside `FoundryAnalyzeClient.cs`) that
     calls Jev with the same `AiAnalysisRequest` shape, decomposed per
     TypeSafe's own guidance — do not ask the four-way verdict as one
     question; split into atomic sub-questions (e.g. "is this a question
     about existing data?", "does it name an action Raffa performs?",
     "does it match a known-gap pattern?") with weighting logic kept in
     Raffa's own code, per the phishing-benchmark lesson in §0's key risk.
  2. Shadow-only: on each live turn, call Foundry as today (its verdict is
     the one used) **and** call Jev in parallel, best-effort, never
     blocking or altering the turn. Log both verdicts + confidences +
     latencies + token/pricing figures to a comparison table (new, or an
     extension of the existing `LoggingAiGateway` audit row — do not log
     raw question text per the existing "never raw content" rule).
  3. A labelled calibration set: at least 200 historical chat turns already
     triaged by a human (or by the existing Foundry verdict, spot-checked)
     across all four `GapVerdict` values, held out from any weight-fitting
     data, to score Jev's decomposed verdict against ground truth (mirrors
     the 1,000/1,000 split TypeSafe used for its own 95% number — do not
     report a number fitted and scored on the same set).
  4. Kill switch: a config flag that disables the shadow call entirely
     without touching the Foundry path (this must never become a
     dependency of the live answer).
- **Acceptance:** J1–J3 below.
- **Seats (hint):** software-architect (new `IAiGateway`-adjacent client,
  shadow wiring), cloud-architect (new vendor call, network egress,
  secrets), security-architect (co-owns NW-99's gate before any live
  traffic reaches Jev), delivery-manager (calibration-set sourcing).
- **Oracle:** ADR-004 (role/model binding), ADR-031 (CapabilityInvestigator
  is fail-open by design), this file §0 key risk (62.6%→95% decomposition
  caveat).

### NW-99 — TypeSafe AI (Jev) data-governance and vendor review (must, blocks NW-98 live traffic)

- **Status:** OPEN — new external subprocessor, launched 2026-09-15, no
  existing review on file.
- **Must:** security-architect / product-owner sign-off covering: what
  Raffa data reaches Jev's API (the chat question text can reference
  tenant-specific facts even though the capability/known-gap catalogs are
  not tenant data), TypeSafe's DPA / data-retention / training-on-input
  posture, region/residency, and whether this needs a tenant-facing
  disclosure (existing subprocessor list, if Raffa maintains one). Until
  this closes, NW-98 runs on synthetic/fixture questions only, never a real
  tenant's turn.
- **Acceptance:** written sign-off (ADR footer or standalone note) naming
  what is and is not permitted to reach Jev, before NW-98's kill switch is
  flipped on for any live traffic.
- **Seats (hint):** security-architect, product-owner.
- **Oracle:** none on file — this is the gap this item closes.

### NW-100 — Go/no-go scorecard for a real swap (must, terminal gate)

- **Status:** OPEN — depends on NW-98 running long enough to have signal
  (suggest: minimum 2 weeks live-shadow or 500 scored turns, whichever is
  later).
- **Must:** one decision record, scored against the stakeholder's own bar
  — **all three**, not a majority:
  1. **Time:** Jev's measured end-to-end latency on this call site (not the
     vendor's headline number) is lower than Foundry's `analyst`
     deployment, measured on the same turns.
  2. **Quality:** Jev's decomposed verdict, scored against the held-out
     calibration set from NW-98.3, matches or beats Foundry's verdict
     accuracy — not merely "close" — and its calibration error on
     out-of-distribution turns (new phrasing the fitting set didn't cover)
     does not blow up (TypeSafe's own audits flag this as Jev's weak
     point).
  3. **Tokens/cost:** measured `$`-per-turn from the shadow run is lower
     than the current Foundry `analyst` cost for this call site, at the
     same volume.
  - If any one of the three is not met, the record says **no swap**, and
    this file's items close as "evaluated, not adopted" rather than being
    silently dropped. A partial win (e.g. cheaper but not more accurate)
    is explicitly **not** a pass, per the stakeholder rule that opened this
    investigation.
- **Acceptance:** the decision record exists and names a verdict; if the
  verdict is "go", the actual `IAiGateway` cutover for
  `CapabilityInvestigator` becomes a **new** item in the next wave — not
  retro-fitted into this one.
- **Seats (hint):** product-owner (owns the go/no-go), software-architect,
  delivery-manager.
- **Oracle:** the stakeholder's own three-way bar (this evaluation's
  starting brief); NW-98's logged comparison data.

## 2. Suggested grouping and seats

| Theme | Items | Seats |
|---|---|---|
| A · pilot infrastructure | NW-98 | software-architect, cloud-architect |
| B · governance gate | NW-99 | security-architect, product-owner |
| C · decision | NW-100 | product-owner, software-architect, delivery-manager |

**Order:** NW-99 before NW-98 goes live on real traffic (fixtures can start
in parallel). NW-100 only after NW-98 has enough scored volume — do not
rush the scorecard to close this wave early.

## 3. Out of this wave — do not swap

Every other `IAiGateway` call site audited 2026-09-26, excluded with reason.
None of these may move to Jev without its own council pass re-running the
same three-way bar.

| Call site | Role | Why excluded now |
|---|---|---|
| `DocumentAdmissionGate.EvaluateAsync` (Documents.Contracts) | classify | Consequential gate (wrong call = wrong document admitted/rejected); already runs on the cheapest/fastest `classify` tier (`gpt-5.4-nano` in dev/demo) — published Jev deltas are measured against Haiku/GPT-5.6, not nano, so the cost/time win is unproven here; single-question classification is exactly the benchmark shape that scored 62.6% |
| `StagedExtractionService` (7-stage extraction), `QuoteExtractionPipeline` | extract | Multi-field structured extraction from legal/commercial text — no public Jev benchmark covers extraction, only classification; wrong extracted clause/date has direct customer impact |
| `EmbeddingRetrievalService`, `MarketIngestionService`, `PgVectorMarketKnowledgeRetrieval` | embed | Jev is a decision model, not an embedding model — not the same product category, no fit |
| `AnswerComposer`, `RagAnswerService` | answer | Free-text grounded answer generation with citations — explicitly outside Jev's target ("GPT/Claude fit workflows needing explanation, synthesis") |
| `NegotiationCouncil` (analyst + strategist), `NegotiationDraftingWorkflow` | analyst | Multi-step reasoning over evidence / ranked argued output / email drafting — synthesis, not discrete classification |
| `MarketResearcher.QueryMarketRagAsync` | analyst | Open-ended query planning (generates keyword/query text), not a fixed-taxonomy decision |
| `MarketPriceEstimator.EstimateWithAiAsync` | analyst | Numeric band estimation + free-text rationale — no public Jev benchmark on regression/estimation tasks; feeds real negotiation advice |
| `WebResearchComposer` | research | Tool-use (hosted web search) + summarization |
| `HybridDocumentParsingService` (OCR) | ocr | Azure Document Intelligence, not an LLM call to begin with |

## 4. Acceptance seeds

| # | Check |
|---|---|
| J1 | With the Jev shadow flag on (fixtures only until NW-99 closes), every `CapabilityInvestigator` turn logs a Foundry verdict **and** a Jev verdict + latency + cost, and the live answer is unaffected by the Jev call failing, timing out, or disagreeing. |
| J2 | The calibration set (≥200 turns, all four `GapVerdict` values represented) is held out from weight-fitting; Jev's decomposed-question accuracy is reported against it, not against the fitting set. |
| J3 | Flipping the kill switch off removes the Jev call path entirely with no code change to `FoundryAiGateway` or the live answer path. |
| J4 | NW-100's decision record states a verdict on all three bars (time, quality, tokens) individually, and the overall verdict is "go" only if all three individually pass. |

## 5. Traceability

| This file | Source |
|---|---|
| Jev capability summary, benchmark figures | 2026-09-26 web research: TypeSafe AI blog "Introducing System One Models & Jev" (2026-09-15 launch) and third-party coverage/benchmarks (MindStudio, TrueFoundry, DataCamp, independent phishing-classification eval) |
| NW-98, NW-100 three-way bar | stakeholder instruction, this conversation: "ok alla sostituzione solo se migliore in tempi, qualità e risparmio tokens, tutti e tre" |
| §3 call-site map | 2026-09-26 audit of `IAiGateway.cs`, `AiGatewayModelOptions.cs`, and every module under `backend/src/Raffa.*` calling `IAiGateway` |
