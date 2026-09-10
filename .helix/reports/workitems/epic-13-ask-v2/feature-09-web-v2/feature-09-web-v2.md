---
id: F09
type: feature
parent: epic-13
wave: 13
status: active
---

# feature-09-web-v2 — Shell, rich reply, Documents V2, Ask V2

## Slice

The web side of the V2 pilot path, built 1:1 from
`inputs/design/prototypes/Raffa V2 Prototype.html` (unpacked
`raffa-v2/`: `markup.html`, `app.jsx`, `styles.css`, `ia-v2.md`,
`screens-v2.md`) on the ADR-019 tokens already in `web/`:

1. **Shell V2** — two-tier rail (Ask Raffa with recent chats and
   "+ New chat", Documents; "From your contracts": Portfolio, Renewals,
   Quote check greyed until the first validated contract; no Home item),
   `/` → `/ask`, `/ask/:conversationId`, `/savings`, `/documents?review=`,
   global Ask bar that always opens a new chat.
2. **Rich reply components** — markdown body with `[n]`, citation cards
   (corpus badge, page / section, snippet, preview or placeholder),
   actions, redirect / refusal / abstain layouts; no route line, no guids.
3. **Documents V2** — onboarding empty state, multi-file drop with
   per-file outcome including **Not added**, attention filter, real
   stages, review as a state, "now askable" hook; server list.
4. **Ask V2** — conversations in the rail, resume, new chat, the reply
   contract wired, scope line, suggestions from the capability catalog,
   `?scope=` for scoped chats.

Absorbs e12 F05 (`inputs/requirements.md` R-WEB-01…05, R-WEB-07).

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | The pilot path in the browser matches the V2 prototype | 13 |

## Architecture decisions in force

- ADR-024 — V2 IA, prototype as pixel reference, requirements win on divergences
- ADR-018 / ADR-020 (amended) — routes, screens, states
- ADR-019 — tokens unchanged; ADR-012 — React + TS + Vite SPA, one generated client

## Target repo

`raffa-web`
