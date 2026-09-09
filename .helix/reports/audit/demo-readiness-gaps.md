# Demo-readiness gaps — V1 Day-1 on Azure `demo`

Audit of everything **defined through wave 9** against the §20 oracle on
`demo` (ADR-016 `demo-v*`). Not a product council.

`class` is exactly one of: `BLOCKER` | `WAVE_COVERED` | `E10`.

Columns:

- **defined** — ADR / spec / task exists
- **implemented** — code or workflow in git
- **on-Azure-dev** — observed on `rg-contigo-dev` (2026-09-05 probe)
- **on-Azure-demo** — observed or evidenced on `demo`

## Matrix

| item | defined | implemented | on-Azure-dev | on-Azure-demo | class |
|------|---------|-------------|--------------|---------------|-------|
| Postgres Flexible Server + empty DB | ADR-003/005 | Terraform | `psql-contigo-dev` / `contigo_dev` Ready | designed (`contigo_demo`) | WAVE_COVERED (e01 infra) |
| EF schema + RLS on Azure | ADR-003/009/021 | migrations in git; **not applied** | tables absent (`/health` ≠ schema) | absent | WAVE_COVERED (**e09**) |
| CA `ConnectionStrings__Identity/Documents/Audit/Storage` | ADR-007 | Terraform + live CA | yes | designed | WAVE_COVERED (e01) |
| CA `ConnectionStrings__Renewals` | git Terraform | git only | **missing on live CA** | designed | WAVE_COVERED (**e09 F02**) |
| CA `ConnectionStrings__Savings` / `Quotes` | ADR-021 | not in live CA | **missing** | designed | WAVE_COVERED (**e09 F02**) |
| HCP apply vs git | ADR-007 | VCS workspaces | `contigo-dev` behind git | `contigo-demo` | WAVE_COVERED (**e09**) |
| Quote extraction / assessment API | ADR-001 R4 | e05 wave | image is E04 (`7f29506`) | no | WAVE_COVERED (**e05**) |
| Web design system + shell + members + upload | ADR-018/019/020 | e06 stories | SWA is OIDC scaffold | scaffold | WAVE_COVERED (**e06**) |
| Portfolio / Contract 360 / Ask UI | ADR-018/020 | e07 stories | no | no | WAVE_COVERED (**e07**) |
| Renewals / savings / quote / Home UI | ADR-018/020 | e08 stories | no | no | WAVE_COVERED (**e08**) |
| TS client vs OpenAPI file | ADR-012 | e06 F01; file hand-authored | n/a | n/a | WAVE_COVERED (**e06**) |
| `config.json` injection (API URL + OIDC) | ADR-012 | `web.yml` | SWA exists; values not re-probed | must be demo API | WAVE_COVERED (**e06** / `web.yml`); smoke in e10 |
| CORS SWA↔API | ADR-012 | e01/e06 | not re-probed | unknown | WAVE_COVERED (**e06**) |
| Sign-in (SPA Entra PKCE) | ADR-010/012 | scaffold | SWA present | unpromoted | WAVE_COVERED (**e06**) |
| API JWT (ADR-010 on host) | ADR-010 | **no** — host uses `X-Tenant-Id` | header only | header only | **not BLOCKER** — ADR-022 |
| Fixture benchmark adapter (in-process) | ADR-001 | `FixtureBenchmarkAdapter` + tests | n/a (in-process) | n/a | WAVE_COVERED (e04) |
| Fixture / savings **rows on Flexible Server** | ADR-001 Day-1 savings | tests only | **not seeded** | **not seeded** | **E10** |
| Foundry project + AI Gateway on CA | ADR-004/008 (amended 2026-09-09) | Terraform creates the shared account, per-env project, deployments and RBAC (`infra/modules/foundry`); env vars published when `ai_gateway_wired` | created by the dev root; wired after the live probe | project + deployments on `ai_account_attached = true`; wired with the next `demo-v*` | **E10** → infra 2026-09-09 |
| Document Intelligence (OCR) on CA | ADR-017 (amended 2026-09-09) | native to `aisvc-contigo`; `prebuilt-read` / 2024-11-30 bound via `AiGateway__Models__Ocr__*` | same gate as above | same gate as above | **E10** → infra 2026-09-09 |
| `demo-v*` promotion ever run | ADR-016 | `demo-promote.yml` | n/a | **no evidence** | **E10** |
| Secrets in git | ADR-011 | none found in this audit | KV `postgres-connection` | designed | WAVE_COVERED |
| Swagger UI | — | must stay absent | `/swagger` 404 | n/a | WAVE_COVERED (out of scope) |
| `MigrateAsync` in API host | ADR-021 forbids | must stay absent | n/a | n/a | WAVE_COVERED (**e09**) |

## Classification notes

- **No BLOCKER rows** at authoring time. Operator HITL may promote
  `X-Tenant-Id` to BLOCKER; that would add an Entra-on-API story **after**
  this slice, not inside the authored e10.
- **WAVE_COVERED** items must not be re-decomposed into e10.
- **E10** residuals only: seed job, Foundry/OCR CA wiring, `demo-v*` / SWA
  smoke.

`COUNCIL_FILES_WRITTEN: gaps plus ADR-022`
`COUNCIL_APPROVED: demo-readiness — epic-10 / e10 (e01-e09 untouched)`
