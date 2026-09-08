# ADR-023 — Ask Contigo is a domain savings copilot

- **Status**: accepted
- **Date**: 2026-09-08
- **Deciders**: product-owner (persona + non-goals) + software-architect (gateway, context pack) + security-architect (two-corpus isolation) + ux-ui-designer (rich reply on screen 7)
- **Locked citations**: `inputs/ask-copilot-brief.md`; ADR-001 (fixture market adapter); ADR-004 (five Foundry roles behind `IAiGateway`); ADR-009 / ADR-011 (tenant RAG, authz-before-retrieval); ADR-018 / ADR-020 (route `/ask`, screen 7); spec §8.3–§8.4, §10.2, §10.4.

## Context and problem statement

Ask Contigo on `demo` today is not an assistant. Structured questions return
“not wired to an endpoint”. Semantic questions dump retrieved chunk text
(including raw PDF headers) because the fixture answer role concatenates
evidence. Citation chips show `Document:<guid>` with no preview and no
articulated prose.

The buyer’s job is to **save money on contracts**, not to search a document
dump or receive legal advice. The product already has deterministic savings
and negotiation numbers (`IBenchmarkService`, `NegotiationStrategyCalculator`).
The missing decision is how Ask Contigo uses Foundry, tenant RAG, and the
market catalog together.

## Decision drivers

- Natural language, well-articulated markdown, citations in prose.
- Never invent P50 / savings / clause text (spec §10.4).
- Tenant documents never leave the tenant (ADR-009 / ADR-011).
- Market comparison must not mean “other tenants’ PDFs”.
- First `demo` must not depend on a paid Tropic/Vendr API (ADR-001).
- No legal advice (spec §1.2).
- Foundry SDKs stay inside `Contigo.AiGateway` (ADR-002 / ADR-004).

## Considered options

1. **Generic grounded Q&A** — retrieve chunks, echo them, cite document ids.
2. **Cross-tenant RAG as “the market”** — embed every customer’s contracts into
   one comparison index.
3. **Savings copilot with two corpora** — tenant RAG for *your* contracts;
   `IBenchmarkService` (fixture now, paid API later) for *the market*; Foundry
   `answer` role narrates deterministic numbers and refuses off-domain / legal.

## Decision outcome

**Chosen: Option 3.** Ask Contigo is a **domain-specialized savings and
negotiation copilot** hosted on Microsoft Foundry.

| Corpus | Seam | Isolation |
|--------|------|-----------|
| Yours | Tenant pgvector (`embedding` + RLS) | Never another tenant |
| Market | `IBenchmarkService` | Not mixed into tenant pgvector |

The LLM **narrates** calculator outputs (P25–P75 band, opening / range /
walk-away). It does not invent prices. Insufficient sample → say so.

Off-domain questions and bare greetings: decline warmly and redirect to a
real contract or savings hook in *this* workspace. Legal interpretation:
refuse, then offer the commercial analogue.

The chat API returns articulated markdown plus structured citations and
in-app actions `{ label, href }`. The `/ask` UI renders prose, human citation
cards with contract preview, and deep links — not engineer route chrome and
not raw `Document:<guid>` chips.

`AddAiGatewayModule` registers a Foundry-backed `IAiGateway` when
`AiGateway:Endpoint` is set; otherwise the fixture. Always wrap
`LoggingAiGateway`. Existing tenant documents must be re-processed so Ask
does not retrieve `%PDF-1.4`.

## Implications for the decomposition

- Epic-12 / e12 implements F01 (Foundry gateway) … F05 (rich UI) against this ADR.
- Amendment footers on ADR-001, 004, 011, 018, 020 point here; they do not
  replace those decisions.
- A later paid market adapter is a new `IBenchmarkService` implementation,
  not a new Ask architecture.

## Assumptions

- The Internal Dataset fixture, labelled *representative*, is enough for the
  first `demo` conversation (ADR-001). Live list prices are a later adapter.
- Container Apps already inject Foundry connection settings; wiring the SDK
  is an application task, not a new Terraform epic.
