import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import type { ApiClient, RenewalPipelineItemBody } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import RenewalTable from "./RenewalTable";
import InsightCard from "./InsightCard";
import { buildRenewalRows, formatRenewalsSummary, getRenewalActionPlan, RENEWALS_SUMMARY_OFF, type RenewalActionKind } from "./renewalPipelineViewModel";
import { loadTrackedRenewalActions, rememberRenewalAction, type TrackedRenewalAction } from "./renewalActionStore";
import "./renewals.css";

export interface RenewalsRouteProps {
  apiClient: ApiClient;
  /**
   * Threaded from `../../components/shell/WorkspaceShellApp.tsx` (the same `account.username`
   * `RailNav.tsx` renders) -- this screen's own "who acted" identity for every action's required
   * `owner` field (see `renewalPipelineViewModel.ts#getRenewalActionPlan`'s own doc comment).
   */
  userLabel: string;
}

type FetchState =
  | { phase: "loading" }
  | { phase: "error"; statusCode: number | null; message: string }
  | { phase: "ready"; items: readonly RenewalPipelineItemBody[]; scores: Readonly<Record<string, number | null>> };

/**
 * Route `/renewals` -- Renewals, V2 (ADR-024 V2 IA amending ADR-020 screen 8; screens-v2.md #7;
 * `raffa-v2/markup.html` "RENEWALS" block, `app.jsx` `renewals` / `rsel` / `rnSummary` / `rAct`).
 * Replaces the Day-1 "Renewal pipeline" (threshold strip, seven-column table, six-fact insight card
 * with three actions) with the prototype's own shape: a header ("Renewals" + `rnSummary`), the list
 * sorted by priority, the selected row's "Why it is here" pane with Start negotiation / Assign to
 * me, and -- while nothing has validated dates -- the tier's reroute state (R-WEB-02): "No renewal
 * dates yet · Renewals are computed from validated end dates and notice periods. Upload a contract
 * to start. · Upload a contract".
 *
 * **Fetch order.** `getRenewals` first -- a non-2xx there is this screen's own error state. Then
 * every row's `GET /api/renewals/{contractId}/priority` score together via `Promise.all` before
 * declaring `"ready"` (the Score column is the list's own sort key). No bulk priority endpoint
 * exists, so this is N calls, bounded by the underlying portfolio page ceiling; one row's fetch
 * failing degrades only that row's score to "—" (it then sorts last), never the whole screen.
 *
 * **Status shared with the Contract 360 tracker** (`racts`): the real write is
 * `POST /api/renewals/{id}/action`; this browser remembers it in `renewalActionStore.ts` so the list's
 * Status column, the pane's acted state and Contract 360 read the same decision.
 */
export default function RenewalsRoute({ apiClient, userLabel }: RenewalsRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });
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
          // Same 503-vs-other split as ../contracts/index.tsx (ADR-019 accessibility baseline:
          // "names the failing job, never a raw stack trace").
          message:
            result.statusCode === 503 || result.statusCode === null
              ? "Raffa's renewal engine is temporarily unavailable. Try again in a moment."
              : (result.error ?? "Renewals could not be loaded."),
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
    // fresh object every call, the same convention every other route's own load() callback follows.
  }, [apiClient, workspace?.id]);

  useEffect(() => {
    load();
  }, [load]);

  const trackedByContract = useMemo(() => {
    const map = new Map<string, TrackedRenewalAction>();
    for (const tracked of trackedActions) map.set(tracked.contractId, tracked);
    return map;
  }, [trackedActions]);

  const rows = useMemo(
    () => (fetchState.phase === "ready" ? buildRenewalRows(fetchState.items, fetchState.scores, trackedByContract) : []),
    [fetchState, trackedByContract],
  );

  if (!workspace) {
    // Should not normally be reachable -- App.tsx only mounts the shell (and therefore this route)
    // once a workspace is current -- but this route reads the store directly rather than trusting
    // that earlier check, the same defensive convention every other route under `src/routes/` follows.
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before viewing renewals.</p>
      </div>
    );
  }

  // The pane always follows a real selection (`app.jsx`: `rsel = renewals.find(r=>r.id===s.rsel) ||
  // renewals[0]`): an explicit click wins while its row is still listed, else the top-priority row.
  const effectiveSelectedId = rows.some((row) => row.item.contractId === selectedContractId)
    ? selectedContractId
    : (rows[0]?.item.contractId ?? null);
  const selectedRow = rows.find((row) => row.item.contractId === effectiveSelectedId) ?? null;

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

        setTrackedActions(
          rememberRenewalAction({
            contractId: result.action.contractId,
            supplierId: selected.supplierId,
            annualSpend: selected.annualSpend,
            owner: result.action.owner,
            status: result.action.status,
            action: result.action.action,
            updatedAt: result.action.updatedAt,
          }),
        );
      });
  };

  const ready = fetchState.phase === "ready";

  return (
    <div className="renewal-screen">
      <header className="screen-header">
        <div>
          <h2 className="screen-title">Renewals</h2>
          <p className="screen-header-summary">
            {ready ? formatRenewalsSummary(rows.length) : fetchState.phase === "loading" ? "Loading renewals…" : RENEWALS_SUMMARY_OFF}
          </p>
        </div>
      </header>

      {fetchState.phase === "loading" && (
        <div className="renewal-skeleton" role="status" aria-live="polite">
          <p className="micro-meta">Loading renewals…</p>
          {Array.from({ length: 6 }, (_, index) => (
            <div key={index} className="skeleton renewal-skeleton-row" />
          ))}
        </div>
      )}

      {fetchState.phase === "error" && (
        <div className="error-state" role="alert">
          <h4>Renewals unavailable</h4>
          <p className="micro-meta">
            {fetchState.message}
            {fetchState.statusCode !== null && ` (HTTP ${fetchState.statusCode})`}
          </p>
          <button type="button" className="btn btn-secondary" onClick={load}>
            Retry
          </button>
        </div>
      )}

      {ready && rows.length === 0 && (
        // markup.html `kbOff`: the tier's reroute state -- copy verbatim.
        <div className="screen-reroute" role="status">
          <h3>No renewal dates yet</h3>
          <p>Renewals are computed from validated end dates and notice periods. Upload a contract to start.</p>
          <Link to="/documents" className="btn btn-primary">
            Upload a contract
          </Link>
        </div>
      )}

      {ready && rows.length > 0 && (
        <div className="renewal-screen-body">
          <RenewalTable rows={rows} selectedContractId={effectiveSelectedId} onSelect={setSelectedContractId} />
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
    </div>
  );
}
