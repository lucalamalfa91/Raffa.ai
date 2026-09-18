import type { AdmittedDocumentType, DocumentListItemBody, DocumentListPageBody } from "../../api/client";
import { getStatusTag, type SemanticTag } from "../../styles/semantics";

/**
 * Pure view-model for the Documents V2 row grid (`raffa-v2/markup.html`'s `docRows`/`kbSummary`/
 * `filterHint`; `raffa-v2/screens-v2.md` #3). Kept separate from `uploadPipeline.ts` (the upload
 * half's own outcome/batch concerns) and `useDocumentsList.ts` (data-fetching/React state) -- the
 * same one-concern-per-file split this folder already established in V1.
 */

/**
 * Task E13/F04/US01/T01's widened admitted-type vocabulary (`web/openapi/raffa-api.v1.json`'s own
 * `listDocuments`/`getDocument` operations): the original 6 plus Quote, Invoice, PriceList, Nda, Dpa.
 * Labels for the 6 original members are quoted verbatim from `ContractDocumentType.cs`'s own doc
 * comment (spec §6.1); the 5 new members follow the same plain-English convention.
 */
export const DOCUMENT_TYPE_LABEL: Record<AdmittedDocumentType, string> = {
  Msa: "MSA",
  OrderForm: "Order Form",
  Sow: "SOW",
  Amendment: "Amendment",
  RenewalLetter: "Renewal Letter",
  Quote: "Quote",
  Invoice: "Invoice",
  PriceList: "Price List",
  Nda: "NDA",
  Dpa: "DPA",
  Other: "Other",
};

export function getDocumentTypeLabel(type: AdmittedDocumentType): string {
  return DOCUMENT_TYPE_LABEL[type];
}

/** Same fixed locale/UTC formatter V1 established (documentTable.ts's own prior header comment has
 * the full reasoning: locale/timezone-independent, so this and its unit tests do not depend on the
 * host/CI runner). */
const UPLOADED_AT_FORMATTER = new Intl.DateTimeFormat("en-GB", {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "UTC",
});

export function formatUploadedAt(createdAt: string): string {
  return UPLOADED_AT_FORMATTER.format(new Date(createdAt));
}

/** R-DOC-09's six real stage names, quoted verbatim (also `raffa-v2/app.jsx`'s own `stageLabels`).
 * `Uploaded`/`Processing` (`DocumentProcessingStatus`) both map to this row family -- see
 * `getRowStatus` below -- `stage` (a free-text field on the wire, not a closed enum; see
 * `web/openapi/raffa-api.v1.json`'s own `listDocuments` operation for why) narrows which of the
 * six is current. */
export const DOCUMENT_PROCESSING_STAGES: readonly string[] = [
  "Uploading",
  "Classifying",
  "OCR / text",
  "Sections & tables",
  "Extracting facts",
  "Validating schema",
];

/** Percent-complete for the inline progress bar (`raffa-v2/app.jsx`: `Math.round(d.stage/6*100)`),
 * derived from the stage's own position in `DOCUMENT_PROCESSING_STAGES` -- never a client timer. An
 * unrecognised or absent stage renders 0%, an honest "just started" reading rather than a guess. */
export function getStagePercent(stage: string | null): number {
  if (stage === null) return 0;
  const index = DOCUMENT_PROCESSING_STAGES.indexOf(stage);
  return index === -1 ? 0 : Math.round(((index + 1) / DOCUMENT_PROCESSING_STAGES.length) * 100);
}

/** The six statuses a real (server-known) row can render. `"rejected"` is task E16/F02/US03/T01's
 * (wave w15, ADR-027 §D6): the content gate now runs on the Worker after the upload has returned,
 * so a file that turns out not to be a contract is a *server row* in `Rejected` with a
 * `rejectionReason`, no longer only the session-local card the synchronous 422 used to produce.
 * `"uploaded"` is task E16/F03/US02/T02's (wave w15, ADR-020 w15 footer 10): `Uploaded` and
 * `Processing` used to fold into one `"processing"` reading -- now `Uploaded` reads "Uploaded", no
 * bar, because that is the state a document is in the instant its row appears (perceived-instant
 * batch), and only `Processing` (the Worker has genuinely claimed the job) earns the stage bar. */
export type RowStatus = "uploaded" | "processing" | "needs_review" | "completed" | "failed" | "rejected";

