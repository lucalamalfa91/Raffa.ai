---
id: us-01
type: user-story
parent: feature-09
wave: 13
status: active
---

# us-01-web-v2 — The pilot path in the browser matches the V2 prototype

## Story

As **procurement**, I want Raffa to open on Ask, to keep my recent chats
one click away, to show me only the documents that need me, to refuse
non-contracts with a plain "Not added" card, and to answer in readable
prose with citation cards and buttons — exactly as
`inputs/design/prototypes/Raffa V2 Prototype.html` shows — so that the
product feels like the design I approved.

## Acceptance criteria

- [ ] AC-1 `/` lands on `/ask`; the rail is two-tier (Ask Raffa with the
      last 5 conversations and "+ New chat", Documents with "N to review";
      "From your contracts": Portfolio, Renewals, Quote check greyed until
      the first validated contract; no Home item; Workspace & members in
      the footer for Admin); Savings is at `/savings`.
- [ ] AC-2 The global Ask bar on every screen (square mark, full-width
      input, two suggestion chips from the capability catalog for the
      current screen; ⌘K / Ctrl+K focus) opens a **new chat** on Enter.
- [ ] AC-3 Raffa turns render markdown with inline `[n]`, citation cards
      (corpus badge *validated contract* / *market · representative* /
      *Raffa*, title, page / section or record, snippet, preview or
      placeholder) and action buttons; `redirect` / `refusal` turns use
      warm prose + one CTA; `abstain` uses the accent-left block; no route
      line, no guid anywhere.
- [ ] AC-4 Documents shows the onboarding empty state ("First your
      contracts. Then your questions."), accepts a multi-file drop (PDF ·
      DOCX · XLSX · PNG · JPG, 50 MB / file), renders one row per file with
      the real stage from the API, a **Not added** card with the reason for
      rejected files (session-only, never counted), the "Needs your
      attention" default filter with "Nothing needs you right now.", the
      review state at `/documents?review=:id`, and the "*X* is now askable —
      Ask: when does it expire?" hook after validation; the list survives a
      reload (server list).
- [ ] AC-5 `/ask/:conversationId` resumes a chat with cards and actions
      clickable; the scope line reads "Answers only from N validated
      contracts (…) · cites or abstains"; with zero validated contracts Ask
      is off with the prototype copy and one CTA to Documents;
      `/ask?scope=<contractId>` opens a scoped new chat.
- [ ] AC-6 The OpenAPI contract documents every V2 endpoint the web uses
      and `npm run generate:api` reproduces the committed client.

## Definition of done

- [ ] every AC above is verified by at least one test named in a task
- [ ] the change honours the ADRs listed below
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01-documents-v2 | list / preview / reprocess endpoints (T03) |
| us-01-conversations, us-01-ask-engine, us-01-market-intelligence, us-01-capability-catalog | conversations, messages, market record, capabilities endpoints (T04) |

## Architecture decisions in force

- ADR-024 — V2 IA, prototype as pixel reference, requirements win on divergences
- ADR-018 / ADR-020 (amended) — routes, screens, states
- ADR-019 — Modernist tokens unchanged (`web/src/styles/tokens.css`)
- ADR-012 — one generated TS client from `web/openapi/raffa-api.v1.json`

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| T01 | Web shell V2: two-tier rail, Ask home, routes, greyed modules, Ask bar → new chat | L | phase-1 |
| T02 | Rich reply components: markdown, citation cards, actions, redirect / refusal / abstain | L | phase-2 |
| T03 | Documents V2: multi-file, Not added, attention filter, real stages, review state; OpenAPI + client regen (documents) | L | phase-3 |
| T04 | Ask V2: conversations in the rail, resume, reply wiring, scope line, suggestions, `?scope=`; OpenAPI + client regen (conversations, messages, capabilities, market, insights) | L | phase-4 |

## Council decisions carried into this story

Design anchors (cite in every task): `raffa-v2/app.jsx` → `primaryNav`,
`kbNav`, `kbReady`, `askOffReason`, `askOffCta`, `askHello`, `askScope`,
`chipsFor`, `convs`, `newChat`, `docRows`, `stageLabels`, `filterAttn`,
`justValidated`, `mkMsg`; `raffa-v2/markup.html` strings "From your
contracts", "+ New chat", "First your contracts. Then your questions.",
"Nothing needs you right now.", "Ask needs at least one validated
contract.", "Mark as validated"; `raffa-v2/screens-v2.md` §2, §3, §4;
`styles.css` for the V2 component classes. Reply contract:
`inputs/requirements.md` §6.

## Open questions

- OQ-askv2-003 — Savings at `/savings` (assumed)
- OQ-askv2-005 — `X-User-Id` header sent by the SPA until the API JWT (assumed)
