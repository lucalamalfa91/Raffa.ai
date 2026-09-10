---
id: E11/F04/US01/T01
type: task
story: us-01-documents-screen
wave: 11
status: live
target_repo: raffa-web
---

# task-01-documents-mockup — Documents screen vs day1-demo.html

## Coding objective

Extract the Documents / upload screen from
`inputs/design/prototypes/day1-demo.html` (screens.md §3). Copy grid
(`400px 1fr`), dropzone, formats strip, pipeline dots, result card, and
table header type into `web/src/routes/documents/`. Do not invent spacing.
Keep `overflow-wrap: anywhere` on filenames (F06). Do not rebuild the
upload API client.

## Parent story AC covered
- AC-1, AC-2

## Files to create or modify
| Path | Change |
|------|--------|
| `web/src/routes/documents/documents.css` | match export |
| `web/src/routes/documents/*.tsx` | markup only if the export has missing nodes |
| `web/tests/routes/documents/` | two-column layout; no glyph-stack filename |

## Context the implementer needs
- Gap G-DOC. ADR-019/020. **Do not touch** sign-in, shell, other route CSS.

## Definition of done
- [ ] `npm test` in `web/` — Documents tests pass.
- [ ] CSS grid for `.documents-columns` is `400px 1fr` (or `minmax(280px, 400px) 1fr` if the export uses a min).

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | layout + wrap | `web/tests/routes/documents/` |

## Open questions blocking this task
- none
