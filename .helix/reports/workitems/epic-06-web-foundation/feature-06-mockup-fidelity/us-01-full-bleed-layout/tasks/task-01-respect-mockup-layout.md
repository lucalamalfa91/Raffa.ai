---
id: E06/F06/US01/T01
type: task
story: us-01-full-bleed-layout
wave: 7
status: live
target_repo: raffa-web
---

# task-01-respect-mockup-layout — Full-bleed layout matching the Claude Design mockup

## Coding objective
Make every shipped web screen occupy the viewport the way the operator's
Claude artifact does:

https://claude.ai/code/artifact/9249d66d-5e60-4823-a1d0-8a1758273f58

Local export of the same system: `inputs/design/prototypes/day1-demo.html`
+ `screens.md`. A wave that leaves a "reduced" / card / `max-width: 40rem`
layout is **not done**.

## Root cause (already diagnosed — fix this first)

`web/src/index.css` still has the E01 OIDC-scaffold rule:

```css
main,
.startup-error {
  max-width: 40rem;
  margin: 3rem auto;
  padding: 0 1.5rem;
}
```

That rule hits **every** `<main>`:

- `App.tsx` wraps `/signin` + workspace picker in `<main>` → login is a
  ~640px centered card; the statement panel collapses and type wraps to
  a sliver.
- `AppShell.tsx` uses `<main className="shell-main">` → Home, Documents
  and every later screen live inside the same 40rem column. Documents'
  `grid-template-columns: minmax(280px, 400px) 1fr` then gives the
  result-card column a few dozen pixels, so filenames stack one letter
  per line.

This is leftover scaffold CSS, not a Design decision. ADR-019 / the
prototype are full-bleed (rail 224px + fluid main; sign-in is a 100vh
two-column page).

## Parent story AC covered
- Sign-in (screens.md §1): left statement panel + right action, full
  viewport, north-star readable, 4 V1 jobs in a horizontal row.
- Workspace picker: same canvas as the prototype, not a narrow article.
- Shell (ia.md + day1-demo.html): 224px rail + main that fills the rest.
- Documents (screens.md §3): dropzone column ~400px, result/status
  column `1fr`, fields and filenames readable (no vertical glyph stack).

## Files to create or modify
| Path | Change |
|------|--------|
| `web/src/index.css` | Remove `max-width: 40rem` / `margin: 3rem auto` from `main`. Keep it only on `.startup-error` if that one-off still needs a narrow column. |
| `web/src/App.tsx` | Do not wrap SignInRoute in a constraining `<main>`; sign-in owns the page. |
| `web/src/routes/signin/signin.css` | Full-viewport grid; left panel must actually take remaining width (not a 1fr inside a 640px parent). |
| `web/src/components/shell/shell.css` | `shell-main` fills the 1fr track; no inherited max-width. |
| `web/src/routes/documents/documents.css` | Two-column mockup (`~400px 1fr`); result card / table text wraps as sentences, not glyphs. |
| `web/tests/` | Regression: sign-in, picker, shell, documents are not descendants of a 40rem `main`. |

## Context the implementer needs
- **Source of truth (operator)**:
  https://claude.ai/code/artifact/9249d66d-5e60-4823-a1d0-8a1758273f58
  Full-bleed desktop SPA: 224px rail + fluid main; sign-in is a 100vh
  two-column page, not a centered card.
- **Local export**: `inputs/design/prototypes/day1-demo.html`,
  `screens.md`, `design-system.md`. Prefer `/design-sync`.
- **Architecture**: ADR-018, ADR-019, ADR-020.
- **Do not** invent a third layout. Do not "improve" spacing off-brief.
- **Do not** mark this task done from a screenshot of a card layout.

## Definition of done
- [ ] `main` in `index.css` is no longer a 40rem page wrapper.
- [ ] Sign-in, workspace picker, Home/shell, and Documents match the
      prototype proportions on a desktop viewport (≥1280px).
- [ ] Documents result/status column is readable (filename and body
      text wrap as words, never one character per line).
- [ ] `npm run build` and `npm test` exit 0.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | `#root main` used for sign-in/shell is not `max-width: 40rem` | `web/tests` |
| unit | documents column grid is `~400px 1fr` and not crushed by an ancestor max-width | `web/tests` |

## Wave-spec entry
```yaml
- id: E06/F06/US01/T01
  prompt: reports/workitems/epic-06-web-foundation/feature-06-mockup-fidelity/us-01-full-bleed-layout/tasks/task-01-respect-mockup-layout.md
  produces: [web-layout-mockup]
  depends_on: []
  effort: L
  layer: frontend
  status: live
```
