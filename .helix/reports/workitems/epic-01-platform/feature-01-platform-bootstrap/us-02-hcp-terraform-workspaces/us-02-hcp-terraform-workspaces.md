---
id: us-02
type: user-story
parent: feature-01
wave: R0
status: active
---

# us-02-hcp-terraform-workspaces — Create HCP Terraform org + per-env workspaces

## Story

As a **platform engineer**, I want two HCP Terraform workspaces (`raffa-dev` and
`raffa-demo`) under the `raffa` repo, so that `dev` and `demo` each have an
isolated remote state and can never share Terraform state.

## Acceptance criteria

- [ ] AC-1 An HCP Terraform organization exists for `raffa`.
- [ ] AC-2 Two workspaces `raffa-dev` and `raffa-demo` exist with independent remote state.
- [ ] AC-3 No Terraform state is stored in git.

## Definition of done

- [ ] AC-1..AC-3 verified by `scripts/bootstrap_hcp_org.py` exiting 0 and reporting both workspaces.

## Dependencies

| Depends on | Why |
|------------|-----|
| us-01 | HCP workspaces are wired to the `raffa` repo's VCS |

## Architecture decisions in force

- ADR-007 — remote state per environment (HCP workspaces `raffa-dev`/`raffa-demo`).

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Bootstrap HCP org + two workspaces | S | phase-2 |

## Council decisions carried into this story

Two workspaces `raffa-dev` and `raffa-demo` (ADR-007). HCP Terraform (locked IaC). State never in git.

## Open questions

- none
