import { useCallback, useEffect, useState } from "react";
import { useLocation, useParams } from "react-router-dom";
import type { ApiClient, Contract360Body, RenewalPipelineItemBody, RenewalPriorityBody } from "../../../api/client";
import { loadCurrentWorkspace } from "../../signin/workspaceStore";
import { forgetRenewalAction, getTrackedRenewalAction, rememberRenewalAction, type TrackedRenewalAction } from "../../renewals/renewalActionStore";
import { getRenewalActionPlan, type RenewalActionKind } from "../../renewals/renewalPipelineViewModel";
import AnswersBand from "./AnswersBand";
import Contract360Header from "./Contract360Header";
import DetailsSection from "./DetailsSection";
import WhyClauses from "./WhyClauses";
import { buildAnswers, buildNegotiationSteps, resolveBackLink, resolveHighlightedClauseId, resolveSupplierLabel } from "./contract360ViewModel";
import { clearNegotiationSteps, loadNegotiationSteps, saveNegotiationSteps } from "./negotiationStepsStore";
import "./contract360.css";

export interface Contract360RouteProps {
  apiClient: ApiClient;
  /** The signed-in user (`../../../components/shell/WorkspaceShellApp.tsx` `account.username`) -- the `owner` of every action posted from here. */
  userLabel: string;
}

type FetchState =
  | { phase: "loading" }
  | { phase: "not-found" }
  | { phase: "error"; statusCode: number | null; message: string }
  | {
      phase: "ready";
      contract: Contract360Body;
      renewals: readonly RenewalPipelineItemBody[];
      priority: RenewalPriorityBody | null;
    };

/**
 * Route `/contracts/:contractId` -- Contract 360, V2 "no tabs" (ADR-024 V2 IA; screens-v2.md #5;
 * `raffa-v2/markup.html` "CONTRACT 360 — three answers, then proof, then details"). One page:
 * header (origin back link · supplier · title · meta), the answers band (Where you can save · When
 * you must move · What to do, with Start negotiation / Assign to me or the tracker once acted),
 * "Why — the clauses behind it" with the selected clause's original wording, and the "Details ▾"
 * drawer (key terms, documents, facts to decide, priority score, extracted lists).
 *
 * **Fetch order**: `getContract360` first -- a `404` is this screen's own "not found" state and
 * short-circuits the rest. Then `getRenewals` (for this contract's recommendation) and
 * `getRenewalPriority` together, both independently optional: either failing degrades its own
 * answer to an honest "not yet" rather than failing the screen.
 *
 * **Citation landing (R-EVD-02)**: `?clause=<clauseId>` / `?page=<n>` pre-select a real clause so
 * its wording is highlighted without a click; `location.state.from` drives the back label.
 *
 * **Actions** are the same real write Renewals makes (`POST /api/renewals/{id}/action`, owner =
 * `userLabel`), mirrored in `renewalActionStore.ts` so the list, pane, Savings and this tracker
 * agree. "Undo" forgets the mirror and re-posts the action as NotStarted / "Open" -- the list's own
 * default label -- so the server is not left saying "In negotiation".
 */
