import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { ApiClient, SavingsOpportunityBody } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import { loadTrackedRenewalActions } from "../renewals/renewalActionStore";
import KpiRow from "./KpiRow";
import OpportunitiesTable from "./OpportunitiesTable";
import { buildOpportunityRows, buildSupplierNameIndex, formatSavingsSummary, reduceKpiFetch, type KpiFetchState } from "./savingsViewModel";
import "./savings.css";

export interface SavingsRouteProps {
  apiClient: ApiClient;
}

type OpportunitiesFetchState =
  | { phase: "loading" }
  | { phase: "error"; statusCode: number | null; message: string }
  | { phase: "ready"; items: readonly SavingsOpportunityBody[] };

/**
 * Route `/savings` -- Savings, V2 (ADR-024 V2 IA amendment "No Home item": reached from Ask
 * actions, Renewals and Contract 360, not the rail; screens-v2.md #8; `app.jsx` `kpis` / `opps`).
 * Header ("Savings" + summary), the three-cell KPI band, the opportunities table (Supplier · Action
 * · Estimate · Status, rows open Contract 360), and the reroute state while nothing feeds it.
 *
 * **Three independent fetches, independent degrade states.** `getSavingsKpis` backs the band; its
 * failure degrades the band to a stale-labelled last-known state (`reduceKpiFetch`), never a blank.
 * `getSavingsOpportunities` backs the table; its failure renders the table section's own scoped
 * error + Retry, while this session's tracked renewal actions (sessionStorage-local) still render.
 * `getPortfolio` only supplies supplier *names* for the rows (`SavingsOpportunityResult` carries a
 * supplier id only); if it fails the rows fall back to the same id-fragment label the Portfolio
 * table uses -- never a fabricated name.
 */
export default function SavingsRoute({ apiClient }: SavingsRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [kpiState, setKpiState] = useState<KpiFetchState>({ phase: "loading" });
  const [opportunitiesState, setOpportunitiesState] = useState<OpportunitiesFetchState>({ phase: "loading" });
  const [supplierNames, setSupplierNames] = useState<ReadonlyMap<string, string>>(new Map());

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

  // sessionStorage read, not React state: this screen never writes to it (only ../renewals and
  // Contract 360 do), so re-reading it on every render is all the sync there is to do.
  const trackedRenewalActions = loadTrackedRenewalActions();
  const opportunityItems = opportunitiesState.phase === "ready" ? opportunitiesState.items : [];
  const rows = buildOpportunityRows(opportunityItems, trackedRenewalActions, supplierNames);
  const kpis = kpiState.phase === "ready" ? kpiState.kpis : null;
  const summary =
    kpiState.phase === "loading" || opportunitiesState.phase === "loading" ? "Loading savings…" : formatSavingsSummary(kpis, rows.length);

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

        {opportunitiesState.phase !== "loading" && rows.length > 0 && <OpportunitiesTable rows={rows} />}
      </section>
    </div>
  );
}
