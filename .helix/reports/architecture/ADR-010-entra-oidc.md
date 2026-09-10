# ADR-010 — Entra ID app registrations / OIDC for web and mobile

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: security-architect (owner); client-architect and delivery-manager concur at council-close
- **Locked citations**: `locked-decisions.md` row "Auth/secrets" (OIDC, SSO-ready Entra ID); row "API"
  (API-first, web and mobile consume the backend API); row "Cloud/Environments" (two isolated envs).
  Product spec §14.1 (MFA capability, SSO-ready OIDC/SAML, Entra ID / Okta), §13.1 (API-first domains).

## Context and problem statement

Raffa must be SSO-ready on Entra ID and every client (web and mobile) consumes the backend API over
OIDC. There are two Azure environments (`dev`, `demo`), each fully isolated (data, identities, resource
groups). The same codebase and client apps must authenticate against whichever environment they target,
without shipping secrets or environment-specific config into client bundles.

The question this ADR answers: **how many Entra app registrations, and what OIDC flow does each client
use against the API?**

## Decision drivers

- **SSO-ready on Entra ID** (locked) — customers authenticate with their own Entra tenant in the future,
  so we must not hard-code a single customer's directory.
- **API-first** (locked) — both web (SPA/browser) and mobile (native) call the same ASP.NET Core API;
  the JWT audience/scopes must be uniform.
- **Two isolated environments** (locked) — `dev` and `demo` have separate identities; a token minted for
  one environment must not be accepted by the other.
- **No secrets in the client** (locked) — the public client uses PKCE/authorization-code, never a client
  secret; the API validates tokens by signature + issuer, never by shared secret.

## Considered options

1. **Two Entra app registrations per environment** — one *public client* registration (web + mobile
   share redirect/native reply URLs) and one *API* registration exposing scopes. Per env, so `dev` and
   `demo` each have their own pair. Four registrations total.
2. **A single pair shared across both environments** — one client + one API registration, issuer
   constant across `dev`/`demo`.
3. **Separate registrations for web vs mobile clients** — more granular but more registrations to manage.

## Decision outcome

**Chosen: Option 1 — per-environment pairs: one public-client registration and one API registration in
`dev`, and the same pair in `demo` (four app registrations total).** Web and mobile share the public
client registration via a browser redirect URI (web) and a native reply/custom redirect (mobile), both
using the OIDC **authorization-code + PKCE** flow (spec §14.1 "OIDC/SAML; Entra ID"). The API
registration exposes scopes (e.g. `Raffa.Read`, `Raffa.Write`) that both clients request. Each
environment's API validates `iss` + `aud` against that environment's known Entra tenant/registration, so
a `demo` token never works on `dev`.

### Consequences

- **Good**: Matches the locked "two isolated environments" rule exactly (separate identities = separate
  registrations). No client secrets anywhere (PKCE). Uniform audience/scopes for web and mobile.
- **Good**: Multi-customer SSO-ready: adding a customer's Entra tenant later means adding it to the
  appropriate environment's API trust, not changing client code.
- **Bad**: Four registrations to maintain in Terraform (IaC), each with reply-URI and scope config that
  must stay in sync.
- **Neutral**: Web and mobile differ only in *reply URI type*, not flow or scopes.

## Pros and cons of the options

### Option 1 — per-environment pairs (chosen)
- Good: clean isolation boundary; uniform scope model; no secrets; multi-tenant-ready.
- Bad: four registrations; Terraform must template them identically per env.

### Option 2 — single shared pair
- Good: fewer objects to manage.
- Bad: violates environment identity isolation; a shared issuer weakens the `dev`/`demo` data boundary.

### Option 3 — separate web vs mobile registrations
- Good: per-client consent granularity.
- Bad: more surface area for no isolation benefit in V1 (both are first-party clients of the same API).

## Implications for the decomposition

- Terraform (cloud-architect's ADR) must declare two app registrations per environment (public client +
  API) with the API exposing `Raffa.*` scopes and the client pre-authorized for them.
- The backend API must configure JWT bearer auth using that environment's Entra `issuer` and `audience`
  (metadata via OIDC discovery URL), not a single hard-coded authority — injected per environment at
  runtime from a config value that is not a secret.
- Mobile uses the native OIDC authorization-code + PKCE flow (no secret); the redirect URI for the
  native client is the platform's declared scheme (e.g. `raffa://callback`), registered on the public
  client registration.
- Tokens are validated by signature/issuer/audience/expiry only; the API does **not** store or share
  client secrets (there are none).

## Assumptions

- Client stack selection (client-architect) produces a web client and a native mobile client that both
  support the OIDC authorization-code + PKCE flow (SAML is listed as future, not required in V1 — we
  adopt OIDC only, consistent with the locked row "OIDC, SSO-ready (Entra ID)").
- The exact scopes (`Raffa.Read`/`Raffa.Write`) are named here as placeholders; final scope names are
  adopted when the API surface (software-architect) is fixed, without changing the registration shape.
