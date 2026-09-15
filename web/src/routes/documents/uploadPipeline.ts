import type { ApiClient, UploadDocumentResult } from "../../api/client";

/**
 * Upload-outcome concerns for Documents V2 (task E13/F09/US01/T03, screens-v2.md #3; requirements
 * R-DOC-01/02/04), reshaped by task E16/F03/US01/T01 (wave w15; ADR-027 §D1/§D6, ADR-020 w15 §1 and
 * §6, ADR-012 w15 §5 and §13.4). `POST /api/documents` now returns the moment the bytes are stored;
 * the content gate (parse, classify, threshold) runs on the Worker afterwards and its refusal comes
 * back as a *server row* in `Rejected` carrying a `rejectionReason` code -- never as a 422 on this
 * call. V1's client-side pacing ticker is long gone (the real stage comes from `GET /api/documents`,
 * polled by `useDocumentsList.ts`). This file owns: multi-file upload limits (R-DOC-01), the
 * concurrency-capped batch runner (up to 20 files, <=8 in flight), the one client-owned upload
 * deadline, and the designed sentences -- for the two refusals that never reach the server
 * (oversize, 413/415) and for the server's own reason codes on a `Rejected` row.
 */

/** R-DOC-01: "batch <= 20 files". */
export const MAX_FILES_PER_BATCH = 20;

/** R-DOC-01: "<= 50 MB per file" (`Documents:MaxFileBytes`, task-01-documents-admission.md). Checked
 * client-side too, purely to avoid a doomed round trip for an obviously oversized file -- the server
 * (413) stays authoritative; see `useDocumentsList.ts#uploadFiles`. */
export const MAX_FILE_BYTES = 50 * 1024 * 1024;

/** Raised from 3 → 8 (plan instant-identity-ingest: outbox landed, safe to widen the window for
 * a 50-file drop without starving the server; ADR-005 container concurrency is comfortably above 8). */
export const MAX_CONCURRENT_UPLOADS = 8;

/** Widened accept list (PDF/DOCX/XLSX plus PNG/JPG via OCR, D7/ADR-017) -- a soft, OS-level filter
 * only; the server's 415 (extension + magic-byte sniffing) stays authoritative (R-DOC-02 AC-1). */
export const ACCEPTED_EXTENSIONS = ".pdf,.docx,.xlsx,.png,.jpg,.jpeg";

/**
 * ADR-012 w15 §5 (OQ-w15-ca-01): the one client-owned deadline per upload request, configured here
 * and nowhere per call site. It must exceed the time to *send* a legitimate 50 MB body on a slow
 * link and sit below the ~240 s Container Apps platform default ADR-005 clause 13 deliberately
 * leaves unpinned -- so an upload that hangs turns into Raffa.ai's own retryable "failed" row
 * (`getTransportFailureCopy`) instead of an opaque platform 502 minutes later. A safety net, not
 * the mechanism: what makes an upload feel instant is that the request returns at the store.
 */
export const UPLOAD_DEADLINE_MS = 120_000;

export function isOversized(file: File): boolean {
  return file.size > MAX_FILE_BYTES;
}

/**
 * R-DOC-04's own verbatim rejection sentences, keyed by the Worker's `rejectionReason` code on a
 * `Rejected` row (ADR-027 §D6; ADR-020 w15 §7). The `"Not added: "` lead-in the retired card used
 * to carry is dropped at source (ADR-020 w15 §1.3/§6.2): beside the row's own "Not added" tag, the
 * tag *is* the lead-in. `null` for a code this app does not know, or for no code at all -- silence,
 * never a guessed reason (§7's "a blank hint is a gap a user can ask about; a remembered one is a
 * lie the next browser tells"). The two sentences are requirements copy and are not re-authored.
 */
export function getRejectionReasonCopy(reason: string | null): string | null {
  switch (reason) {
    case "not_a_contract":
      return "This looks like a recipe, not a contract. Raffa.ai only keeps contracts, order forms, quotes and the documents around them. Drop the signed agreement or the supplier's proposal.";
    case "no_readable_text":
      return "Raffa.ai could not read any contract text in this file. Try a clearer scan or the original PDF.";
    default:
      return null;
  }
}

/** A file this browser refused before ever calling the API (oversized). Same "Not added" reading as
 * the server's `Rejected` row (ADR-019 w15 clause 4), rendered as a *local* row (ADR-020 w15 §6).
 * App-authored copy, so it may lose the file name the row's own filename cell already carries. */
