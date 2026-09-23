import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import type { ApiClient, PortfolioPageBody, SavingsOpportunityBody } from "../../api/client";
import AskRaffaLink from "../../components/ask-bar/AskRaffaLink";
import { ASK_PROMPTS } from "../../components/ask-bar/askLaunch";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import ScreenTitle from "../../components/ScreenTitle";
import KpiRow from "./KpiRow";
import ContextStrip from "./ContextStrip";
import SavingsPipeline from "./SavingsPipeline";
import SavingsTimeline from "./SavingsTimeline";
import RecentVerified from "./RecentVerified";
import SavingsBreakdown from "./SavingsBreakdown";
import DeadlineQueue from "./DeadlineQueue";
import OpportunitiesTable from "./OpportunitiesTable";
import {
  EMPTY_SAVINGS_FILTERS,
  getCurrencyFilterOptions,
  getSupplierFilterOptions,
  readSavingsFiltersFromSearch,
  SAVINGS_STATUS_FILTER_OPTIONS,
  type SavingsFilterState,
} from "./savingsFilters";
import {
  buildContextCells,
  buildOpportunitiesPortfolioHref,
  buildOpportunityRows,
  buildSupplierNameIndex,
  filterOpportunityRows,
  findNoticeSoonContractIds,
  formatSavingsSummary,
  getSavingsStatusTag,
  reduceKpiFetch,
  resolveOpportunitySupplier,
  type KpiFetchState,
} from "./savingsViewModel";
import {
  buildDeadlineQueue,
  buildMonthlySavings,
  buildNoticeIndex,
  buildRecentVerified,
  buildSavingsBreakdown,
  buildSavingsPipeline,
  buildVerifiedStats,
  listDashboardCurrencies,
  type BreakdownDimension,
} from "./savingsDashboard";
import "./savings.css";

export interface SavingsRouteProps {
  apiClient: ApiClient;
}

type OpportunitiesFetchState =
  | { phase: "loading" }
  | { phase: "error"; statusCode: number | null; message: string }
  | { phase: "ready"; items: readonly SavingsOpportunityBody[] };

/** Loading until the portfolio answers; `failed` falls back honestly -- id-fragment supplier labels, no notice dates. */
type PortfolioState = { phase: "loading" } | { phase: "ready"; items: PortfolioPageBody["items"] } | { phase: "failed" };

/**
 * Route `/savings` -- the Savings dashboard. A rail destination under "From your contracts", also
 * reached from Ask actions, Renewals and Contract 360. Top to bottom:
 *
 * 1. **Header** -- "Savings" + summary, and "Ask Raffa where to save" (a new Ask chat asking it).
 * 2. **Headline band** (`KpiRow`) -- Savings verified (the lead figure) · identified · in progress ·
 *    potential as a share of spend, from `GET /api/savings/kpis`, stale-labelled if that call fails.
 * 3. **Portfolio context** (`ContextStrip`) -- contracts and spend analyzed, upcoming renewals,
 *    notice deadlines inside 45 days; each cell a link into Portfolio or Renewals.
 * 4. **Charts** (`savingsDashboard.ts`), one currency at a time with a switch when there are more:
 *    the identified → in progress → verified pipeline; "When you saved" (money verified per month,
 *    with this year / last 90 days / last verified saving) beside the latest verified outcomes;
 *    "Where the savings are" by supplier or lever; "Act before the notice deadline".
 * 5. **Opportunities** -- filters and the table; a pipeline stage or a supplier bar filters it.
 *
 * **Three independent fetches, independent degrade states.** `getSavingsKpis` backs the band; its
 * failure degrades the band to a stale-labelled last-known state (`reduceKpiFetch`), never a blank.
 * `getSavingsOpportunities` backs the charts and the table; its failure renders the table section's
 * own scoped error + Retry. `getPortfolio` supplies supplier *names* and notice dates; if it fails
 * the rows fall back to the id-fragment label the Portfolio table uses and "Notice in" reads "—" --
 * never a fabricated name or date.
 *
 * **Deep links.** `?contract=<id>` (Contract 360 "Track it in Savings", Renewals) focuses one
 * contract's opportunities and `?status=` one stage -- read once on mount, like Renewals' `?select=`,
 * so a later click is never fought by the URL.
 */
