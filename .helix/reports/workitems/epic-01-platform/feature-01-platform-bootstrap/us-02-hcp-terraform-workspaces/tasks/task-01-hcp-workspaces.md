---
id: E01/F01/US02/T01
type: task
story: us-02-hcp-terraform-workspaces
wave: R0
status: live
target_repo: raffa-infra
# requires: [github_org]
# requires: [hcp_terraform]
---

# task-01-hcp-workspaces — Bootstrap HCP Terraform org + two workspaces

## Coding objective

Bootstrap the HCP Terraform organization for `raffa` and create two workspaces
`raffa-dev` and `raffa-demo` (ADR-007: remote state per environment). Wire the
workspaces to the `raffa` GitHub repo's VCS connection so a change under `infra/`
triggers the right workspace plan/apply. Verify state lives only in HCP, never in
git. Use `scripts/bootstrap_hcp_org.py` (or the HCP API) for reproducibility.

## Parent story AC covered

- AC-1 (HCP org exists)
- AC-2 (`raffa-dev` + `raffa-demo` workspaces, independent state)
- AC-3 (no state in git)

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/raffa-infra/.terraformignore | exclude local state/backends |
| scripts/bootstrap_hcp_org.py | create/assert workspaces |

## Context the implementer needs

- **Architecture decisions in force**: ADR-007 (two workspaces, remote state per env); ADR-006 (region `westeurope` is data-plane only, not state).
- **Do not touch**: Terraform modules yet (feature-02).

## Definition of done

- [ ] `python scripts/bootstrap_hcp_org.py` exits 0 and prints both `raffa-dev` and `raffa-demo` workspaces.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| script | two workspaces exist, VCS-wired, no state in git | `scripts/bootstrap_hcp_org.py` |

## Open questions blocking this task

- none

## Wave-spec entry

```yaml
- id: E01/F01/US02/T01
  prompt: reports/workitems/epic-01-platform/feature-01-platform-bootstrap/us-02-hcp-terraform-workspaces/tasks/task-01-hcp-workspaces.md
  produces: [hcp-terraform-workspaces]
  depends_on: [github-org-repos]
  effort: S
  layer: backend
  status: live
```
