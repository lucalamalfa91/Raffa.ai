# Ask copilot mandate — epic-12 / e12

Delta only. ADR-001 still forbids a paid market API on the first `demo`.
ADR-004 still splits Foundry into five roles behind `IAiGateway`.
ADR-011 still requires authz-before-retrieval and tenant-scoped RAG.
This wave makes Ask Raffa a **savings / negotiation copilot**, not a
generic document dump.

## Oracle

- Brief: `inputs/ask-copilot-brief.md`
- Live evidence: https://mango-pond-061bc6d1e.6.azurestaticapps.net/ask

## Scope

**In:** Foundry-backed `IAiGateway` (five roles) when `AiGateway:Endpoint` is
set; fixture otherwise; always wrap `LoggingAiGateway`. Expand the
`IBenchmarkService` fixture catalog (labelled representative). Domain gate
(off-topic / greeting / legal). Context pack from **tenant RAG** plus
**benchmark numbers** (two corpora, never mixed in pgvector). Chat endpoint
returns articulated markdown + citations + actions. Re-OCR / re-embed so
Ask does not retrieve `%PDF-1.4`. Rich `/ask` UI (prose, cards, preview,
deep links). Amend ADR-001/004/011/018/020; add ADR-023.

**Out:** paid Tropic/Vendr as a hard demo dependency. Legal advice. Cross-tenant
RAG. Rewriting epic-01…11. Touching `slice.current.yaml` while e1011 runs.

## Success

An operator on `demo` asking “ciao”, “carbonara”, “is Allianz above market?”,
or “can I sue?” gets natural-language Raffa that saves money or honestly
abstains — never a PDF dump or “not wired to an endpoint”.
