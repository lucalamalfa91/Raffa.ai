import type { ApiClient, RejectedUploadBody, UploadDocumentResult } from "../../api/client";

/**
 * Upload-outcome concerns for Documents V2 (task E13/F09/US01/T03, screens-v2.md #3; requirements
 * R-DOC-01/02/04). V1's client-side pacing ticker (`PIPELINE_STAGE_LABELS`/`PIPELINE_STEP_INTERVAL_MS`
 * /`getPipelineStageViews`) is gone -- the real stage now comes from `GET /api/documents` (R-DOC-09;
 * see `../../api/client.ts#DocumentProcessingStage`, `documentTable.ts#DOCUMENT_PROCESSING_STAGES`),
 * polled by `useDocumentsList.ts`, not simulated here. This file now owns: multi-file upload limits
 * (R-DOC-01), the concurrency-capped batch runner (up to 20 files, <=3 in flight), and mapping a
 * rejected/failed `uploadDocument` result onto the requirements' own "Not added" copy.
 */

/** R-DOC-01: "batch <= 20 files". */
export const MAX_FILES_PER_BATCH = 20;

/** R-DOC-01: "<= 50 MB per file" (`Documents:MaxFileBytes`, task-01-documents-admission.md). Checked
 * client-side too, purely to avoid a doomed round trip for an obviously oversized file -- the server
 * (413) stays authoritative; see `useDocumentsList.ts#startUploadBatch`. */
export const MAX_FILE_BYTES = 50 * 1024 * 1024;

/** Task's own coding objective: "uploads run with <= 3 in flight." */
export const MAX_CONCURRENT_UPLOADS = 3;

/** Widened accept list (PDF/DOCX/XLSX plus PNG/JPG via OCR, D7/ADR-017) -- a soft, OS-level filter
 * only; the server's 415 (extension + magic-byte sniffing) stays authoritative (R-DOC-02 AC-1). */
export const ACCEPTED_EXTENSIONS = ".pdf,.docx,.xlsx,.png,.jpg,.jpeg";

export function isOversized(file: File): boolean {
  return file.size > MAX_FILE_BYTES;
}

/**
 * R-DOC-04's own verbatim rejection copy, keyed by the admission gate's `reason` (ADR-024 §6). The
 * backend's own `hint` field (a shorter fragment, e.g. "Raffa only keeps contracts, order forms,
 * quotes and the documents around them.") is the tail of the `not_a_contract` sentence below, not a
 * substitute for it -- this app owns the full, exact requirements sentence so it is testable without
 * trusting a body a backend task authored independently.
 */
export function getRejectionReasonCopy(reason: RejectedUploadBody["reason"]): string {
  switch (reason) {
    case "not_a_contract":
      return "Not added: this looks like a recipe, not a contract. Raffa.ai only keeps contracts, order forms, quotes and the documents around them. Drop the signed agreement or the supplier's proposal.";
    case "no_readable_text":
      return "Not added: Raffa.ai could not read any contract text in this file. Try a clearer scan or the original PDF.";
  }
}

/** A file this browser refused before ever calling the API (oversized). Same "Not added" family as a
 * server 422/415, kept out of `RejectedUploadBody`'s own shape since no HTTP call happened. */
export function getOversizedCopy(fileName: string): string {
  return `Not added: ${fileName} is larger than 50 MB. Raffa.ai accepts files up to 50 MB.`;
}

/** One rejected/refused file this session (R-DOC-04: "shown for the current session only ... never
 * counted in 'documents' or 'askable'"). Never persisted, never sent to the server as its own entity. */
export interface RejectedFileOutcome {
  /** Local-only key (`crypto.randomUUID()`), not a server id -- rejected files have none (R-DOC-05 AC-1). */
  key: string;
  fileName: string;
  message: string;
}

/** A file selected/dropped this session, tracked locally until `uploadDocument` resolves into a real
 * server document (at which point `useDocumentsList.ts` drops it from local state in favour of the
 * server list). `phase: "failed"` keeps the original `File` so "Retry upload" can resubmit it without
 * asking the user to re-pick it (V1's own precedent) -- a `Failed` row read back from the server list
 * (not this session's own upload) has no `File` to retry with; see `documentTable.ts#getRowAction`.
 */
export interface LocalUploadEntry {
  key: string;
  file: File;
  phase: "queued" | "uploading" | "failed";
  /** Set only when `phase === "failed"` -- the client's own error text (never a raw stack trace). */
  errorMessage?: string;
}

export type UploadBatchOutcome =
  | { kind: "admitted"; key: string; document: NonNullable<UploadDocumentResult["document"]> }
  | { kind: "rejected"; key: string; fileName: string; message: string }
  | { kind: "failed"; key: string; fileName: string; message: string };

/**
 * Runs `files` through `apiClient.uploadDocument`, at most `MAX_CONCURRENT_UPLOADS` in flight at
 * once (the task's own "uploads run with <= 3 in flight"), and reports each file's outcome via
 * `onSettled` as soon as it resolves -- callers do not wait for the whole batch to render the first
 * result (R-DOC-01 AC-1: "each file ... reaches a terminal outcome without blocking the others").
 * Oversized files never reach the network at all (`isOversized`, checked up front) -- reported as a
 * `"rejected"` outcome identically to a server 413, so the caller does not need two code paths.
 */
export async function runUploadBatch(
  entries: readonly { key: string; file: File }[],
  apiClient: ApiClient,
  tenantId: string,
  onSettled: (outcome: UploadBatchOutcome) => void,
): Promise<void> {
  let cursor = 0;

  async function worker(): Promise<void> {
    for (;;) {
      const index = cursor;
      cursor += 1;
      if (index >= entries.length) return;
      const { key, file } = entries[index];

      if (isOversized(file)) {
        onSettled({ kind: "rejected", key, fileName: file.name, message: getOversizedCopy(file.name) });
        continue;
      }

      const result = await apiClient.uploadDocument(tenantId, file);

      if (result.ok && result.document) {
        onSettled({ kind: "admitted", key, document: result.document });
        continue;
      }

      if (result.statusCode === 422 && result.rejection) {
        onSettled({ kind: "rejected", key, fileName: file.name, message: getRejectionReasonCopy(result.rejection.reason) });
        continue;
      }

      if (result.statusCode === 413 || result.statusCode === 415) {
        onSettled({ kind: "rejected", key, fileName: file.name, message: result.error ?? "Not added: this file could not be added." });
        continue;
      }

      onSettled({
        kind: "failed",
        key,
        fileName: file.name,
        message: result.error ?? `Raffa could not process ${file.name}. Try again.`,
      });
    }
  }

  const workerCount = Math.min(MAX_CONCURRENT_UPLOADS, entries.length);
  await Promise.all(Array.from({ length: workerCount }, () => worker()));
}
