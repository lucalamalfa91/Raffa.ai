import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import type { ApiClient, RenewalPipelineItemBody } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import AskRaffaLink from "../../components/ask-bar/AskRaffaLink";
import { ASK_PROMPTS } from "../../components/ask-bar/askLaunch";
import RenewalTable from "./RenewalTable";
import InsightCard from "./InsightCard";
import RenewalKpiStrip from "./RenewalKpiStrip";
import RenewalBulkBar from "./RenewalBulkBar";
import ReadinessFilter from "../../components/ReadinessFilter";
import {
  countReadiness,
  DEFAULT_READINESS_FILTER,
  filterByReadiness,
  getReadinessEmptyCopy,
  type ReadinessFilterValue,
} from "../../components/readiness";
import {
  buildRenewalContractIndex,
  buildRenewalKpis,
  buildRenewalRows,
  EMPTY_RENEWAL_FILTERS,
  filterRenewalRows,
  formatRenewalsSummary,
  getRenewalActionPlan,
  isRenewalFilterActive,
  isRenewalItemReady,
  RENEWALS_SUMMARY_OFF,
  toggleKpiFilter,
  type RenewalActionKind,
  type RenewalContractInfo,
  type RenewalListFilters,
  type RenewalStateFilter,
} from "./renewalPipelineViewModel";
import { buildRenewalBulkActions, type RenewalBulkActionView } from "./renewalActions";
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

const STATE_FILTER_OPTIONS: ReadonlyArray<{ value: RenewalStateFilter; label: string }> = [
  { value: "all", label: "Any status" },
  { value: "open", label: "Not started" },
  { value: "negotiating", label: "In negotiation" },
  { value: "closed", label: "Closed" },
];

/**
 * Route `/renewals` -- Renewals (ADR-024 V2 IA amending ADR-020 screen 8; screens-v2.md #7). The
 * screen where a notice deadline is worked, and the launch point for everything that can be done
 * about one. Top to bottom:
 *
 * 1. **Header** -- "Renewals" + summary, and "Ask Raffa which to start first".
 * 2. **KPI strip** (`RenewalKpiStrip`) -- notice in 30 / 90 days, spend renewing inside 90 days,
 *    not started / in negotiation / closed; each cell filters the list.
 * 3. **Toolbar** -- the readiness `.seg` (Ready / To review / All, default Ready: the same
 *    validation rule as Portfolio), a supplier search and a workflow-status select.
 * 4. **Bulk bar** (`RenewalBulkBar`) while rows are ticked -- assign them to me, show them in
 *    Portfolio, ask Raffa which to start first.
 * 5. **The list, sorted by priority, and the selected row's pane** (`InsightCard`) -- facts,
 *    recommendation, the workflow buttons and the action registry's launcher (`renewalActions.ts`).
 *
 * **Fetch order.** `getRenewals` is the screen: a non-2xx there is this screen's own error state.
 * Every row carries its own `priority`, so the list paints already scored and sorted (the old
 * one-priority-call-per-row fan-out could starve the demo server's 50 Postgres connections). The
 * portfolio (`getPortfolio`) is read alongside, never blocking: it only adds each contract's type
 * and currency; without it the contract reads as an id fragment and the spend carries no code.
 *
 * **Status shared with the Contract 360 tracker**: the real write is `POST /api/renewals/{id}/action`;
 * every surface reads `savedAction` on the same `GET /api/renewals` row.
 *
 * **`?select=` deep link** (task E29/F03/US01/T01, NW-84; ADR-012 cl. 51): a chat reply, Savings or
 * Contract 360 can open `/renewals?select={contractId}`; see `effectiveSelectedId` below for how an
 * unmatched value falls back to the top-priority visible row.
 */
