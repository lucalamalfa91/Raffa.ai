import { useCallback, useEffect, useState } from "react";
import { Link, useLocation, useParams } from "react-router-dom";
import type { ApiClient, Contract360Body, RenewalActionRow, RenewalPipelineItemBody, RenewalPriorityBody } from "../../../api/client";
import { loadCurrentWorkspace } from "../../signin/workspaceStore";
import { CHECK_AGAIN_LABEL, UPDATES_PAUSED_NOTICE, usePollBudget } from "../../../components/shell/usePollBudget";
import { getRenewalActionPlan, savedActionOnScreen, type RenewalActionKind } from "../../renewals/renewalPipelineViewModel";
import AnswersBand from "./AnswersBand";
import Contract360Header from "./Contract360Header";
import DetailsSection from "./DetailsSection";
import WhyClauses from "./WhyClauses";
import {
  buildAnswers,
  buildNegotiationSteps,
  resolveBackLink,
  resolveHighlightedClauseId,
  resolveReadinessCopy,
  resolveSupplierLabel,
  ticksFromServer,
} from "./contract360ViewModel";
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
 * `userLabel`); the tracker and Renewals both read `savedAction` on `GET /api/renewals`. "Undo"
 * writes ticks empty first, then posts `NotStarted` / "Open". Ticks are named keys on
 * `GET`/`PUT /api/contracts/{id}/negotiation-steps`.
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
  const [tracked, setTracked] = useState<RenewalActionRow | null>(null);
  const [tickedKeys, setTickedKeys] = useState<ReadonlySet<string>>(() => new Set());
  const [actionPending, setActionPending] = useState<RenewalActionKind | "undo" | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [stepsError, setStepsError] = useState<string | null>(null);

  const load = useCallback((silent = false) => {
    if (!workspace || !contractId) return;

    // A silent re-read (the "still being prepared" poll) refreshes the fetched contract in place --
    // no skeleton flash every 2 s, no reset of the drawer or the tracker.
    if (!silent) {
      setFetchState({ phase: "loading" });
      setDetailsOpen(false);
      setTracked(null);
      setTickedKeys(new Set());
      setActionError(null);
      setStepsError(null);
    }

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
              ? "Raffa.ai's contract service is temporarily unavailable. Try again in a moment."
              : (result.error ?? "The contract could not be loaded."),
        });
        return;
      }

      const contract = result.contract;
      // Citation landing: select the cited clause before the first paint of the ready state.
      setSelectedClauseId(resolveHighlightedClauseId(contract.tabs.clauses, clauseParam, pageParam));

      const [renewalsResult, priorityResult, stepsResult] = await Promise.all([
        apiClient.getRenewals(workspace.id),
        apiClient.getRenewalPriority(workspace.id, contractId),
        apiClient.getNegotiationSteps(workspace.id, contractId),
      ]);

      const items = renewalsResult.ok && renewalsResult.renewals ? renewalsResult.renewals.items : [];
      setTracked(savedActionOnScreen(items.find((row) => row.contractId === contractId)?.savedAction));
      setTickedKeys(ticksFromServer(stepsResult.ok && stepsResult.steps ? stepsResult.steps : []));
      if (!stepsResult.ok && stepsResult.statusCode !== 404) {
        setStepsError("The negotiation steps could not be loaded.");
      } else if (!silent) {
        setStepsError(null);
      }

      setFetchState({
        phase: "ready",
        contract,
        renewals: items,
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

  // ADR-020 w15 §2.3 / §8.3: while the server says the contract is still being prepared, re-read it
  // on the shared 2 s cadence under the five-minute no-change budget; once spent, the block keeps
  // its heading and sentence and adds "Check again" beside its CTA.
  const readiness = fetchState.phase === "ready" ? fetchState.contract.readiness : null;
  const silentLoad = useCallback(() => load(true), [load]);
  const { paused: updatesPaused, resume: resumeUpdates } = usePollBudget({
    active: readiness !== null && readiness.state === "processing",
    fingerprint: readiness === null ? "none" : `${readiness.state}/${readiness.stage ?? ""}/${readiness.documentCount}/${readiness.completedDocumentCount}`,
    onTick: silentLoad,
  });

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
        <button type="button" className="btn btn-secondary" onClick={() => load()}>
          Retry
        </button>
      </div>
    );
  }

  const { contract, renewals, priority } = fetchState;
  const { header, tabs } = contract;

  // The fifth state (ADR-020 w15 §2.3), driven by the server's `readiness` and never by an empty
  // clause array: a contract mid-pipeline, or one whose documents all ended without a validated
  // fact, is told so instead of being rendered as an empty aggregate.
  const readinessCopy = resolveReadinessCopy(contract.readiness);
  if (readinessCopy !== null) {
    return (
      <div className="contract360-screen">
        <div className="screen-reroute contract360-not-ready" role="status">
          <h3>{readinessCopy.heading}</h3>
          <p>{readinessCopy.sentence}</p>
          {updatesPaused && <p className="hint">{UPDATES_PAUSED_NOTICE}</p>}
          <div className="screen-reroute-actions">
            <Link to="/documents" className="btn btn-secondary">
              Go to Documents
            </Link>
            {updatesPaused && (
              <button type="button" className="btn btn-secondary" onClick={resumeUpdates}>
                {CHECK_AGAIN_LABEL}
              </button>
            )}
          </div>
        </div>
      </div>
    );
  }
  const answers = buildAnswers(header, tabs.renewal, renewals);
  const steps = buildNegotiationSteps(resolveSupplierLabel(header).label, answers.move.deadline);

  const postAction = (status: RenewalActionRow["status"], action: string, pending: RenewalActionKind | "undo") => {
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
      setTracked(savedActionOnScreen(saved));
    });
  };

  const reloadSteps = () => {
    setStepsError(null);
    void apiClient.getNegotiationSteps(workspace.id, contractId).then((result) => {
      if (!result.ok) {
        setStepsError("The negotiation steps could not be loaded.");
        return;
      }
      setTickedKeys(ticksFromServer(result.steps ?? []));
    });
  };

  const handleUndo = () => {
    setActionPending("undo");
    setActionError(null);
    setStepsError(null);
    void apiClient.putNegotiationSteps(workspace.id, contractId, { steps: [] }).then(async (ticks) => {
      if (!ticks.ok) {
        setActionPending(null);
        setStepsError("The negotiation steps could not be saved.");
        return;
      }
      setTickedKeys(new Set());
      const saved = await postAction("NotStarted", "Open", "undo");
      if (saved !== null) setTracked(savedActionOnScreen(saved));
    });
  };

  const handleToggleStep = (key: string) => {
    const previous = tickedKeys;
    const next = new Set(previous);
    if (next.has(key)) next.delete(key);
    else next.add(key);
    setTickedKeys(next);
    setStepsError(null);
    void apiClient.putNegotiationSteps(workspace.id, contractId, { steps: [...next] }).then(async (putResult) => {
      if (!putResult.ok) {
        setTickedKeys(previous);
        setStepsError("The negotiation steps could not be saved.");
        return;
      }
      const readBack = await apiClient.getNegotiationSteps(workspace.id, contractId);
      if (readBack.ok) setTickedKeys(ticksFromServer(readBack.steps ?? []));
    });
  };

  return (
    <div className="contract360-screen">
      <Contract360Header header={header} currency={tabs.commercials.currency} docCount={tabs.documents.length} backLink={backLink} />

      <AnswersBand
        answers={answers}
        tracked={tracked}
        steps={steps}
        tickedKeys={tickedKeys}
        actionPending={actionPending}
        actionError={actionError}
        stepsError={stepsError}
        onAction={handleAction}
        onUndo={handleUndo}
        onToggleStep={handleToggleStep}
        onRetrySteps={reloadSteps}
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