export function getRowStatus(processingStatus: DocumentListItemBody["processingStatus"]): RowStatus {
  switch (processingStatus) {
    case "Uploaded":
      return "uploaded";
    case "Processing":
      return "processing";
    case "NeedsReview":
      return "needs_review";
    case "Completed":
      return "completed";
    case "Failed":
      return "failed";
    case "Rejected":
      return "rejected";
  }
}

/** Delegates to `styles/semantics.ts#getStatusTag` (ADR-019's locked mapping) -- never re-derived.
 * Every `RowStatus` member is a real `DocumentStatus` value, `"rejected"` included since ADR-019
 * w15 clause 1 (`.tag-outline` "Not added" -- a decision about the file, never `failed`'s accent),
 * so there is no cast here any more and the switch there stays exhaustive under `tsc`. */
export function getRowStatusTag(status: RowStatus): SemanticTag {
  return getStatusTag(status);
}

/** Where a row's filename opens, for every `RowStatus` -- `null` only when there is genuinely
 * nowhere to go yet (`"failed"`, `"rejected"`, or `"completed"` with no contract id resolved).
 * Shared by `DocumentStatusTable.tsx` (the row grid's own `<Link>`) and `documentProgress.ts` (the
 * progress panel opened from an `"uploaded"`/`"processing"` row, task E16/F03/US02/T02, wave w15,
 * ADR-020 w15 footer 11) so the two destinations can never drift apart -- one definition, not two. */
export function getOpenTarget(item: Pick<DocumentListItemBody, "id" | "contractId" | "documentType">, rowStatus: RowStatus): string | null {
  switch (rowStatus) {
    case "needs_review":
      return `/documents?review=${item.id}`;
    case "completed":
      return item.documentType === "Quote" ? "/quotes" : item.contractId !== null ? `/contracts/${item.contractId}` : null;
    case "uploaded":
    case "processing":
      return `/documents?progress=${item.id}`;
    case "failed":
    case "rejected":
      return null;
  }
}

export type RowActionKind = "review" | "ask" | "quote";

export interface RowAction {
  kind: RowActionKind;
  label: string;
}

/** How long an `Uploaded` row may sit with "Processing in the background" before the list
 * automatically calls `POST /api/documents/{id}/reprocess` once. After this the Worker has had a
 * fair chance to claim the job; staying `Uploaded` means the pointer is gone (dead-lettered or
 * never delivered). The table never offers a Retry upload CTA — next-step copy stays informational.
 * The same window applies to a `Processing` row whose stage has not changed: the Worker claimed
 * the job, then went silent (claim held, Service Bus message already completed as claim-lost).
 */
export const STUCK_REPROCESS_AFTER_MS = 3 * 60 * 1000;

/** Full restart cap shared with `ExtractionRequestedHandler.MaxAttempts`. After this many
 * auto-reprocess calls the server marks the row Failed (or the client stops looping if the
 * Worker is still gone). */
export const MAX_STUCK_REPROCESS_ATTEMPTS = 3;

/** True when an `Uploaded` document has sat long enough that the list should fire one auto-reprocess. */
export function isStuckUploaded(
  item: Pick<DocumentListItemBody, "processingStatus" | "createdAt">,
  nowMs: number = Date.now(),
): boolean {
  if (item.processingStatus !== "Uploaded") return false;
  const created = Date.parse(item.createdAt);
  return Number.isFinite(created) && nowMs - created >= STUCK_REPROCESS_AFTER_MS;
}

/** True when a `Processing` row has shown the same stage for `STUCK_REPROCESS_AFTER_MS`.
 * `stageUnchangedSinceMs` is when the client first observed this stage (the list API has no
 * per-stage timestamp; the server hang detector uses job `claimed_at`/`started_at`). */
export function isStuckProcessing(
  item: Pick<DocumentListItemBody, "processingStatus">,
  stageUnchangedSinceMs: number,
  nowMs: number = Date.now(),
): boolean {
  if (item.processingStatus !== "Processing") return false;
  return nowMs - stageUnchangedSinceMs >= STUCK_REPROCESS_AFTER_MS;
}