export default function RenewalsRoute({ apiClient, userLabel }: RenewalsRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [searchParams] = useSearchParams();
  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });
  const [contracts, setContracts] = useState<ReadonlyMap<string, RenewalContractInfo>>(new Map());
  // Seeds the initial selection from the `?select=` deep link above, read once via the lazy
  // initializer -- the same "read once, on mount" convention `routes/documents/index.tsx`'s
  // `?filter=` already uses -- so a later click (`onSelect` below) is never fought by a stale URL on
  // re-render. A value matching no row (absent, malformed, or another tenant's contract -- `rows` is
  // already this tenant's own `GET /api/renewals` list, so a foreign id simply never appears in it)
  // is handled entirely by `effectiveSelectedId`'s existing fallback.
  const [selectedContractId, setSelectedContractId] = useState<string | null>(() => searchParams.get("select"));
  const [actionPending, setActionPending] = useState<RenewalActionKind | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [readiness, setReadiness] = useState<ReadinessFilterValue>(DEFAULT_READINESS_FILTER);
  const [filters, setFilters] = useState<RenewalListFilters>(EMPTY_RENEWAL_FILTERS);
  const [checkedIds, setCheckedIds] = useState<ReadonlySet<string>>(new Set());
  const [bulkProgress, setBulkProgress] = useState<string | null>(null);
  const [bulkError, setBulkError] = useState<string | null>(null);

  const load = useCallback(() => {
    if (!workspace) return;

    setFetchState({ phase: "loading" });
    setActionError(null);

    void apiClient.getRenewals(workspace.id).then((result) => {
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
      // The score is on the row itself (see the route doc comment): one read paints the list
      // scored; a row without one stays "—".
      setFetchState({
        phase: "ready",
        items,
        scores: Object.fromEntries(items.map((item) => [item.contractId, item.priority?.totalScore ?? null])),
      });
    });
    // Depends on workspace?.id (a primitive), not workspace itself: loadCurrentWorkspace() returns a
    // fresh object every call, the same convention every other route's own load() callback follows.
  }, [apiClient, workspace?.id]);

  const loadContracts = useCallback(() => {
    if (!workspace) return;
    void apiClient.getPortfolio(workspace.id, { pageSize: 100 }).then((result) => {
      if (result.ok && result.portfolio) setContracts(buildRenewalContractIndex(result.portfolio.items));
    });
  }, [apiClient, workspace?.id]);

  useEffect(() => {
    load();
    loadContracts();
  }, [load, loadContracts]);

  const rows = useMemo(
    () => (fetchState.phase === "ready" ? buildRenewalRows(fetchState.items, fetchState.scores, contracts) : []),
    [fetchState, contracts],
  );
  const readinessCounts = useMemo(
    () => countReadiness(rows.filter((row) => isRenewalItemReady(row.item)).length, rows.filter((row) => !isRenewalItemReady(row.item)).length),
    [rows],
  );
  const readyRows = useMemo(() => filterByReadiness(rows, readiness, (row) => isRenewalItemReady(row.item)), [rows, readiness]);
  const kpis = useMemo(() => buildRenewalKpis(readyRows), [readyRows]);
  const visibleRows = useMemo(() => filterRenewalRows(readyRows, filters), [readyRows, filters]);
  // A bulk action only ever runs on ticked rows the list is showing -- never on one a filter has hidden since.
  const checkedRows = useMemo(() => visibleRows.filter((row) => checkedIds.has(row.item.contractId)), [visibleRows, checkedIds]);
  const bulkActions = useMemo(() => buildRenewalBulkActions(checkedRows), [checkedRows]);

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
  // to the top-priority *visible* row (the readiness and list filters can hide the previous selection).
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

  // A bulk write runs the same real write the pane makes, one row at a time, and stops at the first
  // failure -- so the error names what did not happen and nothing is assumed saved.
  const handleBulkWrite = async (action: RenewalBulkActionView) => {
    if (action.target.kind !== "write") return;
    const plan = getRenewalActionPlan("assign");
    setBulkError(null);
    let done = 0;
    for (const row of action.rows) {
      setBulkProgress(`Assigning ${done + 1} of ${action.rows.length}…`);
      const result = await apiClient.postRenewalAction(workspace.id, row.item.contractId, { owner: userLabel, status: plan.status, action: plan.action });
      if (!result.ok || !result.action) {
        setBulkError(`${done} of ${action.rows.length} assigned. ${result.error ?? "The next one could not be saved."}`);
        break;
      }
      done += 1;
    }
    setBulkProgress(null);
    if (done === action.rows.length) setCheckedIds(new Set());
    load();
  };

  const toggleChecked = (contractId: string) =>
    setCheckedIds((current) => {
      const next = new Set(current);
      if (next.has(contractId)) next.delete(contractId);
      else next.add(contractId);
      return next;
    });
  const toggleAllChecked = () =>
    setCheckedIds((current) => {
      const allChecked = visibleRows.length > 0 && visibleRows.every((row) => current.has(row.item.contractId));
      return allChecked ? new Set() : new Set(visibleRows.map((row) => row.item.contractId));
    });

  const ready = fetchState.phase === "ready";
  const filtersActive = isRenewalFilterActive(filters);

  return (
    <div className="renewal-screen">
      <header className="screen-header">
        <div>
          <h2 className="screen-title">Renewals</h2>
          <p className="screen-header-summary">
            {ready ? formatRenewalsSummary(readinessCounts.ok) : fetchState.phase === "loading" ? "Loading renewals…" : RENEWALS_SUMMARY_OFF}
          </p>
        </div>
        {ready && rows.length > 0 && (
          <div className="screen-header-actions">
            <Link to="/savings" className="btn btn-ghost">
              Savings →
            </Link>
            <AskRaffaLink question={ASK_PROMPTS.renewalsStartFirst} className="btn btn-primary renewal-ask">
              Ask Raffa which to start first →
            </AskRaffaLink>
          </div>
        )}
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
          <RenewalKpiStrip cells={kpis} filters={filters} onToggle={(cell) => setFilters((current) => toggleKpiFilter(cell, current))} />

          <div className="renewal-toolbar">
            <ReadinessFilter value={readiness} onChange={setReadiness} counts={readinessCounts} ariaLabel="Filter renewals by readiness" />
            <div className="renewal-toolbar-filters" role="group" aria-label="Filter the list">
              <input
                type="search"
                className="input renewal-search"
                placeholder="Search supplier or contract"
                aria-label="Search renewals"
                value={filters.query}
                onChange={(event) => setFilters((current) => ({ ...current, query: event.target.value }))}
              />
              <select
                className="input renewal-state-select"
                aria-label="Workflow status"
                value={filters.state}
                onChange={(event) => setFilters((current) => ({ ...current, state: event.target.value as RenewalStateFilter }))}
              >
                {STATE_FILTER_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
              <button type="button" className="btn btn-ghost" disabled={!filtersActive} onClick={() => setFilters(EMPTY_RENEWAL_FILTERS)}>
                Clear filters
              </button>
            </div>
          </div>

          {checkedRows.length > 0 && (
            <RenewalBulkBar
              selectedCount={checkedRows.length}
              actions={bulkActions}
              onWrite={(action) => void handleBulkWrite(action)}
              onClear={() => {
                setCheckedIds(new Set());
                setBulkError(null);
              }}
              progress={bulkProgress}
              error={bulkError}
            />
          )}

          {readyRows.length === 0 ? (
            <div className="renewal-readiness-empty" role="status">
              <p className="micro-meta">{getReadinessEmptyCopy(readiness)}</p>
            </div>
          ) : visibleRows.length === 0 ? (
            <div className="renewal-readiness-empty" role="status">
              <p className="micro-meta">No renewal matches these filters.</p>
              <button type="button" className="btn btn-secondary" onClick={() => setFilters(EMPTY_RENEWAL_FILTERS)}>
                Clear filters
              </button>
            </div>
          ) : (
            <div className="renewal-screen-body">
              <RenewalTable
                rows={visibleRows}
                selectedContractId={effectiveSelectedId}
                onSelect={setSelectedContractId}
                checkedIds={checkedIds}
                onToggleChecked={toggleChecked}
                onToggleAllChecked={toggleAllChecked}
              />
              {selectedRow && (
                <InsightCard
                  key={selectedRow.item.contractId}
                  row={selectedRow}
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
