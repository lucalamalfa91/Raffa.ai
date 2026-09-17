import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { ApiClient, SavingsOpportunityBody } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import KpiRow from "./KpiRow";
import OpportunitiesTable from "./OpportunitiesTable";
import {
  EMPTY_SAVINGS_FILTERS,
  getCurrencyFilterOptions,
  getSupplierFilterOptions,
  SAVINGS_STATUS_FILTER_OPTIONS,
  type SavingsFilterState,
} from "./savingsFilters";
import {
  buildOpportunityRows,
  buildSupplierNameIndex,
  filterOpportunityRows,
  formatSavingsSummary,
  getSavingsStatusTag,
  reduceKpiFetch,
  type KpiFetchState,
} from "./savingsViewModel";
import "./savings.css";

export interface SavingsRouteProps {
  apiClient: ApiClient;
}

type OpportunitiesFetchState =
  | { phase: "loading" }
  | { phase: "error"; statusCode: number | null; message: string }
  | { phase: "ready"; items: readonly SavingsOpportunityBody[] };

/**
 * Route `/savings` -- Savings, V2 (screens-v2.md #8; `app.jsx` `kpis` / `opps`). First-class rail
 * destination under "From your contracts" (`navItems.ts`), also reached from Ask actions, Renewals
 * and Contract 360.
 * Header ("Savings" + summary), the four-cell KPI band, the opportunities table (Supplier · Action
 * · Estimate · Status, rows open Contract 360), and the reroute state while nothing feeds it.
 *
 * **Three independent fetches, independent degrade states.** `getSavingsKpis` backs the band; its
 * failure degrades the band to a stale-labelled last-known state (`reduceKpiFetch`), never a blank.
 * `getSavingsOpportunities` backs the table; its failure renders the table section's own scoped
 * error + Retry. `getPortfolio` only supplies supplier *names* for the rows (`SavingsOpportunityResult` carries a
 * supplier id only); if it fails the rows fall back to the same id-fragment label the Portfolio
 * table uses -- never a fabricated name.
 */
