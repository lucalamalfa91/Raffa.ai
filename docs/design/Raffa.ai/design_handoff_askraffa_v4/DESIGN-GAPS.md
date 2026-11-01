# Design gaps — what the prototype does NOT define, and its known quirks

Goal of this handoff: the built app must be **visually and behaviourally identical to `reference/AskRaffa V4.dc.html`**. Where the prototype is silent, this file fixes the rule so the developer doesn't improvise. Where the prototype has a quirk, it is listed so it is either **replicated on purpose** or **fixed with approval** — never silently changed.

Legend — **R** = replicate exactly · **D** = decision made here (implement as written) · **Q** = needs a product/design answer (default given).

---

## A. Not defined in the prototype (implement as specified here)

| # | Gap | Rule | |
|---|---|---|---|
| A1 | **Viewports & responsive** — prototype is desktop only. | Design width 1440×900 (golden). Supported 1280–1920 wide: content stretches (see max-widths in README). Below 1100px: Guida Raffa collapses by default (toggle remains); lists keep flex-wrap. Below 900px: not supported in 4.0 → show a "use a larger window" notice. Mobile app (`/mobile`) is out of scope. | D |
| A2 | **Loading states for API calls** | Skeleton = same layout with `--color-neutral-200` blocks (no shimmer), 150 ms delay before showing. Button in flight: label unchanged, `disabled`, 45% opacity + inline "…" suffix. Never a full-screen spinner except document reading, which uses the 18px accent spinner from *Revisione*. | D |
| A3 | **Error states** | Inline, at the point of failure, never a toast: accent-100 background block with 2px left border `--color-accent`, one sentence (what happened) + one recovery button (e.g. *Riprova*, *Carica un'altra versione*). Server 403 on approve → the existing "Può procedere: <approver>" banner. 409 on a stale email version → banner "Il collega ha aggiornato la bozza alla vN…" + *Recupera il mio testo* (prototype shows this in the engine; text is in `renewal-case-v4.js`). | D |
| A4 | **Keyboard & accessibility** | Tab order follows visual order. Focus ring = DS `2px solid --color-accent`, offset 2px (never default blue). After a case step changes, move focus to the new step card's heading (`tabindex=-1`). Guide panel = `<aside aria-label="Guida Raffa">`, content `aria-live="polite"` so "Cosa fare ora" is announced when it changes. Rows that navigate (`.r-row`) are `<a>`/`role=link` with Enter activation. Nav tabs = `<nav>` + `aria-current="page"`. Contrast: body text ≥4.5:1; accent as text only via `--color-accent-700`; `--color-neutral-600` on white is used for 10–12px labels in the prototype — keep it (it is the DS secondary). | D |
| A5 | **Motion** | Only the 18px spinner (`rotate 0.8s linear`). No page transitions, no animated counters, no hover transforms. Hover = background `--color-neutral-200` on rows/intents; buttons use the DS accent-ramp hover. | R |
| A6 | **Icons / logo** | No icon library in the prototype: logo = 12×12 `--color-accent` square + "Raffa.ai" (Archivo 800 17px). Glyphs used as text: `↺ ▾ ▴ → ← ·`. If icons are needed later use Lucide (DS rule). The real brand logo, if any, replaces only the square+text pair at the same height. | D |
| A7 | **Number, date, currency formats** | Currency "CHF" prefix, no decimals for ≥1k: `CHF 156k`, `CHF 2.4M`; unit prices keep decimals as in the contract (`CHF 2.34`). Thousands separator in tables: apostrophe-free `1,500` in EN, `1.500` in IT (prototype shows EN style in both — **Q**: switch to locale formatting; default = locale). Dates: `18 Oct 2026` (EN) / `18 Ott 2026` (IT) — day, 3-letter month, year. Percent: `20%` / `+7%`. Days: `40 g` (IT) / `40 d` (EN). | D/Q |
| A8 | **Text overflow rules** | Supplier names and titles wrap (`text-wrap: pretty`); only the goal summary, nav labels and conversation header truncate with ellipsis. Buttons never wrap (`white-space: nowrap`). Table-like rows wrap their cells (flex-wrap), they never scroll horizontally. | R |
| A9 | **Empty states** | Defined in prototype for: workspace without contracts (Oggi), Contratti, Rinnovi, Oggi "Per oggi hai finito", Chiedi (no conversation), team lists. Not defined: Risultati with zero results → keep the 3 headline numbers at `CHF 0k` (as prototype) and add one sentence under the KPI strip: "Nessun risultato firmato nel periodo."; charts show axis and "—" labels. | D |
| A10 | **Printing / export** | Out of scope for 4.0. | D |
| A11 | **Dark mode / theming** | None. Single light theme (Modernist). Page ground `#fff` (prototype override of `--color-bg`), surface blocks `--color-surface`. | R |
| A12 | **Permissions UX for read-only roles** (viewer) | Same screens; all action buttons hidden, not disabled; Oggi shows "Nessuna azione per te". Buyer vs CPO behaviour is as prototype (*Vedi come*). | D |
| A13 | **Notifications** | None in 4.0 besides the Oggi badge (count of items for the user, accent-700 11px, hidden when 0). | D |
| A14 | **Case step variants not shown in screenshots** | Every step card is rendered from `RC.view(...)`; the card anatomy (kicker / title / banner / lines / rows / table / phase chips / email / quote / fields / chips / inputs / ≤3 buttons / "Altro ▾" / note) is fixed in README §3. Variants to implement from the engine: interview question, policy exception, plan, email (all 5 states + conflict banner), waiting-for-supplier, reply analysis, decision, agreed terms, request contract, contract check (agreed vs found), corrective email, ready for signature, signed check, acquisition, outcome. Use `renewal-case-v4.js` as the content spec for each. | D |
| A15 | **Guida Raffa copy per state** | Fixed strings per screen/step are in `COPY-CATALOGUE.md` Part B and `AskRaffa V4.dc.html` (`const G = …`). Unknown states fall back to the screen-level copy. | D |

## B. Known quirks of the prototype (what to do)

| # | Quirk (visible in `screenshots/`) | Action |
|---|---|---|
| B1 | **Italian UI shows English data strings** in the review card — e.g. field "Price uplift at renewal", value "7% or CPI, whichever is higher", clause quote in English (`04-revisione-campo-2.png`). | Data is demo content (contract in English). **R** for the contract quote; **Q**: field *labels* should be localised by the app (default = localise labels, keep extracted values/quotes in the document's language). |
| B2 | **"CHF 0k" headline numbers** in Risultati when nothing is signed (`13-…png`). | **R** (honest zero) + A9 sentence. |
| B3 | **Headline-numbers grid** in Risultati drew a stray vertical line when wrapping to 2 columns. | **Fixed in the prototype** (1px-gap divider grid, same pattern as the Contract dates). The reference already contains the fix — replicate the reference. `13-…png` predates the fix. |
| B4 | **Disclosure chevrons wrapped** under their label in narrow cells. | **Fixed in the prototype** (`white-space: nowrap` on the label). Replicate the reference; `07-…png` and `08-…png` predate the fix. |
| B5 | **"Passi completati · 1"** appears already at the very start of a case (the "Caso di rinnovo aperto" overview step). | **R** — it is the step that "Rivedi la panoramica del caso" opens. |
| B6 | Page content width: at 924px preview width the guide takes 300px and the main column shrinks; at ≥1280 it does not. Screens are captured at 924×540 (the editor pane), so **long pages are cut**. | Use the Playwright script (`parity/`) to produce full-page goldens at 1440×900; the PNGs in `screenshots/` are orientation only. |
| B7 | **IT/EN**: EN is produced by dictionary + fragment replacement; some compound strings may read slightly off in EN. | Replace with proper message catalogues (strings listed in `COPY-CATALOGUE.md`); EN source of truth for case steps is the `B(en,it)` pairs in `renewal-case-v4.js`. |
| B8 | **Composer shadow** (chat/Chiedi composer, `0 1px 2px rgba(0,0,0,.04), 0 4px 16px rgba(0,0,0,.04)`) is the only shadow; the Modernist DS says "nothing floats". | **R** (kept deliberately for the input affordance). |
| B9 | **Demo bar** (36px, dark) is part of every screenshot. | Not part of the product. For parity tests, hide with `[data-no-i18n]` selector or the `?demo=0` flag you add; compare everything below it. |
| B10 | Prototype scenario clock is fixed to **08 Sep 2026**. | App must accept an injectable "now" in test mode (`?now=2026-09-08` or Playwright `page.clock`). |
| B11 | Quote assessment in the "Con dati" sample shows English contract SKUs and line names. | **R** (demo data). |

## C. Open questions that change pixels (defaults applied unless you answer)
1. Localise numbers (A7)? Default **yes**.
2. Localise extracted field labels in Revisione (B1)? Default **yes**.
3. Real logo asset? Default: square + wordmark as in prototype.
4. Minimum supported width (A1)? Default **1100px** with guide auto-collapsed below that.
