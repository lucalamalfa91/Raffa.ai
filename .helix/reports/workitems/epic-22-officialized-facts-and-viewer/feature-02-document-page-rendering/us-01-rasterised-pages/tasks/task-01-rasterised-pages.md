---
id: E22/F02/US01/T01
type: task
story: us-01-rasterised-pages
wave: w17
status: live
target_repo: raffa-backend
---

# task-01-rasterised-pages — a real renderer in the Worker, per-page keys, and `?page=n`

## Context

**Closes: NW-26.**

Decision row: `reports/architecture/waves/w17.md` — the **NW-26** row
(software-architect and cloud-architect halves, plus cloud round 2) and the
rulings on **OQ-w17-sa-04** and **OQ-w17-ca-02**.

ADRs in force: **ADR-029** clauses 1–7 and its **round-3 footer clauses 1–2**;
**ADR-027** w17 clause 1; **ADR-009** w17 clauses 6 and 8; **ADR-005** w17
§19–§21 and §23–§25; **ADR-017** w17 footer.

Already on `main`, so **not** a dependency to build: the
`IDocumentPreviewRenderer` port (`Application/Preview/IDocumentPreviewRenderer.cs:19`),
its `TryAddSingleton` seam (`Infrastructure/ServiceCollectionExtensions.cs:127`),
the placeholder fallback (`Preview/DocumentPreviewService.cs:53-54`), the
pipeline's preview step (`Extraction/DocumentProcessingPipeline.cs:239-244`),
`document.page_count` (`Migrations/Scripts/documents-contracts.sql:834`) and the
tenant guard (`DocumentStoragePath.EnsureWithinTenant`, `:28-38`).

⚠ **No migration.** `page_count` already exists — that is exactly what gives
`documents-contracts.sql` a single writer this wave (ADR-021 w17 clause 1).
**Do not open** `.github/workflows/backend.yml`.

⚠ **This task writes no contract.** `?page=n` and `pageCount` reach
`web/openapi/raffa-api.v1.json` through `E22/F01/US01/T01` in **phase 2**.

## Coding objective

In `raffa-backend`, make the preview a real render of the document's own pages.

1. **Name the renderer before you build it** (OQ-w17-sa-04 — this is a DoD
   line, see below). Choose a rasteriser; record its **package**, its
   **licence** and its **Linux native-dependency list** (possibly empty) in the
   Definition of done. If that list is **non-empty**, the Dockerfile layer lands
   **in this same task** (step 7). **Splitting the renderer from its image layer
   across two tasks is forbidden** — the second task would be the one that
   discovers the first is broken.
2. **Implement the renderer behind the existing port.** Add the implementation
   beside `Application/Preview/PlaceholderDocumentPreviewRenderer.cs` and
   register it **ahead of** the placeholder at
   `Infrastructure/ServiceCollectionExtensions.cs:127` — the `TryAdd` seam and
   the port do **not** change, and the placeholder stays the honest fallback
   (`DocumentPreviewService.cs:53-54`, `:56-61` already absorbs a throwing
   renderer as "no preview"). Rewrite `Preview/PngImage.cs:22-24`'s comment,
   which states a real raster "is therefore NOT done here".
3. **Render in the Worker, never on the request path** (ADR-029 clause 1). The
   stage runs in `Extraction/DocumentProcessingPipeline.cs` beside the existing
   preview step (`:239-244`), **after admission and independently of extraction
   success** (clause 2): a document whose extraction failed must still be
   viewable — that is precisely the document a human needs to look at, and it is
   what makes NW-73's bulk reprocess reviewable.
4. **Page-by-page, disposing each bitmap** (ADR-005 w17 §19). Both apps run at
   **0.25 vCPU / 0.5 GiB** from a **single shared pair**
   (`infra/modules/containerapps/variables.tf:70-80`), an A4 page at 150 DPI is
   ≈ **8.4 MB per bitmap**, and the Worker runs **`MaxConcurrentCalls = 4`** per
   replica. Never materialise a document's pages as a set: multi-page must
   multiply the **work**, not the **peak**.
