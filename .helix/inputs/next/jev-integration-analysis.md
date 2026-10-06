# Raffa — Jev (TypeSafe AI) integration analysis: what to build next

Status: **analysis, not a wave request** — written 2026-10-06, after the
classify-role and capability-investigator pilots were already coded (branch
`claude/jev-classify-pilot`, not merged) and after reading TypeSafe's public
cookbook index. This file is the engineering catalogue the next council pass
should pick items from (§2); it does not itself queue a wave. IDs continue
from `jev-pilot-todo.md` (last used: NW-100).

## 0. Where things stand

| Item | Status | Where |
|---|---|---|
| Document-type classification (`DocumentAdmissionGate`) → Jev | **Coded, not merged** | `backend/src/Raffa.AiGateway/Jev/*`, decorator `JevAiGateway` on the `classify` role only |
| Capability-investigator verdict (`CapabilityInvestigator`) → Jev | **Coded, not merged** | `backend/src/Raffa.Chat/Application/Gaps/JevVerdictClient.cs`; `question`/`supported`/`known-gap` never call Foundry once Jev decides; `gap` still calls Foundry once, only to write the free-text feature description |
| Both behind one switch | `AiGateway:Jev:Enabled`/`AiGateway:Jev:ApiKey`, off everywhere until a real OpenRouter key is supplied and the flag is flipped on `dev` | — |
| Open from the first evaluation (`jev-pilot-todo.md`) | NW-98/99/100 (shadow-mode harness, data-governance sign-off, go/no-go scorecard) are **superseded** by shipping real code directly instead of a shadow harness — this file's own NW-101+ below carries the "measure before trusting it" discipline forward per item, not as one upfront harness | — |

Hard constraint that governs every item below (do not re-litigate): Jev has
exactly three primitives — **Choice** (pick one of a few given options),
**Noul** (yes/no with a probability), **Score** (a position on an ordered
rubric). It cannot generate or locate free-value text — a date, an amount, a
verbatim clause, a name. A deterministic pre-pass (not an LLM) must enumerate
candidates before Jev can choose among them. This is the one fact every
pattern below either exploits or is blocked by.

## 1. The five patterns, and the design each implies

### Pattern A — Jev as a gate before an expensive generative call

A cheap Noul/Choice decides **whether** to make the Foundry call at all. On a
negative answer the Foundry call is skipped outright — real time and cost
savings, not just a quality check, because the LLM call that would have run
anyway never runs.

| Call site | Today | Gate |
|---|---|---|
| `AnswerComposer`/`RagAnswerService` | `canDetermine` decided by the LLM *inside* the same call that writes the answer | Jev Noul screening of the retrieved evidence (Pattern B) decides first; empty/unusable evidence → abstain, Foundry never called |
| `MarketPriceEstimator.EstimateWithAiAsync` | `canEstimate` decided by Foundry *alongside* the price band and rationale, same call | Jev Noul per line: "is there a comparable reference price in the given set?" — a "no" skips Foundry for that line |
| `WebResearchComposer` | `offTopic` decided by the LLM *inside* the same Responses-API call that also runs the costly `web_search` tool | Jev screens topic-fit before the tool-using call is made at all |

### Pattern B — Jev screens retrieved evidence before it reaches a generative role

For each retrieved passage/snippet, 2-4 Noul questions (relevant? usable?
contradicts the question's premise? attempting to instruct the model —
prompt-injection detection) decide in code whether it is added to the
evidence block, the conflict block, or dropped — before it ever reaches
`answer`/`analyst`.

Applies to all three evidence-assembly points: `EmbeddingRetrievalService`
results feeding `AnswerComposer`; the pack items `NegotiationCouncil` hands
its analysts; the passages `MarketResearcher` retrieves from the market
corpus. One shared screening client, three call sites.

### Pattern C — Jev verifies a generative call's own output, after the fact

Independent of A/B: after `AnswerComposer` writes an answer with citations, a
Choice question per citation — "does the cited text actually support this
claim?" — before the citation is shown. Never replaces the generative call;
adds an integrity check on Appendix C rule 10 ("no evidence, no claim") that
today relies entirely on the LLM self-reporting correctly.

