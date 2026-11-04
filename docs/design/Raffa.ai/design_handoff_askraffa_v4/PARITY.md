# PARITY — making the app identical to the prototype

"Identical" is verified, not eyeballed. Three mechanisms, all in `parity/`:

1. **Golden screenshots** captured from the *reference prototype* at a fixed viewport (so the PNGs in `screenshots/`, taken at the editor's 924×540 pane, are for orientation only).
2. **Same click-path** run against the real app (text/role selectors, so the app must use the same copy — see `COPY-CATALOGUE.md`).
3. **Computed-style assertions** for the values that screenshots can't prove (borders, paddings, widths, fonts).

## 0. Ground rules
- Reference = `reference/AskRaffa V4.dc.html` opened from the `reference/` folder (it loads `support.js`, `raffa-data.js`, the engines and `_ds/…` relatively; no server needed beyond static file serving — `npx serve reference`).
- Fixed conditions: viewport **1440×900** (also 1280×800 and 1920×1080), device scale factor 1, locale `it-IT`, `reducedMotion: 'reduce'`, scenario clock **08 Sep 2026**, Archivo loaded (wait for `document.fonts.ready`), scenario **Da zero**, language **IT** (`localStorage['raffa4.mode']='zero'`, `['raffa4.lang']='it'`).
- The **demo bar and the Scenario/Vedi-come switches are not part of the product**. Compare the area below the demo bar (clip `y ≥ 36`) — or hide it in both runs.
- The real app must expose the same **test hooks** as the prototype: `data-screen-label` on the blocks below (also used by comments/QA).

### Required `data-screen-label` hooks
`Oggi · Punto zero · Focus · Pratiche(=Rinnovi) · Pratica · Scheda pratica · Passo corrente · Contratti · Contratto · Decisione · Risultati · Obiettivo · Verso l'obiettivo · Risultati · KPI · Risultati · grafici · Chiedi · Conversazioni · Offerta · Revisione · Guida Raffa`

## 1. Run it
```bash
cd parity && npm i -D @playwright/test && npx playwright install chromium
# 1) capture goldens from the reference prototype
REFERENCE_URL="http://localhost:4173/AskRaffa%20V4.dc.html" npx playwright test reference.capture.spec.ts
# 2) compare the app (needs ?now / test clock support + the 'Da zero' fixtures, see §3)
APP_URL="http://localhost:5173" npx playwright test app.parity.spec.ts
# 3) style assertions
APP_URL="http://localhost:5173" npx playwright test geometry.spec.ts
```
Tolerance: `maxDiffPixelRatio: 0.002`, `threshold: 0.1` (anti-aliasing only). Anything above is a bug, not "close enough".

## Run locally
One command starts the whole parity stack the way CI (`.github/workflows/parity.yml`) does:

```bash
scripts/parity-local.sh                 # foreground; Ctrl-C stops everything
DETACH=1 scripts/parity-local.sh        # background; stop with: scripts/parity-local.sh down
```
It starts Postgres (`pgvector/pgvector:pg16` in docker, every module's `Migrations/Scripts/*.sql` applied in ADR-021 order), the API in the `Parity` environment (fixtures on, in-memory storage, no Azure; see `backend/README.md` "AskRaffa 4.0 fixture mode"), the web app (`vite build` + `vite preview` with a `config.json` that has `"fixtures": true`) and a static server for `reference/`. Ports: `PG_PORT` (55432), `API_PORT` (5180), `WEB_PORT` (5173), `REF_PORT` (4173). Also: `SKIP_WEB_BUILD=1` (reuse `web/dist`), `NO_WEB=1`, `NO_REF=1`, `KEEP_DB=1`. Logs in `.parity-local/`.

Then open `http://localhost:5173/today?fixture=zero&now=2026-09-08` (or `fixture=full`) and run the suite from `web/parity`:
```bash
REFERENCE_URL="http://localhost:4173/AskRaffa%20V4.dc.html" npx playwright test -c playwright.config.ts reference.capture.spec.ts
APP_URL="http://localhost:5173" npx playwright test -c playwright.config.ts app.parity.spec.ts geometry.spec.ts
```
Fixture mode in the app: `?fixture=zero|full` resets the fixture tenant (*Helvetia Foods AG*) and skips sign-in; `?now=` sets the scenario clock; the demo bar (Vedi come / Scenario / IT-EN / Reimposta) appears only in this mode and never in a deployed environment (`fixtures` is absent from their `config.json`, and the API refuses `Fixtures:Enabled` in Production).

## 2. What must be pixel-identical
Every screen in `screenshots/` (names = golden names), in IT; plus EN for *Oggi* (`25`), plus *Guida chiusa* (`26`). Dynamic content that must be neutralised in both runs: scenario clock, caret blink (`caret-color: transparent`), spinner (`animation: none`, capture after reading finishes).

## 3. What the app needs to support for tests
- Deterministic clock (`?now=2026-09-08` or `page.clock`).
- A "Da zero" workspace fixture: empty tenant, then the single sample contract **Salesforce_MSA_2024.pdf** whose extraction returns the same 2 flagged fields (renewal term, uplift) and the same clause quotes as `raffa-data.js` (`EVIDENCE`).
- A seeded "Con dati" fixture matching `reference/raffa-data.js` + the three seeded cases in `AskRaffa V4.dc.html → seed()` (DocuSign counter-proposal to approve, Microsoft in interview, AWS waiting for reply).
- Supplier replies, the draft contract and the signed file are fixtures (`sampleReply`, `addDoc`, `signed` in `renewal-case-v4.js`) — provide the same texts in test mode.

## 4. Definition of done (per screen)
1. Golden screenshot passes at 1440×900, 1280×800, 1920×1080.
2. Geometry assertions pass.
3. Copy matches `COPY-CATALOGUE.md` (IT and EN).
4. Behaviour matches the flow in `parity/flow.ts` end to end, including the max-3-buttons rule.
5. No deviation from the reference. B3 and B4 (see `DESIGN-GAPS.md`) are already fixed in the reference itself.

## Approved deviations (product owner, after the 4.0 release)
The app differs from the prototype only by these rules. `web/parity/deviations.ts` applies the same rules to the prototype before the goldens are captured, so parity still proves "prototype + approved deviations" pixel for pixel.

| # | Rule | Why |
|---|---|---|
| D1 | ~~Body text in **Inter** (`--font-body`); headings stay Archivo 800.~~ Superseded by D8. | Archivo is hard to read at 10–13px. |
| D2 | Every font size up to 14px is **one pixel larger** (10→11 … 14→15, root 15px). | Readability, without changing the hierarchy. |
| D3 | No page **max-width at or above 900px** (1100/1400/900): screens use the whole window, with or without the Guida Raffa panel. Text measures (`ch`) and small blocks (<900px) are kept. | Use the screen width; the layout organises better on large windows. |
| D4 | A sixth tab, **Impostazioni** (company rules), after Chiedi. | The company writes the rules Raffa follows. The prototype has no such screen: the parity run hides the tab and compares the rest of the nav. |
| D5 | Oggi shows a case step only when its notice deadline is within the **company's lead time** (default 120 days, set in Impostazioni). | A renewal 522 days away is not "today". The prototype lists every case: the parity run sets the lead time to its maximum (365 days) after each fixture reset, so the app lists the same items. |
| D6 | At the right end of the nav: the **IT / EN switch** and the **account menu** (user, role, workspace, Workspace & members, Documents, Sign out). | The prototype's demo bar carried the language switch and has no sign-in. A real workspace needs both: the retired rail's footer functions come back here. The parity run hides them. |
| D7 | The chat has the **right-hand action panel** (Ask: email drafts, dashboards, KPIs, documents; Pratica: the drafted email, with a one-line card in the step) and replies are formatted (headings, lists, source chips). | As in Claude's artifact panel and the chat before the restyle. The prototype shows the draft inline: the parity run keeps the inline copy and hides the card and the panel (`sessionStorage raffa4.parity`). |
| D8 | One family, **IBM Plex Sans**, for headings and body (`--font-heading`, `--font-body`). Weight 800 becomes **700** for titles of 18px and up (`--weight-heading`) and **600** below (`--weight-title`: row names, tabs, buttons). Figures are tabular. | A more sober, professional face for an enterprise tool; tabular figures keep dates and amounts aligned in columns. `deviations.ts` applies the same font and weight rule to the prototype. |
| D9 | Layout precision: (a) lists (Contratti, Rinnovi) are **data tables** with one header row and fixed column edges, figures right-aligned, the row action in its own column; (b) the Risultati headline numbers and KPIs are **one tile grid** (3 tiles of 2 columns over 6 of 1) with the same 20px padding; bar rows (per supplier, lever, proof) use fixed label / bar / figure columns; (c) the page's main action sits on the **title line**; (d) amounts from a million up read **CHF 1.2M** on every screen; (e) a contract without a notice date shows **Non indicata**, never "0 g"; (f) Documents and Workspace & members use the same 40px gutters as the other pages. | Columns, values and badges must not move from row to row whatever their content. Product owner's choice among mock-ups (options 1A, 2A, 3A, 4A). This changes the structure of Contratti, Rinnovi and Risultati, which no style rule can reproduce on the prototype: their golden shots no longer compare pixel for pixel and need a new baseline (see below). |
| D10 | The **Risultati** tab is **Spesa e risparmi** (EN *Spend and savings*): one report of what the validated contracts cost per fiscal year and quarter, with each contract's share and the savings on the same periods. | Product owner, 4 Nov 2026: budget owners need spend and savings in one place, not a separate results page. `deviations.ts` renames the prototype's tab, so the nav still compares pixel for pixel; the screen itself was already a D9 shot (13–15). |

### How the D9 screens are verified
D9 changes the structure of 7 shots — `09-rinnovi-lista`, `10-contratti-lista`, `13-risultati-senza-obiettivo`, `14-risultati-obiettivo-bozza`, `15-risultati-obiettivo-attivo`, `16-chiedi-conversazioni` (intent tiles with even padding) and `19-rinnovi-con-dati` — so no style rule can make the prototype's pixels describe them. On those shots `app.parity.spec.ts` asserts the rules D9 exists for instead (`parity/alignment.ts`), at every viewport:
- every cell of a table column starts and ends on the same x (header row included);
- the KPI tiles of one row put their values, and their notes, on one line;
- every bar of a bar chart starts and ends on the same x;
- no text is cut (screen-reader-only labels excepted) and nothing scrolls sideways.

The other 19 shots are still compared with the prototype pixel for pixel (with D2, D3 and D8 applied to it), and the click-path, the `data-screen-label` hooks, the copy and the geometry assertions apply to all 26.
