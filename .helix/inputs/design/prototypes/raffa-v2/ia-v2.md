# Raffa — Information architecture (web V2, pilot path)

Source: `Raffa V2 Prototype.html` (unpacked here as `app.jsx` +
`markup.html`). Binding for the requirements: `inputs/requirements.md`
§5.12 (R-WEB-01…07) and HITL decisions D1–D8. Where the prototype and the
requirements differ, the requirements win and the difference is listed in
"Divergences" below.

## Roles on the pilot path

- **Workspace Admin** — everything below; also deletes documents and manages
  members (`markup.html`: "Also uploads, deletes, manages members").
- **Procurement** — asks, reviews, triages renewals, **uploads** (D8; the
  prototype copy "Asks, reviews, triages renewals" is extended by D8).
  Members screen is read-only with "request access".
- Legal / Finance / read-only exist in the permission model; not nav variants.

## Navigation — two tiers (`app.jsx`: `primaryNav`, `kbNav`; `markup.html`: "From your contracts")

**Primary**

1. **Ask Raffa** — badge `⌘K`; the last 5 conversations nested under it
   (`convs`, "resume by click"); **+ New chat** when a conversation is
   active (`showNewChat`).
2. **Documents** — badge `N to review` (accent) or `N docs`.

**Secondary — "From your contracts"** (greyed until the first validated
contract; `kbReady`, `kbDot`)

3. **Portfolio** — badge = number of validated contracts.
4. **Renewals** — badge = number of validated contracts.
5. **Quote check** — badge `optional`.

**Footer** — workspace name, role label, **Workspace & members** (admin),
sign out. **No Home item** (design brief "V2 principles").

Global: the **Ask bar** on every app screen (square accent mark, full-width
input, two quiet suggestion chips per screen from `chipsFor`; on Contract 360
the chips name the current supplier). Enter or a chip **always opens a new
chat** on Ask (`go('ask')` + `ask(text,'global')`). ⌘K / Ctrl+K focuses it.

## Route map (V2)

| Route | Screen | Primary object | Prototype block |
|---|---|---|---|
| `/signin` | Entra sign-in → workspace create/pick → lands on Ask | Tenant | `isSignin`, `sign0/1`, `signCreate`, `signPick` |
| `/ask` | Ask Raffa — new chat (home) | Conversation | `scr.ask`, `chatEmpty`, `askHello` |
| `/ask/:conversationId` | Ask Raffa — resumed conversation | Conversation | `convs[].resume`, `convTitle` |
| `/documents` | Documents — onboarding empty state, dropzone, attention filter, list, result cards | Document | `docsEmpty`, `docsList`, `docRows`, `filterAttn` |
| `/documents?review=:documentId` | Review as a **state of Documents** (weak facts, evidence pane, Mark as validated) | Extraction, Correction | `docsReview`, `reviewFields`, `sel`, `finishReview` |
| `/contracts` | Portfolio (validated contracts only; "More columns" on demand) | Contract | `scr.portfolio`, `kbContracts`, `moreCols` |
| `/contracts/:id` | Contract 360 — answers band, clauses ("Why"), details ▾, negotiation tracker; citation landing with highlight | Contract + children | `scr.c360`, `cur`, `clauses`, `hl`, `steps360` |
| `/renewals` | Renewals — priority list + insight + action; shared status with the 360 tracker | Renewal | `scr.renewals`, `renewals`, `rsel`, `rAct` |
| `/savings` | Savings KPIs + opportunities (prototype `home` block; reachable from actions, not from the rail — requirements A3) | SavingsOpportunity | `scr.home`, `kpis`, `opps` |
| `/quotes`, `/quotes/:id` | Quote check (Extract → Assessment → Target → Negotiation) — from nav or from Ask routing | Quote, NegotiationOutcome | `scr.quote`, `noQuote`, `loadQuote` |
| `/workspace/members` | Members & roles, invite (admin) | User, Role | `scr.workspace`, `members`, `sendInvite` |

`/` redirects to `/ask` (R-WEB-01).

## Object model → screens

- **Conversation** (new in V2) → rail (recent 5), Ask (`/ask/:id`), messages
  with citations + actions; opened scoped from Contract 360 "Ask about it".
- **Document** → Documents rows (attention / all), result cards, review
  state; Contract 360 › documents in family.
- **Contract** (validated) → Portfolio row, Contract 360, Renewals row,
  Savings opportunity, Ask citation target.
- **MarketRecord** (new in V2, `inputs/requirements.md` §5.8) → Ask citation
  card (`corpus: market`) and side panel; never a screen of its own.
- **Capability** (new in V2, §5.7) → Ask feature cards + actions; Ask bar
  suggestions per screen.

