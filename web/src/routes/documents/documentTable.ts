import type { AdmittedDocumentType, DocumentListItemBody } from "../../api/client";
import { getStatusTag, type DocumentStatus, type SemanticTag } from "../../styles/semantics";

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

/** The four statuses a real (server-known) row can render (R-DOC-05: no `Rejected` server-side --
 * rejected files are a wholly separate, session-only concept; see `uploadPipeline.ts
 * #RejectedFileOutcome`). `Uploaded` and `Processing` both fold into `"processing"` -- the row grid
 * (screens-v2.md #3) does not distinguish "queued" from "actively processing" visually, only the
 * stage text underneath the tag does that. */
export type RowStatus = "processing" | "needs_review" | "completed" | "failed";

export function getRowStatus(processingStatus: DocumentListItemBody["processingStatus"]): RowStatus {
  switch (processingStatus) {
    case "Uploaded":
    case "Processing":
      return "processing";
    case "NeedsReview":
      return "needs_review";
    case "Completed":
      return "completed";
    case "Failed":
      return "failed";
  }
}

/** Delegates to `styles/semantics.ts#getStatusTag` (ADR-019's locked mapping) -- never re-derived;
 * `RowStatus`'s four members are each a real `DocumentStatus` value. */
export function getRowStatusTag(status: RowStatus): SemanticTag {
  return getStatusTag(status as DocumentStatus);
}

export type RowActionKind = "review" | "ask" | "quote" | "retry";

export interface RowAction {
  kind: RowActionKind;
  label: string;
}

/**
 * Next-step action per row (`raffa-v2/app.jsx`'s own `docRows` action ternary; screens-v2.md #3
 * "Review N fields / Ask about it / Retry upload"). A `Quote`-typed document is routed to Quote
 * check instead of the review/ask flow at any resolved status -- OQ-askv2-008's own assumption in
 * force: "no automatic Quote record; the result card and Ask route to Quote check (/quotes) where
 * the user uploads the quote" -- there is nothing to review or ask about inside Documents for a
 * Quote, only a hand-off. `null` for a still-processing row (the stage text is the only thing shown
 * there, not an action button -- see `DocumentStatusTable.tsx`).
 */
export function getRowAction(item: Pick<DocumentListItemBody, "processingStatus" | "documentType" | "weakFactCount">): RowAction | null {
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
  if (status === "failed") {
    return { kind: "retry", label: "Retry upload" };
  }
  return null;
}

/** `"Needs your attention"` (default) vs `"All documents"` (`raffa-v2/app.jsx`'s own
 * `attnDocs=docs.filter(d=>d.status!=='completed')` -- everything except `completed`, i.e.
 * processing/needs_review/failed, R-DOC-06). */
export type AttentionFilterValue = "attention" | "all";

export function isAttentionStatus(processingStatus: DocumentListItemBody["processingStatus"]): boolean {
  return processingStatus !== "Completed";
}

export function filterDocumentsByAttention(
  items: readonly DocumentListItemBody[],
  filter: AttentionFilterValue,
): readonly DocumentListItemBody[] {
  return filter === "attention" ? items.filter((item) => isAttentionStatus(item.processingStatus)) : items;
}

/** `raffa-v2/app.jsx`'s own `kbSummary` string: "N documents · M askable[ · K waiting for your
 * review]". */
export function buildKbSummary(items: readonly DocumentListItemBody[]): string {
  const total = items.length;
  const askable = items.filter((item) => item.processingStatus === "Completed").length;
  const needsReview = items.filter((item) => item.processingStatus === "NeedsReview").length;
  const base = `${total} document${total === 1 ? "" : "s"} · ${askable} askable`;
  return needsReview > 0 ? `${base} · ${needsReview} waiting for your review` : base;
}

/** `raffa-v2/app.jsx`'s own `filterHint` ternary, verbatim. */
export function getFilterHint(filter: AttentionFilterValue): string {
  return filter === "attention"
    ? "Completed documents are hidden — they are already askable."
    : "Everything, including validated documents.";
}
