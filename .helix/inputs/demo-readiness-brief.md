# Demo-readiness brief — V1 Day-1 on Azure `demo`

Operator brief for **`contigo-readiness-process.yaml`**. Audit whether
everything **defined through wave 9** is enough to open `demo` and walk
product-spec §20 without errors. Then decompose **only residuals** into e10.

Do **not** re-open ADR-001…021. Do **not** rewrite e01–e09 or
`slice.current.yaml`. Do **not** add Swagger UI.

## Oracle (enough = this path on `demo`)

From spec Day-1 / E08 final-integration, **in the browser**, after `demo-v*`
(ADR-016):

sign-in → invite Procurement → upload contracts → extract → Contract 360 →
Ask (citations + one abstain) → renewal action → savings (fixture) →
quote check → record outcome → Home realized.

`/health` on `dev` is **not** enough.

## Already covered — do not put in e10

| Gap | Covered by |
|-----|------------|
| EF schema never applied to Azure Postgres | **e09** (ADR-021) |
| CA missing `ConnectionStrings__Savings` / `Quotes`; HCP behind git | **e09 F02** |
| Web is OIDC scaffold only | **e06–e08** |
| Quote extraction / assessment API | **e05** (live or next) |

## Known residuals (confirm in the gap matrix; these are e10 candidates)

- Fixture benchmark + savings **seed** on Flexible Server `contigo_demo` (today
  only Testcontainers / in-process).
- Foundry project `contigo-demo` + Document Intelligence (OCR) **wired into
  Container Apps** (Terraform has identity hooks; live CA env list on `dev`
  showed only a subset of connection strings, no confirmed AI/OCR vars).
- No evidence a `demo-v*` promotion has ever run; SWA `config.json` must
  point at demo API + Entra public client.
- API host still takes `X-Tenant-Id` (ADR-010 JWT not on the host). Decide
  if Day-1 stakeholder demo may keep the header (proposed: **yes**, lock in
  ADR-022) or Entra on the API is a BLOCKER.

## Gap report contract

Write `reports/audit/demo-readiness-gaps.md` with one row per item:

`item | defined | implemented | on-Azure-dev | on-Azure-demo | class`

`class` is exactly one of: `BLOCKER` | `WAVE_COVERED` | `E10`.

## Wave placement

New work is **epic-10 / slice e10**, append-only. Fan-out only after HITL
on the gap report **and** e05 idle:

```
e05 HITL → e09 → e06–e08 → e10 → demo-v*
```
