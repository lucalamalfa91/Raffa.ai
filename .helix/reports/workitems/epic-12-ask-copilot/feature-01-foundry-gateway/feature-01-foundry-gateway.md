---
id: F01
type: feature
parent: epic-12
wave: 12
status: active
---

# feature-01-foundry-gateway — Foundry 5-role IAiGateway

## Slice

Implement `FoundryAiGateway` for ADR-004 roles. Register it when
`AiGateway:Endpoint` is set; otherwise keep the fixture. Always wrap
`LoggingAiGateway`. Azure SDKs stay in `Contigo.AiGateway`.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | Foundry gateway + DI wrap | 12 |

## Architecture decisions in force

- ADR-004, ADR-008, ADR-023

## Target repo

`contigo-backend`
