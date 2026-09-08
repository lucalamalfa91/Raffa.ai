---
id: E12/F01/US01/T01
type: task
story: us-01-foundry-gateway
wave: 12
status: live
target_repo: contigo-backend
---

# task-01-foundry-gateway — FoundryAiGateway + LoggingAiGateway wrap

## Coding objective

Implement `FoundryAiGateway` in `Contigo.AiGateway` covering ADR-004's five
roles (`ocr`, `classify`, `extract`, `embed`, `answer`). Azure SDKs (Foundry /
Document Intelligence / embeddings) live **only** in this project.

Change `AddAiGatewayModule`: if `AiGateway:Endpoint` (CA already injects
`AiGateway__Endpoint` / `ProjectName` / `DocumentIntelligenceConnection`) is
set, register Foundry; else keep `FixtureAiGateway`. Always wrap the inner
gateway with `LoggingAiGateway` (exists, currently unregistered).

Do not change Chat, Documents, or Benchmark in this task. HTTP to Foundry is
tested with a fake handler — no live Azure in unit tests.

## Parent story AC covered

- AC-1, AC-2, AC-3

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Contigo.AiGateway/` | `FoundryAiGateway` + role clients |
| `backend/src/Contigo.AiGateway/ServiceCollectionExtensions.cs` | endpoint swap + log wrap |
| `backend/src/Contigo.AiGateway/Logging/LoggingAiGateway.cs` | register as decorator |
| `backend/tests/Contigo.AiGateway.Tests/` | fake HTTP + DI tests |

## Context the implementer needs

- **Architecture**: ADR-004, ADR-008, ADR-023. Gap G-FOUNDRY.
- **Do not touch**: Chat endpoints, `FixtureBenchmarkAdapter`, `web/`.
- Fixture `AnswerAsync` chunk-concat stays until F03; this task only swaps the
  live path and wrapping.

## Definition of done

- [ ] `dotnet test` on `Contigo.AiGateway.Tests` — Foundry registration when
      endpoint set; fixture when unset; `LoggingAiGateway` is the outer type.
- [ ] Architecture test or csproj assert: Azure AI SDK refs only in
      `Contigo.AiGateway`.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | DI swap + log wrap | `Contigo.AiGateway.Tests` |
| unit | no SDK leak | allow-list test |

## Open questions blocking this task

- none

## Wave-spec entry

```yaml
- id: E12/F01/US01/T01
  produces: [foundry-gateway]
```
