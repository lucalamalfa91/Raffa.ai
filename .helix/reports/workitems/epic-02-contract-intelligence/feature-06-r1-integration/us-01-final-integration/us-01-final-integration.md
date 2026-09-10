---
id: us-01
type: user-story
parent: feature-06
wave: R1
status: active
---

# us-01-final-integration — R1 Contract Intelligence integration

## Story

As a **product owner**, I want the R1 definition of success proven end-to-end on
`dev`/`demo`, so that I can confirm "a customer can upload contracts and ask reliable
questions".

## Acceptance criteria

- [ ] AC-1 Upload → parse/OCR → classify → extract → portfolio → 360 → Ask Raffa (with citations) works end-to-end.
- [ ] AC-2 Low-confidence field correction preserves original extraction + history.
- [ ] AC-3 Cross-tenant isolation holds across the whole path.
- [ ] AC-4 At least one scanned or image-based contract extracts via Document Intelligence (full document, ADR-017).

## Definition of done

- [ ] `dotnet test` (integration) runs the full R1 path on a born-digital fixture **and** a scanned/image fixture with citations; `demo` smoke path documented.

## Dependencies

| Depends on | Why |
|------------|-----|
| every R1 leaf artifact | proves the wave |

## Architecture decisions in force

- ADR-002, ADR-003, ADR-004, ADR-009, ADR-011, ADR-016, ADR-017.

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | R1 end-to-end integration test | L | phase-18 |

## Council decisions carried into this story

R1 success = upload → OCR/parse (digital + scanned) → extract with evidence → reliable Q&A with citations.

## Open questions

- none.
