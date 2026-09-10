import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import type { ApiClient, PortfolioListItem } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import PortfolioTable from "./PortfolioTable";
import { buildPortfolioRows, buildPortfolioSummary, formatPortfolioSummary, moreColumnsLabel, PORTFOLIO_SUMMARY_OFF } from "./portfolioViewModel";
import "./contracts.css";

export interface PortfolioRouteProps {
  apiClient: ApiClient;
}

/** Matches `PortfolioPageRequest.MaxPageSize` (backend) -- the largest single page the endpoint allows. */
const PORTFOLIO_PAGE_SIZE = 100;

type FetchState =
  | { phase: "loading" }
  | { phase: "error"; statusCode: number | null; message: string }
  | { phase: "ready"; items: readonly PortfolioListItem[] };

/**
 * Route `/contracts` -- Portfolio, V2 (ADR-024 V2 IA amending ADR-018/ADR-020 screen 4;
 * screens-v2.md #6; `raffa-v2/markup.html` "PORTFOLIO" block, `app.jsx` `kbContracts` /
 * `pfSummary` / `moreCols`). Replaces the Day-1 screen's seven filter chips, attention strip and
 * ten-column severity-sorted table with the prototype's own shape: a header ("Portfolio" + the
 * `pfSummary` line + "More columns"), the validated contracts only, sorted by how soon notice must
 * be given, urgent rows tinted, rows opening Contract 360 -- and, while no contract is validated,
 * the tier's reroute state (R-WEB-02): "Nothing to triage yet · The portfolio lights up from
 * validated contracts. Upload one to start. · Upload a contract".
 *
 * **Fetch-once, derive client-side.** One `GET /api/contracts` call per mount (and per Retry) for
 * the tenant's first page (`PORTFOLIO_PAGE_SIZE`, the backend's own ceiling); validated-only
 * filtering, ordering, urgency and the summary are all `portfolioViewModel.ts` over that page --
 * the same architecture the Day-1 screen already used, minus the client-side filter chips the V2
 * design does not have. "Validated" is `contractStatus.ts`'s one shared predicate, so this screen
 * can never show a contract the rail's own "From your contracts" count excludes.
 */
export default function PortfolioRoute({ apiClient }: PortfolioRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });
  const [moreColumns, setMoreColumns] = useState(false);

  const loadPortfolio = useCallback(() => {
    if (!workspace) return;

    setFetchState({ phase: "loading" });
    void apiClient.getPortfolio(workspace.id, { pageSize: PORTFOLIO_PAGE_SIZE }).then((result) => {
      if (!result.ok || !result.portfolio) {
        setFetchState({
          phase: "error",
          statusCode: result.statusCode,
          // A 503 (or a proxy/gateway response the API handler never ran) gets a plain-language,
          // service-shaped message; anything else surfaces the API's own reason (ADR-019
          // accessibility baseline: "names the failing job, never a raw stack trace").
          message:
            result.statusCode === 503 || result.statusCode === null
              ? "Raffa.ai's portfolio service is temporarily unavailable. Try again in a moment."
              : (result.error ?? "The portfolio could not be loaded."),
        });
        return;
      }
      setFetchState({ phase: "ready", items: result.portfolio.items });
    });
    // Depends on workspace?.id (a primitive), not workspace itself: loadCurrentWorkspace() returns a
    // fresh object every call, the same convention every other route's own load() callback follows.
  }, [apiClient, workspace?.id]);

  useEffect(() => {
    loadPortfolio();
  }, [loadPortfolio]);

  const rows = useMemo(() => (fetchState.phase === "ready" ? buildPortfolioRows(fetchState.items) : []), [fetchState]);
  const summary = useMemo(() => buildPortfolioSummary(rows), [rows]);

  if (!workspace) {
    // Should not normally be reachable -- App.tsx only mounts the shell (and therefore this route)
    // once a workspace is current -- but this route reads the store directly rather than trusting
    // that earlier check, the same defensive convention every other route under `src/routes/` follows.
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before viewing the portfolio.</p>
      </div>
    );
  }

  const ready = fetchState.phase === "ready";
  const lit = ready && rows.length > 0;

  return (
    <div className="portfolio-screen">
      <header className="screen-header">
        <div>
          <h2 className="screen-title">Portfolio</h2>
          <p className="screen-header-summary">
            {ready ? formatPortfolioSummary(summary) : fetchState.phase === "loading" ? "Loading portfolio…" : PORTFOLIO_SUMMARY_OFF}
          </p>
        </div>
        {lit && (
          <div className="screen-header-actions">
            <button type="button" className="btn btn-ghost portfolio-columns-toggle" aria-pressed={moreColumns} onClick={() => setMoreColumns((current) => !current)}>
              {moreColumnsLabel(moreColumns)}
            </button>
          </div>
        )}
      </header>

      {fetchState.phase === "loading" && (
        <div className="portfolio-skeleton" role="status" aria-live="polite">
          <p className="micro-meta">Loading portfolio…</p>
          {Array.from({ length: 6 }, (_, index) => (
            <div key={index} className="skeleton portfolio-skeleton-row" />
          ))}
        </div>
      )}

      {fetchState.phase === "error" && (
        <div className="error-state" role="alert">
          <h4>Portfolio unavailable</h4>
          <p className="micro-meta">
            {fetchState.message}
            {fetchState.statusCode !== null && ` (HTTP ${fetchState.statusCode})`}
          </p>
          <button type="button" className="btn btn-secondary" onClick={loadPortfolio}>
            Retry
          </button>
        </div>
      )}

      {ready && rows.length === 0 && (
        // markup.html `kbOff`: the tier's reroute state -- copy verbatim.
        <div className="screen-reroute" role="status">
          <h3>Nothing to triage yet</h3>
          <p>The portfolio lights up from validated contracts. Upload one to start.</p>
          <Link to="/documents" className="btn btn-primary">
            Upload a contract
          </Link>
        </div>
      )}

      {lit && <PortfolioTable rows={rows} moreColumns={moreColumns} />}
    </div>
  );
}