export default function SavingsRoute({ apiClient }: SavingsRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [kpiState, setKpiState] = useState<KpiFetchState>({ phase: "loading" });
  const [opportunitiesState, setOpportunitiesState] = useState<OpportunitiesFetchState>({ phase: "loading" });
  const [supplierNames, setSupplierNames] = useState<ReadonlyMap<string, string>>(new Map());
  // Supplier / status / currency (task-01-savings-filters, ADR-020: presentation only, no client
  // store). Pure view state over the already-loaded rows below -- never written to storage, and
  // never touched by the three fetches' own load/retry callbacks.
  const [filters, setFilters] = useState<SavingsFilterState>(EMPTY_SAVINGS_FILTERS);

  const loadKpis = useCallback(() => {
    if (!workspace) return;
    void apiClient.getSavingsKpis(workspace.id).then((result) => {
      setKpiState((previous) => reduceKpiFetch(previous, result.ok && result.kpis ? { ok: true, kpis: result.kpis } : { ok: false }));
    });
    // Depends on workspace?.id (a primitive), not workspace itself: loadCurrentWorkspace() returns a
    // fresh object every call, the same convention every other route's own load() callback follows.
  }, [apiClient, workspace?.id]);

  const loadOpportunities = useCallback(() => {
    if (!workspace) return;

    setOpportunitiesState({ phase: "loading" });

    void apiClient.getSavingsOpportunities(workspace.id).then((result) => {
      if (!result.ok || !result.opportunities) {
        setOpportunitiesState({
          phase: "error",
          statusCode: result.statusCode,
          message:
            result.statusCode === 503 || result.statusCode === null
              ? "Raffa.ai's savings service is temporarily unavailable. Try again in a moment."
              : (result.error ?? "The opportunities list could not be loaded."),
        });
        return;
      }

      setOpportunitiesState({ phase: "ready", items: result.opportunities.items });
    });
  }, [apiClient, workspace?.id]);

  const loadSupplierNames = useCallback(() => {
    if (!workspace) return;
    void apiClient.getPortfolio(workspace.id, { pageSize: 100 }).then((result) => {
      if (result.ok && result.portfolio) setSupplierNames(buildSupplierNameIndex(result.portfolio.items));
    });
  }, [apiClient, workspace?.id]);

  useEffect(() => {
    loadKpis();
    loadOpportunities();
    loadSupplierNames();
  }, [loadKpis, loadOpportunities, loadSupplierNames]);

  if (!workspace) {
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before viewing savings.</p>
      </div>
    );
  }

  const opportunityItems = opportunitiesState.phase === "ready" ? opportunitiesState.items : [];
  const rows = buildOpportunityRows(opportunityItems, supplierNames);
  const kpis = kpiState.phase === "ready" ? kpiState.kpis : null;
  const summary =
    kpiState.phase === "loading" || opportunitiesState.phase === "loading" ? "Loading savings…" : formatSavingsSummary(kpis, rows.length);

  // Filter options always come from the full, unfiltered `rows` -- so picking a currency never
  // makes the supplier list (or vice versa) shrink out from under the user.
  const visibleRows = filterOpportunityRows(rows, filters);
  const supplierFilterOptions = getSupplierFilterOptions(rows);
  const currencyFilterOptions = getCurrencyFilterOptions(rows);
  const filtersActive = filters.supplier !== null || filters.status !== null || filters.currency !== null;
  const clearFilters = () => setFilters(EMPTY_SAVINGS_FILTERS);

  return (
    <div className="savings-screen">
      <header className="screen-header">
        <div>
          <h2 className="screen-title">Savings</h2>
          <p className="screen-header-summary">{summary}</p>
        </div>
      </header>

      <KpiRow kpiState={kpiState} onRetry={loadKpis} />

      <section className="savings-opportunities-section" aria-label="Opportunities">
        <h6>Opportunities</h6>

        {opportunitiesState.phase === "loading" && (
          <div className="savings-opportunities-skeleton" role="status" aria-live="polite">
            {Array.from({ length: 4 }, (_, index) => (
              <div key={index} className="skeleton savings-opportunities-skeleton-row" />
            ))}
          </div>
        )}

        {opportunitiesState.phase === "error" && (
          <div className="error-state" role="alert">
            <h4>Opportunities unavailable</h4>
            <p className="micro-meta">
              {opportunitiesState.message}
              {opportunitiesState.statusCode !== null && ` (HTTP ${opportunitiesState.statusCode})`}
            </p>
            <button type="button" className="btn btn-secondary" onClick={loadOpportunities}>
              Retry
            </button>
          </div>
        )}

        {opportunitiesState.phase === "ready" && rows.length === 0 && (
          <div className="screen-reroute" role="status">
            <h3>No savings opportunities yet</h3>
            <p>Opportunities appear once a renewal is actioned or a saving is identified from validated contracts.</p>
            <Link to="/renewals" className="btn btn-primary">
              Open renewals
            </Link>
          </div>
        )}

        {opportunitiesState.phase === "ready" && rows.length > 0 && (
          <>
            {/* Anchored above the opportunities table (screens-v2.md #8; task-01-savings-filters,
                ADR-020). Supplier / status / currency -- the council's exact filter set (AC-2);
                "Estimate" stays a sort/numeric column, never a filter. Pure client-side view state:
                filtering never re-fetches and never writes to storage (AC-3). */}
            <div
              role="group"
              aria-label="Filter opportunities"
              style={{ display: "flex", flexWrap: "wrap", alignItems: "flex-end", gap: "var(--space-4)" }}
            >
              <div className="field" style={{ marginBottom: 0, minWidth: "160px" }}>
                <label htmlFor="savings-filter-supplier">Supplier</label>
                <select
                  id="savings-filter-supplier"
                  className="input"
                  value={filters.supplier ?? ""}
                  onChange={(event) => setFilters((previous) => ({ ...previous, supplier: event.target.value === "" ? null : event.target.value }))}
                >
                  <option value="">All suppliers</option>
                  {supplierFilterOptions.map((supplier) => (
                    <option key={supplier} value={supplier}>
                      {supplier}
                    </option>
                  ))}
                </select>
              </div>

              <div className="field" style={{ marginBottom: 0, minWidth: "160px" }}>
                <label htmlFor="savings-filter-status">Status</label>
                <select
                  id="savings-filter-status"
                  className="input"
                  value={filters.status ?? ""}
                  onChange={(event) =>
                    setFilters((previous) => ({
                      ...previous,
                      status: event.target.value === "" ? null : (event.target.value as SavingsOpportunityBody["status"]),
                    }))
                  }
                >
                  <option value="">All statuses</option>
                  {SAVINGS_STATUS_FILTER_OPTIONS.map((status) => (
                    <option key={status} value={status}>
                      {getSavingsStatusTag(status).label}
                    </option>
                  ))}
                </select>
              </div>

              <div className="field" style={{ marginBottom: 0, minWidth: "160px" }}>
                <label htmlFor="savings-filter-currency">Currency</label>
                <select
                  id="savings-filter-currency"
                  className="input"
                  value={filters.currency ?? ""}
                  onChange={(event) => setFilters((previous) => ({ ...previous, currency: event.target.value === "" ? null : event.target.value }))}
                >
                  <option value="">All currencies</option>
                  {currencyFilterOptions.map((currency) => (
                    <option key={currency} value={currency}>
                      {currency}
                    </option>
                  ))}
                </select>
              </div>

              <button type="button" className="btn-ghost" onClick={clearFilters} disabled={!filtersActive}>
                Clear filters
              </button>
            </div>

            {visibleRows.length === 0 ? (
              <div className="empty-state" role="status">
                <h3>No opportunities match the selected filters</h3>
                <p className="micro-meta">Clear a filter above to see the full list.</p>
              </div>
            ) : (
              <OpportunitiesTable rows={visibleRows} />
            )}
          </>
        )}
      </section>
    </div>
  );
}
