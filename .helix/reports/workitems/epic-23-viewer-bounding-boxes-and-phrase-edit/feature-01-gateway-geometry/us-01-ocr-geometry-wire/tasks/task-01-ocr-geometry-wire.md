---
id: E23/F01/US01/T01
type: task
story: us-01-ocr-geometry-wire
wave: w18
status: live
target_repo: raffa-backend
---

# task-01-ocr-geometry-wire — Call `prebuilt-layout` and widen the OCR wire to carry geometry

## Coding objective
Widen the AI Gateway's OCR wire so a pixel box can ride beside each page's text.
(a) In `Raffa.AiGateway/Foundry/Wire/DocumentIntelligenceContracts.cs`, add
`Words`/`Polygon` geometry members to `DocumentIntelligencePage` (and any
nested `DocumentIntelligenceWord`/`DocumentIntelligencePolygon` shape) matching
the `prebuilt-layout` analyze response, keeping the existing `Spans`
page-text resolution intact. (b) In `Raffa.AiGateway/Contracts/AiOcrPage.cs`
and `AiOcrResult.cs`, grow `AiOcrPage` with the matching geometry so page +
text + box travel together. (c) In
`Raffa.AiGateway/Foundry/FoundryOcrClient.cs`, call `prebuilt-layout` (in
addition to the existing `prebuilt-read`) via the same
`documentModels/{model}:analyze?…&stringIndexType=utf16CodeUnit` long-running
operation, and extend `MapPages` to carry the `words`/`polygon` geometry per
page; a page with no geometry maps to a null box, never an error. Keep the
`ocr` role binding, the page budget and the page-count logging unchanged
(ADR-017 w18: no new role, no SKU, cost stays metered).

## Parent story AC covered
- AC-1 `prebuilt-layout` is called alongside `prebuilt-read`, behind the AI Gateway, on the same account.
- AC-2 `DocumentIntelligencePage` gains `Words`/`Polygon` geometry beside its existing `Spans`.
- AC-3 `AiOcrPage` carries the matching geometry.
- AC-4 a page with no geometry still maps to `AiOcrPage` with a null box.

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/Raffa.AiGateway/Foundry/Wire/DocumentIntelligenceContracts.cs | add `Words`/`Polygon` to `DocumentIntelligencePage` (geometry beside `Spans`) |
| backend/src/Raffa.AiGateway/Contracts/AiOcrPage.cs | add geometry member to `AiOcrPage(page, text)` |
| backend/src/Raffa.AiGateway/Contracts/AiOcrResult.cs | geometry flows on `Pages` |
| backend/src/Raffa.AiGateway/Foundry/FoundryOcrClient.cs | call `prebuilt-layout`, widen `MapPages` to carry geometry |
| backend/src/Raffa.AiGateway/Fixtures/FixtureAiGateway.cs | fixture `DecodePages` emits null/box geometry so CI stays provider-free |

## Context the implementer needs

**Closes: NW-63r**

- **Architecture decisions in force**: ADR-017 w18 (`prebuilt-layout` called; wire widens; `AiOcrPage` grows geometry); ADR-002 (gateway stays domain-agnostic — `AiOcrPage` is the gateway-side twin).
- **Do not touch**: `Raffa.Documents.Contracts` (the domain consumes `AiOcrPage` in a later phase); Terraform / `modules/foundry` (model binding unchanged).

## Definition of done
- [ ] `dotnet build backend/Raffa.slnx` exits 0
- [ ] `dotnet test backend/tests/Raffa.AiGateway.Tests` exits 0, with a test asserting `MapPages` carries `words`/`polygon` and a null-geometry page maps to a null box
- [ ] a fixture test proves a `prebuilt-layout`-less document still reads as text-only (null geometry)

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | `MapPages` widens geometry and tolerates null | `backend/tests/Raffa.AiGateway.Tests` |
| unit | `AiOcrPage` carries geometry without breaking the text path | `backend/tests/Raffa.AiGateway.Tests` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E23/F01/US01/T01
  prompt: reports/workitems/epic-23-viewer-bounding-boxes-and-phrase-edit/feature-01-gateway-geometry/us-01-ocr-geometry-wire/tasks/task-01-ocr-geometry-wire.md
  produces: [ocr-geometry-wire]
  depends_on: []
  effort: L
  layer: backend
  status: live
```
