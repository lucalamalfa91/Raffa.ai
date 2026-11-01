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
