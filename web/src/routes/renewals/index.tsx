import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import type { ApiClient, RenewalPipelineItemBody } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import ThresholdStrip from "./ThresholdStrip";
import RenewalTable from "./RenewalTable";
import InsightCard from "./InsightCard";
import {
  applyRenewalWindowFilter,
  computeRenewalWindowCounts,
  getRenewalActionPlan,
  type RenewalActionKind,
  type RenewalTableRow,
} from "./renewalPipelineViewModel";
import { loadTrackedRenewalActions, rememberRenewalAction, type TrackedRenewalAction } from "./renewalActionStore";
import "./renewals.css";

export interface RenewalsRouteProps {
  apiClient: ApiClient;
  /**
   * Threaded from `../../components/shell/WorkspaceShellApp.tsx` (the same `account.username`
   * `RailNav.tsx` already renders) -- this screen's own "who acted" identity for every action's
   * required `owner` field (AC-3; see `renewalPipelineViewModel.ts#getRenewalActionPlan`'s own
   * header comment for why there is no separate assignee picker in V1).
   */
  userLabel: string;
}

/** Matches the underlying `GET /api/contracts` (auto-renewing filter) page-size ceiling `GET /api/renewals` itself reuses (`PortfolioPageRequest.MaxPageSize`) -- see that operation's own OpenAPI description. Not sent as a query parameter (the endpoint takes none); named here only for this screen's own doc comments. */
const RENEWAL_PIPELINE_PAGE_SIZE_CEILING = 100;

type FetchState =
  | { phase: "loading" }
  | { phase: "error"; statusCode: number | null; message: string }
  | { phase: "ready"; items: readonly RenewalPipelineItemBody[]; scores: Readonly<Record<string, number | null>> };

/**
 * Route `/renewals` (ADR-018; screens.md #8 "Renewal pipeline"; ADR-020 screen 8; task
 * E08/F01/US01/T01, us-01-renewal-pipeline AC-1 threshold strip / AC-2 table / AC-3 insight card +
 * actions / AC-4 states). Wired into `../../components/shell/WorkspaceShellApp.tsx`'s `renewals`
 * route in place of that shell task's `ScaffoldScreen` placeholder, the same seam `../contracts/index.tsx`
 * (PortfolioRoute) and `../documents/index.tsx` already used for their own routes.
 *
 * **Fetch order.** `getRenewals` first -- a non-2xx there is this screen's own AC-4 "error (engine
 * unavailable)" state. Once the pipeline itself resolves, this screen fetches every row's own
 * `GET /api/renewals/{contractId}/priority` score together via `Promise.all` before declaring
 * `"ready"` (AC-2's own "Score" column is a named, required column, not an optional extra) -- the
 * same "fetch order, not fetch-once" shape `../contracts/contract360/index.tsx` already uses for its
 * own contract-then-(renewals+priority) sequence. No bulk priority endpoint exists on the backend
 * (`GET /api/renewals/{contractId}/priority` only ever answers for one contract at a time -- see
 * that operation's own OpenAPI description), so this is genuinely N calls, not one; every one of
 * them is real, honest data, never fabricated, and `RENEWAL_PIPELINE_PAGE_SIZE_CEILING` names the
 * same bound the underlying portfolio query already caps the pipeline at, so N is never unbounded.
 * A single row's own priority fetch failing degrades only that row's Score cell to "-"
 * (`renewalPipelineViewModel.ts#formatScore`), never the whole screen -- `apiClient` calls never
 * throw, so `Promise.all` here cannot reject either.
 */
