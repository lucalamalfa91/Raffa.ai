import { useCallback, useEffect, useMemo, useState } from "react";
import type { ApiClient, Contract360Body, CorrectionHistoryEntryBody } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import ReviewHeader from "../contracts/review/ReviewHeader";
import ReviewFieldList from "../contracts/review/ReviewFieldList";
import EvidencePane from "../contracts/review/EvidencePane";
import { buildReviewFields, computeReviewProgress, type CorrectableFieldName } from "../contracts/review/reviewViewModel";
import "../contracts/review/review.css";

export interface ReviewStateProps {
  apiClient: ApiClient;
  /** The reviewed document's own linked contract -- resolved by `index.tsx` from the already-loaded
   * documents list (`DocumentListItemBody.contractId`), not fetched again here. */
  contractId: string;
  onBack: () => void;
  /** Fired once, on a real "Mark as validated" click (never blocked -- see `ReviewHeader`'s own
   * `disabled` gate). `index.tsx` returns to the list and shows the "*X* is now askable." hook. */
  onValidated: (contractId: string) => void;
}

type FetchState =
  | { phase: "loading" }
  | { phase: "not-found" }
  | { phase: "error"; statusCode: number | null; message: string }
  | {
      phase: "ready";
      contract: Contract360Body;
      history: readonly CorrectionHistoryEntryBody[];
      historyDegraded: boolean;
    };

/**
 * Review as a **state of Documents** (`/documents?review=:documentId`; task E13/F09/US01/T03's own
 * coding objective; `contigo-v2/ia-v2.md` route map; requirements R-WEB-05). Reuses
 * `../contracts/review/{ReviewHeader,ReviewFieldList,EvidencePane}.tsx` and every pure function in
 * `../contracts/review/reviewViewModel.ts` **unmodified** ("routes/contracts/review/* internals
 * (reuse as-is)") -- this file is a new orchestration wrapper around those same building blocks,
 * not a copy of them. It cannot reuse `../contracts/review/index.tsx`'s own default-exported
 * `ReviewRoute` component directly: that component is bound to the URL param `:contractId`
 * (`/contracts/:contractId/review`) and hard-codes its own "Mark as validated" as
 * `navigate('/contracts/:id')` -- neither fits a document-id query param or "return to Documents
 * with the validated hook" (this task's own explicit requirement). The fetch orchestration below
 * (`getContract360` then `getCorrectionHistory`, the same two-source decision-state split) is
 * therefore mirrored here, not imported, from that file's own logic.
 *
 * **No backend "finalize" endpoint exists, by design, not by omission** -- `../contracts/review/
 * index.tsx`'s own `ReviewRoute` already treats "Mark as validated" as a purely client-side action,
 * gated on `computeReviewProgress`/`isValidationBlocked` (every blocking field resolved), with no
 * `PATCH`/`POST` call of its own; this wrapper keeps that exact same honest posture rather than
 * inventing a status-transition call no OpenAPI operation documents. A reload before this click
 * re-asks any field that was only session-`Accept`ed, never `Correct`ed -- the same, already-shipped
 * consequence `../contracts/review/index.tsx`'s own header comment names.
 */
