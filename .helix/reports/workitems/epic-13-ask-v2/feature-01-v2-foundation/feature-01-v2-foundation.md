---
id: F01
type: feature
parent: epic-13
wave: 13
status: active
---

# feature-01-v2-foundation — Solution scaffold + Foundry five-role gateway

## Slice

Two independent backend foundations the rest of e13 builds on: (1) the
V2 solution shape — new projects `Contigo.Market`, `Contigo.Insights`
(+ tests), `Contigo.Suppliers.Products.Tests`, `Contigo.AiEval`, registered
in `backend/Contigo.slnx` and in the architecture allow-list — so later
phases never touch the solution file; (2) `FoundryAiGateway` for ADR-004's
five roles, registered when `AiGateway:Endpoint` is set, fixture otherwise,
always wrapped by `LoggingAiGateway`, with structured `classify` / `answer`
output and a compliance test that no tools / grounding payload is ever
sent (`inputs/requirements.md` R-AI-01…04). Absorbs e12 F01.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | V2 foundation (scaffold + live Foundry behind `IAiGateway`) | 13 |

## Architecture decisions in force

- ADR-024 — module map delta, no-tools answer role
- ADR-002 — modular monolith, one project per bounded context
- ADR-004 (amended) — five roles, structured output, always log-wrapped
- ADR-008, ADR-011 — Foundry project per env, managed identity, no-training, hash logging

## Target repo

`contigo-backend`
