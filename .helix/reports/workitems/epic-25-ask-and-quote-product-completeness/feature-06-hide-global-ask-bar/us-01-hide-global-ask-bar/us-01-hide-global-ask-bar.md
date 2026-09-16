---
id: us-01
type: user-story
parent: feature-06
wave: w18
status: active
---

# us-01-hide-global-ask-bar — No second Ask bar on Ask screens

## Story
As a **user** on the Ask screen, I want not to see the global Ask bar (which
duplicates the composer), so the screen has one input and ⌘K focuses it.

## Acceptance criteria
- [ ] AC-1 the global Ask bar is suppressed on `/ask` and `/ask/:id` (via `useLocation`), kept everywhere else.
- [ ] AC-2 ⌘K / Ctrl+K focuses the composer on Ask screens.
- [ ] AC-3 the bar still appears on non-Ask routes.

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] honours ADR-018/ADR-020 (IA / no duplicate bar)
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| — | `AppShell` always renders `GlobalAskBar` today (`AppShell.tsx:82`); only the suppression is missing |

## Architecture decisions in force
- ADR-018 — `/ask` owns its composer; ADR-020 — screen inventory (no duplicate bar).

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | hide-global-ask-bar | S | phase-2 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Suppress on `/ask` + `/ask/:id` via `useLocation`; keep elsewhere; ⌘K focuses the composer.

## Open questions
- none
