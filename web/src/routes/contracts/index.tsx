import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import type { ApiClient, DocumentListPageBody, GetPortfolioResult, PortfolioListItem } from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import { CHECK_AGAIN_LABEL, UPDATES_PAUSED_NOTICE, usePollBudget } from "../../components/shell/usePollBudget";
import PortfolioTable from "./PortfolioTable";
import {
  buildPortfolioRows,
  buildPortfolioSummary,
  formatPortfolioSummary,
  moreColumnsLabel,
  PORTFOLIO_SUMMARY_OFF,
  readCategoryFilter,
} from "./portfolioViewModel";
import "./contracts.css";

export interface PortfolioRouteProps {
  apiClient: ApiClient;
}

/** Matches `PortfolioPageRequest.MaxPageSize` (backend) -- the largest single page the endpoint allows. */
const PORTFOLIO_PAGE_SIZE = 100;

/** GET /api/contracts can hang (auth refresh, a stalled API replica) while GET /api/workspaces
 * already painted the rail badge -- leave loading for this long, then the error/Retry path. */
const PORTFOLIO_LOAD_TIMEOUT_MS = 15_000;

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
 * `Raffa.ai V2.dc.html` PORTFOLIO block, `kbContracts` / `pfSummary` / `moreCols` in its logic).
 * The prototype's own shape, and nothing beside it: a header ("Portfolio" + the `pfSummary` line +
 * "More columns"), the validated contracts only, sorted by how soon notice must be given, urgent
 * rows tinted, rows opening Contract 360 -- and, while no contract is validated, the tier's reroute
 * state (R-WEB-02): "Nothing to triage yet" with one of the three sentences above.
 *
 * **Fetch, derive client-side, re-read while ingest is in flight.** One `GET /api/contracts` call
 * per mount (and per Retry) for the tenant's first page (`PORTFOLIO_PAGE_SIZE`); validated-only
 * filtering, ordering, urgency and the summary are all `portfolioViewModel.ts` over that page.
 * "Validated" is `contractStatus.ts`'s one shared predicate, so this screen can never show a
 * contract the rail's own "From your contracts" count excludes. A hung fetch leaves loading for
 * `PORTFOLIO_LOAD_TIMEOUT_MS` then the error/Retry path -- the rail badge can be live while this
 * call is not. While documents are still `Uploaded`/`Processing`, both the list and the document
 * counts re-read on the shared 2 s cadence (also when rows are already on screen, so later
 * completions appear without a remount); the five-minute no-change budget still applies.
 *
 * **`?category=`** on the URL still reaches `GET /api/contracts` so a shared link keeps working
 * (ADR-012); there is no filter toolbar on the screen itself, exactly as in the prototype.
 */
export default function PortfolioRoute({ apiClient }: PortfolioRouteProps) {
  const workspace = loadCurrentWorkspace();
  const [searchParams] = useSearchParams();
  // Task E24/F01/US02/T01 (story us-02-portfolio-category-web; closes NW-23): the only state this
  // filter has. Never copied into a `useState` -- re-read from the URL on every render, so a
  // shared link, a reload or the browser's back/forward all reproduce the same filtered request
  // (ADR-012 "a client store never stands in for a missing GET").
  const category = readCategoryFilter(searchParams);
  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });
  const [moreColumns, setMoreColumns] = useState(false);
  const [documentCounts, setDocumentCounts] = useState<DocumentListPageBody["counts"] | null>(null);
  const loadGeneration = useRef(0);

  const loadPortfolio = useCallback(
    (silent = false) => {
      if (!workspace) return;

      const generation = ++loadGeneration.current;
      if (!silent) setFetchState({ phase: "loading" });
      const request = apiClient.getPortfolio(workspace.id, {
        pageSize: PORTFOLIO_PAGE_SIZE,
        // Absent (not a blank string) when unset, matching every other optional field's own
        // "omit rather than send blank" convention (PortfolioQueryParams' own doc comment).
        category: category === "" ? undefined : category,
      });
      let timeoutId: number | undefined;
      const timedOut: Promise<GetPortfolioResult> = new Promise((resolve) => {
        timeoutId = window.setTimeout(() => {
          resolve({
            ok: false,
            statusCode: null,
            portfolio: null,
            error: "The portfolio request timed out.",
          });
        }, PORTFOLIO_LOAD_TIMEOUT_MS);
      });
      // Silent re-reads keep the last good page if the API is slow; only the first paint / Retry
      // may fall through to the error state after the timeout.
      void Promise.race(silent ? [request] : [request, timedOut]).then((result) => {
        if (timeoutId !== undefined) window.clearTimeout(timeoutId);
        if (generation !== loadGeneration.current) return;
        if (!result.ok || !result.portfolio) {
          if (silent) return;
          setFetchState({
            phase: "error",
            statusCode: result.statusCode,
            message:
              result.statusCode === 503 || result.statusCode === null
                ? "Raffa.ai's portfolio service is temporarily unavailable. Try again in a moment."
                : (result.error ?? "The portfolio could not be loaded."),
          });
          return;
        }
        setFetchState({ phase: "ready", items: result.portfolio.items });
      });
    },
    [apiClient, workspace?.id, category],
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

  // Counts drive both the zero-state sentence and the in-flight poll, including when rows are
  // already on screen (later Completions must be able to appear without a remount).
  useEffect(() => {
    if (ready) loadDocumentCounts();
  }, [ready, loadDocumentCounts]);

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
    active: ready && documentCounts !== null && documentCounts.processing > 0,
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
        // `kbOff`: the tier's reroute state -- the h3 verbatim, the sentence one of
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
