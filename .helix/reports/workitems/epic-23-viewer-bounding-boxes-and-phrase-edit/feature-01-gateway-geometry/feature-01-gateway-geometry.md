---
id: feature-01
type: feature
parent: epic-23
wave: w18
status: active
---

# feature-01-gateway-geometry — Gateway ships `words`/`polygon` geometry

## Slice
The AI Gateway calls `prebuilt-layout` (in addition to `prebuilt-read`) and
widens the wire so the OCR result carries per-word `words`/`polygon` geometry,
and `AiOcrPage` grows the matching geometry so the evidence rows can persist a
pixel box, not just a utf16 span. No new role or SKU.

## User stories
| ID | Title | Wave |
|----|-------|------|
| us-01 | ocr-geometry-wire | w18 |

## Architecture decisions in force
- ADR-017 w18 — `prebuilt-layout` is called (its w17 "not called" clause ended); `words`/`polygon` widen the wire; `AiOcrPage` grows geometry.
- ADR-002 — the gateway stays domain-agnostic (`AiOcrPage` is the gateway-side twin).

## Target repo
`raffa-backend`
