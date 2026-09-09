import type { DocumentType } from "../../api/client";

/**
 * **Deprecated for `src/routes/documents/` itself** (task E13/F09/US01/T03, web-documents-v2):
 * `GET /api/documents` (`listDocuments`, `../../api/client.ts`) now exists and V2's own list
 * (`useDocumentsList.ts`) reads it, server-side, instead of this module -- R-DOC-06 AC-1
 * "reloading the browser shows the same list as before" depends on that call, not
 * `sessionStorage`. `DocumentsRoute` (`./index.tsx`), `DocumentStatusTable.tsx` and
 * `documentTable.ts` no longer import anything from this file.
 *
 * **Kept, unmodified, only because `components/shell/RailNav.tsx` still reads
 * `loadTrackedDocuments()` for its own "N to review"/"N docs" badge** (`navItems.ts
 * #DocumentCounts`'s own doc comment already named this gap as "F09/T03" before this task started).
 * `components/shell/**` is out of this task's own file scope (do-not-touch, landed by an earlier
 * phase, task E13/F09/US01/T01) -- deleting this file, as this task's own "Files to create or
 * modify" table literally says ("remove store"), would break that already-shipped component's
 * build for a fix that belongs to a `components/shell/` task, not this one. `rememberDocument`
 * (the write half) now has no caller anywhere in `src/routes/documents/` -- nothing repopulates
 * this session's own `sessionStorage` key after this task, so `RailNav`'s badge will read as
 * empty (no badge) for any session that started after this change, a known, flagged regression of
 * an already-documented interim, not a new gap. **Follow-up needed**: a `components/shell/` task
 * should replace `RailNav.tsx`'s own `loadTrackedDocuments()` call with a real count sourced from
 * `listDocuments` (e.g. a small `useDocumentCounts` hook `AppShell.tsx` fetches once, the same
 * shape `useValidatedContractCount.ts` already establishes for the secondary rail tier), then this
 * file can be deleted outright.
 *
 * Everything below this comment is unchanged from V1 (session-scoped `sessionStorage`, not keyed
 * per workspace -- see the git history for the original, fuller header comment on why).
 */

const TRACKED_DOCUMENTS_KEY = "contigo.documents.readback";

export interface TrackedDocument {
  id: string;
  /** Null until classification links a contract, or permanently null when processing failed before it could (AC-3's cross-link has nothing to open for such a row -- see DocumentStatusTable.tsx). */
  contractId: string | null;
  fileName: string;
  /**
   * Null until the `GET /api/documents/{id}` read-back resolves --
   * `POST /api/documents`'s own `201` body does not carry `documentType`
   * (see `src/api/client.ts`'s `UploadedDocument` vs `ReadBackDocument`).
   * Rendered as a "Classifying…" loading state
   * (`documentTable.ts#getDocumentTypeLabel`) until then.
   */
  documentType: DocumentType | null;
  /** AC-2's three terminal statuses only -- see `uploadPipeline.ts#isTerminalProcessingStatus`; a row is never tracked before reaching one of these. */
  processingStatus: "NeedsReview" | "Completed" | "Failed";
  createdAt: string;
}

function readTrackedDocuments(storage: Storage): TrackedDocument[] {
  const raw = storage.getItem(TRACKED_DOCUMENTS_KEY);
  if (!raw) return [];
  try {
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as TrackedDocument[]) : [];
  } catch {
    // Malformed/foreign sessionStorage content under this key is not this
    // screen's problem to throw over -- treat it the same as "nothing
    // tracked yet" (workspaceStore.ts's own readWorkspaceArray follows the
    // same convention).
    return [];
  }
}

/** Every document this browser has uploaded/read back this session, most-recently-uploaded first. */
export function loadTrackedDocuments(storage: Storage = window.sessionStorage): TrackedDocument[] {
  return readTrackedDocuments(storage);
}

/**
 * Inserts or updates `document` by id (an upload followed by its own
 * read-back updates the same row instead of duplicating it) and persists the
 * result. New/updated documents are moved to the front -- the table reads
 * most-recently-touched first, matching an activity list, not an archive.
 */
export function rememberDocument(
  document: TrackedDocument,
  storage: Storage = window.sessionStorage,
): TrackedDocument[] {
  const existing = readTrackedDocuments(storage);
  const withoutThisOne = existing.filter((known) => known.id !== document.id);
  const next = [document, ...withoutThisOne];
  storage.setItem(TRACKED_DOCUMENTS_KEY, JSON.stringify(next));
  return next;
}
