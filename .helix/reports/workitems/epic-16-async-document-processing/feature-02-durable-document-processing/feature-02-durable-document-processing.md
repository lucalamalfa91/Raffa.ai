---
id: feature-02
type: feature
parent: epic-16
wave: w15
status: active
extends: epic-01 F06 (document ingestion), epic-13 F04 (documents V2 admission gate)
---

# feature-02-durable-document-processing — The upload request stores the file; the Worker does the rest

## Slice

`POST /api/documents` returns **201 once the blob and the rows are durable**,
and the AI pipeline — classify, OCR, sections, extraction, embedding — runs on
`Raffa.Worker` behind a durable Service Bus message. The admission gate
**splits**: format and size stay in the request (nothing is stored for a
non-document), content classification moves to the Worker, and a refused file
becomes a **persistent, visible, terminal `Rejected` row** whose content Raffa
does not keep. `reprocess` takes the same shape. The aggregates that other
screens read gain the server-computed fields that let those screens say "still
processing" instead of guessing.

Three stories in strict order, one phase each, because each needs the previous
one's artifact to exist: the schema the claim depends on, then the transport,
then the endpoint that publishes to it.

## User stories

| ID | Title | Wave |
|----|-------|------|
| us-01 | The schema can hold a claim, an attempt count and a refusal | w15 |
| us-02 | A durable message carries the work to the Worker | w15 |
| us-03 | Upload returns when the file is stored, and every aggregate tells the truth | w15 |

## Architecture decisions in force

- **ADR-027** — the whole contract. §D1–§D12 plus the round-2 (§C1–§C5) and round-3 (§C6–§C11) footers.
- **ADR-002** (w15 footers) — the queue port becomes a durable transport with a real handler; new project **`Raffa.Storage`**; the R0 "no messaging middleware" assumption retires.
- **ADR-009** — the worker's tenant scope comes from the message; the message carries **ids only, never a storage path**; no cross-tenant read and no sweeper.
- **ADR-024** (w15 footer §1–§4) — R-DOC-01/03/09 asynchronous; A7 / `OQ-askv2-007` / R-DOC-05 AC-1 `assumed-wrong`; one definition of *validated*.
- **ADR-011** — Service Bus authenticates with managed identity and RBAC. No secret (OQ-w15-sec-04).
- **ADR-012** (w15 §9) — `counts` and `readiness` land **inline**; the generator recurses and nothing is hand-written in `client.ts` as a DTO.
- **`none — ADR-021`** — `processing_status` is `character varying(30)` with no CHECK (`documents-contracts.sql:110`); `Rejected` is code-only, so neither `backend.yml` array moves.
- **`none — ADR-004 / ADR-017`** — the five model roles and the gateway boundary do not change; only the host that calls `ocr` and `classify`.

## Target repo

`raffa-backend`, plus `web/openapi/raffa-api.v1.json` and the generated client
in us-03 (the contract is published from the backend side).
