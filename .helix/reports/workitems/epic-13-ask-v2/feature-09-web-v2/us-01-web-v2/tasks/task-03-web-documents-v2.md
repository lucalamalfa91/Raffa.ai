---
id: E13/F09/US01/T03
type: task
story: us-01-web-v2
wave: 13
status: live
target_repo: contigo-web
---

# task-03-web-documents-v2 — Multi-file, Not added, attention filter, real stages, review state; OpenAPI + client regen (documents)

## Coding objective

Rebuild `/documents` to screen 3 of `inputs/design/prototypes/contigo-v2/screens-v2.md`
from `inputs/design/prototypes/Contigo V2 Prototype.html` — search the
unpacked `contigo-v2/markup.html` for **"First your contracts. Then your
questions."**, **"Nothing needs you right now."**, **"All documents ·"**,
**"is now askable."** and `contigo-v2/app.jsx` for `docsEmpty`, `docRows`,
`stageLabels`, `filterAttn` / `filterAll`, `filterHint`, `kbSummary`,
`justValidated`, `askValidated`, `pickFiles` / `startUpload`. First,
document the phase-2 endpoints in `web/openapi/contigo-api.v1.json`
exactly as `inputs/requirements.md` §6 and the backend tests define them
(`GET /api/documents`, `GET /api/documents/{id}/preview`,
`POST /api/documents/{id}/reprocess`, `DELETE /api/documents/{id}`, the
413 / 415 / 422 upload responses and the widened `documentType` enum)
and run `npm run generate:api`; extend `web/src/api/client.ts` with
`listDocuments`, `getDocumentPreviewUrl`, `reprocessDocument`,
`deleteDocument` (this task is the phase-3 writer of the contract and
the client). Then: onboarding empty state (three steps 01 · Upload / 02 ·
Review / 03 · Ask with the prototype copy; dropzone "Upload contracts" /
"or drop files anywhere in this box"); strip PDF · DOCX · XLSX · PNG · JPG,
50 MB / file; **multi-file** drop / pick (`accept` widened, up to 20
files, uploads run with ≤ 3 in flight); one row per file from the moment
it is picked; per-file outcome cards: completed / needs_review / failed
(existing copy) and the new **Not added** card for 422 with the reason
mapped to the requirements copy ("Not added: this looks like a recipe,
not a contract. Contigo only keeps contracts, order forms, quotes and the
documents around them. Drop the signed agreement or the supplier's
proposal." / for `no_readable_text`: "Not added: Contigo could not read
any contract text in this file. Try a clearer scan or the original PDF.")
and for 415 the format message — rejected files are session-only, never
counted in the summary; a Quote outcome card offers "Open Quote check" →
`/quotes`. The list comes from `GET /api/documents` (remove
`documentStore.ts` / `sessionStorage`), default filter **Needs your
attention** (processing / needs_review / failed) with the "All documents ·
N" toggle and the prototype hints; rows show Document · Supplier
(`supplierName`) · Type · Status · action (**Review N fields** →
`/documents?review=<id>`; **Ask about it** → `/ask?scope=<contractId>`
(a new chat; the Ask route handles the param in F09/T04); **Retry
upload**); processing rows show the real `stage` from the API polled every
2 s until terminal (drop the client-side ticker in `uploadPipeline.ts`).
Review as a **state of Documents**: `/documents?review=<documentId>`
renders the existing review components (`routes/contracts/review/*`) in
place with "← Documents" and, on **Mark as validated**, returns to the
list showing the "*X* is now askable." hook with "Ask: when does it
expire?" (→ `/ask` new chat with that question). Admin sees Delete on a
row; Procurement does not.

## Parent story AC covered
- AC-4, AC-6 (documents part)

## Files to create or modify
| Path | Change |
|------|--------|
| `web/openapi/contigo-api.v1.json` | documents V2 endpoints + responses + enum (phase-3 writer) |
| `web/src/api/generated/schema.ts`, `web/src/api/client.ts` | regenerate + new methods (phase-3 writer) |
| `web/src/routes/documents/index.tsx`, `UploadDropzone.tsx`, `UploadResultCard.tsx`, `DocumentStatusTable.tsx`, `ProcessingPipeline.tsx`, `documentTable.ts`, `uploadPipeline.ts`, `documents.css` | V2 behaviour + copy |
| `web/src/routes/documents/OnboardingEmptyState.tsx`, `AttentionFilter.tsx`, `ReviewState.tsx`, `useDocumentsList.ts` | new |
| `web/src/routes/documents/documentStore.ts`, `sampleDocument.ts` | remove store; keep sample only if the prototype's "sample file" stays |
| `web/tests/routes/documents/*`, `web/tests/api/client.test.ts` | updated / new tests |

## Context the implementer needs
- **Design**: `inputs/design/prototypes/Contigo V2 Prototype.html`; unpacked anchors above; `contigo-v2/styles.css`; `contigo-v2/screens-v2.md` §3 and §4; requirements R-DOC-01/04/05/06/09, R-WEB-05, R-WEB-07 (`inputs/requirements.md`). Divergence: Procurement can upload (D8).
- **Architecture decisions in force**: ADR-024, ADR-018 / ADR-020 (amended), ADR-019 (tokens), ADR-012 (one generated client).
- Gaps G-DOCS-V2-UI, G-ADMISSION (web side), G-DOC-API (web side).
- **Do not touch**: `web/src/routes/ask/**`, `components/shell/**`, `routes/contracts/contract360/**` (F10/T01 this phase), `routes/contracts/review/*` internals (reuse as-is).

## Definition of done
- [ ] `npm run generate:api` in `web/` produces no diff after commit; `npm run build` exit 0
- [ ] `npm test` in `web/` exit 0 — three files dropped → three rows; a 422 `not_a_contract` → Not added card with the requirements copy and the summary count unchanged; a 415 → format message; attention filter hides completed rows and shows "Nothing needs you right now." when empty; stage text comes from the mocked API; `?review=<id>` renders the review state; Mark as validated → "is now askable." hook; Delete visible for admin only

## Tests required
| Level | What it proves | Where |
|-------|----------------|-------|
| unit | multi-file, outcomes, filter, stages, review state, roles | `web/tests/routes/documents/*` |
| unit | client methods against the contract | `web/tests/api/client.test.ts` |

## Open questions blocking this task
- OQ-askv2-008 — Quote admitted → route to Quote check (assumed)

## Wave-spec entry
```yaml
- id: E13/F09/US01/T03
  prompt: reports/workitems/epic-13-ask-v2/feature-09-web-v2/us-01-web-v2/tasks/task-03-web-documents-v2.md
  produces: [web-documents-v2]
  depends_on: [documents-v2-api, web-shell-v2]
  effort: L
  layer: web
  status: live
```