5. **Per-page keys, deterministic and replaced in place** (ADR-029 round-3
   clause 1). Add
   `DocumentStoragePath.BuildPreviewPage(TenantId tenantId, EntityId documentId, int page)`
   resolving to `{TenantPrefix(tenantId)}documents/{documentId:D}/preview/page-{n}.png`,
   1-based, derived from **(tenantId, documentId, page) and nothing else** — no
   render id, no timestamp, no content hash, no attempt counter. `BuildPreview`
   (`:20-21`) **stays page 1** so nothing that exists today breaks. The rule is
   already written in the file: `:16-18` states a preview is "a derived
   rendering, **replaced in place** whenever the document is reprocessed".
   **Validate in the builder and throw** (ADR-009 w17 clause 6): `page < 1`
   throws exactly as `Build` does for `versionNumber` (`:42-46`) — four lines
   above. NW-73 reaches this method with **no route, no model binder and no
   404**, so an endpoint-only bound is not a bound. `page` is an **`int`, never
   a string**: `EnsureWithinTenant` is a prefix test with **no
   canonicalisation** (`:32`) and `Sanitize` is applied to `fileName` in `Build`
   and to **nothing** in the preview path.
6. **Reap the surplus** (ADR-029 round-3 clause 2, bounded by ADR-009 w17
   clause 8). In the **same stage** that writes pages 1…N, delete
   `page-{n}.png` for `n > pageCount` for that document. Bound it two ways:
   keys under `{TenantPrefix}documents/{documentId}/preview/` **only** — never
   an enumeration of the *tenant* prefix, which `EnsureWithinTenant` would not
   catch (same tenant, wrong document, guard silent) — and only on a
   **confirmed positive** render result: a null, zero or defaulted `pageCount`
   deletes **nothing**, because "reap everything above 0" is the arithmetic that
   reaps the document. Precedent to copy:
   `DocumentReprocessService.RemoveChunksAsync(tenantId, DocumentSourceType, documentId)`
   (`:99-101`).
7. **The image layer, only if the native list is non-empty** (ADR-005 w17 §20,
   ADR-029 `:121-129`). In `backend/src/Raffa.Worker/Dockerfile`, the `apt-get`
   layer goes **above `USER $APP_UID` (`:34`)** — or the build fails
   permission-denied **inside ACR Tasks** (`backend.yml:130-144`), where no one
   is watching a terminal — **and above `COPY --from=build` (`:31`)**, so it
   lands in a layer shared by every `:<sha>` tag instead of being re-stored per
   commit. The base is `mcr.microsoft.com/dotnet/runtime:10.0` (`:29`), chosen
   over `aspnet` deliberately (`:25-28`): slim Debian, **no fontconfig, no
   freetype, no libjpeg, no libpng, no libgdiplus**. A missing native layer
   fails at **runtime** with `Unable to load shared library`, not at build.
   If the list is **empty**, the image is byte-identical and **no Dockerfile
   change is owed**.
8. **`?page=n` on the route.** In `backend/src/Raffa.Api/DocumentsEndpointExtensions.cs`,
   `GET /api/documents/{id}/preview` (route `:93`, handler `:443-472`, `image/png`
   at `:471`) gains an **optional, 1-based `page`** query parameter bounded by
   the persisted `document.page_count`. Out of range is **404, never a silent
   page 1** — silently serving the wrong page to a citation deep-link is how a
   viewer lies about where a clause came from. Absent `page` keeps today's
   behaviour exactly.
9. **`pageCount` on the document read model.** In
   `backend/src/Raffa.Documents.Contracts/Application/DocumentQueryService.cs`,
   add `PageCount` to the document read model the API serialises. Without it the
   viewer renders exactly one page — the failure that would strand NW-63.
10. **A stated cap, never a silent truncation** (ADR-029 clause 7). Mirror the
    existing per-document page budget (`AiGateway:Ocr:MaxPagesPerDocument`,
    default 300 — `backend/README.md:494`,
    `backend/tests/Raffa.AiGateway.Tests/AiGatewayOcrOptionsTests.cs:16`). Pages
    beyond it are **not rendered and the read model says so**. A viewer that
    stops at page N with no explanation reads as a broken document.

## Parent story AC covered

