---
id: E13/F09/US01/T01
type: task
story: us-01-web-v2
wave: 13
status: live
target_repo: contigo-web
---

# task-01-web-shell-v2 — Two-tier rail, Ask home, V2 routes, greyed modules, Ask bar → new chat

## Coding objective

Rebuild the app shell to the V2 information architecture of
`inputs/design/prototypes/Contigo V2 Prototype.html` — search the unpacked
`inputs/design/prototypes/contigo-v2/markup.html` for **"From your
contracts"** and **"+ New chat"**, and `contigo-v2/app.jsx` for
`primaryNav`, `kbNav`, `kbReady`, `kbDot`, `showNewChat`, `goWorkspace`;
routes and roles in `contigo-v2/ia-v2.md`. Concretely: replace
`web/src/components/shell/navItems.ts` with the two-tier model (primary:
Ask Contigo with badge `⌘K` and a nested list slot for recent
conversations — render an empty slot with "+ New chat" now; F09/T04 fills
it from the API — and Documents with the "N to review" / "N docs" badge;
secondary "From your contracts": Portfolio, Renewals, Quote check with the
validated-contract count badge and `optional`, greyed with the
`var(--color-neutral-500)` foreground until `kbReady`; footer: workspace
name, role label, Workspace & members for Admin, sign out); remove the
Home and Review queue items. Routes in `WorkspaceShellApp.tsx`: `/` →
`Navigate` to `/ask`; `ask` and `ask/:conversationId` → `AskRoute`;
`savings` → the former Home route (rename folder `routes/home` →
`routes/savings`, keep the component); keep `documents`, `contracts`,
`contracts/:contractId`, `contracts/:contractId/review` (still reachable;
F09/T03 adds the `?review=` state), `renewals`, `quotes`, `quotes/:quoteId`,
`workspace/members`; `/review` redirects to `/documents?filter=attention`.
`kbReady` comes from `apiClient.getPortfolio` (count of validated
contracts) fetched once by the shell and passed to the rail (a
`useValidatedContractCount` hook). The global Ask bar keeps its markup
and CSS (e11) but `submit` must **always** navigate to `/ask` with
`{ state: { query, newChat: true } }` so a new conversation starts
(`app.jsx`: `go('ask')` then `ask(text,'global')`), and the placeholder
switches to the prototype's "Ask Contigo switches on after your first
validated contract" when `kbReady` is false. Keep the Modernist tokens
(ADR-019); measurements from `contigo-v2/styles.css` and `markup.html`
(rail 224px, item padding, kicker sizes).

## Parent story AC covered
- AC-1, AC-2

## Files to create or modify
| Path | Change |
|------|--------|
| `web/src/components/shell/navItems.ts` | two-tier model + role gates |
| `web/src/components/shell/RailNav.tsx`, `AppShell.tsx`, `shell.css` | render tiers, badges, greyed state, footer |
| `web/src/components/shell/WorkspaceShellApp.tsx` | V2 routes, `/` → `/ask`, `/review` redirect, `useValidatedContractCount` |
| `web/src/components/shell/useValidatedContractCount.ts` | new |
| `web/src/components/ask-bar/GlobalAskBar.tsx`, `askSuggestions.ts`, `ask-bar.css` | new-chat navigation state, off placeholder |
| `web/src/routes/savings/*` (moved from `routes/home/*`) | rename, keep behaviour |
| `web/tests/components/shell/*`, `web/tests/routes/savings/*`, `web/tests/components/ask-bar/GlobalAskBar.test.tsx`, `web/tests/App.test.tsx` | updated / new tests |

## Context the implementer needs
- **Design**: `inputs/design/prototypes/Contigo V2 Prototype.html` (bundled); unpacked anchors above; `contigo-v2/screens-v2.md` §2 (Ask home) and the rail description in `ia-v2.md`. The requirements win on divergences (`ia-v2.md` table): Procurement can upload; Savings at `/savings`.
- **Architecture decisions in force**: ADR-024, ADR-018 / ADR-020 (amended), ADR-019 (no new tokens), ADR-012.
- Gap G-IA-V2.
- **Do not touch**: `web/src/routes/ask/**` (F09/T02 / T04), `web/src/routes/documents/**` (F09/T03), `web/src/api/**`, `web/openapi/**`, `routes/contracts/**`.

## Definition of done
- [ ] `npm test` in `web/` exit 0 — rail renders two tiers in order (Ask Contigo, Documents, "From your contracts", Portfolio, Renewals, Quote check), no Home / Review queue items, greyed secondary tier with 0 validated contracts, `/` redirects to `/ask`, `/savings` renders the KPI row, Ask bar submit navigates to `/ask` with `newChat: true`
- [ ] `npm run build` in `web/` exit 0

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | nav model + role gates | `web/tests/components/shell/navItems.test.ts` |
| unit | rail tiers, badges, greyed state | `web/tests/components/shell/RailNav.test.tsx` |
| unit | routes: `/` → `/ask`, `/savings`, `/review` redirect | `web/tests/components/shell/WorkspaceShellApp.test.tsx` |
| unit | Ask bar new-chat state + off placeholder | `web/tests/components/ask-bar/GlobalAskBar.test.tsx` |

## Open questions blocking this task
- OQ-askv2-003 — Savings at `/savings`, not in the rail (assumed)

## Wave-spec entry
```yaml
- id: E13/F09/US01/T01
  prompt: reports/workitems/epic-13-ask-v2/feature-09-web-v2/us-01-web-v2/tasks/task-01-web-shell-v2.md
  produces: [web-shell-v2]
  depends_on: []
  effort: L
  layer: web
  status: live
```