export default function ReviewState({ apiClient, contractId, onBack, onValidated }: ReviewStateProps) {
  const workspace = loadCurrentWorkspace();

  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });
  const [selectedField, setSelectedField] = useState<CorrectableFieldName | null>(null);
  const [acceptedThisSession, setAcceptedThisSession] = useState<ReadonlySet<CorrectableFieldName>>(new Set());
  const [correctionError, setCorrectionError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(() => {
    if (!workspace) return;
    setFetchState({ phase: "loading" });

    void apiClient.getContract360(workspace.id, contractId).then(async (contractResult) => {
      if (!contractResult.ok || !contractResult.contract) {
        if (contractResult.statusCode === 404) {
          setFetchState({ phase: "not-found" });
          return;
        }
        setFetchState({
          phase: "error",
          statusCode: contractResult.statusCode,
          message:
            contractResult.statusCode === 503 || contractResult.statusCode === null
              ? "Contigo's contract service is temporarily unavailable. Try again in a moment."
              : (contractResult.error ?? "The contract could not be loaded."),
        });
        return;
      }

      const historyResult = await apiClient.getCorrectionHistory(workspace.id, contractId);
      setFetchState({
        phase: "ready",
        contract: contractResult.contract,
        history: historyResult.ok && historyResult.history ? historyResult.history : [],
        historyDegraded: !historyResult.ok,
      });
    });
  }, [apiClient, workspace?.id, contractId]);

  useEffect(() => {
    load();
  }, [load]);

  if (!workspace) {
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before reviewing a document.</p>
      </div>
    );
  }

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
        <button type="button" className="btn btn-secondary" onClick={load}>
          Retry
        </button>
      </div>
    );
  }

  const { contract, history, historyDegraded } = fetchState;

  return (
    <ReviewStateReady
      contract={contract}
      history={history}
      historyDegraded={historyDegraded}
      selectedField={selectedField}
      onSelectField={setSelectedField}
      acceptedThisSession={acceptedThisSession}
      onAccept={(name) => {
        setAcceptedThisSession((previous) => new Set(previous).add(name));
        setSelectedField(name);
      }}
      onCorrect={async (name, newValue, reason) => {
        setSubmitting(true);
        setCorrectionError(null);
        const result = await apiClient.correctContract(workspace.id, contractId, {
          corrections: { [name]: newValue },
          reason: reason.trim() === "" ? null : reason.trim(),
        });
        setSubmitting(false);
        if (!result.ok) {
          setCorrectionError(result.error ?? "The correction could not be saved.");
          return;
        }
        setSelectedField(name);
        load();
      }}
      correctionError={correctionError}
      submitting={submitting}
      onBack={onBack}
      onMarkValidated={() => onValidated(contractId)}
    />
  );
}

interface ReviewStateReadyProps {
  contract: Contract360Body;
  history: readonly CorrectionHistoryEntryBody[];
  historyDegraded: boolean;
  selectedField: CorrectableFieldName | null;
  onSelectField: (name: CorrectableFieldName) => void;
  acceptedThisSession: ReadonlySet<CorrectableFieldName>;
  onAccept: (name: CorrectableFieldName) => void;
  onCorrect: (name: CorrectableFieldName, newValue: string | null, reason: string) => void;
  correctionError: string | null;
  submitting: boolean;
  onBack: () => void;
  onMarkValidated: () => void;
}

/** Split out of the default export only so the "ready" branch's derived state (`rows`/`progress`)
 * is computed with plain hooks at a stable position -- same reason `../contracts/review/index.tsx
 * #ReviewScreen` is its own function. */
function ReviewStateReady({
  contract,
  history,
  historyDegraded,
  selectedField,
  onSelectField,
  acceptedThisSession,
  onAccept,
  onCorrect,
  correctionError,
  submitting,
  onBack,
  onMarkValidated,
}: ReviewStateReadyProps) {
  const rows = useMemo(
    () => buildReviewFields(contract, history, acceptedThisSession),
    [contract, history, acceptedThisSession],
  );
  const progress = computeReviewProgress(rows);
  const selectedRow = rows.find((row) => row.name === selectedField) ?? null;

  return (
    <div className="review-screen">
      <button type="button" className="btn btn-ghost documents-review-back" onClick={onBack}>
        ← Documents
      </button>

      <ReviewHeader header={contract.header} progress={progress} onMarkValidated={onMarkValidated} />

      {historyDegraded && (
        <p className="hint" role="alert">
          Correction history could not be loaded — an already-corrected field may show as pending review.
        </p>
      )}

      <div className="review-body">
        <ReviewFieldList rows={rows} selectedField={selectedField} onSelect={onSelectField} onAccept={onAccept} />
        <EvidencePane row={selectedRow} onCorrect={onCorrect} submitting={submitting} error={correctionError} />
      </div>
    </div>
  );
}
