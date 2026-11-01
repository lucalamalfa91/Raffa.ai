# Handoff: AskRaffa 4.0 — guided renewal workflow

## Overview
AskRaffa 4.0 is a procurement-renewal workspace for a CPO (Luca) and a Buyer (Marta). The redesign replaces a multi-section dashboard with **one guided flow**: the app always shows *one thing to do*, with **at most 2–3 choices per screen**, and a persistent **Guida Raffa** panel that says *where you are · what to do now · why · what comes next*.

End-to-end journey the prototype covers (start from an empty workspace):

1. **Upload a contract** → Raffa reads it → user verifies only the doubtful fields (one at a time) → contract validated.
2. **Oggi** surfaces the notice deadline with no owner → user starts the renewal (alone, or assigns it to Marta).
3. **Renewal case (chat)**: interview (4 questions) → plan → email (draft → to approve → approved → copied → send confirmed) → wait for supplier → analyse reply → counter-proposal → agreed terms → request contract → read draft → corrective email → **signature (outside Raffa)** → add signed contract → acquisition → outcome **Renewed**.
4. **Risultati** records the signed result; **Contratti** shows the new dates.

Language: full IT / EN switch. Brief that drove it: `reference/AskRaffa_4.0_brief.md` (decisions in its section 2 are final).

