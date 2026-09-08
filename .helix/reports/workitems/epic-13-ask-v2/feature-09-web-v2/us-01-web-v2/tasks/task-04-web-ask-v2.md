---
id: E13/F09/US01/T04
type: task
story: us-01-web-v2
wave: 13
status: live
target_repo: contigo-web
---

# task-04-web-ask-v2 — Conversations in the rail, resume, reply wiring, scope line, suggestions; OpenAPI + client regen

## Coding objective

Turn `/ask` into screen 2 of `inputs/design/prototypes/contigo-v2/screens-v2.md`
from `inputs/design/prototypes/Contigo V2 Prototype.html` — search the
unpacked `contigo-v2/markup.html` for **"Ask needs at least one validated
contract."**, **"{{ askHello }}"**, **"{{ askScope }}"**, **"+ New chat"**
and `contigo-v2/app.jsx` for `askOffReason`, `askOffCta`, `askScope`,
`askPlaceholder`, `askChips` / `chipsFor` / `c360Chips`, `convs`,
`activeConv`, `convTitle`, `newChat`, `closeChat`, `gchat`, `mkMsg`.
First, document in `web/openapi/contigo-api.v1.json` every endpoint this
task uses, exactly as `inputs/requirements.md` §6 and the backend tests
define them (`GET/POST /api/conversations`, `GET /api/conversations/{id}`,
`POST /api/conversations/{id}/messages` with the reply contract,
`GET /api/capabilities`, `GET /api/market/records/{id}`,
`GET /api/insights/criticality`, `GET /api/contracts/{id}/strategy`, and
`supplierName` on portfolio / 360 / renewals / documents responses), run
`npm run generate:api`, extend `web/src/api/client.ts`
(`listConversations`, `createConversation`, `getConversation`,
`postMessage`, `getCapabilities`, `getMarketRecord`) — this task is the
phase-4 writer of the contract and the client. Then: (1) **Off state**
when the validated-contract count is 0 (from the shell hook): title,
`askOffReason`, one CTA (`askOffCta`) → `/documents`. (2) **New chat**:
"What do you want to know?", scope line "Answers only from N validated
contracts (names) · cites or abstains" + the prototype sentence, input
placeholder "Ask Contigo — spend, dates, clauses, liability…", two
suggestion chips from `GET /api/capabilities` (`suggestionsFor("ask")`);
a question creates a conversation (`POST /api/conversations`, with
`scopeContractId` when `?scope=` is present — chips then name the supplier
as `c360Chips`) then posts the message; the URL becomes
`/ask/<conversationId>`; `location.state.query` from the global Ask bar
starts a new chat immediately (`newChat: true`). (3) **Conversation
view**: header `convTitle` + **+ New chat**; turns rendered with the
phase-2 `ReplyBody` from the reply contract (`kind`, `answerMarkdown`,
`citations[]`, `actions[]`, `followUps[]`); clicking a tenant citation →
`/contracts/<contractId>?clause=<clauseId>` (or `?page=`) with
`state.from = "ask"`; a market citation opens a side panel loading
`GET /api/market/records/{id}` (title, category, geography, band,
provenance label, updatedAt); a Contigo feature card navigates to its
href; actions navigate to their href; follow-ups post as new messages.
(4) **Resume**: `/ask/:conversationId` loads the conversation and renders
past turns with cards and actions clickable. (5) **Rail**: the shell's
nested slot lists the last 5 conversations (`GET /api/conversations`),
active one in accent, click → resume (`RailNav.tsx` consumes a
`useRecentConversations` hook; F09/T01 left the slot). (6) Remove
`ROUTE_LINE_BY_INTENT`, the raw `Document:<guid>` chips and the
`Structured query…` line; the "thinking" copy stays. The SPA sends
`X-User-Id` = the MSAL account username on every API call
(`client.ts`, OQ-askv2-005). Keep ADR-019 tokens; `ask.css` updated to the
prototype measurements (`contigo-v2/styles.css`).

## Parent story AC covered
- AC-1 (rail conversations), AC-3, AC-5, AC-6

## Files to create or modify
| Path | Change |
|------|--------|
| `web/openapi/contigo-api.v1.json` | conversations, messages, capabilities, market, insights, `supplierName` (phase-4 writer) |
| `web/src/api/generated/schema.ts`, `web/src/api/client.ts` | regenerate + new methods + `X-User-Id` (phase-4 writer) |
| `web/src/routes/ask/index.tsx`, `askViewModel.ts`, `ChatMessage.tsx` (replaced by `reply/ReplyBody`), `ask.css` | V2 screen |
| `web/src/routes/ask/useConversation.ts`, `useRecentConversations.ts`, `MarketRecordPanel.tsx`, `AskOffState.tsx` | new |
| `web/src/components/shell/RailNav.tsx` | recent conversations slot wired |
| `web/src/components/ask-bar/askSuggestions.ts`, `GlobalAskBar.tsx` | suggestions from capabilities per screen (fallback to static copy) |
| `web/tests/routes/ask/*`, `web/tests/components/shell/RailNav.test.tsx`, `web/tests/api/client.test.ts` | updated / new tests |

## Context the implementer needs
- **Design**: `inputs/design/prototypes/Contigo V2 Prototype.html`; unpacked anchors above; `contigo-v2/screens-v2.md` §2; `contigo-v2/ia-v2.md` (cross-links, divergences: no route line, redirect layouts, `/savings`); reply contract `inputs/requirements.md` §6; requirements R-CONV-02, R-ASK-07/08/10, R-EVD-02, R-SYS-03, R-WEB-03/04.
- **Architecture decisions in force**: ADR-024, ADR-018 / ADR-020 (amended), ADR-019, ADR-012, ADR-022 (`X-User-Id` non-authoritative).
- Gaps G-RICH-UI (wiring), G-CONVERSATIONS (web), G-CAPABILITIES (web), G-IA-V2 (rail slot).
- **Do not touch**: `routes/ask/reply/*` internals (phase 2; extend props only if a contract field is missing), `routes/documents/**` (phase 3), `routes/contracts/contract360/**` (F10 this wave, phase 3), shell CSS beyond the rail slot.

## Definition of done
- [ ] `npm run generate:api` produces no diff after commit; `npm run build` exit 0
- [ ] `npm test` in `web/` exit 0 — off state with 0 validated contracts; new chat creates a conversation and navigates to `/ask/<id>`; a mocked `answer` reply renders markdown + cards + actions; a `redirect` reply renders no abstain block; tenant citation navigates to `/contracts/<id>?clause=<clauseId>` with `state.from === "ask"`; market citation opens the panel with the provenance label; `/ask/<id>` resumes; rail lists 5 conversations; no "Structured query" and no guid text anywhere; every request carries `X-User-Id`

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | off / new / resume states, reply wiring, citation navigation, panel | `web/tests/routes/ask/*` |
| unit | rail conversations | `web/tests/components/shell/RailNav.test.tsx` |
| unit | client methods + header | `web/tests/api/client.test.ts` |

## Open questions blocking this task
- OQ-askv2-005 — `X-User-Id` until the API JWT (assumed)

## Wave-spec entry
```yaml
- id: E13/F09/US01/T04
  prompt: reports/workitems/epic-13-ask-v2/feature-09-web-v2/us-01-web-v2/tasks/task-04-web-ask-v2.md
  produces: [web-ask-v2]
  depends_on: [ask-engine, market-index, web-rich-reply, web-documents-v2]
  effort: L
  layer: web
  status: live
```