export default function RenewalsRoute({ apiClient, userLabel }: RenewalsRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });
  const [activeWindow, setActiveWindow] = useState<string | null>(null);
  const [selectedContractId, setSelectedContractId] = useState<string | null>(null);
  const [trackedActions, setTrackedActions] = useState<readonly TrackedRenewalAction[]>(() => loadTrackedRenewalActions());
  const [actionPending, setActionPending] = useState<RenewalActionKind | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(() => {
    if (!workspace) return;

    setFetchState({ phase: "loading" });
    setActionError(null);

    void apiClient.getRenewals(workspace.id).then(async (result) => {
      if (!result.ok || !result.renewals) {
        setFetchState({
          phase: "error",
          statusCode: result.statusCode,
          // Same 503-vs-other split as ../contracts/index.tsx's own loadPortfolio (ADR-019
          // accessibility baseline: "names the failing job, never a raw stack trace"), naming the
          // renewal engine specifically per screens.md #8's own "error (engine unavailable)".
          message:
            result.statusCode === 503 || result.statusCode === null
              ? "Contigo's renewal engine is temporarily unavailable. Try again in a moment."
              : (result.error ?? "The renewal pipeline could not be loaded."),
        });
        return;
      }

      const items = result.renewals.items;
      const scoreEntries = await Promise.all(
        items.map((item) =>
          apiClient
            .getRenewalPriority(workspace.id, item.contractId)
            .then((priorityResult): readonly [string, number | null] => [
              item.contractId,
              priorityResult.ok && priorityResult.priority ? priorityResult.priority.totalScore : null,
            ]),
        ),
      );

      setFetchState({ phase: "ready", items, scores: Object.fromEntries(scoreEntries) });
    });
    // Depends on workspace?.id (a primitive), not workspace itself: loadCurrentWorkspace() returns a
    // fresh object every call, the same convention ../contracts/index.tsx's own loadPortfolio uses.
  }, [apiClient, workspace?.id]);

  useEffect(() => {
    load();
  }, [load]);

  const trackedByContract = useMemo(() => {
    const map = new Map<string, TrackedRenewalAction>();
    for (const tracked of trackedActions) map.set(tracked.contractId, tracked);
    return map;
  }, [trackedActions]);

  if (!workspace) {
    // Should not normally be reachable -- App.tsx only mounts the shell (and therefore this route)
    // once a workspace is current -- but this route reads the store directly rather than trusting
    // that earlier check, the same defensive convention every other route under `src/routes/` follows.
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before viewing the renewal pipeline.</p>
      </div>
    );
  }

  const items = fetchState.phase === "ready" ? fetchState.items : [];
  const scores = fetchState.phase === "ready" ? fetchState.scores : {};
  const windowCounts = computeRenewalWindowCounts(items);
  const filteredItems = applyRenewalWindowFilter(items, activeWindow);
  const visibleRows: RenewalTableRow[] = filteredItems.map((item) => ({
    item,
    score: scores[item.contractId] ?? null,
    tracked: trackedByContract.get(item.contractId) ?? null,
  }));
  // The insight card always follows a real selection (AC-3 shows one as soon as the pipeline is
  // populated, matching day1-demo.html's own `rsel=allRenewals.find(...)||allRenewals[0]`): an
  // explicit click (`selectedContractId`) wins as long as its row is still visible under the current
  // threshold filter; otherwise this falls back to the first visible row, and self-heals the next
  // time the filter changes again -- no separate effect needed to keep the two in sync.
  const effectiveSelectedId = visibleRows.some((row) => row.item.contractId === selectedContractId)
    ? selectedContractId
    : (visibleRows[0]?.item.contractId ?? null);
  const selectedRow = visibleRows.find((row) => row.item.contractId === effectiveSelectedId) ?? null;

  const handleToggleWindow = (key: string) => {
    setActiveWindow((current) => (current === key ? null : key));
  };

  // Always acts on `selectedRow` (the effective selection above), never the raw `selectedContractId`
  // state alone -- a user who never explicitly clicked a row (the common case: AC-3's insight card
  // is visible from the moment the pipeline populates) still has a real, correct selection to act on.
  const handleAction = (kind: RenewalActionKind) => {
    if (!selectedRow) return;
    const selected = selectedRow.item;

    const plan = getRenewalActionPlan(kind);
    setActionPending(kind);
    setActionError(null);

    void apiClient
      .postRenewalAction(workspace.id, selected.contractId, { owner: userLabel, status: plan.status, action: plan.action })
      .then((result) => {
        setActionPending(null);

        if (!result.ok || !result.action) {
          setActionError(result.error ?? "This action could not be saved. Try again.");
          return;
        }

        const tracked: TrackedRenewalAction = {
          contractId: result.action.contractId,
          supplierId: selected.supplierId,
          annualSpend: selected.annualSpend,
          owner: result.action.owner,
          status: result.action.status,
          action: result.action.action,
          updatedAt: result.action.updatedAt,
        };
        setTrackedActions(rememberRenewalAction(tracked));
      });
  };

  return (
    <div className="renewal-screen">
      <p className="screen-kicker">R2</p>
      <h2 className="screen-title">Renewal pipeline</h2>
      <p className="micro-meta">
        {fetchState.phase === "ready" && `${items.length} renewal${items.length === 1 ? "" : "s"} in the pipeline`}
        {fetchState.phase !== "ready" && "Priority, deadlines, and the insight card for every auto-renewing contract."}
      </p>

      {fetchState.phase === "loading" && (
        <div className="renewal-skeleton" role="status" aria-live="polite">
          <p className="micro-meta">Loading renewal pipeline…</p>
          {Array.from({ length: 6 }, (_, index) => (
            <div key={index} className="skeleton renewal-skeleton-row" />
          ))}
        </div>
      )}

      {fetchState.phase === "error" && (
        <div className="error-state" role="alert">
          <h4>Renewal pipeline unavailable</h4>
          <p className="micro-meta">
            {fetchState.message}
            {fetchState.statusCode !== null && ` (HTTP ${fetchState.statusCode})`}
          </p>
          <button type="button" className="btn btn-secondary" onClick={load}>
            Retry
          </button>
        </div>
      )}

      {fetchState.phase === "ready" && (
        <>
          <ThresholdStrip buckets={windowCounts} activeKey={activeWindow} onToggle={handleToggleWindow} />

          {items.length === 0 && (
            <div className="empty-state" role="status">
              <h3>No renewals in your pipeline yet</h3>
              <p className="micro-meta">
                Renewals appear here once an auto-renewing contract is in your portfolio, up to
                {` ${RENEWAL_PIPELINE_PAGE_SIZE_CEILING}`} at a time.
              </p>
              <Link to="/contracts" className="btn btn-primary">
                View portfolio
              </Link>
            </div>
          )}

          {items.length > 0 && visibleRows.length === 0 && (
            <div className="empty-state" role="status">
              <h3>No renewals in this window</h3>
              <p className="micro-meta">Try a different threshold, or clear it to see the whole pipeline.</p>
              <button type="button" className="btn btn-secondary" onClick={() => setActiveWindow(null)}>
                Show all renewals
              </button>
            </div>
          )}

          {visibleRows.length > 0 && (
            <div className="renewal-screen-body">
              <RenewalTable rows={visibleRows} selectedContractId={effectiveSelectedId} onSelect={setSelectedContractId} />
              {selectedRow && (
                <InsightCard
                  item={selectedRow.item}
                  tracked={selectedRow.tracked}
                  actionPending={actionPending}
                  actionError={actionError}
                  onAction={handleAction}
                />
              )}
            </div>
          )}
        </>
      )}
    </div>
  );
}