export function getOversizedCopy(): string {
  return "This file is larger than 50 MB, the most Raffa.ai accepts.";
}

/**
 * ADR-020 w15 §6.3: the format/size half of the gate stays in-request (413/415), and its refusal is
 * the one path the Worker never sees. The status code *selects* the sentence a user reads; the
 * server's error prose never *supplies* it. The 413 sentence deliberately does not restate 50 MB
 * (the browser already refuses at that limit, so a 413 is the platform refusing a file the client
 * thought was fine); the 415 sentence is derived from the shipped `no_readable_text` clause.
 */
export function getRefusalCopy(statusCode: 413 | 415): string {
  return statusCode === 413
    ? "This file is too large for Raffa.ai to accept. Try a smaller file, or split it into parts."
    : "Raffa.ai cannot open this file type. Try the original PDF, or a clear scan of the signed pages.";
}

/** The retryable "failed" sentence for a request that never got an answer (deadline hit, network
 * down) -- Raffa.ai's own words, never a platform 502's. */
export function getTransportFailureCopy(fileName: string): string {
  return `Raffa.ai could not process ${fileName}. Try again.`;
}

/**
 * A file selected/dropped this session, tracked locally until the server list carries its row. The
 * `"admitted"` outcome records the 201's `serverId` on the entry, and `useDocumentsList.ts` drops
 * the entry only once a server row with that id is present -- never on a timer, never between a
 * drop and a reload (ADR-012 w15 §5: on a 15-file batch that gap is a visible flicker on every row).
 * `phase: "failed"` keeps the original `File` so "Retry upload" can resubmit it without asking the
 * user to re-pick it; `phase: "rejected"` (ADR-020 w15 §6) is a refusal that never reached the
 * server -- oversize, 413, 415 -- kept as a row exactly where the retired "Not added" card was, with
 * no next step and no dismiss (a reload clears it, which is the one honest lifetime it has).
 */
export interface LocalUploadEntry {
  key: string;
  file: File;
  phase: "queued" | "uploading" | "failed" | "rejected";
  /** Set when `phase` is `"failed"` or `"rejected"` -- the client's own designed sentence (never a raw stack trace, never API prose). */
  errorMessage?: string;
  /** The server id from the 201, once the bytes are stored (`phase` stays `"uploading"` until the server row shows up). */
  serverId?: string;
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
 * `"rejected"` outcome identically to a server 413/415, so the caller has one local-row path. Every
 * request carries the `UPLOAD_DEADLINE_MS` abort signal; a request that hits it lands in the
 * `"failed"` branch with Raffa.ai's own retryable sentence.
 */
export async function runUploadBatch(
  entries: readonly { key: string; file: File }[],
  apiClient: ApiClient,
  tenantId: string,
  onSettled: (outcome: UploadBatchOutcome) => void,
  deadlineMs: number = UPLOAD_DEADLINE_MS,
): Promise<void> {
  let cursor = 0;

  async function worker(): Promise<void> {
    for (;;) {
      const index = cursor;
      cursor += 1;
      if (index >= entries.length) return;
      const { key, file } = entries[index];

      if (isOversized(file)) {
        onSettled({ kind: "rejected", key, fileName: file.name, message: getOversizedCopy() });
        continue;
      }

      const controller = new AbortController();
      const deadline = setTimeout(() => controller.abort(), deadlineMs);
      let result: UploadDocumentResult;
      try {
        result = await apiClient.uploadDocument(tenantId, file, { signal: controller.signal });
      } finally {
        clearTimeout(deadline);
      }

      if (result.ok && result.document) {
        onSettled({ kind: "admitted", key, document: result.document });
        continue;
      }

      if (result.statusCode === 413 || result.statusCode === 415) {
        onSettled({ kind: "rejected", key, fileName: file.name, message: getRefusalCopy(result.statusCode) });
        continue;
      }

      onSettled({
        kind: "failed",
        key,
        fileName: file.name,
        message: controller.signal.aborted ? getTransportFailureCopy(file.name) : (result.error ?? getTransportFailureCopy(file.name)),
      });
    }
  }

  const workerCount = Math.min(MAX_CONCURRENT_UPLOADS, entries.length);
  await Promise.all(Array.from({ length: workerCount }, () => worker()));
}
