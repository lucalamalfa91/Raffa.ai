---
id: E13/F09/US01/T02
type: task
story: us-01-web-v2
wave: 13
status: live
target_repo: contigo-web
---

# task-02-web-rich-reply — Markdown body, citation cards, actions, redirect / refusal / abstain layouts

## Coding objective

Build the reply components of the V2 Ask screen as pure, API-agnostic
React components under `web/src/routes/ask/reply/` (wired to the real
reply contract by F09/T04): `ReplyMarkdown` (safe markdown → HTML:
paragraphs, bold, short lists, inline `[n]` rendered as superscript links
to the matching card; no raw HTML pass-through; a small hand-written
renderer or a dependency-free approach — adding a markdown library is
allowed only if it is ≤ 30 kB gzipped and sanitizes by default),
`CitationCard` (props: `n`, `corpus` `tenant|market|contigo`, `title`,
`subtitle`, `snippet`, `previewUrl?`, `href?`; renders the corpus badge
*validated contract* / *market · representative* / *Contigo*, the title,
subtitle, quoted snippet with the accent left rule, and the first-page
preview `<img>` or the honest placeholder block; click → `onOpen`),
`ActionRow` (buttons `btn-primary` / `btn-secondary` from `{ label, href,
kind }`), `ReplyBody` composing `kind` → layout: `answer` = markdown +
cards + actions + follow-ups; `redirect` and `refusal` = warm prose + one
CTA (no abstain block); `abstain` = the accent-left "Cannot determine
reliably." block + reason (the only place that block appears); `error` =
the existing `.error-state`. Copy the measurements and the message grid
from `inputs/design/prototypes/Contigo V2 Prototype.html` — unpacked
`contigo-v2/markup.html` Ask block (`m.who` label column 72px, 14px body,
`white-space: pre-line`, citation chip row → cards, action row with
`btn-secondary`), `contigo-v2/styles.css` (`.tag-*`, `.btn-*`,
`--color-accent-100` fills) and `contigo-v2/screens-v2.md` §2. Never
render an engineer route line or a guid. Styles in a new
`web/src/routes/ask/reply/reply.css` on the existing tokens
(`web/src/styles/tokens.css`, ADR-019).

## Parent story AC covered
- AC-3

## Files to create or modify
| Path | Change |
|------|--------|
| `web/src/routes/ask/reply/ReplyMarkdown.tsx`, `CitationCard.tsx`, `ActionRow.tsx`, `ReplyBody.tsx`, `replyTypes.ts`, `reply.css` | new |
| `web/tests/routes/ask/reply/*.test.tsx` | new |
| `web/package.json` (+ lockfile) | only if a sanitizing markdown dependency is added |

## Context the implementer needs
- **Design**: `inputs/design/prototypes/Contigo V2 Prototype.html`; unpacked anchors: `contigo-v2/markup.html` (`sc-for list="{{ gchat }}"` block: `m.hasCites`, `m.hasActions`, `m.abstain`), `contigo-v2/styles.css`, `contigo-v2/screens-v2.md` §2 (answer / abstain / redirect / refusal states). Reply contract: `inputs/requirements.md` §6.
- **Architecture decisions in force**: ADR-024 (rich reply, no engineer chrome), ADR-019 (tokens unchanged), ADR-020 (amended: screen 2).
- Gap G-RICH-UI.
- **Do not touch**: `web/src/routes/ask/index.tsx`, `ChatMessage.tsx`, `askViewModel.ts`, `ask.css` (F09/T04), the shell (F09/T01 this wave), `web/src/api/**`, `web/openapi/**`, `routes/documents/**`.

## Definition of done
- [ ] `npm test` in `web/` exit 0 — markdown renders paragraphs / bold / lists and `[2]` as a link to card 2; `<script>` in markdown is escaped; card shows badge per corpus, preview img when `previewUrl`, placeholder otherwise; `redirect` renders no `.abstain-block`; `abstain` renders exactly one; no element contains a guid pattern or "Structured query"
- [ ] `npm run build` in `web/` exit 0

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | markdown safety + `[n]` links | `web/tests/routes/ask/reply/ReplyMarkdown.test.tsx` |
| unit | card variants, actions, layouts per kind | `web/tests/routes/ask/reply/{CitationCard,ActionRow,ReplyBody}.test.tsx` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E13/F09/US01/T02
  prompt: reports/workitems/epic-13-ask-v2/feature-09-web-v2/us-01-web-v2/tasks/task-02-web-rich-reply.md
  produces: [web-rich-reply]
  depends_on: [web-shell-v2]
  effort: L
  layer: web
  status: live
```
