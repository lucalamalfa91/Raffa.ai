# ADR-017 — OCR in V1 (Document Intelligence behind the AI Gateway)

- **Status**: accepted
- **Date**: 2026-09-01
- **Deciders**: software-architect (pipeline / gateway role), cloud-architect (SKU / account), product-owner (V1 scope); security-architect reconciles no-training and isolation at council-close
- **Locked citations**: AI — Microsoft Foundry only, via Contigo AI Gateway; domain modules never call a provider directly; cheapest models that still meet the tasks. Brief §8: full contract documents must be processed (not a 2-page-only path for real MSAs); OCR vs native parse was council-owned — this ADR answers it. Cost — free/cheapest SKUs. Spec §7.1 pipeline ("Native Text Extraction / OCR if required"); spec §13.3 background job "OCR and document parsing"; spec §4 upload of PDF/DOCX/XLSX.

## Context and problem statement

Contigo's Day-1 path on `demo` is upload → async extract → review → Ask Contigo with citations (brief §13). Real MSAs, order forms, and quote PDFs are frequently scanned or image-only. A native PDF-text parser alone cannot process those documents, and the brief forbids a 2-page-only path for real MSAs.

ADR-004 previously left "OCR vs native document parse" as an open CQ-008 sub-item and assumed native parse might be enough, with Document Intelligence added later *if* it was not. That assumption would silently drop scanned contracts out of V1 and break the Day-1 promise and spec §17.1 ("100 contracts processable").

This ADR locks OCR as a **V1 capability**, not a later wave, and names how it sits behind the AI Gateway.

## Decision drivers