- AC-1 Opening the preview of a validated PDF shows **that file's first page**, not a placeholder card. (N17)
- AC-2 `?page=2` on a multi-page document returns page 2.
- AC-3 `?page=0`, `?page=-1` and a page above `page_count` return **404**.
- AC-4 The document read model carries **`pageCount`**.
- AC-5 A document whose extraction failed is still viewable; a throwing renderer degrades to "no preview".
- AC-6 A reprocess **overwrites** page objects at the same keys, and a shorter re-render **deletes** `page-{n}.png` for `n > pageCount`.
- AC-7 Pages beyond the stated budget are not rendered and the product says so.
- AC-8 A **20-file batch on `dev`** completes with **zero Worker replica restarts**.
- AC-9 A cross-tenant path still throws, and `BuildPreviewPage(…, 0)` throws rather than building a path.

## Files to create or modify

| Path | Change |
|------|--------|
| `backend/src/Raffa.Documents.Contracts/Application/Preview/IDocumentPreviewRenderer.cs` | modify — the render contract gains the page dimension; the port itself stays the seam |
| `backend/src/Raffa.Documents.Contracts/Application/Preview/PdfPageDocumentPreviewRenderer.cs` | **new** — the real `IDocumentPreviewRenderer`, beside `PlaceholderDocumentPreviewRenderer.cs` in the same folder. Rename the type to match the renderer actually chosen (OQ-w17-sa-04), but **one named file**, not a folder |
| `backend/src/Raffa.Documents.Contracts/Application/Preview/PlaceholderDocumentPreviewRenderer.cs` | modify — stays the fallback; follows the widened port |
| `backend/src/Raffa.Documents.Contracts/Application/Preview/DocumentPreviewService.cs` | modify — store page by page, disposing each bitmap; reap `n > pageCount`; the `:53-54` fallback and the `:56-61` absorb path stay |
| `backend/src/Raffa.Documents.Contracts/Application/Preview/PngImage.cs` | modify — rewrite the `:22-24` comment that says a real raster is not done here |
| `backend/src/Raffa.Documents.Contracts/Infrastructure/ServiceCollectionExtensions.cs` | modify — register the real renderer **ahead of** the placeholder at `:127` |
| `backend/src/Raffa.SharedKernel/Storage/DocumentStoragePath.cs` | modify — add `BuildPreviewPage(tenantId, documentId, page)`; validate `page >= 1` and throw as `Build` does at `:42-46`; `BuildPreview` (`:20-21`) unchanged |
| `backend/src/Raffa.Documents.Contracts/Application/Extraction/DocumentProcessingPipeline.cs` | modify — the rasterisation stage beside `:239-244`, after admission, independent of extraction success |
| `backend/src/Raffa.Api/DocumentsEndpointExtensions.cs` | modify — optional 1-based `?page=n` on `:93`/`:443-472`, bounded by `page_count`, **404** out of range |
| `backend/src/Raffa.Documents.Contracts/Application/DocumentQueryService.cs` | modify — `PageCount` (and the budget flag) on the document read model |
| `backend/src/Raffa.Worker/Dockerfile` | modify **only if** the renderer's native list is non-empty — the `RUN` layer above `USER $APP_UID` (`:34`) and above `COPY --from=build` (`:31`) |
| `backend/src/Raffa.Worker/Raffa.Worker.csproj` | modify — the renderer package reference, if the Worker composes it directly |
| `backend/tests/Raffa.Documents.Contracts.Tests/Preview/DocumentPreviewRenderingTests.cs` | modify — pages render one at a time; a throwing renderer degrades to "no preview"; the reap deletes only `n > pageCount` and only under this document's prefix; `pageCount` null/zero deletes nothing |
| `backend/tests/Raffa.Documents.Contracts.Tests/DocumentStoragePathTests.cs` | modify — `BuildPreviewPage` is deterministic; `page < 1` throws; a cross-tenant path still throws; `BuildPreview` is unchanged |
| `backend/tests/Raffa.Api.Tests/DocumentsV2EndpointTests.cs` | modify — `?page=2` returns page 2; `?page=0` and `page > page_count` return 404; no `page` behaves as today; a second tenant gets nothing |
| `backend/tests/Raffa.Worker.Tests/ExtractionSettlementTests.cs` | modify — the rasterisation stage runs for a document whose extraction failed (this is the file that already exercises a settled-but-failed extraction; the folder holds only `DeployableWorkerTests`, `ExtractionSettlementTests`, `ExtractionTransportSelectionTests` and `RenewalThresholdSchedulerHostedServiceTests`) |

