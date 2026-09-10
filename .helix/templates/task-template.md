---
id: E01/F01/US01/T01
type: task
story: us-NN-<slug>
wave: R0
status: live
target_repo: raffa-backend
# requires: [azure_subscription]   # only if this task calls az / terraform apply
# requires: [hcp_terraform]        # only if this task creates HCP workspaces / inits cloud backend
---

# task-NN-<slug> — <title>

## Coding objective
<Imperative, 3-8 sentences. What to build, in which of the four repos, against
 which contract. Name the real types, endpoints, Terraform modules, SKUs and
 libraries the council decided on — never "a suitable X".>

## Parent story AC covered
- AC-2 <verbatim from the story>
- AC-3 <verbatim>

## Files to create or modify
| Path | Change |
|------|--------|
| backend/src/… | … |

Passata 2 cwd is the per-task git worktree of the product clone. Product
code goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree
root — not `workspace/<repo>/` (PROCESS.md D1).

Do **not** list `infra/README.md`, `backend/README.md`, `web/README.md`,
`mobile/README.md`, or root `README.md` here. README hygiene is standing
implementer/reviewer scope (`skills/readme-hygiene.md`); putting those
paths on every task violates single-writer-per-file in the same phase.

## Context the implementer needs
- **Architecture decisions in force**: ADR-NNN (<the constraint>)
- **Do not touch**: <files or areas out of scope for this task>

## Definition of done
- [ ] <a check that can be RUN: a named test, a command with exit code 0>
- [ ] <another>

Never "the code is clean" — the reviewer runs these boxes.

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | <the behaviour> | <path> |

## Open questions blocking this task
- <OQ-NNN — the question, and the assumption in force> | none

## Wave-spec entry
```yaml
- id: E01/F01/US01/T01
  prompt: reports/workitems/epic-NN-<slug>/feature-NN-<slug>/us-NN-<slug>/tasks/task-NN-<slug>.md
  produces: [<artifact-name>]
  depends_on: []
  effort: S
  layer: <infra|backend|web|mobile>
  status: live
```
