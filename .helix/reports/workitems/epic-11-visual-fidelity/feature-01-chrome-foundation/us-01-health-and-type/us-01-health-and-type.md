---
id: us-01
type: user-story
parent: feature-01
wave: 11
status: active
---

# us-01-health-and-type — Product chrome without the E01 probe

## Acceptance criteria

- [ ] AC-1 Opening `/signin` does not show the text `API: reachable` (or
      checking/unreachable) in the visible canvas. The `/health` call may
      still run.
- [ ] AC-2 Primary block buttons use the prototype's 12px 14px padding.
- [ ] AC-3 Tests still prove the health probe ran (via `data-testid`).