## Cross-links (all implemented in the prototype unless marked *req*)

- Documents row (needs review) → **Review N fields** → review state → **Mark
  as validated** → back to Documents with the hook "*X* is now askable. Ask:
  when does it expire?" (`justValidated`, `askValidated`).
- Documents row (completed) → **Ask about it** → new chat "When does *X*
  expire?"; row click → Contract 360.
- Ask citation card → Contract 360 with the clause highlighted (`hl`,
  `citedOpened`), back label "Ask Raffa".
- Ask action buttons → `/quotes` (benchmark intent), `/documents` (unknown
  supplier / upload), `/renewals`, `/contracts` (capabilities intent);
  *req*: `/contracts/:id`, `/savings`, `/documents?review=:id`.
- Contract 360 **Start negotiation now** → inline tracker (status, 4 steps,
  Undo) → **Track it in Renewals →** (`goRenewals360`, `rsel`).
- Renewals **Start negotiation / Assign** share status with the 360 tracker
  (`racts`).
- Savings opportunity row → Contract 360.
- Sign-in workspace pick → Ask.

## Ask intents in the prototype (`app.jsx` → `ask(text, scope)`)

The prototype's branch order is the behavioural oracle for the planner
(R-ASK-02 / R-ASK-03); the backend replaces keyword matching with the domain
gate + planner but must reproduce these outcomes:

| Question shape (prototype keywords) | Outcome | Actions |
|---|---|---|
| benchmark / compare / competitor / "in linea" / market / fair price / too much | route to **Quote check** (named supplier validated → "Benchmark *X* in Quote check"; else upload + Quote check) — *req*: when validated, answer inline (R-CMP-01) **and** offer Quote check | `/quotes`, `/documents` |
| "what can you do" / "cosa puoi" / help / "how do I" / "come faccio" | module list (Documents, Portfolio, Renewals, Quote check) | `/renewals`, `/contracts`, `/quotes` |
| unknown supplier (Databricks, Snowflake, SAP, …) | **abstain-redirect**: "No *X* contract has been uploaded and validated…" | `/documents`, `/quotes` |
| expire / scad / end / "when does" + supplier | structured fact: end date, auto-renew, notice deadline, days left; cites term + renewal clauses | citations |
| notice / preavviso / disdett | structured: notice deadline, days left | citation |
| liability / responsabil / cap / capped / massimale (+ uncapped / unlimited) | clause retrieval per validated contract | citations |
| legal + paid / cost / fee | abstain: "Legal fees are not a contract fact…" | — |
| 120 / days / giorni / renew / rinnov / scadono | structured list: renews in the next 120 days with notice dates | citations |
| askable / "not yet" / confidence / fields / missing | document status: which documents are not askable yet and which fields are weak | — |
| top / first / why / perché / start | priority: "*X* is first: priority N/100 — …" | citation |
| saving / risparm / largest | largest identified saving with benchmark provenance (`adapter A, n = 214`) and the weak fact that gates it | citation |
| anything else | abstain: "Nothing in the N validated contracts supports a reliable answer. Try a question about dates, spend, notice periods or clauses." | — |

Off-domain, greetings and legal questions are not in the prototype; they
are defined by `inputs/requirements.md` R-ASK-02 (decline + portfolio hook).

## Pilot path (single clickable flow; demo strip acts)

Sign in → pick / create workspace → **Ask (home, off until a validated
contract)** → Documents: drop contracts (non-contracts refused, D3) →
processing stages → review weak facts → Mark as validated → "now askable"
hook → Ask: contract vs market, renewal strategy → **+ New chat**: portfolio
strategy → follow a citation into Contract 360 → Start negotiation →
Track it in Renewals.

## Divergences (requirements win)

| Prototype | Requirements |
|---|---|
| Only Admin uploads ("Also uploads, deletes…") | Admin **and** Procurement upload (D8); only Admin deletes. |
| Benchmark questions always route to Quote check | Validated contract + benchmark match → inline comparison (R-CMP-01) plus the Quote check action. |
| Documents accept PDF · DOCX · XLSX | plus PNG · JPG via OCR (D7); non-contract files are refused with a **Not added** card (D3). |
| Engineer route line under each answer (`m.route`) | never rendered (R-ASK-08). |
| Abstain = accent-left block only | abstain only for true insufficiency; greetings / off-domain / needs-document use warm redirect prose + one CTA (R-ASK-07). |
| Home block reachable only through the app state | `/savings`, reachable from actions, Renewals and Contract 360; not in the rail (A3). |
| Conversations live in component state | server-side per user and workspace (D5), resumable at `/ask/:conversationId`. |