- **Day-1 / R1 sufficiency**: scanned and image-based contracts must extract on `dev`/`demo` in V1 (spec §7, §16 R1, §20).
- **Full document**: entire MSA/quote, not the first two pages (brief §8).
- **Gateway isolation**: domain modules never call Azure AI Document Intelligence (or any OCR provider) directly; all model I/O goes through the AI Gateway (locked AI row).
- **Cost**: pay-per-page, no idle-expensive SKU; native text parse stays the cheap path when the file already has extractable text.
- **Evidence**: OCR output must preserve page identity so source spans (spec §7.2, Appendix C #2) still resolve to the original page.

## Considered options

1. **OCR in V1, hybrid parse** — native text extraction when the file has sufficient extractable text; Azure AI Document Intelligence (`prebuilt-read` / `prebuilt-layout`) when the page is scanned, image-based, or text-poor, or when section/table layout is required. Both paths are implemented and provisioned in V1, invoked only through the AI Gateway, and process the **full** document.
2. **Native-parse only in V1, OCR later** — ship digital-PDF text extract in R1; add Document Intelligence in a post-V1 wave if scanned MSAs appear.
3. **Always OCR every page, no native parse** — one path (Document Intelligence Layout on every page) from the first extraction job.

## Decision outcome

**Chosen: Option 1** — OCR is **in V1**, from the R1 extraction slice, not deferred. The worker runs a hybrid pre-pass **behind the AI Gateway**: native text for born-digital PDF/DOCX/XLSX with sufficient extractable text; Azure AI Document Intelligence Read (OCR) and Layout (sections/tables) for scanned, image, or low-text pages and for table/section detection the native parser cannot do. There is no 2-page cap. Domain code never references a Document Intelligence SDK.

This supersedes the ADR-004 assumption that native PDF text "handles full MSAs" and the CQ-008 sub-item "OCR vs native document parse."

### Gateway role

The AI Gateway (ADR-004) gains a fifth role, **`ocr`**, bound through configuration like the other roles:

| Role | Provider surface | Requirement |
| --- | --- | --- |
| `ocr` | Azure AI Document Intelligence on the ADR-008 AI services account (`prebuilt-read`, `prebuilt-layout`) | Full-document text + page map + layout/tables; cheapest Read/Layout that meets evidence (page/section) |

Exact model IDs and per-page prices are confirmed in `northeurope` at implementation time (same rule as ADR-004 / ADR-006) and recorded next to the other Foundry IDs. Until the live endpoint is wired, a fixture OCR adapter is acceptable for R0 scaffolding only — **R1 extraction on `demo` must use the real OCR path for at least one scanned/image fixture.**

### Placement vs Foundry models

OCR is not a chat/completions model. It still counts as AI I/O: it rides the same Azure AI services account and per-environment Foundry project connections (`contigo-dev` / `contigo-demo`, ADR-008), uses managed identity + Key Vault (ADR-011), and logs resource/model id, version, timestamp, and input hash (brief §8) plus **page count** so OCR spend is observable (Appendix C rule 8). Customer contract bytes must not train public/shared models (same no-training rule as Foundry chat/embed).

### Consequences

- **Good**: scanned MSAs and image quotes work on the first `demo`; Day-1 and spec §17.1 stay honest; native parse keeps born-digital cost low; one gateway keeps domain modules provider-free.
- **Bad**: a fifth gateway binding and a pay-per-page line on the AI services bill; runaway OCR on huge portfolios must be metered (log page count, fail or pause on configured page-budget).
- **Neutral**: hybrid routing (native vs OCR vs Layout) is an implementation detail of the gateway/worker, not a second provider; swapping Read/Layout versions remains config.

## Pros and cons of the options

### Option 1 — OCR in V1, hybrid
- Good: meets brief §8 (full documents) and Day-1; cheapest path per page type; stays behind the gateway.
- Bad: two parse implementations to test (native + OCR); routing rules must be explicit (text-density / file-type / table need).

### Option 2 — native only, OCR later
- Good: lower V1 bill and fewer Azure surfaces.
- Bad: scanned/image MSAs fail or look "processed" with empty text; violates brief §8 and the 100-contract acceptance bar; silently re-opens CQ-008.

### Option 3 — always OCR
- Good: one code path; Layout tables are consistent.
- Bad: pays per-page OCR on every born-digital PDF; violates "cheapest that still meets the task" when native text is already sufficient.

## Implications for the decomposition

- R1 extraction tasks MUST implement the `ocr` AI Gateway role and the hybrid pre-pass. A task that ships "native PDF text only" is incomplete for V1.
- Domain modules (Documents/Contracts) MUST call `IAiGateway.Ocr` / parse abstractions only — never `Azure.AI.DocumentIntelligence` or a REST client to Document Intelligence.
- Every OCR call MUST process the full document (page through all pages) and MUST persist a page map so evidence `source.page` / section still resolve.
- Infra/bootstrap MUST expose a Document Intelligence endpoint per environment on the existing pay-as-you-go Azure AI services account (ADR-008). Do not add a second AI subscription. Do not add an idle-expensive dedicated cluster.
- `dev` and `demo` MUST use distinct project/connection endpoints (same isolation as Foundry chat/embed). They must not share document bytes.
- Per-page OCR usage MUST be logged (page count, model id, cost attribution) from the first R1 wiring task.
- A 2-page or "first pages only" limiter MUST NOT ship for real MSAs. A configured safety budget (max pages per job / per tenant) is allowed so a runaway scan cannot blow the `demo` bill; over-budget jobs fail visibly (`failed` status), they are not silently truncated.
- Fixture OCR is allowed for R0. R1 `demo` acceptance MUST include at least one scanned or image-based contract that extracts via Document Intelligence.

## Assumptions

- Azure AI Document Intelligence Read and Layout (`prebuilt-read`, `prebuilt-layout`) are available in `northeurope` on the same AI services account as Foundry (ADR-006, ADR-008). Confirm ID/price at implementation time.
- S0 / pay-per-page is the cheapest SKU that can process a 100-contract Day-1 portfolio; F0 free-tier page caps are insufficient for `demo` and are not the V1 SKU.
- Native libraries for PDF/DOCX/XLSX text exist for the ASP.NET worker and are good enough for born-digital files; OCR is the backstop, not a replacement for those formats.

## Amendment (2026-09-09, Read for every PDF and image; page map from spans)

The hybrid rule is narrowed. The worker sends **every PDF and every image
upload (PNG/JPG) to Azure AI Document Intelligence `prebuilt-read`**
(api-version 2024-11-30) on the shared account; native text extraction is
used **only for DOCX and XLSX** (OpenXml). Reasons: the interim "native" PDF
path was a hand-written content-stream scanner (no PDF library) that could
not read real supplier PDFs (CID fonts, hex strings, object streams) and
routed them to a fixture OCR that produced no text; Read returns a digital
PDF's embedded text at the same per-page price with a uniform page map; one
path is cheaper to prove than two. The last Assumption above ("native
libraries ... good enough for born-digital files") is withdrawn for PDF.

The page map is derived from the analyze result's `pages[].spans` over the
concatenated `content` (`stringIndexType=utf16CodeUnit`), so `source.page`
keeps resolving; the response carries no page delimiter of its own.
`prebuilt-layout` stays available on the account and is not called in V1;
the Gateway-role table's requirement reads "full-document text + page map"
(layout/tables deferred). The `ocr` role binds `AiGateway:Models:Ocr` =
`prebuilt-read` / `2024-11-30` from Terraform (`modules/foundry` output
`model_env`) like the other four roles. The per-document page budget
(`AiGateway:Ocr:MaxPagesPerDocument`) and page-count logging are unchanged.
The account and the per-environment projects are Terraform-managed
(ADR-008 amendment, same date); the "per-project connection" is an
informational header value, not a resource. The fixture gateway keeps a copy
of the retired scanner so CI stays provider-free.