## Read in this order
1. **README.md** (this file) — what to build, screen by screen.
2. **PARITY.md** + `parity/` — how "identical to the prototype" is *proved* (goldens, same click-path, style assertions). **The reference prototype is the source of truth; if this text and the prototype disagree, the prototype wins.**
3. **DESIGN-GAPS.md** — what the prototype does not define (loading, errors, a11y, responsive, formats) with the rule to apply, plus its known quirks.
4. **BACKEND-SPEC.md** — data model, state machines, API, jobs and conflicts with the existing `Raffa.ai` backend (grounded in `backend/src/Raffa.*` and `web/openapi/raffa-api.v1.json`).
5. **COPY-CATALOGUE.md** (all strings IT→EN) and **STYLE-INVENTORY.md** (every font size, weight, colour token, border, padding, gap, grid and max-width used).
6. **screenshots/** — 26 orientation shots (IT; taken at the editor's 924×540 pane, so long pages are cut — the goldens are produced by `parity/` at 1440×900).
7. **reference/** — the runnable prototype and its engines. Open `reference/AskRaffa V4.dc.html` via a static server (`npx serve reference`), Scenario *Da zero*.
8. **PROMPT.md** — the prompt to paste into Claude Code.

## About the Design Files
Everything in `reference/` is a **design reference built in HTML** — a prototype that shows intended look, copy and behaviour. It is **not production code to copy**. Recreate it in the target codebase's own environment (React/TS, router, data layer, i18n, design tokens) using its established patterns. The repo already linked to this project is `lucalamalfa91/Raffa.ai`, folder `web/src` (see `github.md` in the project): map each screen below onto the existing routes (`routes/renewals`, `routes/savings`, `routes/quotes`, `components/shell`, `components/InfoTip`) rather than creating parallel ones.

The prototype's data and "engine" are **simulated** (see *State & engine* below) — real implementations must come from the API.

## Fidelity
**High-fidelity.** Final colours, type, spacing, copy and interactions, built on the Modernist design system (flat, square corners, 2px rules, Archivo, near-mono red accent). Recreate pixel-faithfully using the codebase's tokens; the tokens are in `reference/modernist-styles.css` and map 1:1 to `var(--color-*)`, `var(--font-*)`.

## Global shell
- **Demo bar** (top, 36px, bg `--color-neutral-900`, bottom border 2px `--color-accent`): scenario clock, *Scenario* switch (*Da zero* default / *Con dati*), *Vedi come* (Luca·CPO / Marta·Buyer), IT/EN, *Reimposta*. **Demo-only — do not ship.**
- **Top nav** (height ≈ 56px, border-bottom 2px `--color-text`, padding 0 40px): logo mark (12×12 `--color-accent` square) + "Raffa.ai"; tabs **Oggi · Rinnovi · Contratti · Risultati · Chiedi** (Archivo 800, 14px; active = 3px `--color-accent` bottom border; badge = accent-700 11px count, only on Oggi = number of items for the current user). Right: **Guida Raffa** toggle button (1px border, 12px/700; filled `--color-text` when open).
- **Main area**: grid `minmax(0,1fr) 300px` when the guide is open, `minmax(0,1fr)` when closed. Content is **full width** (max-widths: 1400 for lists, 1100 for Chiedi/Offerta/Revisione) with 40px horizontal padding. No content centering when the guide is open.
- **Guida Raffa panel** (right, 300px, bg `--color-text`, text `--color-bg`, border-top 3px accent, padding 18/20): header (12px accent square, "Guida Raffa", *Nascondi*); four labelled blocks — **Dove sei / Cosa fare ora / Perché / Dopo** (labels 10px uppercase `.1em` `--color-neutral-400`; "Cosa fare ora" is Archivo 800 19px); one primary button (the recommended action) when there is one; footer note 11px. Content is contextual to screen **and** to the active step of a case.

## Screens

### 1. Oggi (home) — "What do I need to do today?"
- Purpose: show **one** next action, then what follows. Never a dashboard.
- Header: role line (11px uppercase, `Luca Lamalfa · Head of Procurement · 08 Set 2026`), H2 title (*"Oggi hai N cose da fare. Una alla volta."* / *"Oggi nulla richiede la tua azione."* / *"Si parte da qui."* on an empty workspace), 14px subtitle.
- **Progress dots** (28×6px, current = `--color-accent`, others `--color-neutral-300`), one per item for me.
- Layout: grid `minmax(0,1.6fr) minmax(260px,1fr)`, gap 24/40.
  - **Focus card** (left): border 2px `--color-text`, bg `#fff`, padding 28/32/26. Kicker `1 di N · Azione tua · Fase x/6`; supplier (Archivo 800 30px) + contract (14px muted); action title 17px/700; reason 14px muted (max 64ch); deadline row (label 10px uppercase + date, red `--color-accent` if ≤30 days); divider; **buttons (max 3)**: primary *Fai ora — apri il passo* (46px high), contextual secondary (*Delega a Marta* / *Me ne occupo io* / *Avvia e assegna a Marta*), ghost *Più tardi · 3 giorni* (snooze 3 days; snoozed items listed under *Rimandate* with *Riprendi ora*). Below the card: "Dopo questa: …".
  - **Right column**: *Poi, per te · N* (rows: supplier 15px/800 · action · days, clickable) and *In attesa di altri · N* (waiting on colleague/supplier, clickable). Each list has an empty line.
- **Empty workspace ("Punto zero")**: a 2px-bordered block: *"Carica un contratto. Tutto il resto parte da lì."*, 4-step strip (Carica · Raffa lo legge · Verifichi i campi dubbi · Parte il rinnovo; current step has 3px accent top border) and one primary CTA *Carica il primo contratto*.
- **Calm state**: *"Per oggi hai finito."* + next notice deadline + *Vedi i rinnovi*.
- Ordering of items for a user: own actions by notice-deadline ascending; then items waiting on others. Item kinds: case step, unassigned notice deadline (≤120 days, CPO only), quote decision, review.

### 2. Rinnovi (list)
- H2 "Rinnovi" + one summary line (open · to start · sorted by notice deadline).
- Groups (2px bottom rule header, count): **Tocca a te · In corso con il team · In attesa del fornitore · Da avviare · Chiuse**.
- Row (flex-wrap, padding 14/8/14/0, hover bg `--color-neutral-200`, whole row clickable): supplier (Archivo 800 18px) + contract; phase `Fase x/6 · name` + next action in bold; **Disdetta entro** date + days (red ≤30); owner; one button (*Fai ora* primary when it is my turn, else *Apri*; *Avvia e assegna* for not started).
- Empty state when there is no validated contract.
- Notice deadline (last day to give notice) and contract end are **always labelled separately**.

### 3. Rinnovo (the guided case / chat)
- **Case summary** (top, border-bottom 2px): `← Rinnovi`, title `Rinnovo · Supplier · Contract`, *Scheda: condizioni, email, team, date ▾*. Three cells: **Fase** (`Fase n/6 · label`), **Prossima azione · chi**, **Disdetta entro · fine contratto**. Optional permission banner (accent-100 bg) with "Può procedere: <approver>".
- **Scheda** (collapsed by default): four columns — Condizioni (terms + state), Piano · obiettivi, Email · documenti · team, Prossime date.
- **Feed**: first a row `↺ Rivedi la panoramica del caso` (secondary button) + `Passi completati · N ▾`. Opening it shows each completed step as an **expandable row** (kicker, title, note, ▾) revealing its lines/rows/phase chips. *Rivedi la panoramica* opens the first completed step ("Caso di rinnovo aperto") — for users resuming after days.
- **Current step card** (border 2px, bg `#fff`, padding 20/24): kicker (accent-700 11px/700), title (Archivo 800 22px), optional banner, lines, then step content — key/value rows, tables, phase chips, **email block**, quoted source, extracted fields (accept/correct), option chips, inputs. **Buttons: max 3 visible** (primary first); everything else under *Altro ▾* (ghost links), including secondary paths (*Prepara la disdetta*, *Valuta alternative*) and demo shortcuts.
- Reply box (when pasting a supplier reply): textarea + *Aggiungi alla pratica* / *Usa risposta di esempio* / *Annulla*.
- **Composer** (bottom): *"Hai un dubbio? Chiedi a Raffa su questo contratto — poi torniamo al passo"*. A free question gets an answer **with its source**, followed by *"Torniamo al passo in sospeso — <next action>"*. Commands (approva, copia, ho inviato, conferma piano) are routed to the step.
- **Phases (6)**: Avvio · Piano · Negoziazione · Accordo · Contratto e firma · Acquisizione.
- **Email states**: Bozza → Da approvare → Approvata vN da X → Copiata → Invio confermato (+ Superata). Rules: recipient is never invented (user must add it; demo offers an example address); only the approver or an explicit delegate approves the *exact version*; any edit after approval returns it to Bozza; **copying is not sending**; the user confirms the send; the text actually sent may differ from the approved text and is recorded.
- Signature happens **outside Raffa**; the user then adds the signed contract, Raffa checks it against the agreed terms, then the case enters acquisition and closes with an outcome (*Rinnovato* / *Disdetto*).

### 4. Contratti
- H2, summary line, *Carica un contratto* (secondary). If a document is being read or needs review: accent-100 banner with left border 2px accent (`Salesforce_MSA_2024.pdf · 2 campi da verificare` → *Verifica ora →*).
- Rows (like Rinnovi): supplier, contract, **Disdetta entro**, **Fine contratto**, annual value, status tag (*Validato / Pratica aperta / Rinnovato / Disdetto*). After a renewal is signed the row shows the **new** dates and value.

### 5. Contratto (Contract 360, decisions first)
- `← Contratti`, supplier (accent kicker) + contract name, meta (type · version · validated · CHF/year).
- **Decisive dates**: 4 equal cells in a grid with 1px gaps (`background: --color-divider`, cells `#fff`, padding 16/20): *Disdetta entro* (red if ≤45 d), *Fine contratto*, *Aumento al rinnovo*, *Decorrenza*.
- **Cosa richiede una decisione** (≤3 cards): tag (*Da discutere / Rischio / SLA*), title, **Dice** (what the contract says), **Perché conta**, **Raffa propone**, source + policy note; right column three choices *Lo negoziamo / Lo accettiamo / Va chiarito* (toggle; recorded per contract with author/date).
- **Prossima azione** row: one primary (*Apri la pratica* / *Avvia il rinnovo*) + *Chiedi a Raffa*.
- **Tutto il contratto ▾** (collapsed): key terms grid, products & prices (from the contract only, never market data), obligations (we / supplier), standard terms already accepted.

### 6. Risultati (savings)
- Subtitle: *"Solo cifre verificabili. Raffa non stima mai un risparmio."*
- **Goal bar** (slim, 1px border, **collapsed by default**): `Il tuo obiettivo · <summary> · [Imposta un obiettivo | Cambia]`. Opened: free-text input (e.g. *"Voglio risparmiare il 20% sui contratti IT l'anno prossimo"*), example chips, **Imposta** → *Cosa ho capito* draft (parsed percent/amount, scope, period, spend base, how it counts) → *Sì, è questo*. Parsing in the prototype is a regex heuristic (`parseGoal`); replace with an LLM-backed parser that returns the same shape (`pct, annual, scope(ids|cats|supplier), period`).
- **When a goal is set** the page adapts: *Verso il tuo obiettivo* with 4 KPIs (Obiettivo · Firmato finora · Obiettivi nelle pratiche · Ancora da coprire), a stacked progress bar (signed / objectives / gap), a one-sentence verdict, and **"Contratti su cui puoi agire per arrivarci"** (supplier, spend, line status, value if target hit, days to notice, CTA to open/start the case).
- **Headline numbers** (3): *Risultati contrattuali · AF 2026* (signed price vs previous contract, same quantities), *Realizzati · da fatture*, *Riduzioni di quantità* (always shown apart — not a negotiation result).
- **KPI strip** (6): signed results count · average per contract · % proven by invoices · analysed spend · open objectives · next notice deadline.
- **Charts** (2×2 grid, minmax(340px,1fr)): signed per month (12 bars, accent), by supplier (stacked: proven `--color-text`, signed `--color-accent`, objective `--color-accent-100`), by lever (price/term/clauses, quantity reductions excluded), proven-by-invoices per result.
- **Open objectives** (user targets only) and **Contractual results** (each with baseline · period · method · evidence · realized status) side by side.

### 7. Chiedi (AskRaffa conversations)
- Grid `260px | 1fr`. **Both columns share one 56px header line** (border-bottom 1px divider) — never two stacked lines.
- Left: *Conversazioni* + **+ Nuova**; groups **Pratiche aperte** (cases, clicking opens the case), **Recenti**, **Archiviate · N ▾** (collapsed by default; reopen by clicking or by writing). Each item: title (13px) + `date · N domande`.
- Right: conversation title/date + *Archivia / Riapri*; empty state *"Cosa vuoi sapere o fare?"* with 3 intents (**Prepara un rinnovo**, **Riassumi un contratto**, **Confronta un'offerta**); user question as 18px Archivo 800, answer 14px with **"Fonte: …"**; parameter picker only when the context is missing (e.g. which contract). Composer: *"Es. «Quando dobbiamo dare disdetta a Salesforce?»"* + **Chiedi**; footer *"Solo procurement · cita la fonte o dice che non può"*.
- Market questions get an honest refusal: *no market benchmark in this release*.

### 8. Offerta (quote check, 3 steps)
Steps **Verifica → Confronta → Decidi** (3px top-border stepper). Step 1: lines (qty, unit price, annual) + what the proposal states or **omits** ("non indicato" in accent-700). Step 2: cells *Questa proposta / Il tuo contratto attuale / Differenza*, ≤3 evidence points with source (your contract, your target, other offers, policy, validity vs notice date), "Manca per un confronto completo". Step 3: one-sentence verdict + **three choices** (e.g. *Prepara la controproposta · Firma comunque · Rifiuta e valuta alternative*; when nothing is comparable: *Chiedi al fornitore i dati mancanti · Procedi verso la firma · Rifiuta…*). **Comparison only against: the customer's own contract price, their target, other offers received, company policy. Never a market benchmark.**

### 9. Revisione (document review)
Processing: spinner (18px, 2px border, accent top) + stage label; "you can leave, work stays saved". Review: **one field at a time** — `Campo n di N`, label (Archivo 800 22px), *Letto* value + page, *Cosa controllare* (why it is flagged), quote with `<mark>` highlight (accent-200) on a `--color-surface` block, buttons **Accetta: è corretto / Correggi**. Done: *"Contratto validato."* + *Apri il contratto* / *Torna a Oggi*. A high confidence score never replaces the user's check on a conflict.

## Interactions & behaviour
- All navigation is in-app; no page reloads. Deep links: tab keys `oggi · pratiche(=Rinnovi) · contratti · risultati · chiedi`; screens `caso, contratto, offerta, revisione`.
- Opening a case (from Oggi, Rinnovi, Contratto, Risultati, Chiedi) **reuses the same case** — never creates a second — and lands on the current step.
- Primary buttons use `.btn-primary` (solid accent); secondary `.btn-secondary`; ghost for tertiary. Hover = accent ramp one step darker; focus ring `2px solid --color-accent` offset 2px. Disabled = 45% opacity **plus a visible reason** (e.g. *"Add the recipient first — I never invent addresses"*).
- Timers in the demo: file reading ≈ 3.6s (5 stages), simulated supplier replies. The "check date" reminder can be jumped with a demo button.
- Language switch re-renders everything, including engine-generated strings.

## State & engine
Prototype state (single component): `screen, viewer, cases{cid→case}, outcomes, snooze, validated[ids], doc{file,status,stage,fields,res}, guide, scheda, hist/doneOpen, goal/goalDraft/goalOpen, convs[{id,title,when,archived,msgs}], conv, c360Dec, qDec/qStep/qLink, lang, mode`.

Domain logic lives in two pure modules — treat them as **the spec of the business rules**, to be reimplemented server-side:
- `renewal-case-v4.js` (`RC`): `create(cid, ctx, branch, {owner})`, `act(case, type, payload, ctx)`, `view(case, ctx)` → header/cards/buttons/summary/timeline, `todo(case, ctx)` → items for Oggi, `stage(case)`. Permissions: owner works the case; **only approver or explicit delegate approves emails and closes**; chat participation grants nothing.
- `savings-quote-v4.js` (`SQ`): `assess(quote, {target, planTerm, others, noticeDate, policy})` → verdict `nocompare | misses_target | meets_target | below_current | above_current | same` + evidence points; `report(...)` → contractual / realized / quantity-reduction / objectives; `counterEmail(...)`.
- `raffa-data.js` (`RD`): fictional demo contracts (Salesforce, Microsoft, AWS, DocuSign), deep-dive terms, evidence quotes.

**Hard product rules** (must survive implementation): no market benchmark and **no estimated savings** anywhere; a saving counts only with a signed contract (contractual) or invoices (realized); quantity reductions are shown apart; recipients are never invented; copying an email is not sending; negotiation completes only with the signed contract + confirmation; scope is procurement only and every answer cites its source or says it cannot.

API surface the real app needs (not in the prototype): contracts + extraction (fields with confidence/source page), review decisions, renewal cases + steps + emails + timeline events + delegations, reminders, quotes (lines, mapping to contract price), savings records (baseline, period, method, evidence, invoices reconciliation), conversations (+archive), document dedup by hash, unreadable-document status.

## Design tokens (Modernist)
Use the CSS variables from `reference/modernist-styles.css`. Key values in this design:
- Text `#201e1d` (`--color-text`); accent `#ec3013` (`--color-accent`) for primary action and urgency only; body-size accent text uses `--color-accent-700`; tints `--color-accent-100/200/300`; neutrals `--color-neutral-200…900`; divider `--color-divider`; surface `--color-surface` (`#f3f2f2`); page ground in this prototype `#fff` (overrides `--color-bg`), cards `#fff`.
- Type: Archivo only. Headings 800 (`--font-heading`), body 400/600 (`--font-body`). Sizes used: 10/11 (uppercase labels, `.08–.1em`), 12, 13 (default dense), 14, 15–18 (row titles), 22 (step titles), 28–34 (focus/hero), 34 (headline numbers).
- Shape: **radius 0 everywhere**; rules 1px (`--color-divider`) and 2px (`--color-text`) for section headers; no shadows except the chat composer.
- Spacing: 40px page gutters, 8/12/16/24 rhythm, grid gaps 24–40.
- Interaction: min button height 40–46px on primary actions; labels flush left.

## Assets
No images. Icons are minimal (12px accent square mark, ↺ ▾ ▴ → ← glyphs); the design system calls for Lucide if icons are added. Contract/clause texts, supplier names and figures are **fictional demo data**.

## Files
In `reference/`:
- `AskRaffa V4.dc.html` — the full prototype (template + logic). Open it first; use *Scenario → Da zero* to walk the end-to-end flow, *Con dati* for pre-filled cases.
- `renewal-case-v4.js`, `savings-quote-v4.js`, `raffa-data.js` — domain logic and demo data (see above).
- `i18n-en.js` — IT→EN dictionary (the prototype's strings are IT; EN is generated by dictionary + fragment replacement — in production use proper message catalogues).
- `support.js` — prototype runtime (ignore).
- `modernist-styles.css` — design tokens and component classes.
- `handoff-v4-audit.md` — audit of what was removed/kept vs V3, state matrix, requirements list.
- `AskRaffa_4.0_brief.md` — the original product brief.
