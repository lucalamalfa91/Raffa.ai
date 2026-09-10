# Security Architect — Web Delta Lane: no security delta

- **Seat**: security-architect
- **Lane**: `reports/architecture/draft/security-architect-web/`
- **Date**: 2026-09-04
- **Scope**: confirm OIDC/PKCE public-client SPA posture and Postgres RLS tenancy are unchanged by the web experience pass (epic 6+, `layer: web`).

## What I confirmed (read on this pass)

| Source | What it establishes |
| --- | --- |
| `reports/context/web-integration-mandate.md` | Delta frame: ADR-001…017 + epic-01…05 done; new work starts at epic/wave 6, `layer: web` only, append-only. |
| `reports/context/locked-decisions.md` | Locked row "Auth/secrets": OIDC, SSO-ready (Entra ID); secrets in Key Vault; **no secrets in code, client bundles, or Terraform source**. |
| `reports/architecture/INDEX.md` | ADR-009 (RLS), ADR-010 (Entra OIDC), ADR-011 (Key Vault/RAG) accepted; ADR-012 (web SPA + PKCE) accepted. |
| `reports/architecture/ADR-012-web-stack.md` | Web = React + TS + Vite SPA, OIDC Authorization Code + PKCE, static bundle on SWA free tier; **no client secret** — only public `client_id` + redirect URI. |
| `reports/architecture/ADR-010-entra-oidc.md` | Per-env public-client + API registration pair (4 total); web and mobile share the public client; PKCE only, no secret; API validates `iss`+`aud`. |
| `reports/architecture/ADR-009-tenancy.md` | Postgres RLS on every tenant table, `tenant_id` passed by app, RLS is the non-bypassable backstop. |
| `reports/architecture/ADR-011-secrets-and-rag.md` | Per-env Key Vault + managed identity + OIDC federation; authz-before-retrieval; input-hash logging; no-training. |
| `workspace/raffa-infra/modules/identity/main.tf` | ADR-015 deployment SP uses OIDC federation — **no `client_secret` anywhere** in IaC. |

## Confirmation: no security delta

The web experience pass (epic 6+: IA, design system, screen inventory, capability
UIs for R0→R4) **changes only the browser surface**. It does not change any
security boundary established by ADR-009/010/011. Specifically:

1. **SPA stays a PKCE public client.** ADR-012's Authorization Code + PKCE flow,
   and ADR-010's per-env public-client registration, remain in force. The web pass
   adds routing/screens, not an auth model change. No client secret is introduced;
   the SPA holds only non-secret public config (`client_id`, authority, redirect
   URI, API base URL) — already encoded as "no secrets in client bundles"
   (locked-decisions, and ADR-011/012 "config-not-code" implications).

2. **No secrets in `web/`.** The locked "no secrets in code, client bundles, or
   Terraform source" row stays satisfied. Any env-specific value the SPA needs is
   non-secret runtime config (`config.json`), not a credential. No thin-gap or
   screen story on this pass may introduce a secret into the static bundle; if a
   proposed field appears secret-bearing it is a red flag the drafting seat must
   OBJECT to (it belongs on the backend, behind Key Vault + managed identity).

3. **RLS unchanged.** ADR-009's RLS backstop is a database/backend property and is
   not touched by the web pass. All tenant-scoping remains server-side; the SPA is
   a pure API consumer and never holds `tenant_id` as a client-side trust
   boundary — the backend resolves tenant/role from the validated token.

4. **No new backend surface for security reasons.** The web pass consumes existing
   HTTP contracts. The only permitted backend write is a "thin named API gap"
   (mandate §3), which must not redesign a module or weaken any RLS/Key Vault/RAG
   isolation control. No thin gap on this pass introduces a new secret, a new
   cross-tenant path, or a client-supplied raw blob URL (ADR-009).

## Residual watch-items for the decomposer / council close (not deltas)

These are confirmations to carry into later seats, not new decisions:

- Every screen story that touches auth-adjacent UI (login/redirect, workspace
  switch, role display) must keep the PKCE flow intact and never store a token in
  `localStorage` as a persistent refresh secret — access-token storage policy is
  non-secret and already public-client-appropriate; confirm the chosen `web/`
  token holder is a non-secret session store (in-memory or short-lived) when the
  `raffa-web` repo mounts.
- Any screen displaying contract evidence/citations consumes server-rendered
  authorized results only; the client must never assemble RAG context itself
  (ADR-011 authz-before-retrieval is a backend invariant).

## Recommendation

No security ADR is required for this pass. No change to ADR-009/010/011/012.

No-security delta: the SPA remains a PKCE public client; no secrets in `web/`;
RLS and Key Vault/RAG isolation are untouched by `layer: web` work.
