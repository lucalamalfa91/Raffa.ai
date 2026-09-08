# Ask copilot gaps — epic-12 / e12

Oracle: `inputs/ask-copilot-brief.md`. Status: OPEN rows become e12 live tasks.

| ID | Status | Brief | Evidence | e12 |
|----|--------|-------|----------|-----|
| G-FOUNDRY | OPEN | §6 Foundry 5-role gateway | `AddAiGatewayModule` registers only `FixtureAiGateway`; `LoggingAiGateway` is not wrapped | F01 |
| G-FIXTURE-CATALOG | OPEN | §6 expand fixture; Allianz-class names | `FixtureBenchmarkAdapter` is a thin Internal Dataset, not a labelled worldwide mock | F02 |
| G-NOT-WIRED | OPEN | §1 structured “not wired”; §4 natural language | `ChatEndpointExtensions.ToStructuredNotWiredResponse`; `FixtureAiGateway.AnswerAsync` concatenates chunks | F03 |
| G-DOMAIN-GATE | OPEN | §4.3–§4.4 greetings / off-domain / legal | No persona prompt; “ciao” hits Semantic RAG | F03 |
| G-PDF-CHUNKS | OPEN | §6 re-OCR / re-embed | `IDocumentStorage` has `SaveAsync` only; retrieved `chunk_text` includes `%PDF-1.4` | F04 |
| G-RICH-UI | OPEN | §5 rich reply | `ChatMessage.tsx` shows raw text + `Document:<guid>` chips + engineer `route` line | F05 |
| G-RLS | WAVE_COVERED | §2 tenant RAG isolation | ADR-009 / ADR-011 already in force; e12 must not regress | — |
| G-PAID-API | DEFERRED | ADR-001 Internal Dataset | Paid Tropic/Vendr stays a later adapter behind `IBenchmarkService` | — |

Every OPEN row cites a brief section and a code path. DEFERRED is not an e12 task.
