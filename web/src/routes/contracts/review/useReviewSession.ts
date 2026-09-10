import { useCallback, useEffect, useMemo, useState } from "react";
import type {
  ApiClient,
  Contract360Body,
  ContractFieldEvidenceBody,
  CorrectionHistoryEntryBody,
} from "../../../api/client";
import {
  acceptedFieldNames,
  buildReviewFields,
  computeReviewProgress,
  indexEvidence,
  resolveReviewDocument,
  type CorrectableFieldName,
  type ReviewFieldRow,
  type ReviewProgress,
} from "./reviewViewModel";

export type ReviewFetchState =
  | { phase: "loading" }
  | { phase: "not-found" }
  | { phase: "error"; statusCode: number | null; message: string }
  | {
      phase: "ready";
      contract: Contract360Body;
      history: readonly CorrectionHistoryEntryBody[];
      evidence: readonly ContractFieldEvidenceBody[];
      /** True when `getCorrectionHistory` itself failed after the contract was already confirmed to
       * exist -- degrades to "treat as no corrections yet" rather than failing the whole screen
       * (same posture `../contract360/index.tsx` already takes for its own optional fetches),
       * surfaced as a visible, honest banner rather than swallowed. */
      historyDegraded: boolean;
      /** Same degradation for `getContractEvidence`: every field falls back to the conservative
       * "Needs review" tag with no source passage, and a banner says why. */
      evidenceDegraded: boolean;
    };

export interface ReviewSession {
  fetchState: ReviewFetchState;
  reload: () => void;
  rows: readonly ReviewFieldRow[];
  progress: ReviewProgress;
  selectedField: CorrectableFieldName | null;
  selectField: (name: CorrectableFieldName) => void;
  selectedRow: ReviewFieldRow | null;
  /** "Accept" for one field: a client-side acknowledgement for a value the contract already holds,
   * a real `correctContract` write for a proposal the pipeline did not apply (`proposalPending`). */
  accept: (name: CorrectableFieldName) => Promise<void>;
  /** "Save correction": `PATCH /api/contracts/{id}` then a full re-fetch (reload, never a locally
   * patched copy -- the same "reload, don't guess" convention `../contract360/index.tsx` follows). */
  correct: (name: CorrectableFieldName, newValue: string | null, reason: string) => Promise<void>;
  correctionError: string | null;
  submitting: boolean;
  /** The document this review signs off (`null` when the contract has none -- nothing to validate). */
  reviewDocumentId: string | null;
  /** True when that document is already `Completed` per the aggregate: the review is closed. */
  alreadyValidated: boolean;
  /** "Mark as validated": `POST /api/documents/{id}/validate` with every Accepted field's name.
   * Resolves `true` on success; on failure the reason lands in `validationError` and this resolves
   * `false`, so the caller decides what to do next (navigate, or stay). */
  markValidated: () => Promise<boolean>;
  validating: boolean;
  validationError: string | null;
}

/**
 * The review screen's whole data/decision lifecycle, shared by the routed
 * `/contracts/:contractId/review` screen (`./index.tsx`) and Review-as-a-state-of-Documents
 * (`../../documents/ReviewState.tsx`) -- the two orchestrators used to mirror this logic by hand,
 * which is how "Mark as validated" shipped as a navigation with no write behind it in both.
 *
 * **Fetch order**: `getContract360` first -- a `404` there is this screen's own named "not found"
 * state, distinct from a transport/5xx error, and short-circuits before anything else runs. Then
 * `getCorrectionHistory` (the durable "which fields has a human already corrected" signal) and
 * `getContractEvidence` (per-field confidence + source) together; each degrades independently.
 *
 * **Decision state has two sources, one durable and one not.** A field with a correction-history
 * entry is `"corrected"` -- persists across reloads, came from a real `PATCH`. A field the user
 * clicks "Accept" for is `"accepted"` -- session state, because the backend rejects a no-op
 * correction; it becomes durable when "Mark as validated" sends the accepted field names to
 * `POST /api/documents/{id}/validate`, which records them on the `document.validated` audit row and
 * moves the document to `Completed`.
 */
