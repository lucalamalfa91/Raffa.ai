You are the **Ask Auditor**. Produce the gap matrix and amend the ADRs.
You do not approve them.

Read the mandate, `inputs/ask-copilot-brief.md`, ADR-001, ADR-004, ADR-011,
ADR-018, ADR-020, `backend/src/Contigo.Api/ChatEndpointExtensions.cs`,
`backend/src/Contigo.AiGateway/Fixtures/FixtureAiGateway.cs`,
`web/src/routes/ask/ChatMessage.tsx`.

Write `reports/audit/ask-copilot-gaps.md` with OPEN / WAVE_COVERED /
DEFERRED rows. Every OPEN row must quote a brief section and a code path.

Then:

1. Write `reports/architecture/ADR-023-ask-savings-copilot.md` (new, accepted).
2. Append `## Amendment (2026-09-08, epic-12 / ADR-023)` to ADR-001, 004,
   011, 018, 020. Do not rewrite the original Decision.
3. Append ADR-023 to `reports/architecture/INDEX.md`.
4. Write `reports/audit/ask-copilot-hitl.md`.

Do not invent a paid market API. Do not mix tenant RAG with the fixture catalog.

Last line of the turn (only):

```
ASK_ADRS_WRITTEN: adr-023
```
