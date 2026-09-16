---
id: E25/F05/US02/T01
type: task
story: us-02-abstain-recovery-web
wave: w18
status: live
target_repo: raffa-web
---

# task-01-abstain-recovery-web — Abstain renders a secondary recovery action

## Coding objective
Let an abstain reply carry and render a recovery action. In
`web/src/routes/ask/askViewModel.ts`, the `abstain` branch of `buildReply`
currently drops `actions` (`{ kind: "abstain", reason: turn.text }`); change it
to map `turn.actions` onto the existing `replyTypes.ts#AbstainReply` (letting
`AbstainReply` carry an optional `actions`). In
`web/src/routes/ask/reply/ReplyBody.tsx`, the `abstain` case renders the
existing `.abstain-block` **and**, when actions are present, an `ActionRow`
styled secondary (never primary) — reusing the existing `ActionRow`, never a
new control (ADR-019). Defensive: an abstain with no action still renders the
abstain block, never an empty screen. Cite `screens-v2.md` §2 (abstain block +
warm recovery).

## Parent story AC covered
- AC-1 abstain renders the recovery action as secondary.
- AC-2 reuses `ActionRow` (native link).
- AC-3 abstain without action still renders the block.

## Files to create or modify
| Path | Change |
|------|--------|
| web/src/routes/ask/reply/replyTypes.ts | `AbstainReply` gains optional `actions` |
| web/src/routes/ask/askViewModel.ts | `abstain` branch maps `turn.actions` |
| web/src/routes/ask/reply/ReplyBody.tsx | abstain case renders a secondary `ActionRow` |

## Context the implementer needs

**Closes: NW-59**

- **Architecture decisions in force**: ADR-024 (abstain carries a recovery action, never primary); ADR-019 (native link).
- **Design anchor**: `inputs/design/prototypes/raffa-v2/screens-v2.md` §2.
- **Do not touch**: the backend abstain action emission (phase 3); `ActionRow` itself (reused as-is).

## Definition of done
- [ ] `npm run typecheck` exits 0
- [ ] `npm run lint` exits 0
- [ ] `npm test` exits 0, with a test proving an abstain with an action renders a secondary `ActionRow` and an actionless abstain still renders the block

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | abstain + action renders secondary; actionless abstain renders block | `web/src/routes/ask/reply/ReplyBody.test.tsx` |

## Open questions blocking this task
- none

## Wave-spec entry
```yaml
- id: E25/F05/US02/T01
  prompt: reports/workitems/epic-25-ask-and-quote-product-completeness/feature-05-abstain-recovery/us-02-abstain-recovery-web/tasks/task-01-abstain-recovery-web.md
  produces: [abstain-recovery-render]
  depends_on: [abstain-recovery-action]
  effort: S
  layer: frontend
  status: live
```