export function useReviewSession(
  apiClient: ApiClient,
  workspaceId: string | null,
  contractId: string | null,
  preferredDocumentId: string | null = null,
): ReviewSession {
  const [fetchState, setFetchState] = useState<ReviewFetchState>({ phase: "loading" });
  const [selectedField, setSelectedField] = useState<CorrectableFieldName | null>(null);
  const [acceptedThisSession, setAcceptedThisSession] = useState<ReadonlySet<CorrectableFieldName>>(new Set());
  const [correctionError, setCorrectionError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [validating, setValidating] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);

  const load = useCallback(() => {
    if (workspaceId === null || contractId === null) return;

    setFetchState({ phase: "loading" });

    void apiClient.getContract360(workspaceId, contractId).then(async (contractResult) => {
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
              ? "Raffa.ai's contract service is temporarily unavailable. Try again in a moment."
              : (contractResult.error ?? "The contract could not be loaded."),
        });
        return;
      }

      const [historyResult, evidenceResult] = await Promise.all([
        apiClient.getCorrectionHistory(workspaceId, contractId),
        apiClient.getContractEvidence(workspaceId, contractId),
      ]);

      setFetchState({
        phase: "ready",
        contract: contractResult.contract,
        history: historyResult?.ok && historyResult.history ? historyResult.history : [],
        evidence: evidenceResult?.ok && evidenceResult.evidence ? evidenceResult.evidence : [],
        historyDegraded: !historyResult?.ok,
        evidenceDegraded: !evidenceResult?.ok,
      });
    });
  }, [apiClient, workspaceId, contractId]);

  useEffect(() => {
    load();
  }, [load]);

  const rows = useMemo(
    () =>
      fetchState.phase === "ready"
        ? buildReviewFields(fetchState.contract, fetchState.history, acceptedThisSession, indexEvidence(fetchState.evidence))
        : [],
    [fetchState, acceptedThisSession],
  );
  const progress = useMemo(() => computeReviewProgress(rows), [rows]);
  const selectedRow = rows.find((row) => row.name === selectedField) ?? null;

  const reviewDocument = fetchState.phase === "ready" ? resolveReviewDocument(fetchState.contract, preferredDocumentId) : null;

  const correct = useCallback(
    async (name: CorrectableFieldName, newValue: string | null, reason: string) => {
      if (workspaceId === null || contractId === null) return;
      setSubmitting(true);
      setCorrectionError(null);
      const result = await apiClient.correctContract(workspaceId, contractId, {
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
    },
    [apiClient, workspaceId, contractId, load],
  );

  const accept = useCallback(
    async (name: CorrectableFieldName) => {
      const row = rows.find((candidate) => candidate.name === name);
      if (row?.proposalPending) {
        // The pipeline recorded this proposal but did not apply it (a supplier below the critical
        // bar): accepting it *is* the correction the backend is waiting for -- a real write that
        // links the supplier, which is what makes it show on the Documents row and in Ask.
        await correct(name, row.rawValue, "Accepted as extracted.");
        return;
      }
      setAcceptedThisSession((previous) => new Set(previous).add(name));
      setSelectedField(name);
    },
    [rows, correct],
  );

  const markValidated = useCallback(async () => {
    if (workspaceId === null || reviewDocument === null) {
      setValidationError("This contract has no document to validate.");
      return false;
    }
    setValidating(true);
    setValidationError(null);
    const result = await apiClient.validateDocument(workspaceId, reviewDocument.documentId, {
      acceptedFields: acceptedFieldNames(rows),
    });
    setValidating(false);
    if (!result.ok) {
      setValidationError(result.error ?? "The document could not be marked as validated.");
      return false;
    }
    return true;
  }, [apiClient, workspaceId, reviewDocument, rows]);

  return {
    fetchState,
    reload: load,
    rows,
    progress,
    selectedField,
    selectField: setSelectedField,
    selectedRow,
    accept,
    correct,
    correctionError,
    submitting,
    reviewDocumentId: reviewDocument?.documentId ?? null,
    alreadyValidated: reviewDocument?.processingStatus === "Completed",
    markValidated,
    validating,
    validationError,
  };
}