/**
 * Next-step action per row (`raffa-v2/app.jsx`'s own `docRows` action ternary; screens-v2.md #3
 * "Review N fields / Ask about it"). A `Quote`-typed document is routed to Quote
 * check instead of the review/ask flow at any resolved status -- OQ-askv2-008's own assumption in
 * force: "no automatic Quote record; the result card and Ask route to Quote check (/quotes) where
 * the user uploads the quote" -- there is nothing to review or ask about inside Documents for a
 * Quote, only a hand-off. `null` for a still-processing / uploaded / failed row: the stage or
 * "Processing in the background" sentence is the only thing shown there, not an action button
 * (`DocumentStatusTable.tsx`). A stuck `Uploaded` row is recovered by `useDocumentsList`'s
 * one-shot auto-reprocess after `STUCK_REPROCESS_AFTER_MS`, not by a table CTA.
 */
export function getRowAction(
  item: Pick<DocumentListItemBody, "processingStatus" | "documentType" | "weakFactCount">,
): RowAction | null {
  const status = getRowStatus(item.processingStatus);

  if (item.documentType === "Quote" && (status === "completed" || status === "needs_review")) {
    return { kind: "quote", label: "Open Quote check" };
  }
  if (status === "needs_review") {
    const n = item.weakFactCount;
    return { kind: "review", label: `Review ${n} field${n === 1 ? "" : "s"}` };
  }
  if (status === "completed") {
    return { kind: "ask", label: "Ask about it" };
  }
  return null;
}

/** The three chips (task E16/F03/US01/T01, wave w15). `"attention"` (the default, R-DOC-06) and
 * `"all"` bucket the fetched page client-side; `"rejected"` reads the server's own `status=Rejected`
 * bucket, because `counts.all` excludes `Rejected` and a client bucket would need the page to carry
 * rows "All documents" says it does not contain -- two definitions of one list (ADR-012 w15 §13.5b). */
export type AttentionFilterValue = "attention" | "all" | "rejected";

/** Mirrors the server's `counts.needsAttention` definition -- *not `Completed` and not `Rejected`*
 * (ADR-027 §C9) -- so the chip's number and the rows it filters to are the same set. One definition,
 * two implementations, kept in step by `documentTable.test.ts` rather than by `tsc`: the council
 * replaced the V2 export's own `attnDocs=docs.filter(d=>d.status!=='completed')`, which predates
 * a refused file having a row at all. */
export function isAttentionStatus(processingStatus: DocumentListItemBody["processingStatus"]): boolean {
  return processingStatus !== "Completed" && processingStatus !== "Rejected";
}

/** `"all"` is every row Raffa.ai keeps -- `counts.all`'s own definition, which excludes `Rejected`
 * -- so an unfiltered page's `Rejected` rows are left to the third chip and never counted twice. */
export function filterDocumentsByAttention(
  items: readonly DocumentListItemBody[],
  filter: AttentionFilterValue,
): readonly DocumentListItemBody[] {
  switch (filter) {
    case "attention":
      return items.filter((item) => isAttentionStatus(item.processingStatus));
    case "all":
      return items.filter((item) => item.processingStatus !== "Rejected");
    case "rejected":
      return items.filter((item) => item.processingStatus === "Rejected");
  }
}

/** ADR-027 §D7/§C5/§C9's tenant-wide `counts` object, as `GET /api/documents` returns it. */
export type DocumentCountsBody = DocumentListPageBody["counts"];

/** The header summary line, a function of the server's `counts` and of nothing page-derived
 * (ADR-012 w15 §13.6/§18): "N documents" reads `counts.all`, "K waiting for your review" reads
 * `counts.needsReview`. The V2 export's "M askable" segment is gone: askability is the
 * contract-level number the shell already carries (`contractCount`), and `Completed` documents
 * counted over one page never were it (ADR-027 §C5). Each segment reads exactly one member; none
 * is computed from another (§C9.1). */
export function buildKbSummary(counts: DocumentCountsBody): string {
  const base = `${counts.all} document${counts.all === 1 ? "" : "s"}`;
  return counts.needsReview > 0 ? `${base} · ${counts.needsReview} waiting for your review` : base;
}

/** The chip hints. The `attention` hint is `raffa-v2/app.jsx`'s own `filterHint`, verbatim; the
 * `all` hint says what "All documents" counts -- everything Raffa.ai keeps -- and the third chip's
 * says why its rows are nowhere else (ADR-020 w15 §1.5). */
export function getFilterHint(filter: AttentionFilterValue): string {
  switch (filter) {
    case "attention":
      return "Completed documents are hidden — they are already askable.";
    case "all":
      return "Everything Raffa.ai keeps, including validated documents.";
    case "rejected":
      return "Files Raffa.ai did not keep — never counted, never askable.";
  }
}
