# Auth / secrets for Day-1 demo (security-architect-readiness)

- ADR-010 (Entra PKCE + API JWT) is **not** on the API host today. Tests and
  the live API take `X-Tenant-Id`.
- SPA still uses Entra public client via `config.json` (e06). That is enough
  for a stakeholder Day-1 **if** the operator accepts ADR-022.
- If the operator marks `X-Tenant-Id` as BLOCKER at HITL, add an Entra-on-API
  story — that is **not** in the e10 slice as authored.
- KV + managed identity stay as designed (ADR-011). No secrets in git.
- RLS lands with e09 scripts. e10 seed must run as a role that can insert
  under the demo tenant without disabling RLS for the API identity.

`VOTE: APPROVE`
