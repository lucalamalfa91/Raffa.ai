---
id: feature-04
type: feature
parent: epic-01
wave: R0
status: active
---

# feature-04-backend-foundation — Modular monolith + store + RLS + deployable API

## Slice

Create the ASP.NET Core modular-monolith solution (one project per bounded context +
shared kernel + thin API/worker hosts), wire PostgreSQL + pgvector via EF Core with
RLS tenant isolation, and produce a deployable API + worker that runs on `dev`/`demo`.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | .NET solution shape + module projects (ADR-002) | R0 |
| us-02 | EF Core + npgsql + pgvector migrations (ADR-003) | R0 |
| us-03 | Tenant RLS policies + tenant claim (ADR-009) | R0 |
| us-04 | Deployable API host + worker host (ADR-002) | R0 |

## Architecture decisions in force

- ADR-002, ADR-003, ADR-009.

## Target repo

`raffa-backend`
