# ADR-web-stack — Web client stack and hosting

- **Status**: proposed
- **Date**: 2026-09-01
- **Deciders**: client-architect (draft), council-close
- **Locked citations**: Frontend/mobile "Council decides the stacks" (brief §1); API "API-first. Web and mobile consume the backend API." (brief §1); "No secrets in code, client bundles" (brief §1); "Hosting choice for the web client: council, under the cost guideline." (brief §9)

## Context and problem statement

Raffa V1 is **web-first** (brief §9). The web client must deliver the full user-visible ladder — auth, workspace/roles, document upload, portfolio, Contract 360 with evidence + confidence, review/correction, Ask Raffa with citations, then renewals, savings, and quote check as later slices land (spec §16; brief §3, §9). It is a pure API consumer: every byte of business data arrives via the ASP.NET Core backend API (brief §1 API-first). The stack is council-owned and must stay within the cost guideline while giving a small team (Claude Code via Helix) fast iteration and a single deployable `web/` folder in the monorepo.

## Decision drivers

- **Cost** — hosting must be cheap/free-tierable and scale-to-zero friendly; no idle-expensive node runtime.
- **API-first, no secrets** — an OIDC/SSO SPA (Entra ID) that holds only a client id and calls the API, never a client secret; refresh/session handled by the auth provider or a BFF.
- **One repo, one deployable** — a static `web/` folder build product (HTML/JS/css) that any cheap static host can serve, with an OIDC auth flow that does not require the static host to proxy anything.
- **Coordination with backend** — TypeScript shares a natural API contract with ASP.NET Core JSON and OpenAPI; it also pairs with the mobile decision (ADR-mobile-stack) so the same language/tooling covers both surfaces.

## Considered options

1. **React + TypeScript + Vite SPA, served as a static bundle** — the team-standard web UI.
2. **Blazor (WASM or Server)** — same C# language as the backend.
3. **Angular + TypeScript** — batteries-included framework.

## Decision outcome

**Chosen: Option 1 — React + TypeScript + Vite, an OIDC SPA with the Authorization Code + PKCE flow against Entra ID, built to a static bundle and hosted on Azure Static Web Apps (free tier).**

React + Vite produces a fully static output, which is the cheapest thing Azure can host (Static Web Apps free tier, scale-to-zero friendly, TLS and global CDN included) and it keeps `web/` a single buildable folder in the monorepo with no server process to pay for or secure. TypeScript is the lowest-friction language for a small AI-assisted team consuming a JSON/OpenAPI backend, and it aligns the web and mobile decisions onto one language family. The OIDC Authorization Code + PKCE flow means no client secret in the bundle (satisfying the "no secrets in client bundles" lock), while the backend remains the single source of authorization.

### Consequences

- **Good**: free-tier hosting; no runtime to operate; fast iteration; shared TypeScript with the RN mobile client (ADR-mobile-stack); secrets stay out of the bundle via PKCE (no client secret).
- **Bad**: static hosting means any server-side-only need would require a separate function/API; build output must be deployed through the council's CI/CD path-filters (`web/`), which is one more job lane.
- **Neutral**: TypeScript is a second language alongside the C# backend (a real but accepted trade-off for a web-native UI).

## Pros and cons of the options

### React + TypeScript + Vite (SPA)
- Good: free static hosting; mature OIDC/PKCE library support (e.g. MSAL); huge ecosystem; shares language with mobile RN.
- Bad: separate toolchain/language from .NET backend; needs OpenAPI/BFF contract discipline to avoid drift.

### Blazor (WASM or Server)
- Good: one language (C#) across UI and API.
- Bad: Blazor WASM is heavier to run/load and Blazor Server needs a persistent circuit + SignalR hosting (idle cost, not static); smaller ecosystem for the Procurement UX; poorer fit for a pure static/cheap host.

### Angular + TypeScript
- Good: batteries included (forms, DI, router).
- Bad: heavier framework and learning curve than this slice needs; no cost advantage over React; less aligned with a lean RN-sharing mobile story.

## Implications for the decomposition

- A change to the API surface must regenerate/update a shared OpenAPI/TypeScript client so `web/` and `mobile/` consume one versioned contract — no hand-written divergent DTOs.
- CI/CD must add a `web/` path-filtered job that runs `npm ci`, `npm run build`, and deploys the static output to the per-environment Static Web App.
- The OIDC client registration (Entra ID) exposes only a public-client `client_id` + redirect URI; no secret is ever written into `web/` source or the static bundle.
- `web/` must read the API base URL and OIDC authority from per-environment config (runtime injection), not hard-coded, so the same bundle deploys to `dev` and `demo`.

## Assumptions

- Azure Static Web Apps free tier is available and sufficient in the chosen region (to be confirmed with cloud-architect at council-close).
- Entra ID is available for the `dev`/`demo` tenants and supports the Authorization Code + PKCE public-client flow (confirmed with security-architect).
- A BFF/API-proxy is not required for V1; the SPA calls the API origin directly with CORS scoped to the registered front-end origins.
