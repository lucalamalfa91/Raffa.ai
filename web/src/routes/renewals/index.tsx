import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import type { ApiClient, RenewalPipelineItemBody } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import RenewalTable from "./RenewalTable";
import InsightCard from "./InsightCard";
import ReadinessFilter from "../../components/ReadinessFilter";
import {
  countReadiness,
  DEFAULT_READINESS_FILTER,
  filterByReadiness,
  getReadinessEmptyCopy,
  type ReadinessFilterValue,
} from "../../components/readiness";
import {
  buildRenewalRows,
  formatRenewalsSummary,
  getRenewalActionPlan,
  isRenewalItemReady,
  RENEWALS_SUMMARY_OFF,
  type RenewalActionKind,
} from "./renewalPipelineViewModel";
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
 * **Fetch order.** `getRenewals` first -- a non-2xx there is this screen's own error state. The
 * pipeline paints as soon as that list returns; each row's `GET /api/renewals/{contractId}/priority`
 * score fills in after (the Score column is the list's own sort key). No bulk priority endpoint
 * exists, so this is N calls, bounded by the underlying portfolio page ceiling; one row's fetch
 * failing degrades only that row's score to "—" (it then sorts last), never the whole screen.
 *
 * **Status shared with the Contract 360 tracker** (`racts`): the real write is
 * `POST /api/renewals/{id}/action`; every surface reads `savedAction` on the same
 * `GET /api/renewals` row.
 *
 * **`?select=` deep link (task E29/F03/US01/T01, NW-84; ADR-012 cl. 51 per
 * `reports/architecture/waves/w19.md` -- the ADR-012 body's own w19 footer text was not yet
 * transcribed when this task ran, so the wave file is the citable source of the decision).** A chat
 * reply can inject `/renewals?select={contractId}` (`Raffa.Chat`'s `CapabilityRouting.BuildHref`,
 * `CapabilityCatalog.RenewalsKey`); see `effectiveSelectedId` below for how an unmatched value falls
 * back to the same top-priority default the no-query case has always used.
 *
 * **Readiness filter.** The pipeline still carries contracts whose dates are not yet determined
 * (`CannotDetermine` -- still in review / not analyzed). A compact `.seg` (Ready / To review / All)
 * defaults to already-OK (`Determined`) so the list is usable; the still-to-review bucket is one
 * click away, never hidden forever.
 */
export default function RenewalsRoute({ apiClient, userLabel }: RenewalsRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [searchParams] = useSearchParams();
  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });
  // Seeds the initial selection from the `?select=` deep link above, read once via the lazy
  // initializer -- the same "read once, on mount" convention `routes/documents/index.tsx`'s
  // `?filter=` already uses -- so a later click (`onSelect` below) is never fought by a stale URL on
  // re-render. A value matching no row (absent, malformed, or another tenant's contract -- `rows` is
  // already this tenant's own `GET /api/renewals` list, so a foreign id simply never appears in it)
  // is handled entirely by `effectiveSelectedId`'s existing fallback: no 500, no leak, no special
  // case needed here.
  const [selectedContractId, setSelectedContractId] = useState<string | null>(() => searchParams.get("select"));
  const [actionPending, setActionPending] = useState<RenewalActionKind | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [readiness, setReadiness] = useState<ReadinessFilterValue>(DEFAULT_READINESS_FILTER);

  const load = useCallback(() => {
    if (!workspace) return;

    setFetchState({ phase: "loading" });
    setActionError(null);

    void apiClient.getRenewals(workspace.id).then(async (result) => {
      if (!result.ok || !result.renewals) {
        setFetchState({
          phase: "error",
          statusCode: result.statusCode,
          message:
            result.statusCode === 503 || result.statusCode === null
              ? "Raffa.ai's renewal engine is temporarily unavailable. Try again in a moment."
              : (result.error ?? "Renewals could not be loaded."),
        });
        return;
      }

      const items = result.renewals.items;
      // Paint the pipeline as soon as the list is back -- waiting on N priority calls kept the
      // screen on "Loading renewals…" for the whole fan-out (the same stall Portfolio hits when
      // GET /api/contracts hangs). Scores fill in after; a failed row stays "—".
      setFetchState({ phase: "ready", items, scores: {} });

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

      setFetchState((current) =>
        current.phase === "ready" ? { ...current, scores: Object.fromEntries(scoreEntries) } : current,
      );
    });
    // Depends on workspace?.id (a primitive), not workspace itself: loadCurrentWorkspace() returns a
    // fresh object every call, the same convention every other route's own load() callback follows.
  }, [apiClient, workspace?.id]);

  useEffect(() => {
    load();
  }, [load]);

  const rows = useMemo(
    () => (fetchState.phase === "ready" ? buildRenewalRows(fetchState.items, fetchState.scores) : []),
    [fetchState],
  );
  const readinessCounts = useMemo(
    () => countReadiness(rows.filter((row) => isRenewalItemReady(row.item)).length, rows.filter((row) => !isRenewalItemReady(row.item)).length),
    [rows],
  );
  const visibleRows = useMemo(() => filterByReadiness(rows, readiness, (row) => isRenewalItemReady(row.item)), [rows, readiness]);

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
  // renewals[0]`): an explicit click, or the initial `?select=` deep link seeded above, wins while
  // its row is still listed; an unmatched id (absent, malformed, or another tenant's) falls through
  // to the top-priority *visible* row (the readiness filter can hide the previous selection).
  const effectiveSelectedId = visibleRows.some((row) => row.item.contractId === selectedContractId)
    ? selectedContractId
    : (visibleRows[0]?.item.contractId ?? null);
  const selectedRow = visibleRows.find((row) => row.item.contractId === effectiveSelectedId) ?? null;

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

        load();
      });
  };

  const ready = fetchState.phase === "ready";

  return (
    <div className="renewal-screen">
      <header className="screen-header">
        <div>
          <h2 className="screen-title">Renewals</h2>
          <p className="screen-header-summary">
            {ready ? formatRenewalsSummary(readinessCounts.ok) : fetchState.phase === "loading" ? "Loading renewals…" : RENEWALS_SUMMARY_OFF}
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
        <>
          <ReadinessFilter value={readiness} onChange={setReadiness} counts={readinessCounts} ariaLabel="Filter renewals by readiness" />
          {visibleRows.length === 0 ? (
            <div className="renewal-readiness-empty" role="status">
              <p className="micro-meta">{getReadinessEmptyCopy(readiness)}</p>
            </div>
          ) : (
            <div className="renewal-screen-body">
              <RenewalTable rows={visibleRows} selectedContractId={effectiveSelectedId} onSelect={setSelectedContractId} />
              {selectedRow && (
                <InsightCard
                  item={selectedRow.item}
                  tracked={selectedRow.tracked}
                  actionPending={actionPending}
                  actionError={actionError}
                  onAction={handleAction}
                  apiClient={apiClient}
                  tenantId={workspace.id}
                />
              )}
            </div>
          )}
        </>
      )}
    </div>
  );
}
