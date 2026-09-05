# Wave sequence (delivery-manager-readiness)

Do **not** launch fan-out from `contigo-readiness-process.yaml`.

```
e05 HITL  →  e09 (schema)  →  e06–e08 (web)  →  e10 (residuals)  →  demo-v*
```

- e05 may still be live in Studio. This process never writes `slice.current.yaml`.
- `MANIFEST.yaml` `e10.previous` is `e09` (HITL stamp). Operator must **also**
  close e06–e08 before launching e10 (documented in READINESS-PROCESS.md).
- CI already: `backend.yml`, `web.yml`, `demo-promote.yml` (ADR-016 reviewers).
- e10 adds: seed job, Foundry/OCR CA env if missing, promote/SWA smoke.
  Not a rewrite of those workflows’ existing jobs.

`VOTE: APPROVE`
