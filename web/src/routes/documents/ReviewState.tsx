import type { ApiClient } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import ReviewHeader from "../contracts/review/ReviewHeader";
import ReviewFieldList from "../contracts/review/ReviewFieldList";
import EvidencePane from "../contracts/review/EvidencePane";
import { useReviewSession } from "../contracts/review/useReviewSession";
import "../contracts/review/review.css";

export interface ReviewStateProps {
  apiClient: ApiClient;
  /** The reviewed document's own linked contract -- resolved by `index.tsx` from the already-loaded
   * documents list (`DocumentListItemBody.contractId`), not fetched again here. */
  contractId: string;
  /** The document being reviewed (`?review=<documentId>`) -- the one "Mark as validated" signs off
   * through `POST /api/documents/{id}/validate`. */
  documentId: string;
  onBack: () => void;
  /** Fired once the sign-off has really been written (a 200 from `validateDocument`). `index.tsx`
   * returns to the list and shows the "*X* is now askable." hook. */
  onValidated: (contractId: string) => void;
}

/**
 * Review as a **state of Documents** (`/documents?review=:documentId`; task E13/F09/US01/T03's own
 * coding objective; `raffa-v2/ia-v2.md` route map; requirements R-WEB-05). Reuses
 * `../contracts/review/{ReviewHeader,ReviewFieldList,EvidencePane}.tsx` and the shared
 * `useReviewSession` hook -- the same lifecycle the routed `/contracts/:contractId/review` screen
 * runs, so the two can never drift again on what "Mark as validated" does. It cannot reuse
 * `../contracts/review/index.tsx`'s default-exported component directly: that one is bound to the
 * URL param `:contractId` and navigates to Contract 360 on validation; this state returns to the
 * Documents list with the validated hook.
 *
 * **"Mark as validated" is a real write.** The hook posts every Accepted field's name to
 * `POST /api/documents/{id}/validate`; the backend moves the document to `Completed` and audits the
 * sign-off, and only a 200 fires `onValidated`. Corrections are already durable through `PATCH
 * /api/contracts/{id}` by then. A reload before that click re-asks any field that was only
 * Accepted -- the session-only half of the decision state, see the hook's own doc comment.
 */
export default function ReviewState({ apiClient, contractId, documentId, onBack, onValidated }: ReviewStateProps) {
  const workspace = loadCurrentWorkspace();
  const session = useReviewSession(apiClient, workspace?.id ?? null, contractId, documentId);

  if (!workspace) {
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before reviewing a document.</p>
      </div>
    );
  }

  const { fetchState } = session;

  if (fetchState.phase === "loading") {
    return (
      <div className="review-skeleton" role="status" aria-live="polite">
        <p className="micro-meta">Loading review…</p>
        {Array.from({ length: 5 }, (_, index) => (
          <div key={index} className="skeleton review-skeleton-row" />
        ))}
      </div>
    );
  }

  if (fetchState.phase === "not-found") {
    return (
      <div className="empty-state" role="status">
        <h3>Contract not found</h3>
        <p className="micro-meta">This document's contract does not exist, or is not in your workspace.</p>
      </div>
    );
  }

  if (fetchState.phase === "error") {
    return (
      <div className="error-state" role="alert">
        <h4>Review unavailable</h4>
        <p className="micro-meta">
          {fetchState.message}
          {fetchState.statusCode !== null && ` (HTTP ${fetchState.statusCode})`}
        </p>
        <button type="button" className="btn btn-secondary" onClick={session.reload}>
          Retry
        </button>
      </div>
    );
  }

  return (
    <div className="review-screen">
      <button type="button" className="btn btn-ghost documents-review-back" onClick={onBack}>
        ← Documents
      </button>

      <ReviewHeader
        header={fetchState.contract.header}
        progress={session.progress}
        onMarkValidated={() => {
          void session.markValidated().then((validated) => {
            if (validated) onValidated(contractId);
          });
        }}
        validating={session.validating}
        validationError={session.validationError}
        alreadyValidated={session.alreadyValidated}
        canValidate={session.reviewDocumentId !== null}
      />

      {fetchState.historyDegraded && (
        <p className="hint" role="alert">
          Correction history could not be loaded — an already-corrected field may show as pending review.
        </p>
      )}
      {fetchState.evidenceDegraded && (
        <p className="hint" role="alert">
          Extraction evidence could not be loaded — every field shows as needing review, with no source passage.
        </p>
      )}

      <div className="review-body">
        <ReviewFieldList
          rows={session.rows}
          selectedField={session.selectedField}
          onSelect={session.selectField}
          onAccept={(name) => void session.accept(name)}
        />
        <EvidencePane
          row={session.selectedRow}
          onCorrect={(name, value, reason) => void session.correct(name, value, reason)}
          submitting={session.submitting}
          error={session.correctionError}
        />
      </div>
    </div>
  );
}
