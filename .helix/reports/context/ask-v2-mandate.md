# Ask V2 mandate — epic-13 / e13 (replaces epic-12 / e12)

Delta only. ADR-001 still forbids a paid market API on the first `demo`.
ADR-004 still splits Foundry into five roles behind `IAiGateway`.
ADR-009 / ADR-011 still require RLS and authz-before-retrieval.
ADR-023 is **superseded** by ADR-024. epic-12 / e12 were never launched and
stay on disk with a superseded banner (HITL 2026-09-08, `inputs/requirements.md`
§0 D4).

## Oracles

- Requirements: `inputs/requirements.md` — decisions D1–D8, requirements
  R-DOC / R-CONV / R-ASK / R-CMP / R-STR / R-PORT / R-SYS / R-MKT / R-SUP /
  R-AI / R-EVD / R-WEB, API contract §6, data model §7, acceptance §10,
  decomposition §12, assumptions §13.
- Design: `inputs/design/prototypes/Raffa V2 Prototype.html` (bundled
  export), unpacked and searchable at `inputs/design/prototypes/raffa-v2/`
  (`app.jsx`, `markup.html`, `styles.css`) with the authored `ia-v2.md`
  (routes, nav, cross-links, Ask intents, divergences) and `screens-v2.md`
  (screens, states, verbatim copy). The requirements win where the prototype
  differs; the difference is named in `ia-v2.md`.
- Code baseline: `integration` @ `4a1bddf` (2026-09-08).

## Scope

**In.** Three sources of truth, one rule (every claim cites one of them or
Raffa abstains; no web, no model memory): (1) validated contracts — tenant
tables + tenant `embedding` (RLS); (2) market intelligence — a third-party
feed of how companies close contracts, **mock now**, behind
`IMarketIntelligenceProvider`, projected into benchmark rows *and* a shared
read-only **market index** (`market_embedding`, never tenant rows); (3) the
Raffa capability catalog. Uploads only in Documents (D1) with an
**admission gate before persistence** that rejects non-contracts and never
stores them (D3); PDF/DOCX/XLSX + PNG/JPG via OCR, all spec §4.1 types,
multi-file with per-file outcome (D7); Admin and Procurement upload (D8).
Conversations server-side per user and workspace under RLS (D5). Domain
gate, intent planner, context pack, Foundry `answer` role with structured
output and **no tools / no web grounding**, grounding + numeric guards,
rich reply (markdown, human citation cards with preview, in-app actions).
Contract vs market comparison, renewal strategy per contract, portfolio
strategy on a **deterministic composite criticality score** (D6). Supplier
identity (fact, entity, resolver, back-fill). Capability catalog + routing +
feature citations. Foundry gateway for the five roles, always wrapped by
`LoggingAiGateway`. Web V2 IA for the pilot path: Ask is home, two-tier
nav, Documents V2, citation landing in Contract 360.

**Out.** Attachments in the chat; persisting rejected documents; legal
advice; paid market API client as a `demo` dependency; web / browsing /
tool grounding in any model role; cross-tenant RAG; mobile; rewriting
epic-01…12; touching `slice.current.yaml` from this process.

## Never touch

`wave-spec.execution.yaml` and the other prior wave-specs, `slices/e01…e11`,
`e1011`, `e12`, `slice.current.yaml`, `epic-01…12` (beyond the epic-12
banner), locked ADRs (002/003/005–010/012–017/019/021/022/023),
`inputs/ask-copilot-brief.md`.

## Success

An operator on `demo` walks the V2 pilot path (`inputs/requirements.md`
§10 A1–A14): drops a recipe, a photo and an MSA together and only the MSA
is kept; asks "ciao", "posso fare causa?", "is my Allianz contract above
market?", "come dovrei affrontare il rinnovo Salesforce?", opens a new chat
for "quali sono i contratti più critici e dove posso risparmiare?", asks
"cosa sai fare?" — and every reply is articulated prose with citation
cards (page / section or market record with provenance) and working deep
links into Contract 360, Renewals, Documents or Quote check; nothing is
invented, nothing comes from the web, another tenant never appears.
