import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { ApiClient } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import AttentionStrip from "./AttentionStrip";
import PortfolioFilters from "./PortfolioFilters";
import PortfolioTable from "./PortfolioTable";
import {
  compareBySeverityThenDeadline,
  computeAttentionBucketCounts,
  computeAttentionRow,
  type AttentionBucketKey,
  type AttentionRow,
} from "./portfolioAttention";
import { EMPTY_PORTFOLIO_FILTERS, applyPortfolioFilters, isAnyPortfolioFilterActive, type PortfolioFilterState } from "./portfolioFilterState";
import "./contracts.css";

export interface PortfolioRouteProps {
  apiClient: ApiClient;
}

/** Matches `PortfolioPageRequest.MaxPageSize` (backend) -- the largest single page the endpoint allows. */
const PORTFOLIO_PAGE_SIZE = 100;

type FetchState =
  | { phase: "loading" }
  | { phase: "error"; statusCode: number | null; message: string }
  | { phase: "ready"; rows: AttentionRow[] };

/**
 * Route `/contracts` (ADR-018; screens.md #4 "Portfolio"; task E07/F01/US01/T01,
 * us-01-portfolio-list-filters AC-1 filters / AC-2 attention strip / AC-3 sort+tint / AC-4 states).
 * Wired into `../../components/shell/WorkspaceShellApp.tsx`'s `contracts` route in place of that
 * shell task's `ScaffoldScreen` placeholder, the same seam `../documents/index.tsx` already used for
 * `/documents`.
 *
 * **Fetch-once, filter client-side.** This screen calls `GET /api/contracts`
 * (`apiClient.getPortfolio`) exactly once per mount (and once per manual Retry), for the tenant's
 * whole first page (`PORTFOLIO_PAGE_SIZE`, the backend's own page-size ceiling) with no server-side
 * filter applied, then computes every row's attention/severity (`portfolioAttention.ts`) and applies
 * both the seven AC-1 chips and the AC-2 attention-strip toggle (`portfolioFilterState.ts`) entirely in
 * memory, mirroring day1-demo.html's own `allContracts.filter(...)` architecture. Two reasons, not
 * one:
 *  1. The attention-strip's four counts (AC-2) must reflect the *whole* portfolio regardless of which
 *     filters are currently active (screens.md's own attention strip is a portfolio-wide summary, not
 *     a per-filter one) -- computing them from a server-filtered result would require a second,
 *     always-unfiltered fetch anyway.
 *  2. Two of the attention buckets (`isDeadlineSoon`, computed severity) and the risk/status-based
 *     ones have no server-side query-parameter equivalent at all (`PortfolioFilter`, backend, only
 *     ever filters raw columns, never a derived "due within 45 days" or "needs review" concept) -- the
 *     severity computation has to happen client-side regardless, so filtering client-side over the
 *     same already-fetched rows avoids seven independent round trips for no accuracy gain.
 * `apiClient.getPortfolio` still accepts the endpoint's full filter/paging surface (`src/api/client.ts`)
 * for a future task to push filtering server-side once portfolios routinely exceed one page.
 */
export default function PortfolioRoute({ apiClient }: PortfolioRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });
  const [filters, setFilters] = useState<PortfolioFilterState>(EMPTY_PORTFOLIO_FILTERS);

  const loadPortfolio = useCallback(() => {
    if (!workspace) return;

    setFetchState({ phase: "loading" });
    void apiClient.getPortfolio(workspace.id, { pageSize: PORTFOLIO_PAGE_SIZE }).then((result) => {
      if (!result.ok || !result.portfolio) {
        setFetchState({
          phase: "error",
          statusCode: result.statusCode,
          // AC-4 "error (503 + retry)": a 503 (or a proxy/gateway response the API handler never ran)
          // gets a plain-language, service-shaped message; anything else surfaces the API's own
          // plain-language reason (ADR-019 accessibility baseline: "names the failing job, never a raw
          // stack trace").
          message:
            result.statusCode === 503 || result.statusCode === null
              ? "Contigo's portfolio service is temporarily unavailable. Try again in a moment."
              : (result.error ?? "The portfolio could not be loaded."),
        });
        return;
      }

      const now = new Date();
      setFetchState({ phase: "ready", rows: result.portfolio.items.map((item) => computeAttentionRow(item, now)) });
    });
    // Depends on workspace?.id (a primitive), not workspace itself: loadCurrentWorkspace() returns a
    // fresh object every call, and re-creating this callback every render would re-run the effect
    // below on every render too. workspace is treated as stable for a mounted component's lifetime,
    // the same convention ../documents/index.tsx's own startUpload already documents -- switching
    // workspace mid-session is not a V1 flow this screen needs to defend against.
  }, [apiClient, workspace?.id]);

  // Mount-only (plus whenever the tenant id itself changes) -- loadPortfolio's own identity (above)
  // already captures every other dependency it needs.
  useEffect(() => {
    loadPortfolio();
  }, [loadPortfolio]);

  if (!workspace) {
    // Should not normally be reachable -- App.tsx only mounts the shell (and therefore this route)
    // once a workspace is current -- but this route reads the store directly rather than trusting
    // that earlier check, the same defensive convention ../documents/index.tsx already follows.
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before viewing the portfolio.</p>
      </div>
    );
  }

  const allRows = fetchState.phase === "ready" ? fetchState.rows : [];
  const attentionBuckets = computeAttentionBucketCounts(allRows);
  const visibleRows = applyPortfolioFilters(allRows, filters).sort(compareBySeverityThenDeadline);

  const handleToggleAttentionBucket = (key: AttentionBucketKey) => {
    setFilters((current) => ({ ...current, attentionBucket: current.attentionBucket === key ? null : key }));
  };

  return (
    <div className="portfolio-screen">
      <p className="screen-kicker">R1</p>
      <h2 className="screen-title">Portfolio</h2>
      <p className="micro-meta">
        {fetchState.phase === "ready" &&
          (isAnyPortfolioFilterActive(filters)
            ? `${visibleRows.length} of ${allRows.length} contracts`
            : `${allRows.length} contract${allRows.length === 1 ? "" : "s"}`)}
        {fetchState.phase !== "ready" && "Renewal and cancellation deadlines, risk, and status across every contract."}
      </p>

      <PortfolioFilters filters={filters} onChange={setFilters} />
      <AttentionStrip buckets={attentionBuckets} activeKey={filters.attentionBucket} onToggle={handleToggleAttentionBucket} />

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

      {fetchState.phase === "ready" && allRows.length === 0 && (
        <div className="empty-state" role="status">
          <h3>No contracts yet</h3>
          <p className="micro-meta">Upload your first contract and it will appear here once processing finishes.</p>
          <Link to="/documents" className="btn btn-primary">
            Upload a contract
          </Link>
        </div>
      )}

      {fetchState.phase === "ready" && allRows.length > 0 && visibleRows.length === 0 && (
        <div className="empty-state" role="status">
          <h3>No contracts match these filters</h3>
          <p className="micro-meta">Try widening a filter, or clear them to see the whole portfolio.</p>
          <button type="button" className="btn btn-secondary" onClick={() => setFilters(EMPTY_PORTFOLIO_FILTERS)}>
            Clear filters
          </button>
        </div>
      )}

      {fetchState.phase === "ready" && visibleRows.length > 0 && <PortfolioTable rows={visibleRows} />}
    </div>
  );
}
