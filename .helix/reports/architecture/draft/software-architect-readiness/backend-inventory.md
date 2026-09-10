# Backend inventory (software-architect-readiness)

- E01–E04 modules are in `backend/` and covered by Testcontainers tests.
- E05 (quotes) is the live/next backend wave — not an e10 residual.
- OpenAPI is the hand-authored file `web/openapi/raffa-api.v1.json`. The
  API host does **not** publish Swagger UI (correct; out of scope).
- `Raffa.Api` must not call `MigrateAsync()` (ADR-021 / e09).
- AI Gateway defaults to `FixtureAiGateway` until a Foundry-backed
  implementation is registered (ADR-004). Live CA env on `dev` did not show
  AI Gateway / Foundry / Document Intelligence settings.
- Benchmark / savings fixture adapter is in-process for tests. There is **no**
  checked-in job that seeds `raffa_demo` (or `raffa_dev`).
- App tenancy header remains `X-Tenant-Id` (ADR-010 JWT not on the host).

e10 residuals: fixture seed job; Foundry/OCR wiring if cloud lane confirms
empty CA env. Do not regenerate EF scripts (e09) or add screens (e06–e08).

`VOTE: APPROVE`
