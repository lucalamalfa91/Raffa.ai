import { useNavigate, useParams } from "react-router-dom";
import type { ApiClient } from "../../../api/client";
import { loadCurrentWorkspace } from "../../signin/workspaceStore";
import ReviewHeader from "./ReviewHeader";
import ReviewFieldList from "./ReviewFieldList";
import EvidencePane from "./EvidencePane";
import { useReviewSession } from "./useReviewSession";
import "./review.css";

export interface ReviewRouteProps {
  apiClient: ApiClient;
}

/**
 * Route `/contracts/:contractId/review` (ADR-018; screens.md #6 "Review / correction"; ADR-020
 * screen 6; task E07/F03/US01/T01, us-01-field-review-correction AC-1/AC-2/AC-3/AC-4). Wired into
 * `../../../components/shell/WorkspaceShellApp.tsx`'s `contracts/:contractId/review` route. Reached
 * from `../contract360/Contract360Header.tsx`'s "Review extraction" button and
 * `../contract360/DetailsSection.tsx`'s "Review all →" link.
 *
 * All fetching and decision state lives in the shared `useReviewSession` hook (also behind
 * `../../documents/ReviewState.tsx`, Review-as-a-state-of-Documents); this file only maps the hook's
 * phases onto the screen states and decides where "Mark as validated" lands afterwards: back on
 * Contract 360, once `POST /api/documents/{id}/validate` has really answered 200. The document it
 * signs off is resolved from the contract's own documents (the one still needing review first) --
 * this route has no document id of its own, unlike the Documents state.
 */
export default function ReviewRoute({ apiClient }: ReviewRouteProps) {
  const { contractId } = useParams<{ contractId: string }>();
  const navigate = useNavigate();
  const workspace = loadCurrentWorkspace();
  const session = useReviewSession(apiClient, workspace?.id ?? null, contractId ?? null);

  if (!workspace) {
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before reviewing a contract.</p>
      </div>
    );
  }

  if (!contractId) {
    return (
      <div className="empty-state" role="status">
        <h3>No contract selected</h3>
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
        <p className="micro-meta">This contract does not exist, or is not in your workspace.</p>
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
      <ReviewHeader
        header={fetchState.contract.header}
        progress={session.progress}
        onMarkValidated={() => {
          void session.markValidated().then((validated) => {
            if (validated) navigate(`/contracts/${contractId}`);
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
