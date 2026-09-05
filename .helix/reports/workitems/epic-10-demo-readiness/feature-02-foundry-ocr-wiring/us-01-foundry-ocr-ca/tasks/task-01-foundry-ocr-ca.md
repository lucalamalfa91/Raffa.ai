---
id: E10/F02/US01/T01
type: task
story: us-01-foundry-ocr-ca
wave: 10
status: live
target_repo: contigo-infra
---

# task-01-foundry-ocr-ca — Wire Foundry + Document Intelligence into CA

## Coding objective

Confirm whether `ca-contigo-*-api` / worker already have Foundry project
and Document Intelligence (ADR-017) settings. If the live env list is still
connection-strings-only, add Terraform for the missing endpoints/identities
and let HCP apply `contigo-dev` / `contigo-demo`. Do not laptop-apply. Do
not add Savings/Quotes connection strings (e09 F02). Do not generate SQL.

## Parent story AC covered

- AC-1, AC-2, AC-3

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/contigo-infra/modules/containerapps/ | CA env for AI Gateway / DI |
| workspace/contigo-infra/modules/ (Foundry/DI) | only if hooks are incomplete |

## Context the implementer needs

- **Architecture decisions in force**: ADR-004, ADR-008, ADR-011, ADR-017,
  ADR-022.
- Identity module already documents Foundry connection verify
  (`scripts/foundry_connection_verify.py`). Prefer completing that path.
- Operator HITL may accept fixture AI for a dry run; still leave the
  wiring in place so `demo` is not permanently fixture-only.

## Definition of done

- [ ] Written inventory of CA AI/OCR vars on `dev` and `demo`, plus either
      “already present” or an HCP-applied Terraform change that adds them.

## Wave-spec entry

```yaml
- {id: E10/F02/US01/T01, prompt: reports/workitems/epic-10-demo-readiness/feature-02-foundry-ocr-wiring/us-01-foundry-ocr-ca/tasks/task-01-foundry-ocr-ca.md, produces: [foundry-ocr-wired], depends_on: [demo-fixture-seed], effort: L, layer: backend, status: live}
```
