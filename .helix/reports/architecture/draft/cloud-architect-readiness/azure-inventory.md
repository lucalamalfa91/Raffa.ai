# Azure inventory (cloud-architect-readiness)

Probed `dev` (2026-09-05) vs designed `demo` (ADR-005/006/007/008/016).

| Resource | `dev` | `demo` | Note |
|----------|-------|--------|------|
| RG | `rg-raffa-dev` present | `rg-raffa-demo` designed | do not laptop-apply |
| Postgres | `psql-raffa-dev` / `raffa_dev` Ready | `raffa_demo` designed | schema apply = **e09** |
| CA API | `ca-raffa-dev-api`, `/health` 200, image `7f29506` | designed | missing Savings/Quotes conn = **e09 F02** |
| CA worker | present on `dev` | designed | same env-var gap |
| ACR / SWA / KV | present on `dev` | designed | SWA `config.json` per `web.yml` |
| Foundry project | ADR-008 `raffa-dev` | `raffa-demo` | Terraform identity hooks; **CA env for gateway/OCR not confirmed** → E10 |
| Document Intelligence | ADR-017 hook | same | **not confirmed on live CA** → E10 |
| HCP | `raffa-dev` behind git | `raffa-demo` | apply = **e09**, not e10 |
| `demo-v*` | n/a | **no evidence a tag has run** | E10 runbook/smoke |

Firewall on Flexible Server is AllowAzureServices only (no laptop `psql`).

`VOTE: APPROVE`
