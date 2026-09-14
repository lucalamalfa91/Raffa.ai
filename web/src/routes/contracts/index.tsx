import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import type { ApiClient, DocumentListPageBody, PortfolioListItem } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import { CHECK_AGAIN_LABEL, UPDATES_PAUSED_NOTICE, usePollBudget } from "../../components/shell/usePollBudget";
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
 * ADR-020 w15 §2.2 (task E16/F03/US01/T01): the tier's zero state has three variants and *no* new
 * string. The h3 "Nothing to triage yet" is true in all three and never changes; the sentence and
 * the CTA are read off the server's own document `counts` (ADR-027 §D7) -- never inferred from the
 * empty page (ADR-012 w15 §4). Dropping "Upload one to start." for a tenant that has already
 * uploaded is the whole fix: it is the one clause that tells a user to do what they have done.
 */
export type PortfolioZeroVariant = "nothing-held" | "processing" | "nothing-validated";

export function resolvePortfolioZeroVariant(counts: DocumentListPageBody["counts"] | null): PortfolioZeroVariant {
  if (!counts || counts.all === 0) return "nothing-held";
  if (counts.processing > 0) return "processing";
  return "nothing-validated";
}

export function getPortfolioZeroCopy(variant: PortfolioZeroVariant): { sentence: string; ctaLabel: string } {
  switch (variant) {
    case "nothing-held":
      return { sentence: "The portfolio lights up from validated contracts. Upload one to start.", ctaLabel: "Upload a contract" };
    case "processing":
      return { sentence: "Your documents are still being processed. The portfolio lights up from validated contracts.", ctaLabel: "Go to Documents" };
    case "nothing-validated":
      return { sentence: "The portfolio lights up from validated contracts.", ctaLabel: "Go to Documents" };
  }
}

/**
 * Route `/contracts` -- Portfolio, V2 (ADR-024 V2 IA amending ADR-018/ADR-020 screen 4;
 * screens-v2.md #6; `raffa-v2/markup.html` "PORTFOLIO" block, `app.jsx` `kbContracts` /
 * `pfSummary` / `moreCols`). Replaces the Day-1 screen's seven filter chips, attention strip and
 * ten-column severity-sorted table with the prototype's own shape: a header ("Portfolio" + the
 * `pfSummary` line + "More columns"), the validated contracts only, sorted by how soon notice must
 * be given, urgent rows tinted, rows opening Contract 360 -- and, while no contract is validated,
 * the tier's reroute state (R-WEB-02): "Nothing to triage yet" with one of the three sentences above.
 *
 * **Fetch-once, derive client-side.** One `GET /api/contracts` call per mount (and per Retry) for
 * the tenant's first page (`PORTFOLIO_PAGE_SIZE`, the backend's own ceiling); validated-only
 * filtering, ordering, urgency and the summary are all `portfolioViewModel.ts` over that page --
 * the same architecture the Day-1 screen already used, minus the client-side filter chips the V2
 * design does not have. "Validated" is `contractStatus.ts`'s one shared predicate, so this screen
 * can never show a contract the rail's own "From your contracts" count excludes.
 *
 * **While the zero state shows** (task E16/F03/US01/T01), the same one-row `listDocuments` read
 * Ask's off state makes says whether documents are in flight; while they are, both reads repeat on
 * the shared 2 s cadence under the five-minute no-change budget (ADR-020 w15 §8.3), and once the
 * budget is spent the block keeps its sentence and adds "Check again" beside its CTA.
 */
export default function PortfolioRoute({ apiClient }: PortfolioRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });
  const [moreColumns, setMoreColumns] = useState(false);
  const [documentCounts, setDocumentCounts] = useState<DocumentListPageBody["counts"] | null>(null);

  const loadPortfolio = useCallback(
    (silent = false) => {
      if (!workspace) return;

      if (!silent) setFetchState({ phase: "loading" });
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
    },
    [apiClient, workspace?.id],
  );

  const loadDocumentCounts = useCallback(() => {
    if (!workspace) return;
    void apiClient.listDocuments(workspace.id, { pageSize: 1 }).then((result) => {
      setDocumentCounts(result.ok && result.page ? result.page.counts : null);
    });
  }, [apiClient, workspace?.id]);

  useEffect(() => {
    loadPortfolio();
  }, [loadPortfolio]);

  const rows = useMemo(() => (fetchState.phase === "ready" ? buildPortfolioRows(fetchState.items) : []), [fetchState]);
  const summary = useMemo(() => buildPortfolioSummary(rows), [rows]);

  const ready = fetchState.phase === "ready";
  const lit = ready && rows.length > 0;
  const zero = ready && rows.length === 0;

  // The zero state's own read: which of the three sentences applies is a server fact.
  useEffect(() => {
    if (zero) loadDocumentCounts();
  }, [zero, loadDocumentCounts]);

  const zeroVariant = resolvePortfolioZeroVariant(documentCounts);
  const countsFingerprint = useMemo(
    () =>
      documentCounts === null
        ? "none"
        : `${documentCounts.all}/${documentCounts.needsAttention}/${documentCounts.needsReview}/${documentCounts.processing}/${documentCounts.rejected}`,
    [documentCounts],
  );
  const tick = useCallback(() => {
    loadDocumentCounts();
    loadPortfolio(true);
  }, [loadDocumentCounts, loadPortfolio]);
  const { paused: updatesPaused, resume: resumeUpdates } = usePollBudget({
    active: zero && zeroVariant === "processing",
    fingerprint: countsFingerprint,
    onTick: tick,
  });

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

  const zeroCopy = getPortfolioZeroCopy(zeroVariant);

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
          <button type="button" className="btn btn-secondary" onClick={() => loadPortfolio()}>
            Retry
          </button>
        </div>
      )}

      {zero && (
        // markup.html `kbOff`: the tier's reroute state -- the h3 verbatim, the sentence one of
        // ADR-020 w15 §2.2's three, selected by the server's counts.
        <div className="screen-reroute" role="status">
          <h3>Nothing to triage yet</h3>
          <p>{zeroCopy.sentence}</p>
          {updatesPaused && <p className="hint">{UPDATES_PAUSED_NOTICE}</p>}
          <div className="screen-reroute-actions">
            <Link to="/documents" className="btn btn-primary">
              {zeroCopy.ctaLabel}
            </Link>
            {updatesPaused && (
              <button type="button" className="btn btn-secondary" onClick={resumeUpdates}>
                {CHECK_AGAIN_LABEL}
              </button>
            )}
          </div>
        </div>
      )}

      {lit && <PortfolioTable rows={rows} moreColumns={moreColumns} />}
    </div>
  );
}
