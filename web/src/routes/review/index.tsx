import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { ApiClient, PortfolioListItem } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import { loadTrackedDocuments } from "../documents/documentStore";
import ReviewQueueTable from "./ReviewQueueTable";
import { buildReviewQueue } from "./reviewQueueViewModel";
import "./review-queue.css";

export interface ReviewQueueRouteProps {
  apiClient: ApiClient;
}

/** Matches `PortfolioPageRequest.MaxPageSize` -- same ceiling PortfolioRoute already uses. */
const PORTFOLIO_PAGE_SIZE = 100;

type FetchState =
  | { phase: "loading" }
  | { phase: "error"; statusCode: number | null; message: string }
  | { phase: "ready"; items: readonly PortfolioListItem[] };

/**
 * Route `/review` (nav landing for "Review queue"; ia.md / ADR-018 name only
 * `/contracts/:id/review`, so this list is the missing half of E07/F03). Wired into
 * `../../components/shell/WorkspaceShellApp.tsx` in place of that shell task's
 * `ScaffoldScreen` placeholder. The field-review detail at `/contracts/:id/review`
 * is unchanged.
 *
 * Fetch-once over `GET /api/contracts`, then keep the needs-review rows. Session-tracked
 * uploads in `NeedsReview` fill the discovery gap for a contract that has not appeared
 * on the portfolio page yet (see `reviewQueueViewModel.ts`).
 */
export default function ReviewQueueRoute({ apiClient }: ReviewQueueRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });

  const loadQueue = useCallback(() => {
    if (!workspace) return;

    setFetchState({ phase: "loading" });
    void apiClient.getPortfolio(workspace.id, { pageSize: PORTFOLIO_PAGE_SIZE }).then((result) => {
      if (!result.ok || !result.portfolio) {
        setFetchState({
          phase: "error",
          statusCode: result.statusCode,
          message:
            result.statusCode === 503 || result.statusCode === null
              ? "Contigo's portfolio service is temporarily unavailable. Try again in a moment."
              : (result.error ?? "The review queue could not be loaded."),
        });
        return;
      }

      setFetchState({ phase: "ready", items: result.portfolio.items });
    });
  }, [apiClient, workspace?.id]);

  useEffect(() => {
    loadQueue();
  }, [loadQueue]);

  if (!workspace) {
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before reviewing extractions.</p>
      </div>
    );
  }

  const trackedDocuments = loadTrackedDocuments();
  const rows =
    fetchState.phase === "ready" ? buildReviewQueue(fetchState.items, trackedDocuments) : buildReviewQueue([], trackedDocuments);
  const showUploadRowsDuringError = fetchState.phase === "error" && rows.length > 0;

  return (
    <div className="review-queue-screen">
      <p className="screen-kicker">R1</p>
      <h2 className="screen-title">Review queue</h2>
      <p className="micro-meta">Contracts whose extraction still needs a human decision.</p>

      {fetchState.phase === "loading" && (
        <div className="review-queue-skeleton" role="status" aria-live="polite">
          {Array.from({ length: 4 }, (_, index) => (
            <div key={index} className="skeleton review-queue-skeleton-row" />
          ))}
        </div>
      )}

      {fetchState.phase === "error" && (
        <div className="error-state" role="alert">
          <h4>Review queue unavailable</h4>
          <p className="micro-meta">
            {fetchState.message}
            {fetchState.statusCode !== null && ` (HTTP ${fetchState.statusCode})`}
          </p>
          <button type="button" className="btn btn-secondary" onClick={loadQueue}>
            Retry
          </button>
        </div>
      )}

      {fetchState.phase === "ready" && rows.length === 0 && (
        <div className="empty-state" role="status">
          <h3>Nothing needs review</h3>
          <p className="micro-meta">Contracts that fail extraction confidence land here after upload.</p>
          <Link to="/documents" className="btn btn-primary">
            Upload a document
          </Link>
        </div>
      )}

      {(fetchState.phase === "ready" || showUploadRowsDuringError) && rows.length > 0 && (
        <ReviewQueueTable rows={rows} />
      )}
    </div>
  );
}
