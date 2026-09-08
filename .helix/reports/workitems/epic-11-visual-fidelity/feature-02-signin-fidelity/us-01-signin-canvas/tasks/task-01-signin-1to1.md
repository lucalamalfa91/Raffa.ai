---
id: E11/F02/US01/T01
type: task
story: us-01-signin-canvas
wave: 11
status: live
target_repo: contigo-web
---

# task-01-signin-1to1 — Sign-in canvas copied from day1-demo.html

## Coding objective

Copy the prototype `<!-- SIGN-IN -->` block in
`inputs/design/prototypes/day1-demo.html` into `web/src/routes/signin/`.
Do not invent values. Verbatim from the export (2026-09-07 extract):

Left panel:
- `background: var(--color-accent-100)`
- grid image: `linear-gradient(var(--color-accent-200) 1px, transparent 1px)` twice, `background-size: 48px 48px`
- `padding: clamp(24px, 4vw, 48px)`
- `justify-content: space-between`
- `border-right: 2px solid var(--color-divider)`
- Lockup: 14×14 accent square + "Contigo" 22px/800
- North-star: `font-size: clamp(30px, 3.8vw, 52px); line-height: 1.02; letter-spacing: -0.02em; font-weight: 800; max-width: 560px`
- Jobs: Contract / Renewal / Savings **Intelligence**; New purchase **Quote Check**

Right panel:
- `padding: 48px`; `max-width: 520px`; `justify-content: center`
- `h2` "Sign in" (not "Contigo")
- muted sentence: "Use your organisation account. Contigo never stores your password — identity is handled by Microsoft Entra ID."
- Primary button: Microsoft 4-square SVG from the export + "Continue with Microsoft Entra ID"
- Existing OIDC/PKCE micro-meta stays below the `hr`

Workspace picker (`WorkspacePickerScreen.tsx`) must keep `SignInStatementPanel`
on the left (already true after F06). Restyle the right column only as the
export's `signStage2` block.

## Parent story AC covered
- AC-1, AC-2, AC-3, AC-4

## Files to create or modify
| Path | Change |
|------|--------|
| `web/src/routes/signin/signin.css` | panel fill/grid/type/lockup/flow |
| `web/src/routes/signin/SignInScreen.tsx` | lockup markup; jobs copy; "Sign in" + caption + SVG |
| `web/src/routes/signin/WorkspacePickerScreen.tsx` | right-column heading/spacing vs export |
| `web/tests/routes/signin/` | lockup present; heading Sign in; jobs Intelligence; north-star class |

## Context the implementer needs
- Gaps G-S1-*. ADR-018/019/020.
- **Do not touch**: `App.tsx`, `styles/components.css` (F01), `shell.css`.
- **Do not** re-introduce `max-width: 40rem`. Grid stays `1fr 1fr`.

## Definition of done
- [ ] `npm test` in `web/` — sign-in tests cover lockup, heading, jobs, two-column canvas.
- [ ] CSS file contains `accent-100`, `48px 48px`, and `clamp(30px, 3.8vw, 52px)`.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | markup + class names from the export | `web/tests/routes/signin/` |

## Open questions blocking this task
- none