Passata 2 cwd is the per-task git worktree of the product clone. Product code
goes under `infra/`, `backend/`, `web/`, `mobile/` at the worktree root — not
`workspace/<repo>/` (PROCESS.md D1).

## Context the implementer needs

- **Architecture decisions in force**:
  - **ADR-029 clause 1** — a synchronous rasterise inside
    `GET /api/documents/{id}/preview` is **forbidden by this ADR**: it puts
    unbounded CPU on the request path.
  - **ADR-029 clause 5** — out of range is **404, never a silent page 1**, and
    **no clamp**. The client is separately forbidden from recreating the clamp
    (ADR-012 w17 §47).
  - **ADR-029 round-3 clause 1, and why it could not be left implicit** — w17
    ships this renderer **and** a bulk whole-tenant re-render (NW-73) in the
    **same wave**. `infra/modules/storage` has **no `management_policy`, no
    lifecycle rule, no `delete_retention`, no versioning** (verified: zero
    matches across `infra/`), so nothing downstream reclaims a suffixed key —
    one bulk reprocess would **permanently double** the container. Security
    reached the same rule from the other side: a suffixed key leaves a prior
    rendering of a **since-corrected** document readable where nothing reaps it.
    **Cost and correctness land on one rule.** ⚠ This also corrects ADR-029's
    own `:142` ("storage grows per page per document"): under this clause it
    grows **once**, not per render.
  - **ADR-009 w17 clause 8** — `EnsureWithinTenant` advertises itself as the
    "fail-closed guard for the read/**delete** side" (`:24-26`) but is a
    **tenant** prefix test only (`:32`). A reap with the wrong document id, or
    one enumerating the *tenant* prefix, is **legal under that guard**. ⚠ This
    is **not** a cross-tenant defect and must not be written up as one.
  - **ADR-005 w17 §24 — the acceptance-ordering constraint that is not a build
    dependency**: AC-8's **20-file measurement on `dev` runs BEFORE the first
    whole-tenant reprocess**, never after. A bulk run is not a substitute for
    it: `MaxConcurrentCalls = 4` with `min_replicas = 0` scales replicas out and
    multiplies the concurrent bitmaps. This constrains the acceptance walk and
    is written in `reports/audit/w17-hitl.md`.
  - **ADR-005 w17 §21** — a renderer that *throws* is absorbed; one that *OOMs*
    kills the replica and burns deliveries against `max_delivery_count = 8`.
    That is why AC-8 exists.
  - **ADR-017 w17 footer** — Document Intelligence returns **geometry, not
    pixels**. Nothing on the AI account renders a page. If the chosen renderer
    calls an Azure API for the raster, **name the operation** and stop: that is a
    cloud-architect re-pricing, not an implementation detail.
