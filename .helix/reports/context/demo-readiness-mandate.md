# Demo-readiness mandate

Copied from `inputs/demo-readiness-brief.md` for later seats. Delta only.

- ADR-001…021 and epic-01…09 are **done** (defined). Do not rewrite them.
- New work is **epic-10 / slice e10** — residuals only.
- Never write `wave-spec.execution.yaml`, `wave-spec.web.yaml`,
  `wave-spec.schema.yaml`, `slices/e01.yaml`–`e09.yaml`, or
  `slice.current.yaml` (e05 may be live on the other artifact).
- Oracle: product-spec Day-1 / §20 **in the browser on Azure `demo`** after
  `demo-v*` (ADR-016). `/health` on `dev` is not enough.
- Already covered — do **not** put in e10:
  - schema on Azure Postgres → **e09**
  - Savings/Quotes CA connection strings + HCP apply → **e09 F02**
  - web screens / TS client / shell → **e06–e08**
  - quote extraction API → **e05**
- e10 candidates: fixture seed on `raffa_demo`, Foundry/OCR CA wiring if
  live inventory is empty, `demo-v*` / SWA config smoke.
- Day-1 auth: propose ADR-022 (`X-Tenant-Id` acceptable until ADR-010 is on
  the API host). Not a BLOCKER unless the operator overrides at HITL.
- No Swagger UI. No `--fresh`. No fan-out from this artifact.

`CONTEXT_READY: demo-readiness-mandate`
