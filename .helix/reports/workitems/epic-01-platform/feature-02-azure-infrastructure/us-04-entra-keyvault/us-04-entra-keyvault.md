---
id: us-04
type: user-story
parent: feature-02
wave: R0
status: active
---

# us-04-entra-keyvault — Entra app registrations + per-env Key Vault

## Story

As a **security engineer**, I want four Entra ID app registrations (public client +
API api pair per env) and a per-environment Key Vault accessed via managed identity,
so that OIDC auth and secrets are isolated per env with no secrets in source.

## Acceptance criteria

- [ ] AC-1 `dev` has one public-client registration and one API registration; `demo` has the same pair (four total).
- [ ] AC-2 API registration exposes scopes (e.g. `Raffa.Read`, `Raffa.Write`); public client uses PKCE (no client secret).
- [ ] AC-3 `kv-raffa-dev` and `kv-raffa-demo` exist; API/worker managed identities granted `get`/`list` on their own env's vault only.

## Definition of done

- [ ] Terraform declares four registrations + two Key Vaults with managed-identity access; no secret in state/source.

## Dependencies

| Depends on | Why |
|------------|-----|
| us-02/us-03 (feature-02) | managed identities live in each env |

## Architecture decisions in force

- ADR-010 (4 registrations, PKCE, scopes), ADR-011 (per-env Key Vault + managed identity).

## Tasks

| ID | Title | Effort | Phase |
|----|-------|--------|-------|
| task-01 | Declare 4 Entra registrations + 2 Key Vaults + managed-identity grants | L | phase-3 |

## Council decisions carried into this story

Four registrations total (public client + API, per env); OIDC Auth Code + PKCE; scopes `Raffa.Read`/`Raffa.Write`; Key Vaults `kv-raffa-dev`/`kv-raffa-demo`.

## Open questions

- none
