---
id: E12/F05/US01/T01
type: task
story: us-01-rich-ask-ui
wave: 12
status: live
target_repo: raffa-web
---

# task-01-rich-ask-ui — Markdown, citation cards, preview, actions

## Coding objective

Update `/ask` to consume the F03 JSON. Render `answerMarkdown` (markdown,
not a raw dump). Replace `Document:<guid>` chips with citation cards
(title, page, snippet) and a first-page preview image or honest
placeholder. Render `actions` as buttons/links using the supplied `href`.

Hide `message.route` engineer chrome. For `domainRedirect` / greeting
turns, use warm prose + CTA — do not force the red abstain block as the
only layout.

Add `GET /api/documents/{id}/preview` in the API **only if** F03 did not
already land it and you can do so without editing Chat domain files F03
owns. Prefer extending the OpenAPI client regen already used by web.
Stay on ADR-019 tokens (`ask.css`, components). Do not restyle the shell
(e11).

Regenerate the TS client if the OpenAPI surface changed.

## Parent story AC covered

- AC-1 … AC-6

## Files to create or modify

| Path | Change |
|------|--------|
| `web/src/routes/ask/ChatMessage.tsx` | markdown + cards + actions |
| `web/src/routes/ask/askViewModel.ts` | new fields; drop raw id chips |
| `web/src/routes/ask/index.tsx` | suggestions still domain-shaped |
| `web/src/styles/ask.css` | card + preview, no new design system |
| `web/tests/` | ciao / citation card / no route line |
| API preview endpoint | only if missing after F03 |

## Context the implementer needs

- **Architecture**: ADR-018/020 amendments, ADR-019, ADR-023. Gap G-RICH-UI.
- **Do not touch**: shell/signin CSS (e11), Foundry gateway, benchmark catalog.

## Definition of done

- [ ] `npm test` in `web/` — markdown renders; citation label is not a raw
      guid; route line absent; greeting layout is not only `.abstain-block`.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | rich reply + no engineer chrome | `web/tests` ask tests |

## Open questions blocking this task

- none

## Wave-spec entry

```yaml
- id: E12/F05/US01/T01
  produces: [rich-ask-ui]
  depends_on: [copilot-endpoint]
```
