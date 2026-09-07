import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { ApiClient, SavingsOpportunityBody } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import { loadTrackedRenewalActions } from "../renewals/renewalActionStore";
import KpiRow from "./KpiRow";
import OpportunitiesTable from "./OpportunitiesTable";
import { buildOpportunityRows, reduceKpiFetch, type KpiFetchState } from "./homeViewModel";
import "./home.css";

export interface HomeRouteProps {
  apiClient: ApiClient;
}

type OpportunitiesFetchState =
  | { phase: "loading" }
  | { phase: "error"; statusCode: number | null; message: string }
  | { phase: "ready"; items: readonly SavingsOpportunityBody[] };

/**
 * Route `/` (ADR-018 "/ (home)"; screens.md #9 "Home -- Savings"; ADR-020 screen 9; task
 * E08/F02/US01/T01, us-01-savings-home AC-1 six KPI cells / AC-2 opportunities table / AC-3 rows +
 * stale-labelled error state). Wired into `../../components/shell/WorkspaceShellApp.tsx`'s index
 * route in place of that shell task's `ScaffoldScreen` placeholder, the same seam
 * `../renewals/index.tsx` (RenewalsRoute) already used for `/renewals`.
 *
 * **Two independent fetches, two independent degrade states** -- the same "independently optional"
 * shape `../contracts/contract360/index.tsx` already established for its own renewals+priority pair.
 * `getSavingsKpis` backs AC-1's KPI row; its own failure never blocks AC-2's table -- it degrades to
 * a stale-labelled KPI row instead (screens.md #9's own named state), via
 * `homeViewModel.ts#reduceKpiFetch`, which keeps whatever summary this screen last successfully
 * fetched (or `null`, before the first successful fetch) rather than blanking the row. Retrying does
 * **not** flash the KPI row back to a loading skeleton -- the stale numbers stay visible, tagged, for
 * the whole in-flight retry, only updating once it resolves (matching the prototype's own "last
 * successful refresh" framing, not a fresh loading state). `getSavingsOpportunities` backs AC-2's
 * opportunities table independently; its own failure renders that section's own scoped `.error-state`
 * + Retry, never the whole screen -- and, per `homeViewModel.ts#buildOpportunityRows`'s own comment,
 * this session's tracked renewal actions still render even when the real fetch has failed (they are
 * `sessionStorage`-local, not sourced from this call at all).
 */
export default function HomeRoute({ apiClient }: HomeRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [kpiState, setKpiState] = useState<KpiFetchState>({ phase: "loading" });
  const [opportunitiesState, setOpportunitiesState] = useState<OpportunitiesFetchState>({ phase: "loading" });

  const loadKpis = useCallback(() => {
    if (!workspace) return;
    void apiClient.getSavingsKpis(workspace.id).then((result) => {
      setKpiState((previous) =>
        reduceKpiFetch(previous, result.ok && result.kpis ? { ok: true, kpis: result.kpis } : { ok: false }),
      );
    });
    // Depends on workspace?.id (a primitive), not workspace itself: loadCurrentWorkspace() returns a
    // fresh object every call, the same convention every other route's own load() callback follows
    // (e.g. ../renewals/index.tsx#load).
  }, [apiClient, workspace?.id]);

  const loadOpportunities = useCallback(() => {
    if (!workspace) return;

    setOpportunitiesState({ phase: "loading" });

    void apiClient.getSavingsOpportunities(workspace.id).then((result) => {
      if (!result.ok || !result.opportunities) {
        setOpportunitiesState({
          phase: "error",
          statusCode: result.statusCode,
          // Same 503-vs-other split as ../renewals/index.tsx's own load (ADR-019 accessibility
          // baseline: "names the failing job, never a raw stack trace").
          message:
            result.statusCode === 503 || result.statusCode === null
              ? "Contigo's savings service is temporarily unavailable. Try again in a moment."
              : (result.error ?? "The opportunities list could not be loaded."),
        });
        return;
      }

      setOpportunitiesState({ phase: "ready", items: result.opportunities.items });
    });
  }, [apiClient, workspace?.id]);

  useEffect(() => {
    loadKpis();
    loadOpportunities();
  }, [loadKpis, loadOpportunities]);

  if (!workspace) {
    // Should not normally be reachable -- App.tsx only mounts the shell (and therefore this route)
    // once a workspace is current -- but this route reads the store directly rather than trusting
    // that earlier check, the same defensive convention every other route under `src/routes/` follows.
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before viewing savings.</p>
      </div>
    );
  }

  // sessionStorage read, not React state: this screen never writes to it (only ../renewals/index.tsx
  // does), so there is nothing to keep in sync beyond re-reading it on every render -- the same
  // "read-only consumer" shape this store's own header comment anticipates for this exact task.
  const trackedRenewalActions = loadTrackedRenewalActions();
  const opportunityItems = opportunitiesState.phase === "ready" ? opportunitiesState.items : [];
  const rows = buildOpportunityRows(opportunityItems, trackedRenewalActions);

  return (
    <div className="home-screen">
      <p className="screen-kicker">R3</p>
      <h2 className="screen-title">Home</h2>
      <p className="micro-meta">Savings KPIs and prioritized opportunities across your portfolio.</p>

      <KpiRow kpiState={kpiState} onRetry={loadKpis} />

      <div className="home-opportunities-section">
        <h6>Opportunities</h6>

        {opportunitiesState.phase === "loading" && (
          <div className="home-opportunities-skeleton" role="status" aria-live="polite">
            {Array.from({ length: 4 }, (_, index) => (
              <div key={index} className="skeleton home-opportunities-skeleton-row" />
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
          <div className="empty-state" role="status">
            <h3>No savings opportunities yet</h3>
            <p className="micro-meta">Opportunities appear here once a renewal is actioned or a saving is identified.</p>
            <Link to="/renewals" className="btn btn-primary">
              View renewal pipeline
            </Link>
          </div>
        )}

        {opportunitiesState.phase !== "loading" && rows.length > 0 && <OpportunitiesTable rows={rows} />}
      </div>
    </div>
  );
}
