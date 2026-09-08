# Ask Contigo — requirements (savings copilot)

> **Superseded (2026-09-08)** by `inputs/requirements.md` (Ask Contigo V2, epic-13 / e13, ADR-024). Kept for the record; do not use as an oracle.

Status: **binding input** for epic-12 / e12. Oracle for Passata 1 (`contigo-ask-process.yaml`)
and for fan-out prompts. Does not replace `inputs/product-spec.md`; it **amends** how
Ask Contigo (§8.3–§8.4) and the benchmark fixture (ADR-001) show up to the buyer.

Audience: procurement. Language: the user's language. Tone: a specialist who helps you
**save money on contracts**, not a lawyer and not a generic chatbot.

---

## 1. Problem

On `demo` today (`/ask`):

1. Structured questions (renewals, spend) return “not wired to an endpoint”.
2. Semantic questions dump retrieved chunk text (including raw PDF headers) because
   `FixtureAiGateway.AnswerAsync` concatenates evidence.
3. Citation chips show `Document:<guid>`. There is no contract preview and no
   articulated prose.

That is not an assistant.

## 2. Goal

Ask Contigo is a **domain-specialized AI assistant** hosted on **Microsoft Foundry**
(ADR-004, ADR-008). It reasons over:

| Corpus | What | Isolation |
|--------|------|-----------|
| **Yours** | Tenant RAG (`embedding` + RLS) — the contracts this workspace uploaded | ADR-009 / ADR-011: never another tenant's documents |
| **Market** | `IBenchmarkService` — **fixture catalog now** (ADR-001 Internal Dataset), **external APIs later** behind the same interface | Not mixed into tenant pgvector |

It tells you whether **the contract you are loading or looking at** is **in line, below, or
above** the market band (P25–P75), and **where you can push in negotiation** vs where you
cannot — using deterministic savings/negotiation numbers already in Contigo, not invented
prices.

## 3. Personas and non-goals

**In scope**

- Commercial comparison vs market fixture (labelled *representative*, not live list).
- Negotiation levers already computed (`NegotiationStrategyCalculator`): opening, range,
  walk-away — the model **narrates** them.
- Routing the user to the right screen (Contract 360, Renewals, Savings, Quote check,
  Documents).
- Polite refusal of off-domain questions, with a redirect into *this* portfolio.

**Out of scope (spec §1.2 + this brief)**

- Legal advice, “would this hold in court”, redline/authoring, e-sign.
- Recipes, sports, generic coding, medical, anything not procurement-savings.
- Cross-tenant raw contracts in RAG.
- Paid Tropic/Vendr (or similar) as a hard dependency of the first `demo` (ADR-001).

## 4. Conversational behaviour

### 4.1 Natural language

Every Contigo turn is **articulated prose** (short paragraphs, optional short bullets).
Not a file dump. Not a list of document ids. Documents appear as **evidence inside the
story**.

### 4.2 Never invent

If benchmark sample is too thin or retrieval found nothing relevant: say **insufficient
data** (spec §10.4). Do not fabricate P50, savings, or clause text.

### 4.3 Off-domain and greetings

If the question is outside Contigo (e.g. carbonara) **or** a bare greeting:

> I can’t help with that. I can help you save money on contracts. Want to look at
> **Allianz**? I can see commercial terms we can improve.

Name a **real** contract or savings opportunity from *this* tenant. If the portfolio is
empty: invite upload. **Do not** run RAG on off-domain questions.

### 4.4 Legal

If they ask for legal interpretation: refuse, then offer the commercial analogue
(“I can tell you if the liability cap sits above the fixture band, not whether it is
valid under CH law”).

## 5. Rich reply (UI)

A successful turn includes:

| Element | Rule |
|---------|------|
| **Markdown body** | Prose with inline citations `[1]`, `[2]` on claims |
| **Citation cards** | Human title (supplier / contract), page/section, snippet — **never** raw `Document:guid` |
| **Contract preview** | First-page thumbnail (or honest placeholder) on the card |
| **Actions** | Deep links the model must not invent: `/contracts/:id`, `/renewals`, `/`, `/quotes/...` as `{ label, href }` from the API |
| **Abstain / redirect** | Same layout: warm prose + optional card + one CTA. Do not use the red “Cannot determine reliably” block as the *only* UX except when there is truly no hook |

Hide engineer chrome: no “Structured query…” / “Clause retrieval…” route line.

## 6. Foundry / pipeline

- Implement `FoundryAiGateway` for ADR-004’s five roles (`ocr`, `classify`, `extract`,
  `embed`, `answer`). Domain modules still call `IAiGateway` only.
- Register Foundry when `AiGateway:Endpoint` is set (CA already injects
  `AiGateway__Endpoint` / `ProjectName` / `DocumentIntelligenceConnection`); else fixture.
- Always wrap `LoggingAiGateway`.
- Re-OCR / re-embed existing tenant documents so Ask does not retrieve `%PDF-1.4`.
- Expand `FixtureBenchmarkAdapter` with many illustrative worldwide suppliers (including
  insurance names such as Allianz). Keep `Source = fixture` and weak-sample abstain.

## 7. Acceptance (observable)

| # | Check |
|---|--------|
| A1 | “ciao” / carbonara → natural-language decline + portfolio hook, no PDF dump |
| A2 | “Is my Allianz above market?” → in line / below / above from P25–P75 **or** insufficient data; provenance `fixture` |
| A3 | “Can I sue?” → no legal advice; commercial redirect + link |
| A4 | Reply is prose + citation card + preview + at least one in-app deep link |
| A5 | Opening/target/walk-away numbers match the calculator, not a model guess |
| A6 | Another tenant’s contracts never appear in evidence (ADR-011) |

## 8. Traceability

| This brief | Existing decision |
|------------|-------------------|
| Market mock → later API | ADR-001, spec §10.2 Internal Dataset |
| Foundry via gateway | ADR-004, ADR-008 |
| Tenant RAG only | ADR-009, ADR-011 |
| Ask screen | ADR-018, ADR-020 screen 7, spec §8.3–§8.4 |
| Savings / negotiation facts | epic-04 / epic-05 calculators |

Amended ADRs: **001, 004, 011, 018, 020**. New: **ADR-023**.
Work: **epic-12**, slice **`e12`**, fan-out on the **live** `contigo-process.yaml` after HITL.
