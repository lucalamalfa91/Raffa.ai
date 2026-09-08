---
id: E13/F01/US01/T02
type: task
story: us-01-v2-foundation
wave: 13
status: live
target_repo: contigo-backend
---

# task-02-foundry-gateway — `FoundryAiGateway` + DI swap + structured output + no-tools compliance

## Coding objective

Implement `FoundryAiGateway` in `backend/src/Contigo.AiGateway/Foundry/`
covering ADR-004's five roles behind the existing `IAiGateway`: `ocr`
(Azure AI Document Intelligence `prebuilt-read` / `prebuilt-layout`, full
document, page map, page count in metadata, page budget from
`AiGatewayOcrOptions` failing visibly), `classify` (fixed label set of
`AiDocumentType` + confidence, JSON schema output), `extract` (schema-
constrained JSON as today's contract), `embed` (1536-dim vectors,
`AiGatewayConstants.EmbeddingDimensions`), `answer` (structured JSON:
`canDetermine`, `answerMarkdown`, `citationKeys[]`, `actionKeys[]`,
`abstainReason?`, `followUps[]` — extend `AiAnswerRequest` with an optional
`SystemPrompt` / `PackJson` and `AiAnswerResult` with the new fields while
keeping today's `Answer` / `Citations` for the fixture path). Model ids
come from `AiGatewayModelOptions`; endpoint / project / Document
Intelligence connection from `AiGateway:Endpoint`, `AiGateway:ProjectName`,
`AiGateway:DocumentIntelligenceConnection` (already injected by Container
Apps, `infra/modules/containerapps/main.tf`); auth via `DefaultAzureCredential`
(managed identity), never a key. The `answer` request must carry **no
`tools`, `tool_choice`, grounding or browsing** fields; temperature ≤ 0.2.
Change `AddAiGatewayModule`: when `AiGateway:Endpoint` is set register
`FoundryAiGateway`, else `FixtureAiGateway`; **always** wrap the inner
gateway with the existing `LoggingAiGateway` (decorator). Every call logs
model, version, prompt version, timestamp, input hash (plus page count for
OCR) through `LoggingAiGateway` — never raw text. HTTP to Foundry is
exercised with a fake handler in tests; no live Azure in unit tests.

## Parent story AC covered
- AC-1, AC-2, AC-3

## Files to create or modify
| Path | Change |
|------|--------|
| `backend/src/Contigo.AiGateway/Foundry/FoundryAiGateway.cs` (+ per-role clients, prompts folder `Prompts/`) | new |
| `backend/src/Contigo.AiGateway/Contracts/AiAnswerRequest.cs`, `AiAnswerResult.cs` | extend (backward compatible) |
| `backend/src/Contigo.AiGateway/Configuration/AiGatewayFoundryOptions.cs` | new (`AiGateway:Endpoint`, `ProjectName`, `DocumentIntelligenceConnection`, `AnswerTemperature`) |
| `backend/src/Contigo.AiGateway/ServiceCollectionExtensions.cs` | endpoint swap + `LoggingAiGateway` decorator |
| `backend/src/Contigo.AiGateway/Contigo.AiGateway.csproj` | Azure SDK packages (only here) |
| `backend/tests/Contigo.AiGateway.Tests/Foundry/*` | fake HTTP handler tests per role; no-tools compliance; DI swap |
| `backend/tests/Contigo.AiGateway.Tests/ServiceCollectionExtensionsTests.cs` | endpoint set → Foundry inside Logging; unset → fixture inside Logging |
| `backend/tests/Contigo.AiGateway.Tests/SdkAllowListTests.cs` | no Azure AI SDK reference outside `Contigo.AiGateway` (reads csproj files) |

## Context the implementer needs
- **Architecture decisions in force**: ADR-004 (amended: structured output, no tools, always log-wrapped), ADR-008 (one Foundry project per env), ADR-011 (no-training endpoint, hash-only logs, managed identity), ADR-017 (OCR full document, page budget), ADR-024.
- Gap G-FOUNDRY. The fixture stays the test / local path; do not change fixture answer behaviour (F06 replaces the chunk-concat by supplying a prompt + pack).
- **Do not touch**: `Contigo.Chat`, `Contigo.Documents.Contracts`, `Program.cs`, `Contigo.slnx` (owned by T01 this phase), `web/`.

## Definition of done
- [ ] `dotnet test backend/tests/Contigo.AiGateway.Tests` exit 0 — Foundry registered when endpoint set, fixture when unset, `LoggingAiGateway` is the outer type; the captured `answer` request body has no `tools` / `tool_choice` / grounding keys; classify returns a label from the fixed set
- [ ] `dotnet test backend/Contigo.slnx` exit 0
- [ ] SDK allow-list test green (only `Contigo.AiGateway.csproj` references `Azure.AI.*` / `Azure.Identity`)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | DI swap + decorator order | `ServiceCollectionExtensionsTests.cs` |
| unit | request shapes per role, no tools on answer, structured parse | `Foundry/*Tests.cs` |
| unit | no SDK leak | `SdkAllowListTests.cs` |

## Open questions blocking this task
- OQ-askv2-009 — live Foundry only on `dev` / `demo`; assumption in force: fake handler in tests

## Wave-spec entry
```yaml
- id: E13/F01/US01/T02
  prompt: reports/workitems/epic-13-ask-v2/feature-01-v2-foundation/us-01-v2-foundation/tasks/task-02-foundry-gateway.md
  produces: [foundry-gateway]
  depends_on: []
  effort: L
  layer: backend
  status: live
```
