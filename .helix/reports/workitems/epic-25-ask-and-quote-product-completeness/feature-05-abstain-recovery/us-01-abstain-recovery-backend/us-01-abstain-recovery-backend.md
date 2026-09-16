---
id: us-01
type: user-story
parent: feature-05
wave: w18
status: active
---

# us-01-abstain-recovery-backend — Every abstain reply carries a recovery action

## Story
As the **Ask engine**, I want every abstain (and error) reply to carry a
recovery action from the catalog, so the user always has a clickable next step.

## Acceptance criteria
- [ ] AC-1 every abstain reply carries at least one recovery action (`actions[]` non-empty).
- [ ] AC-2 the action is a real catalog href (`CapabilityRouting.ResolveActions`), never model-authored.
- [ ] AC-3 the gateway-failure and empty-pack abstain paths also carry the recovery action.

## Definition of done
- [ ] every AC above is verified by at least one test named in a task
- [ ] honours ADR-024 (reply kinds; actions from the catalog)
- [ ] no open question blocking this story is unresolved and unassumed

## Dependencies
| Depends on | Why |
|------------|-----|
| — | `CopilotReplyBuilder` / `AskCopilotService` abstain paths currently emit empty `actions` |

## Architecture decisions in force
- ADR-024 — every abstain has a clickable next step; actions from the catalog only.

## Tasks
| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | abstain-recovery-backend | M | phase-3 |

This table must match the files under `tasks/` exactly.

## Council decisions carried into this story
- Recovery actions are secondary (product-owner); the server selects the action from the catalog that unblocks this failure.

## Open questions
- none