export default function Contract360Route({ apiClient, userLabel }: Contract360RouteProps) {
  const { contractId } = useParams<{ contractId: string }>();
  const location = useLocation();
  const workspace = loadCurrentWorkspace();

  const searchParams = new URLSearchParams(location.search);
  const clauseParam = searchParams.get("clause");
  const pageParam = searchParams.get("page");
  const fromState = (location.state as { from?: unknown } | null)?.from;
  const backLink = resolveBackLink(fromState);

  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });
  const [selectedClauseId, setSelectedClauseId] = useState<string | null>(null);
  const [detailsOpen, setDetailsOpen] = useState(false);
  const [tracked, setTracked] = useState<TrackedRenewalAction | null>(() => (contractId ? getTrackedRenewalAction(contractId) : null));
  const [stepsDone, setStepsDone] = useState<boolean[]>(() => (contractId ? loadNegotiationSteps(contractId) : []));
  const [actionPending, setActionPending] = useState<RenewalActionKind | "undo" | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(() => {
    if (!workspace || !contractId) return;

    setFetchState({ phase: "loading" });
    setDetailsOpen(false);
    setTracked(getTrackedRenewalAction(contractId));
    setStepsDone(loadNegotiationSteps(contractId));
    setActionError(null);

    void apiClient.getContract360(workspace.id, contractId).then(async (result) => {
      if (!result.ok || !result.contract) {
        if (result.statusCode === 404) {
          setFetchState({ phase: "not-found" });
          return;
        }
        setFetchState({
          phase: "error",
          statusCode: result.statusCode,
          message:
            result.statusCode === 503 || result.statusCode === null
              ? "Raffa's contract service is temporarily unavailable. Try again in a moment."
              : (result.error ?? "The contract could not be loaded."),
        });
        return;
      }

      const contract = result.contract;
      // Citation landing: select the cited clause before the first paint of the ready state.
      setSelectedClauseId(resolveHighlightedClauseId(contract.tabs.clauses, clauseParam, pageParam));

      const [renewalsResult, priorityResult] = await Promise.all([
        apiClient.getRenewals(workspace.id),
        apiClient.getRenewalPriority(workspace.id, contractId),
      ]);

      setFetchState({
        phase: "ready",
        contract,
        renewals: renewalsResult.ok && renewalsResult.renewals ? renewalsResult.renewals.items : [],
        priority: priorityResult.ok ? priorityResult.priority : null,
      });
    });
    // Depends on workspace?.id/contractId (primitives), not workspace itself: loadCurrentWorkspace()
    // returns a fresh object every call. clauseParam/pageParam come from location.search, which
    // react-router keeps stable for the lifetime of one navigation entry.
  }, [apiClient, workspace?.id, contractId, clauseParam, pageParam]);

  useEffect(() => {
    load();
  }, [load]);

  if (!workspace) {
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before viewing a contract.</p>
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
      <div className="contract360-skeleton" role="status" aria-live="polite">
        <p className="micro-meta">Loading contract…</p>
        {Array.from({ length: 6 }, (_, index) => (
          <div key={index} className="skeleton contract360-skeleton-row" />
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
        <h4>Contract unavailable</h4>
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

  const { contract, renewals, priority } = fetchState;
  const { header, tabs } = contract;
  const answers = buildAnswers(header, tabs.renewal, renewals);
  const pipelineItem = renewals.find((r) => r.contractId === contractId) ?? null;
  const steps = buildNegotiationSteps(resolveSupplierLabel(header).label, answers.move.deadline);

  const postAction = (status: TrackedRenewalAction["status"], action: string, pending: RenewalActionKind | "undo") => {
    setActionPending(pending);
    setActionError(null);
    return apiClient.postRenewalAction(workspace.id, contractId, { owner: userLabel, status, action }).then((result) => {
      setActionPending(null);
      if (!result.ok || !result.action) {
        setActionError(result.error ?? "This action could not be saved. Try again.");
        return null;
      }
      return result.action;
    });
  };

  const handleAction = (kind: RenewalActionKind) => {
    const plan = getRenewalActionPlan(kind);
    void postAction(plan.status, plan.action, kind).then((saved) => {
      if (saved === null) return;
      const next: TrackedRenewalAction = {
        contractId: saved.contractId,
        supplierId: pipelineItem?.supplierId ?? header.supplierId,
        annualSpend: pipelineItem?.annualSpend ?? header.annualSpend,
        owner: saved.owner,
        status: saved.status,
        action: saved.action,
        updatedAt: saved.updatedAt,
      };
      rememberRenewalAction(next);
      setTracked(next);
    });
  };

  const handleUndo = () => {
    void postAction("NotStarted", "Open", "undo").then((saved) => {
      if (saved === null) return;
      forgetRenewalAction(contractId);
      clearNegotiationSteps(contractId);
      setTracked(null);
      setStepsDone(loadNegotiationSteps(contractId));
    });
  };

  const handleToggleStep = (index: number) => {
    setStepsDone((previous) => {
      const next = previous.map((done, i) => (i === index ? !done : done));
      return saveNegotiationSteps(contractId, next);
    });
  };

  return (
    <div className="contract360-screen">
      <Contract360Header header={header} currency={tabs.commercials.currency} docCount={tabs.documents.length} backLink={backLink} />

      <AnswersBand
        answers={answers}
        tracked={tracked}
        steps={steps}
        stepsDone={stepsDone}
        actionPending={actionPending}
        actionError={actionError}
        onAction={handleAction}
        onUndo={handleUndo}
        onToggleStep={handleToggleStep}
      />

      <WhyClauses
        contractId={contractId}
        clauses={tabs.clauses}
        documents={tabs.documents}
        selectedClauseId={selectedClauseId}
        onSelect={setSelectedClauseId}
      />

      <DetailsSection contract={contract} priority={priority} open={detailsOpen} onToggle={() => setDetailsOpen((open) => !open)} />
    </div>
  );
}
