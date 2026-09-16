---
id: us-01
type: user-story
parent: feature-01
wave: w18
status: active
---

# us-01-ask-chip-role-gate — A non-Admin sees no admin-gated Ask chips

## Story
As a **Procurement member**, I want not to see admin-gated suggestion chips in
the global Ask bar, so that I am not offered actions I cannot take — while an
Admin still sees them, and the capabilities API itself is unchanged.

## Acceptance criteria
- [ ] AC-1 a non-Admin sees no chip whose catalog entry carries `roleGate !== "any"`; an Admin does.
- [ ] AC-2 `GET /api/capabilities` is identical (full catalog) for both roles — presentation only.
- [ ] AC-3 the `role` prop is thread from the server-derived value at `AppShell.tsx` (not re-derived in the bar).

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] honours ADR-022 S16-11 (presentation, never a control)
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| — | the `role` already arrives at `AppShell.tsx`; only the threading is missing |

## Architecture decisions in force
- ADR-022 S16-11 / ADR-012 w17 cl 40 — presentation, never a security fix.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | ask-chip-role-gate | S | phase-1 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- One predicate: drop chips whose catalog `roleGate !== "any"` when role ≠ Admin; thread the single server `role` prop.

## Open questions
- none