export default function SavingsRoute({ apiClient }: SavingsRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [searchParams] = useSearchParams();
  const [kpiState, setKpiState] = useState<KpiFetchState>({ phase: "loading" });
  const [opportunitiesState, setOpportunitiesState] = useState<OpportunitiesFetchState>({ phase: "loading" });
  const [portfolioState, setPortfolioState] = useState<PortfolioState>({ phase: "loading" });
  // Supplier / status / currency (task-01-savings-filters, ADR-020: presentation only, no client
  // store), plus the deep-linked contract focus. Pure view state over the already-loaded rows below
  // -- never written to storage, and never touched by the three fetches' own load/retry callbacks.
  const [filters, setFilters] = useState<SavingsFilterState>(() => readSavingsFiltersFromSearch(searchParams));
  const [selectedCurrency, setSelectedCurrency] = useState<string | null>(null);
  const [breakdownDimension, setBreakdownDimension] = useState<BreakdownDimension>("supplier");
  const tableRef = useRef<HTMLElement>(null);

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

  const loadPortfolio = useCallback(() => {
    if (!workspace) return;
    void apiClient.getPortfolio(workspace.id, { pageSize: 100 }).then((result) => {
      setPortfolioState(result.ok && result.portfolio ? { phase: "ready", items: result.portfolio.items } : { phase: "failed" });
    });
  }, [apiClient, workspace?.id]);

  useEffect(() => {
    loadKpis();
    loadOpportunities();
    loadPortfolio();
  }, [loadKpis, loadOpportunities, loadPortfolio]);

  const portfolioItems = useMemo<PortfolioPageBody["items"]>(() => (portfolioState.phase === "ready" ? portfolioState.items : []), [portfolioState]);
  const supplierNames = useMemo(() => buildSupplierNameIndex(portfolioItems), [portfolioItems]);
  const noticeIndex = useMemo(() => buildNoticeIndex(portfolioItems), [portfolioItems]);
  const opportunityItems = useMemo(() => (opportunitiesState.phase === "ready" ? opportunitiesState.items : []), [opportunitiesState]);
  const rows = useMemo(() => buildOpportunityRows(opportunityItems, supplierNames, noticeIndex), [opportunityItems, supplierNames, noticeIndex]);

  const supplierLabelOf = useCallback(
    (item: SavingsOpportunityBody) => resolveOpportunitySupplier(item.contractId, item.supplierId, supplierNames).label,
    [supplierNames],
  );
  const currencies = useMemo(() => listDashboardCurrencies(opportunityItems), [opportunityItems]);
  const currency = selectedCurrency !== null && currencies.includes(selectedCurrency) ? selectedCurrency : (currencies[0] ?? null);
  const dashboard = useMemo(() => {
    if (currency === null) return null;
    return {
      pipeline: buildSavingsPipeline(opportunityItems, currency),
      months: buildMonthlySavings(opportunityItems, currency),
      stats: buildVerifiedStats(opportunityItems, currency, supplierLabelOf),
      recent: buildRecentVerified(opportunityItems, currency, supplierLabelOf),
      breakdown: buildSavingsBreakdown(opportunityItems, currency, breakdownDimension, supplierLabelOf),
      deadlines: buildDeadlineQueue(opportunityItems, portfolioItems, supplierLabelOf),
    };
  }, [opportunityItems, portfolioItems, currency, breakdownDimension, supplierLabelOf]);

  if (!workspace) {
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before viewing savings.</p>
      </div>
    );
  }

  const kpis = kpiState.phase === "ready" ? kpiState.kpis : null;
  const summary =
    kpiState.phase === "loading" || opportunitiesState.phase === "loading" ? "Loading savings…" : formatSavingsSummary(kpis, rows.length);
  const contextCells = buildContextCells(kpis, portfolioState.phase === "ready" ? findNoticeSoonContractIds(portfolioItems) : null);

  // Filter options always come from the full, unfiltered `rows` -- so picking a currency never
  // makes the supplier list (or vice versa) shrink out from under the user.
  const visibleRows = filterOpportunityRows(rows, filters);
  const supplierFilterOptions = getSupplierFilterOptions(rows);
  const currencyFilterOptions = getCurrencyFilterOptions(rows);
  const filtersActive = filters.supplier !== null || filters.status !== null || filters.currency !== null || filters.contractId !== null;
  const clearFilters = () => setFilters(EMPTY_SAVINGS_FILTERS);
  const focusedRow = filters.contractId === null ? null : (rows.find((row) => row.contractId === filters.contractId) ?? null);
  const portfolioHref = buildOpportunitiesPortfolioHref(visibleRows);

  // A chart that filters the table brings the table into view, so the click visibly does something.
  // Charts show one currency; with more than one, a chart filter also pins the table to it.
  const filterFromChart = (next: Partial<SavingsFilterState>) => {
    const selecting = next.status != null || next.supplier != null;
    const pinCurrency = selecting && currencies.length > 1 ? { currency } : {};
    setFilters((previous) => ({ ...previous, ...next, ...pinCurrency }));
    tableRef.current?.scrollIntoView?.({ behavior: "smooth", block: "start" });
  };

  return (
    <div className="savings-screen">
      <header className="screen-header">
        <div>
          <ScreenTitle guide="savings">Savings</ScreenTitle>
          <p className="screen-header-summary">{summary}</p>
        </div>
        <div className="screen-header-actions">
          <AskRaffaLink question={ASK_PROMPTS.whereToSave} className="btn btn-primary savings-ask">
            Ask Raffa where to save →
          </AskRaffaLink>
        </div>
      </header>

      <KpiRow kpiState={kpiState} onRetry={loadKpis} />
      <ContextStrip cells={contextCells} />

      {opportunitiesState.phase === "ready" && dashboard !== null && currency !== null && (
        <div className="savings-dashboard">
          {currencies.length > 1 && (
            <div className="savings-currency-row">
              <span className="micro-meta">Charts in</span>
              <div className="seg" role="group" aria-label="Chart currency">
                {currencies.map((code) => (
                  <button key={code} type="button" aria-pressed={code === currency} onClick={() => setSelectedCurrency(code)}>
                    {code}
                  </button>
                ))}
              </div>
              <span className="micro-meta">No conversion: each currency is shown on its own.</span>
            </div>
          )}

          <SavingsPipeline
            pipeline={dashboard.pipeline}
            activeStatus={filters.status}
            onSelectStatus={(status) => filterFromChart({ status })}
          />

          <div className="savings-grid savings-grid-wide">
            <SavingsTimeline currency={currency} buckets={dashboard.months} stats={dashboard.stats} />
            <RecentVerified rows={dashboard.recent} />
          </div>

          <div className="savings-grid">
            <SavingsBreakdown
              dimension={breakdownDimension}
              onDimensionChange={setBreakdownDimension}
              rows={dashboard.breakdown}
              activeSupplier={filters.supplier}
              onSelectSupplier={(supplier) => filterFromChart({ supplier })}
            />
            <DeadlineQueue rows={dashboard.deadlines} portfolioLoaded={portfolioState.phase !== "failed"} supplierNames={supplierNames} />
          </div>
        </div>
      )}

      <section className="savings-opportunities-section" aria-label="Opportunities" ref={tableRef}>
        <div className="savings-opportunities-head">
          <h6>Opportunities</h6>
          {opportunitiesState.phase === "ready" && portfolioHref !== null && (
            <Link to={portfolioHref} className="btn btn-ghost savings-panel-link">
              Show these contracts in Portfolio →
            </Link>
          )}
        </div>

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
            <div className="screen-reroute-actions">
              <Link to="/renewals" className="btn btn-primary">
                Open renewals
              </Link>
              <AskRaffaLink question={ASK_PROMPTS.whereToSave} className="btn btn-secondary">
                Ask Raffa where to save
              </AskRaffaLink>
            </div>
          </div>
        )}

        {opportunitiesState.phase === "ready" && rows.length > 0 && (
          <>
            {filters.contractId !== null && (
              <div className="savings-focus-notice" role="status">
                <span>
                  {focusedRow !== null
                    ? `Showing the savings on one ${focusedRow.supplierLabel} contract.`
                    : "This contract has no savings opportunity yet."}
                </span>
                <span className="savings-focus-links">
                  <Link to={`/contracts/${filters.contractId}`} state={{ from: "savings" }} className="btn btn-ghost">
                    Open the contract →
                  </Link>
                  <button type="button" className="btn btn-ghost" onClick={() => setFilters((previous) => ({ ...previous, contractId: null }))}>
                    Show all opportunities
                  </button>
                </span>
              </div>
            )}

            {/* Anchored above the opportunities table (screens-v2.md #8; task-01-savings-filters,
                ADR-020). Supplier / status / currency -- the council's exact filter set (AC-2);
                "Estimate" stays a sort/numeric column, never a filter. Pure client-side view state:
                filtering never re-fetches and never writes to storage (AC-3). */}
            <div role="group" aria-label="Filter opportunities" className="savings-filters">
              <div className="field savings-filter-field">
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

              <div className="field savings-filter-field">
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

              <div className="field savings-filter-field">
                <label htmlFor="savings-filter-currency">Currency</label>
                <select
                  id="savings-filter-currency"
                  className="input"
                  value={filters.currency ?? ""}
                  onChange={(event) => setFilters((previous) => ({ ...previous, currency: event.target.value === "" ? null : event.target.value }))}
                >
                  <option value="">All currencies</option>
                  {currencyFilterOptions.map((code) => (
                    <option key={code} value={code}>
                      {code}
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
