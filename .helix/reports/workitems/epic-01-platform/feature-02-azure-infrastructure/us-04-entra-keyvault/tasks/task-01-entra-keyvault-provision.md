---
id: E01/F02/US04/T01
type: task
story: us-04-entra-keyvault
wave: R0
status: live
target_repo: raffa-infra
# requires: [azure_subscription]
# requires: [hcp_terraform]
---

# task-01-entra-keyvault-provision — Declare 4 Entra registrations + 2 Key Vaults + managed-identity grants

## Coding objective

In the `identity` and `keyvault` Terraform modules, declare four Entra ID app
registrations per ADR-010: one public client + one API registration in `dev`, and
the same pair in `demo` (four total). The API registration exposes scopes
`Raffa.Read` and `Raffa.Write`; the public client is pre-authorized for them
with the web redirect URI and the native `raffa://callback` scheme (PKCE, no
client secret). Create `kv-raffa-dev` and `kv-raffa-demo` (ADR-011) and grant
each env's API + worker managed identity `get`/`list` on its **own** env's vault
only — no cross-env access policy.

## Parent story AC covered

- AC-1 (four registrations)
- AC-2 (API scopes + PKCE public client, no secret)
- AC-3 (two Key Vaults + per-env managed-identity grants)

## Files to create or modify

| Path | Change |
|------|--------|
| workspace/raffa-infra/modules/identity/main.tf | Entra app registrations + scopes |
| workspace/raffa-infra/modules/identity/outputs.tf | client_id, api audience/issuer |
| workspace/raffa-infra/modules/keyvault/main.tf | Key Vault + access policies |

## Context the implementer needs

- **Architecture decisions in force**: ADR-010 (4 registrations, PKCE), ADR-011 (per-env Key Vault, managed identity, no secrets in source).
- **Do not touch**: app runtime; Terraform only references identities/vault, never emits app secrets.

## Definition of done

- [ ] `terraform plan` shows 4 Entra registrations + 2 Key Vaults + scoped access policies, no secret literal.

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| plan | 4 registrations + per-env vault grants, no secret | `modules/identity`, `modules/keyvault` |

## Open questions blocking this task

- none

## Wave-spec entry

```yaml
- id: E01/F02/US04/T01
  prompt: reports/workitems/epic-01-platform/feature-02-azure-infrastructure/us-04-entra-keyvault/tasks/task-01-entra-keyvault-provision.md
  produces: [entra-registrations, keyvaults]
  depends_on: [azure-dev-environment, azure-demo-environment]
  effort: L
  layer: backend
  status: live
```