### Pattern D — Jev decides first, Foundry writes consistent with it (sequential grounding)

Supersedes the "Jev and Foundry independently, in parallel" idea from the
first pass of this analysis — that version had a real coherence risk (two
models judging the same clause independently can disagree). The corrected
design:

1. Jev decides the categorical sub-fields of an extraction stage first —
   `clauseType`, `riskLevel`/`severity`, `riskType`, `obligationType`,
   `recurrenceRule`, `autoRenewal`, `currency`, and `NegotiationCouncil`'s
   `leverType` — as Choice/Score/Noul questions over the same stage text.
2. Jev's answer is handed to Foundry as **given context** in the same
   stage's prompt ("this clause is already classified as Indemnification,
   risk Medium — write the verbatim text and normalized value consistent
   with that"), and Foundry's own JSON schema for the stage is **narrowed**
   to just the free-value fields it must still write (`rawText`,
   `description`, `party`, dates, amounts). Foundry never re-decides the
   categorical fields; it is not asked to.
3. **Confidence-gated trust, not a blind hand-off**: when Jev's confidence on
   a field is high, it goes to Foundry as a stated fact. Below the field's
   calibration threshold, it goes as a hint ("a preliminary signal suggests
   X — verify against the text and correct if wrong"), and the field is
   additionally routed into the review queue Raffa already has
   (`ExtractionConfidencePolicy`/`NeedsReview`) — no new review UI, reusing
   what exists.

Trade-off, stated plainly: the two calls become sequential (Jev's latency —
small, but no longer hidden behind Foundry's) instead of parallel, and the
"two independent opinions might disagree, flag it" safety net disappears —
traded for removing the coherence risk entirely, since only one model ever
decides each field. The confidence-gated hint/fact split above is what keeps
this honest rather than just trusting Jev blindly.

### Pattern E — Pre-parsed value extraction (regex/date-parser finds candidates, Jev chooses)

For the fields Pattern D cannot touch because they are genuinely free-value —
`startDate`/`endDate`/`effectiveDate`/`cancellationDeadline`,
`annualSpend`/`totalContractValue` — a deterministic candidate-finder
(a multi-locale date parser for IT/DE/EN contracts, an amount/currency
pattern matcher) enumerates every date-like/amount-like span in the stage
text; Jev's Choice question picks which span (if any) answers "which of
these is the cancellation deadline / the annual spend". Jev's answer is
always a span the parser already found, verbatim — it cannot invent a value
or transpose a digit, but it also cannot find a date the parser missed, so
the parser's own recall is the ceiling on this pattern's coverage.

This pattern needs its own groundwork (the candidate-finder itself, tested
against real IT/DE/EN contract date and amount formats) before any Jev
question can be asked — it is the only pattern of the five that is blocked
on new non-Jev code rather than ready to wire up today.

## 2. Candidate items for the next wave, ranked

Ranked by (confidence the three-way bar — time, quality, cost — actually
clears) × (how much new groundwork it needs). Each still needs its own
measured validation before a production switch-on, per this file's
inherited discipline from `jev-pilot-todo.md` — ranking is about build
order, not about skipping validation.

### NW-101 — RAG evidence screening (Pattern B), shadow-then-live on `EmbeddingRetrievalService`→`AnswerComposer` first

- **Must:** one shared Jev screening client (relevant/usable/contradicts/
  injection, 4 Noul questions per passage in one batched request per
  retrieval); wire it between `EmbeddingRetrievalService.SearchAsync` and
  `AnswerComposer`; log both the raw retrieval and the post-screen set for
  comparison before trusting it to actually drop passages.
- **Depends on:** nothing new infra-wise (same `AiGateway:Jev:*` switch).
- **Seats:** software-architect (screening client + wiring), security-architect
  (the injection-detection question is a real control — worth his sign-off
  on what counts as a positive hit and what happens then).

### NW-102 — `canDetermine`/`canEstimate`/`offTopic` gates (Pattern A)

- **Must:** three call sites (`AnswerComposer`, `MarketPriceEstimator`,
  `WebResearchComposer`) gated by a Jev Noul before the Foundry call;
  measure the actual skip rate and the time/cost saved on skipped calls
  before claiming a win — a gate that rarely fires saves nothing.
- **Depends on:** NW-101 for the `AnswerComposer` gate specifically (the gate
  reads the screened evidence, not the raw retrieval).
- **Seats:** software-architect.

### NW-103 — Citation support check (Pattern C)

- **Must:** one Choice question per citation key `AnswerComposer` emits,
  run after the answer, before the reply reaches the user; a "does not
  support" verdict routes to the existing abstain/low-confidence path
  rather than silently dropping the citation.
- **Independent of every other item** — can ship on its own.
- **Seats:** software-architect, product-owner (what happens to the answer
  when a citation fails the check — redact just that citation, or abstain
  the whole answer? needs a product decision, not an engineering default).

### NW-104 — Sequential grounding for `StagedExtractionService` categorical fields (Pattern D)

- **Must:** per the corrected design in §1 — Jev decides first, Foundry's
  schema narrows, confidence gates fact-vs-hint. Start with **one** stage
  (`LegalClauses`: `clauseType` + `riskLevel` are the cleanest fixed
  taxonomies) before extending to the other stages — this item's own
  coherence-risk trade-off is exactly the kind of claim that needs one
  stage's real data before it is trusted on the other six.
- **Depends on:** nothing new infra-wise; needs `ExtractionConfidencePolicy`
  read (not modified) to confirm the hint-routing hook-in is a clean reuse.
- **Seats:** software-architect, product-owner (the fact-vs-hint confidence
  cutoff is a product call on acceptable risk, not just an engineering
  default).

### NW-105 — Pre-parsed value extraction for dates/amounts (Pattern E)

- **Must:** the candidate-finder first (multi-locale date parser, amount/
  currency pattern matcher — tested against a real sample of IT/DE/EN
  contract text, not invented formats), *then* the Jev Choice wiring. Do
  not start the Jev half before the parser's own recall is measured against
  real documents — a parser that misses half the dates makes Jev's ceiling
  the same half, however good its choice accuracy is.
- **Depends on:** nothing else in this list; can run in parallel with
  NW-101-104.
- **Seats:** software-architect (parser), product-owner (acceptable parser
  recall before this is worth building the Jev half at all).

### Not in this wave

Everything that stays on Foundry regardless (per every prior pass of this
analysis): `rawText`/`description`/`party`/SKU/line-item free text in
extraction, the generative body of `AnswerComposer`/`NegotiationDraftingWorkflow`,
`MarketResearcher`'s query-text generation. No pattern above touches these —
restated here only so a future reader does not re-propose them.

## 3. What still needs a real answer before any of NW-101...105 goes to production

These are not new — they are the same open items `jev-pilot-todo.md` raised
for the first two pilots, which apply identically here and have not been
closed by shipping code:

1. **The OpenRouter/Jev response contract is still unverified against a
   live account** (this environment cannot reach `openrouter.ai`). Every
   new client in NW-101...105 inherits `JevHttpJsonClient`'s "fail loud on
   an unexpected shape" posture, but the first real call with a real key is
   still the first time any of this is actually proven to work end-to-end.
2. **No calibration data exists yet** for any of these tasks on Raffa's own
   documents/taxonomies — the high/medium/low (or fact/hint) thresholds in
   every pattern above are starting points, not measurements, exactly as
   flagged on the two pilots already shipped.
3. **Data governance**: NW-101/102/103 send retrieved tenant evidence and
   drafted answers to Jev/OpenRouter; NW-104/105 send contract text. This is
   the same "new subprocessor" question NW-99 raised for the first pilot and
   it was never actually closed (no sign-off is on file) — it does not get
   smaller as more call sites are added, it gets larger.

## 4. Addendum (2026-10-06): is Jev even the lever on "upload is slow"? — No.

A full read of the actual upload → admission → OCR → staged-extraction call
chain, done specifically to answer this question, found:

- The synchronous-upload design NW-27/NW-61 flagged is **already fixed**:
  `DocumentProcessingPipeline`/`ExtractionRequestedHandler` now run on
  `Raffa.Worker` behind a durable queue (wave w15/ADR-027). The user's
  "extremely slow" complaint today is about processing wall-clock time, not
  the HTTP request.
- The real cost is **two purely structural, model-independent bottlenecks**:
  (1) `StagedExtractionService.cs:190-200` runs its 7 extraction stages in a
  plain sequential `foreach`/`await` — nothing makes stage N depend on stage
  N-1, so this is pure avoidable serialization, each stage resending the
  whole document text to its own Foundry call (potentially 100s+ per call
  on demo's frontier model, `AiGatewayResilienceOptions.cs:27-29`); (2)
  `FoundryOcrClient.cs:70,93` runs its two Document Intelligence analyze
  calls (`prebuilt-read`, `prebuilt-layout`) sequentially on the same input
  bytes, each its own up-to-120s submit+poll loop, with nothing requiring
  one to wait for the other.
- **Fixing both with `Task.WhenAll` instead of sequential `await` is a pure
  orchestration change, zero model/vendor involvement**, and is plausibly
  the highest-leverage fix available for this complaint — up to ~7x on the
  extraction phase, roughly halving OCR, before any AI-vendor question even
  applies.
- Jev's fit on staged extraction specifically (per §1/§2's own field table)
  is only 1-2 narrow sub-fields per stage (`autoRenewal`, `riskLevel`/
  `severity`, `criticality`, `status`) out of each stage's larger set of
  free-value fields (`rawText`, `description`, dates, amounts, names) that
  only Foundry can produce — and that Foundry call cannot be skipped
  regardless of what Jev decides. Each stage's latency is dominated by
  reading/searching the long document text and writing several free-value
  fields per array row, not by the one enum field Jev could take off its
  plate — so even the "Jev decides first, Foundry's schema shrinks"
  design in §1 Pattern D saves at most a sliver of a 100s+ call, not a
  meaningful fraction of it.

**Verdict carried into §2: add a new item ahead of NW-101...105.**

### NW-106 — Parallelize the 7 extraction stages (not a Jev item)

- **Status: implemented** (branch `claude/parallelize-extraction-ocr`, not
  merged). `StagedExtractionService` now fires all seven `extract` calls
  concurrently (`StartStagesAsync`) and awaits/persists them one at a time
  afterward (`ApplyStageResultAsync`) — `DbContext` is not safe for
  concurrent use, so only the network wait is parallelized, never the
  persistence. A new test (`NW_106_fires_all_seven_extraction_stages_concurrently_not_one_after_another`)
  proves the overlap directly rather than inferring it from wall-clock time.
- **Narrowed from the original proposal**: the OCR half is **not** done.
  Closer reading of `FoundryOcrClient.OcrAsync` found a real dependency the
  first pass of this file missed: the ADR-017 page-budget check reads
  `prebuilt-read`'s own page count *before* deciding whether to call
  `prebuilt-layout` at all, specifically so an over-budget document (about
  to be rejected) never also pays for the more expensive layout call. Firing
  both concurrently would pay for layout on every over-budget document
  instead of none — a real cost regression, not a free win — and an
  existing test (`Ocr_fails_visibly_when_the_page_budget_is_exceeded_instead_of_truncating`)
  already encodes this exact guarantee. Left sequential.
- **Why it still came first:** zero new vendor, zero new calibration, zero
  data-governance question (§3) for the half that *was* safe to do — and it
  dwarfs anything Pattern A-E can plausibly claim on wall-clock time
  specifically. NW-101...105 remain worth doing for their own reasons
  (quality, cost, integrity) — just not as the answer to "why is this slow".
- **Seats:** software-architect, delivery-manager (regression risk on a
  hot, already-productionized path — wants a careful rollout, not a Jev
  question).
