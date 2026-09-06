import type { DocumentType } from "../../api/client";

/**
 * Client-side record of "documents this browser has uploaded/read back this
 * session" (ADR-020 screen 3's *other* half: "screen 3 may be two: upload UI
 * + document-status read-back"; story us-02-document-status-readback, AC-1
 * "document table").
 *
 * Why this is client-side, not server-queried: there is no backend endpoint
 * that lists the documents for a tenant.
 *   - `backend/src/Contigo.Api/Program.cs` maps only `POST /api/documents`
 *     (upload) and `GET /api/documents/{id}` (read back one document by id)
 *     -- no `GET /api/documents` collection route.
 *   - `GET /api/contracts` (`PortfolioEndpointExtensions.cs`) lists
 *     *contracts*, not documents, and belongs to a different, not-yet-built
 *     screen (epic-07/feature-01-portfolio-ui) outside this task's
 *     `src/routes/documents/` scope.
 * A future backend task would need a tenant-scoped, paginated
 * `GET /api/documents` collection endpoint (the same shape
 * `PortfolioPageRequest` already gives Portfolio) before this module can be
 * replaced with a real server call -- the same kind of gap
 * `src/routes/signin/workspaceStore.ts` already documents for the workspace
 * list, and the same interim this module follows.
 *
 * Until then, every row here is a *real* document this browser uploaded via
 * the real `POST /api/documents` (`src/api/client.ts`'s `uploadDocument`)
 * and read back via the real `GET /api/documents/{id}` (`getDocument`) --
 * never fabricated. The known limitation is discovery, not truth: this
 * browser cannot learn about a document uploaded from another
 * device/browser, or one that existed before this session started.
 *
 * Session-scoped (`sessionStorage`, not `localStorage`) and *not* keyed per
 * workspace -- the same scope `workspaceStore.ts`'s `CURRENT_WORKSPACE_KEY`
 * already uses, since this app supports exactly one "current workspace" per
 * browser session today (see that module's own doc comment); switching
 * workspaces mid-session is not a V1 flow this table needs to defend against
 * yet.
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
