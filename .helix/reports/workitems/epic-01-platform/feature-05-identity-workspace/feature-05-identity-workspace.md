---
id: feature-05
type: feature
parent: epic-01
wave: R0
status: active
---

# feature-05-identity-workspace — Identity / Workspace + roles

## Slice

Implement the Identity/Workspace bounded context: Workspace, User, Role, Membership
aggregates with `tenant_id`, OIDC claims, and workspace invite, fronting the RLS
backstop from feature-04.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Workspace + roles + membership | R0 |

## Architecture decisions in force

- ADR-009 (RLS tenant_id), ADR-010 (OIDC), ADR-003 (PostgreSQL).

## Target repo

`raffa-backend`
