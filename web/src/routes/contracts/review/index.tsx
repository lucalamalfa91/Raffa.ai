import { useCallback, useEffect, useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import type { ApiClient, Contract360Body, CorrectionHistoryEntryBody } from "../../../api/client";
import { loadCurrentWorkspace } from "../../signin/workspaceStore";
import ReviewHeader from "./ReviewHeader";
import ReviewFieldList from "./ReviewFieldList";
import EvidencePane from "./EvidencePane";
import { buildReviewFields, computeReviewProgress, type CorrectableFieldName } from "./reviewViewModel";
import "./review.css";

export interface ReviewRouteProps {
  apiClient: ApiClient;
}

type FetchState =
  | { phase: "loading" }
  | { phase: "not-found" }
  | { phase: "error"; statusCode: number | null; message: string }
  | {
      phase: "ready";
      contract: Contract360Body;
      history: readonly CorrectionHistoryEntryBody[];
      /** True when `getCorrectionHistory` itself failed after the contract was already confirmed to
       * exist -- degrades to "treat as no corrections yet" rather than failing the whole screen
       * (same posture `../contract360/index.tsx` already takes for its own optional renewals/
       * priority fetches), surfaced as a visible, honest banner rather than swallowed. */
      historyDegraded: boolean;
    };

/**
 * Route `/contracts/:contractId/review` (ADR-018; screens.md #6 "Review / correction"; ADR-020
 * screen 6; task E07/F03/US01/T01, us-01-field-review-correction AC-1/AC-2/AC-3/AC-4). Wired into
 * `../../../components/shell/WorkspaceShellApp.tsx`'s `contracts/:contractId/review` route in place
 * of that shell task's `ScaffoldScreen` placeholder -- the same seam `../contract360/index.tsx`
 * already used. Reached from `../contract360/Contract360Header.tsx`'s "Review extraction" button and
 * `../contract360/OverviewTab.tsx`'s "Needs your attention → Review all" link, both already wired by
 * task E07/F02/US01/T01 to this exact route.
 *
 * **Fetch order**: `getContract360` first, same reasoning as `../contract360/index.tsx` -- a `404`
 * there is this screen's own named "not found" state, distinct from a transport/5xx error, and
 * short-circuits before the correction-history call runs at all. `getCorrectionHistory` then reads
 * the real, durable "which fields has a human already corrected" signal this screen's whole gate
 * depends on (see `./reviewViewModel.ts`'s own header comment for why there is no live per-field
 * confidence to read yet, and why correction history is the one real signal this screen has).
 *
 * **Decision state has two sources, one durable and one not.** A field with a `getCorrectionHistory`
 * entry is `"corrected"` -- real, persists across reloads, came from a real `PATCH
 * /api/contracts/{id}` write. A field the user clicks "Accept" for is `"accepted"` -- session-only
 * React state (`acceptedThisSession`), because `ContractCorrectionService.CorrectAsync` rejects a
 * no-op correction outright, so there is no backend call an "accept, unchanged" decision could ever
 * make durable. Reloading this screen before clicking "Mark as validated" re-asks the question for
 * any field that was only Accepted, never Corrected -- an honest consequence of the real API's
 * shape (see `./reviewViewModel.ts#buildReviewFields`'s own doc comment), not a bug to silently
 * paper over with invented client-side persistence.
 */
export default function ReviewRoute({ apiClient }: ReviewRouteProps) {
  const { contractId } = useParams<{ contractId: string }>();
  const navigate = useNavigate();
  const workspace = loadCurrentWorkspace();

  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });
  const [selectedField, setSelectedField] = useState<CorrectableFieldName | null>(null);
  const [acceptedThisSession, setAcceptedThisSession] = useState<ReadonlySet<CorrectableFieldName>>(new Set());
  const [correctionError, setCorrectionError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(() => {
    if (!workspace || !contractId) return;

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
          // Same 503-vs-other split as ../contract360/index.tsx's own load (ADR-019 accessibility
          // baseline: "names the failing job, never a raw stack trace").
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
    // Depends on workspace?.id/contractId (primitives), not workspace itself: loadCurrentWorkspace()
    // returns a fresh object every call, the same convention every other route in this folder uses.
  }, [apiClient, workspace?.id, contractId]);

  useEffect(() => {
    load();
  }, [load]);

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
        <button type="button" className="btn btn-secondary" onClick={load}>
          Retry
        </button>
      </div>
    );
  }

  const { contract, history, historyDegraded } = fetchState;

  return (
    <ReviewScreen
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
      onMarkValidated={() => navigate(`/contracts/${contractId}`)}
    />
  );
}

interface ReviewScreenProps {
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
  onMarkValidated: () => void;
}

/** Split out of the default export only so the "ready" branch's derived state (`rows`/`progress`/
 * `selectedRow`) is computed with plain hooks at a stable position -- the guard clauses above return
 * early from several different `FetchState` phases, and hooks cannot follow conditional early
 * returns. */
function ReviewScreen({
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
  onMarkValidated,
}: ReviewScreenProps) {
  const rows = useMemo(
    () => buildReviewFields(contract, history, acceptedThisSession),
    [contract, history, acceptedThisSession],
  );
  const progress = computeReviewProgress(rows);
  const selectedRow = rows.find((row) => row.name === selectedField) ?? null;

  return (
    <div className="review-screen">
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