- **Do not touch**: `web/openapi/raffa-api.v1.json` and
  `web/src/api/generated/schema.ts` (`E22/F01/US01/T01`, phase 2);
  `.github/workflows/backend.yml` (ADR-014 w17 clause 6 — no migration here, and
  the CI-YAML set is `E20/F02/US02/T01`'s alone);
  `backend/src/Raffa.Documents.Contracts/Migrations/**` (no migration is owed);
  `backend/src/Raffa.AiGateway/Foundry/Wire/DocumentIntelligenceContracts.cs`
  and `Contracts/AiOcrPage.cs` (ADR-017: `prebuilt-layout` stays uncalled and the
  wire contract is **not** widened this wave);
  `backend/src/Raffa.Api/ContractsEndpointExtensions.cs` (`E21/F01/US01/T01`,
  **this same phase**); anything under `web/src`.

## Definition of done

- [ ] the task records, in the PR body, the **renderer package**, its **licence** and its **Linux native-dependency list** (possibly empty) — OQ-w17-sa-04's binding rule
- [ ] if that list is non-empty, `backend/src/Raffa.Worker/Dockerfile` carries the `RUN` layer **above** line `31` (`COPY --from=build`) and **above** line `34` (`USER $APP_UID`), in **this** commit
- [ ] if that list is empty, `git diff --stat -- backend/src/Raffa.Worker/Dockerfile` is **empty**
- [ ] `dotnet build backend/Raffa.slnx --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Documents.Contracts.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Api.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.Worker.Tests --configuration Release` exits 0
- [ ] `dotnet test backend/tests/Raffa.ArchitectureTests --configuration Release` exits 0
- [ ] `git diff --stat -- .github/workflows/ backend/src/Raffa.Documents.Contracts/Migrations/` is **empty** for this task's commits
- [ ] `grep -n "PREVIEW NOT RENDERED" backend/src/Raffa.Documents.Contracts/Application/Preview/PlaceholderDocumentPreviewRenderer.cs` still returns the fallback's own caption (the placeholder is **kept**, not deleted)
- [ ] **the render loop renders and stores ONE page at a time, disposing each bitmap before the next is decoded, and never materialises a document's pages as a set** — ADR-005 w17 §19/§23, copied here as this task's own words because the engineer writing the loop reads ADR-029, which does not carry it. At 0.25 vCPU / 0.5 GiB shared between API and Worker (`infra/modules/containerapps/variables.tf:70-80`) and `MaxConcurrentCalls = 4` per replica, an A4 page at 150 DPI is ≈ 8.4 MB: a set-at-once render OOMs the replica and burns deliveries against `max_delivery_count = 8`. Multi-page must multiply the **work**, never the **peak**
- [ ] `grep -rn "page-1.png" backend/src/Raffa.SharedKernel/Storage/DocumentStoragePath.cs` shows `BuildPreview` unchanged
- [ ] `grep -rn "DateTime\|Guid.NewGuid\|Ticks\|attempt" backend/src/Raffa.SharedKernel/Storage/DocumentStoragePath.cs` shows **no** discriminator inside `BuildPreviewPage`
- [ ] **operator/acceptance** — a 20-file batch on `dev` completes with zero Worker replica restarts, run **before** any whole-tenant reprocess

## Tests required

| Level | What it proves | Where |
|-------|----------------|-------|
| unit | `BuildPreviewPage` is deterministic for the same `(tenant, document, page)`; `page < 1` throws; a cross-tenant path throws; `BuildPreview` still resolves to `page-1.png` | `backend/tests/Raffa.Documents.Contracts.Tests/DocumentStoragePathTests.cs` |
| unit | the service stores page by page and never holds more than one bitmap; a throwing renderer leaves `PreviewPath` unset rather than failing the document; the reap deletes `page-{n}.png` only for `n > pageCount` and only under this document's prefix; a null or zero `pageCount` deletes **nothing** | `backend/tests/Raffa.Documents.Contracts.Tests/Preview/DocumentPreviewRenderingTests.cs` |
| integration | `?page=2` returns page 2; `?page=0` and `page > page_count` return **404**; no `page` behaves exactly as today; a second tenant gets nothing | `backend/tests/Raffa.Api.Tests/DocumentsV2EndpointTests.cs` |
| integration | the document read model carries `pageCount`, and the budget flag is set when the document exceeds `AiGateway:Ocr:MaxPagesPerDocument` | `backend/tests/Raffa.Api.Tests/DocumentsV2EndpointTests.cs` |
| unit | the rasterisation stage runs for a document whose extraction failed | `backend/tests/Raffa.Worker.Tests/ExtractionSettlementTests.cs` |

## Open questions blocking this task

- **none blocking.** OQ-w17-sa-04 is ruled as the **DoD rule above** rather than
  a package name — this council will not name a renderer without verifying its
  licence and native-asset set. OQ-w17-ca-02's contingency (split
  `worker_cpu`/`worker_memory`, raise the **Worker only** to 0.5 vCPU / 1.0 GiB)
  is **pre-authorised with a named ceiling**; it is an infra act triggered by
  AC-8, **not** a change this task makes.

## Wave-spec entry

```yaml
- id: E22/F02/US01/T01
  prompt: reports/workitems/epic-22-officialized-facts-and-viewer/feature-02-document-page-rendering/us-01-rasterised-pages/tasks/task-01-rasterised-pages.md
  produces: [document-page-render]
  depends_on: []
  effort: L
  layer: backend
  status: live
```
