# Visual fidelity gaps — mockup vs `dev`

Compared 2026-09-07. Oracle: `inputs/design/prototypes/day1-demo.html`
(same system as https://claude.ai/code/artifact/9249d66d-5e60-4823-a1d0-8a1758273f58).
Live: https://mango-pond-061bc6d1e.6.azurestaticapps.net/ (`index-CYNvgndl.css`,
Last-Modified 2026-09-07 05:35 UTC).

Legend: **OPEN** = e11 work. **WAVE_COVERED** = already true on live CSS.
**DEFERRED** = needs e08 (or recovery) to exist first.

## Chrome (all routes)

| ID | Gap | Status |
|----|-----|--------|
| G-HEALTH | `App.tsx` renders visible `API: reachable (…)` above the page. Prototype has none. Breaks 100vh sign-in. | OPEN |
| G-40REM | Global `main { max-width: 40rem }` | WAVE_COVERED (E06/F06) |
| G-SIGNIN-COLS | Sign-in `grid-template-columns: 1fr 1fr` | WAVE_COVERED (live CSS) |
| G-ARCHIVO | Family declared; loaded via Google Fonts `@import` (prototype embeds woff2). Acceptable if Archivo actually applies. | OPEN if tests show system-ui |

## Screen 1 — Sign-in (prototype `<!-- SIGN-IN -->`)

| ID | Prototype | `web/` today | Status |
|----|-----------|--------------|--------|
| G-S1-FILL | Left fill `--color-accent-100`; grid lines `--color-accent-200` **48px** | Fill **accent-200**; grid **divider 32px** | OPEN |
| G-S1-LOCKUP | 14px accent square + "Raffa" 22px/800, top of panel | Missing | OPEN |
| G-S1-FLOW | `justify-content: space-between` (logo / sentence / jobs) | `center` — sparse middle blob | OPEN |
| G-S1-TYPE | North-star `clamp(30px, 3.8vw, 52px)` / 800 / lh 1.02 / tracking -0.02em | Fixed **32px** (`.screen-title`) | OPEN |
| G-S1-JOBS | Contract / Renewal / Savings **Intelligence**; New purchase **Quote Check** | Tracking / Opportunities invented | OPEN |
| G-S1-RIGHT | `max-width: 520px`; **h2 Sign in**; muted Entra sentence; btn padding 12px 14px; **Microsoft 4-square SVG** | h1 "Raffa"; no subtitle; naked button | OPEN |
| G-S1-PAD | Panel `padding: clamp(24px, 4vw, 48px)` | `--space-8` (32px) | OPEN |

## Shell + Ask bar

| ID | Prototype | Today | Status |
|----|-----------|-------|--------|
| G-SHELL | 224px rail, 2px rules, active item accent-100 + 3px accent bar | Structure exists; verify density vs export | OPEN |
| G-ASKBAR | Surface strip, 14px red square, heading-weight input, 2 chips | Present; verify padding `12px 32px`, chip chrome vs export | OPEN |
| G-STEPPER | Dark prototype-only stepper on top | Must **not** ship (design-system.md) | WAVE_COVERED (correctly absent) |

## Screens that exist (match export, do not invent)

| Screen | Route | Status |
|--------|-------|--------|
| Documents | `/documents` | OPEN (G-DOC) — two-col `400px 1fr`; density, pipeline, result card vs export |
| Portfolio | `/contracts` | OPEN (G-PORT) — attention strip, table headers 11px uppercase, critical row tint |
| Contract 360 | `/contracts/:id` | OPEN (G-360) — 6-cell fact row, tabs, recommended-action block |
| Review | `/contracts/:id/review` | OPEN (G-REV) — 4-col list + evidence pane |

## Screens that are still `ScaffoldScreen` (not e11 live)

Home, Ask (chat), Members, Renewals, Quote check, Review **queue** landing.
**DEFERRED** to e08 + recoveries. e11 must not implement them. After those
land, restyle against the same export (follow-up, not this slice).

## Implementation rule

Every OPEN row is closed by copying values from `day1-demo.html`, not by
restyling from taste. ADR-019 forbids a parallel palette.
