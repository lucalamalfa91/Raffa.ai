# Raffa — Design system (web V1)

Source of truth: **Modernist** design system (bound in Claude Design, folder `_ds/modernist-584f2982-aad7-48d1-aef0-a80897b0b5e4/`). Implement with its `styles.css` tokens; do not fork values into the SPA.

## Principles
- Flat, architectural, ink-on-ground. Structure comes from alignment and 2px rules, not from cards or shadows.
- Zero corner radius everywhere (`--radius-*` = 0).
- Everything flush left, including labels inside wide buttons.
- One accent (red) used sparingly: primary action, critical states, small emphasis. Never as a large field inside the app.
- Photographs (if any) pass through `.grayscale`.

## Tokens (from styles.css)
| Token | Value | Use |
|---|---|---|
| --color-bg | #f3f2f2 | page ground |
| --color-surface | #eae9e9 | panels, inputs, side panes |
| --color-text | #201e1d | ink |
| --color-accent | #ec3013 | primary action, critical marker |
| --color-accent-100/200 | #fff2ef / #ffe0d9 | critical row tint, sign-in ground + grid |
| --color-accent-700 | #ae1800 | accent-colored body text (≥4.5:1) |
| --color-neutral-200…900 | ramp | hovers, selected rows, muted text, stepper chrome |
| --color-divider | ink @ 40% | 2px section rules, 1px row rules |
| --font-heading / --font-body | Archivo | 800 headings, 400/600 body |
| --space-1…8 | 4–32px | spacing scale |
| --shadow-* | tuned | avoid inside the app; only dialogs |

## Type scale in the app
- Screen title `h2` 32px/800; kicker `h6` 13px uppercase, accent (release tag, e.g. "R1 · Contract Intelligence").
- KPI number 26px/800; key-fact number 20–22px/800.
- Table body 13px; table header 11px uppercase letter-spaced.
- Micro meta 11–12px, `--color-neutral-600`.

## Components used (Modernist classes)
- `.btn .btn-primary | .btn-secondary | .btn-ghost | .btn-block` — labels flush left.
- `.tag .tag-neutral | .tag-accent | .tag-outline` — see confidence + status mapping below.
- `.table` — 2px header rule, 1px row rules, hover tint. Fixed layout with widths on `th`.
- `.input`, `.field > label`, `.radio + .dot`, `.seg` — native controls.
- `.card` — only for the recommendation / provenance blocks.
- `.hr` — 2px rule.

## App-level patterns (built in the prototype)
- **Shell**: 224px left rail (workspace name, nav with badges, admin link, user) + main column. Prototype-only stepper strip on top (dark) — not part of the product.
- **Global Ask bar**: on every app screen, surface-colored strip with red square, heading-weight input, 2 contextual suggestion chips. Enter → Ask screen. ⌘K opens Ask.
- **Screen header**: kicker (release · module) + h2 + one-line summary; primary action right; 2px rule under.
- **Attention / threshold strip**: full-width row of equal cells with big number + label; clickable filter; red number = urgent, grey = zero.
- **Urgency in tables**: first column states the problem in words; row tint `--color-accent-100` + 3px left bar for critical; rows sorted by severity then deadline.
- **Detail pane**: 340–400px, `--color-surface`, 2px left rule; wraps below list under ~900px main width.
- **Facts vs AI**: deterministic facts in tables with "Deterministic" tag; AI recommendation in its own block labelled "Recommended action"; never mixed in one cell.

## Semantic mapping
| Meaning | Treatment |
|---|---|
| Confidence > 95% | `.tag-neutral` "Accepted · 97%" |
| Confidence 80–95% | `.tag-accent` "Flagged · 88%" |
| Confidence < 80% | `.tag-outline` "Review · 71%" — blocks consequential use |
| Status completed / Ready | `.tag-neutral` |
| Status needs_review | `.tag-outline` |
| Status failed | `.tag-accent` |
| Risk High | `.tag-accent`; Medium/Low `.tag-neutral` |
| Deadline ≤ 45 d | date in `--color-accent-700`, weight 600 |
| Abstain ("cannot determine reliably") | left 2px accent rule + `--color-accent-100` block |

## States
- **Loading**: skeleton bars `--color-neutral-300`, pulse animation, same row height as data.
- **Empty**: h3 + one sentence + primary action, left-aligned, max-width 480px.
- **Error**: 2px accent left rule, h4, plain sentence naming the endpoint/job, secondary "Retry".
- **Disabled**: 45% opacity (from styles.css).

## Icons
Lucide, inline SVG on currentColor, 1.5 stroke, square caps. Only where they carry meaning (upload, evidence, external link).
