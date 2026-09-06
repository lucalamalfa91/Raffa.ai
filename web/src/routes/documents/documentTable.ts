import type { DocumentType } from "../../api/client";
import { getStatusTag, type SemanticTag } from "../../styles/semantics";
import { getUploadOutcome } from "./uploadPipeline";
import type { TrackedDocument } from "./documentStore";

/**
 * Pure view-model helpers for DocumentStatusTable.tsx (AC-1 "Document table:
 * Document / Type / Supplier / Status / Uploaded", screens.md #3). Kept
 * separate from documentStore.ts (persistence) and uploadPipeline.ts (the
 * upload half's own pacing/outcome helpers), the same one-concern-per-file
 * split this folder already uses.
 */

/**
 * Product-spec §6.1 contract-hierarchy labels, quoted verbatim from
 * `backend/src/Contigo.Documents.Contracts/Domain/ContractDocumentType.cs`'s
 * own doc comment ("MSA / Order Form / Amendment / SOW / Renewal Letter") --
 * not re-derived from the wire enum's PascalCase member names.
 */
const DOCUMENT_TYPE_LABEL: Record<DocumentType, string> = {
  Msa: "MSA",
  OrderForm: "Order Form",
  Amendment: "Amendment",
  Sow: "SOW",
  RenewalLetter: "Renewal Letter",
  Other: "Other",
};

/** "Classifying…" while the `GET /api/documents/{id}` read-back has not resolved yet (see documentStore.ts's own doc comment on `TrackedDocument.documentType`). */
export function getDocumentTypeLabel(documentType: DocumentType | null): string {
  return documentType === null ? "Classifying…" : DOCUMENT_TYPE_LABEL[documentType];
}

/**
 * Status column (AC-2, ADR-019 "Semantic mapping (locked)"): reuses
 * `getUploadOutcome` + `getStatusTag` verbatim -- never re-derived -- the
 * same two functions the upload result card
 * (`UploadResultCard.tsx` via `uploadPipeline.ts#getResultCardContent`)
 * already uses for the same three terminal statuses.
 */
export function getDocumentStatusTag(processingStatus: TrackedDocument["processingStatus"]): SemanticTag {
  return getStatusTag(getUploadOutcome(processingStatus));
}

/**
 * "Uploaded" column formatter. Fixed locale + UTC timezone so the rendered
 * string (and this function's own unit tests) do not depend on the host
 * machine's/CI runner's local timezone or locale -- the compiled
 * `day1-demo.html` bundle is a single ~360KB minified line this task could
 * not read to confirm an exact date format, so this is this task's own
 * reasonable, unambiguous choice, the same kind of flagged copy departure
 * `uploadPipeline.ts#getResultCardContent`'s own comment already makes.
 * Numeric day/month (not `month: "short"`) deliberately -- en-GB's short
 * month name for September is the four-letter "Sept" (confirmed against
 * this runtime's own ICU data), which would be the only four-letter
 * abbreviation next to eleven three-letter ones; DD/MM/YYYY sidesteps that
 * inconsistency and any month-name localisation question entirely.
 */
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
