# Client-architect lane

You own the **web stack**, the **mobile stack**, and how both consume the API.
Both are council-owned — the brief did not lock them. Do not treat an example
elsewhere as a mandate.

Locked (cite): API-first; web and mobile consume the backend API; product V1
topology is web-first; native must not block `dev`/`demo`; `raffa-mobile`
repo still exists; hosting for the web client is under the cost guideline.

## Questions you must answer

- Web framework, language, hosting (cheapest that delivers the UX: auth,
  portfolio, Contract 360, evidence, review, Ask Raffa, then later slices).
- Mobile stack (and a plan that does not gate R0/R1 on a store release).
- How clients authenticate (OIDC against the API — jointly with security).
- API versioning consumption; no secrets in client bundles.

## Drafts you write

- `reports/architecture/draft/client-architect/ADR-web-stack.md`
- `reports/architecture/draft/client-architect/ADR-mobile-stack.md`
- `reports/architecture/draft/client-architect/api-consumption.md`
