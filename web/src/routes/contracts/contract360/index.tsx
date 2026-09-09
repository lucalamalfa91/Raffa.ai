import { useCallback, useEffect, useState } from "react";
import { useLocation, useParams } from "react-router-dom";
import type { ApiClient, Contract360Body, RenewalPipelineItemBody, RenewalPriorityBody } from "../../../api/client";
import { loadCurrentWorkspace } from "../../signin/workspaceStore";
import ClauseHighlight from "./ClauseHighlight";
import Contract360Header from "./Contract360Header";
import FactTable from "./FactTable";
import OverviewTab from "./OverviewTab";
import RenewalTab from "./RenewalTab";
import {
  CONTRACT_360_TABS,
  buildClausesRows,
  buildCommercialsRows,
  buildDocumentsRows,
  buildObligationsRows,
  buildOverviewDetailRows,
  buildProductsRows,
  buildRecommendation,
  buildRisksRows,
  computeNeedsAttention,
  computeTopRisks,
  isContract360TabName,
  resolveBackLink,
  resolveHighlightedClauseId,
  type Contract360TabName,
} from "./contract360ViewModel";
import "./contract360.css";

export interface Contract360RouteProps {
  apiClient: ApiClient;
}

type FetchState =
  | { phase: "loading" }
  | { phase: "not-found" }
  | { phase: "error"; statusCode: number | null; message: string }
  | {
      phase: "ready";
      contract: Contract360Body;
      renewals: readonly RenewalPipelineItemBody[];
      priority: RenewalPriorityBody | null;
    };

/**
 * Route `/contracts/:contractId` (ADR-018; screens.md #5 "Contract 360"; ADR-020 screen 5; task
 * E07/F02/US01/T01, us-01-contract-360 AC-1 header / AC-2 fact row / AC-3 overview / AC-4 10 tabs).
 * Wired into `../../../components/shell/WorkspaceShellApp.tsx`'s `contracts/:contractId` route in
 * place of that shell task's `ScaffoldScreen` placeholder -- the same seam `../index.tsx`
 * (PortfolioRoute) already used for `/contracts`.
 *
 * **Fetch order**: `getContract360` first -- a `404` there is this screen's own named "not found"
 * state (AC-4-equivalent), distinct from a transport/5xx error, and short-circuits before either
 * other call runs. Once the contract itself is confirmed to exist, `getRenewals` (the tenant's whole
 * auto-renewing pipeline) and `getRenewalPriority` (this one contract's score breakdown) run
 * together -- both **independently optional**: a non-auto-renewing contract legitimately has no
 * pipeline entry, and either call failing degrades the Overview recommendation / header priority
 * fact to their own honest "not yet available" state rather than failing the whole screen (see
 * `contract360ViewModel.ts#buildRecommendation` / `formatPriorityFact`), never a second Retry
 * button for a sub-fetch nothing else depends on.
 *
 * **Facts vs AI (ADR-019, AC-4)**: `activeTab` gates which tab renders, but every non-Overview tab
 * renders through the shared `FactTable` (deterministic facts, confidence-tagged); only `Overview`
 * ever mounts the `.ai-recommendation` card (`OverviewTab.tsx`) -- the two are never in the same
 * branch of this switch, let alone the same DOM node.
 *
 * **Deep-linking a tab (task E07/F04/US01/T01)**: `activeTab` defaults to whichever tab
 * `location.state.tab` names (validated by `contract360ViewModel.ts#isContract360TabName`), falling
 * back to `"Overview"` when absent/unrecognised -- the seam the Ask Contigo screen's citation chips
 * use to land directly on `Clauses` (AC-2 "opening Contract 360 > Clauses"), rather than always
 * resetting to Overview and making the user click through tabs themselves.
 *
 * **Citation landing (task E13/F10/US01/T01, ADR-024; parent story us-01-contract360-landing
 * AC-1/AC-2)**: a `?clause=<clauseId>` or `?page=<n>` query string (read off `location.search`, the
 * same plain-`URLSearchParams` convention `../../../components/shell/workspaceRole.ts` already uses
 * for `?role=`) also opens straight on `Clauses` -- `requestedTab` (the existing `state.tab` seam
 * above) is checked first, so a caller that somehow sets both is not silently overridden by this
 * task's own newer seam. Once the contract loads, `resolveHighlightedClauseId` resolves which real
 * clause (if any) the params name; a `ClauseHighlight` card rendered right below the unmodified
 * Clauses `FactTable` shows that clause's `rawText` (the "highlight it" + "render its original
 * wording" the task text asks for, both satisfied by this one new card -- `FactTable.tsx` itself is
 * out of this task's own "Files to create or modify"), with no click required (see that component's
 * own doc comment). `location.state.from` similarly drives the header's back link (`resolveBackLink`)
 * -- `"ask"` reads "← Ask Contigo" (AC-1), other origins are ready but unsent today, and an
 * absent/unrecognised one renders no back link, unchanged from before this task.
 */
export default function Contract360Route({ apiClient }: Contract360RouteProps) {
  const { contractId } = useParams<{ contractId: string }>();
  const location = useLocation();
  const workspace = loadCurrentWorkspace();
  // Task E07/F04/US01/T01 (ask-contigo-ui, AC-2 "citation chip ... opening Contract 360 > Clauses"):
  // an Ask Contigo citation navigates here with `{ state: { tab: "Clauses" } }`, mirroring the same
  // `useLocation().state` seam ../../ask/GlobalAskBar's own header comment already established for
  // /ask's `query`. `isContract360TabName` guards against trusting an arbitrary router-state value;
  // an absent/unrecognised one falls back to "Overview", this screen's pre-existing default.
  const requestedTabState = (location.state as { tab?: unknown } | null)?.tab;
  const requestedTab = isContract360TabName(requestedTabState) ? requestedTabState : null;

  // Task E13/F10/US01/T01 (contract360-landing, AC-1): `?clause=`/`?page=` also lands on Clauses,
  // stable for the lifetime of one navigation entry exactly like `location.state` above (react-router
  // does not recreate `location` -- and therefore not `location.search` -- on a re-render that isn't
  // a navigation), so it is safe to fold into `defaultTab` below and into `load`'s own deps array.
  const searchParams = new URLSearchParams(location.search);
  const clauseParam = searchParams.get("clause");
  const pageParam = searchParams.get("page");
  const hasClauseLanding = clauseParam !== null || pageParam !== null;
  const defaultTab: Contract360TabName = requestedTab ?? (hasClauseLanding ? "Clauses" : "Overview");

  // AC-1 "the back label reads 'Ask Contigo' when arriving from a chat" -- `resolveBackLink`'s own
  // doc comment has the full four-origin story (ADR-020 screen 5).
  const fromState = (location.state as { from?: unknown } | null)?.from;
  const backLink = resolveBackLink(fromState);

  const [fetchState, setFetchState] = useState<FetchState>({ phase: "loading" });
  const [activeTab, setActiveTab] = useState<Contract360TabName>(defaultTab);

  const load = useCallback(() => {
    if (!workspace || !contractId) return;

    setFetchState({ phase: "loading" });
    setActiveTab(defaultTab);

    void apiClient.getContract360(workspace.id, contractId).then(async (result) => {
      if (!result.ok || !result.contract) {
        if (result.statusCode === 404) {
          setFetchState({ phase: "not-found" });
          return;
        }
        setFetchState({
          phase: "error",
          statusCode: result.statusCode,
          // Same 503-vs-other split as ../index.tsx's own loadPortfolio (ADR-019 accessibility
          // baseline: "names the failing job, never a raw stack trace").
          message:
            result.statusCode === 503 || result.statusCode === null
              ? "Contigo's contract service is temporarily unavailable. Try again in a moment."
              : (result.error ?? "The contract could not be loaded."),
        });
        return;
      }

      const contract = result.contract;

      const [renewalsResult, priorityResult] = await Promise.all([
        apiClient.getRenewals(workspace.id),
        apiClient.getRenewalPriority(workspace.id, contractId),
      ]);

      setFetchState({
        phase: "ready",
        contract,
        renewals: renewalsResult.ok && renewalsResult.renewals ? renewalsResult.renewals.items : [],
        priority: priorityResult.ok ? priorityResult.priority : null,
      });
    });
    // Depends on workspace?.id/contractId (primitives), not workspace itself: loadCurrentWorkspace()
    // returns a fresh object every call, the same convention ../index.tsx's own loadPortfolio uses.
    // defaultTab is derived from location.state/location.search, which react-router keeps
    // referentially stable for the lifetime of one navigation entry.
  }, [apiClient, workspace?.id, contractId, defaultTab]);

  useEffect(() => {
    load();
  }, [load]);

  if (!workspace) {
    // Should not normally be reachable (App.tsx only mounts the shell once a workspace is current)
    // -- same defensive convention ../index.tsx (Portfolio) and ../../documents/index.tsx follow.
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before viewing a contract.</p>
      </div>
    );
  }

  if (!contractId) {
    return (
      <div className="empty-state" role="status">
        <h3>No contract selected</h3>
      </div>
    );
  }

  if (fetchState.phase === "loading") {
    return (
      <div className="contract360-skeleton" role="status" aria-live="polite">
        <p className="micro-meta">Loading contract…</p>
        {Array.from({ length: 6 }, (_, index) => (
          <div key={index} className="skeleton contract360-skeleton-row" />
        ))}
      </div>
    );
  }

  if (fetchState.phase === "not-found") {
    return (
      <div className="empty-state" role="status">
        <h3>Contract not found</h3>
        <p className="micro-meta">This contract does not exist, or is not in your workspace.</p>
      </div>
    );
  }

  if (fetchState.phase === "error") {
    return (
      <div className="error-state" role="alert">
        <h4>Contract unavailable</h4>
        <p className="micro-meta">
          {fetchState.message}
          {fetchState.statusCode !== null && ` (HTTP ${fetchState.statusCode})`}
        </p>
        <button type="button" className="btn btn-secondary" onClick={load}>
          Retry
        </button>
      </div>
    );
  }

  const { contract, renewals, priority } = fetchState;
  const { header, tabs } = contract;

  // Task E13/F10/US01/T01, AC-1: resolved once per render -- `ClauseHighlight` below is `FactTable`'s
  // own unmodified list (out of this task's "Files to create or modify") plus this one extra card,
  // not a change to how the list itself renders.
  const highlightedClauseId = resolveHighlightedClauseId(tabs.clauses, clauseParam, pageParam);
  const highlightedClause = tabs.clauses.find((c) => c.clauseId === highlightedClauseId) ?? null;

  return (
    <div className="contract360-screen">
      <Contract360Header header={header} docCount={tabs.documents.length} priority={priority} backLink={backLink} />

      <nav className="contract360-tabs" aria-label="Contract 360 sections">
        {CONTRACT_360_TABS.map((tab) => (
          <button
            key={tab}
            type="button"
            className="contract360-tab"
            aria-pressed={activeTab === tab}
            onClick={() => setActiveTab(tab)}
          >
            {tab}
          </button>
        ))}
      </nav>

      {activeTab === "Overview" && (
        <OverviewTab
          contractId={contractId}
          recommendation={buildRecommendation(header, renewals)}
          attention={computeNeedsAttention(tabs)}
          topRisks={computeTopRisks(tabs.risks)}
          detailRows={buildOverviewDetailRows(tabs.overview)}
          onOpenRenewalTab={() => setActiveTab("Renewal")}
          onOpenRisksTab={() => setActiveTab("Risks")}
        />
      )}
      {activeTab === "Commercials" && (
        <FactTable title="Commercials" rows={buildCommercialsRows(tabs.commercials)} emptyMessage="No commercial terms recorded." />
      )}
      {activeTab === "Products" && (
        <FactTable title="Products" rows={buildProductsRows(tabs.products)} emptyMessage="No line items recorded for this contract." />
      )}
      {activeTab === "Clauses" && (
        <>
          <FactTable title="Clauses" rows={buildClausesRows(tabs.clauses)} emptyMessage="No clauses extracted for this contract." />
          {highlightedClause !== null && <ClauseHighlight clause={highlightedClause} />}
        </>
      )}
      {activeTab === "Obligations" && (
        <FactTable
          title="Obligations"
          rows={buildObligationsRows(tabs.obligations)}
          emptyMessage="No obligations extracted for this contract."
        />
      )}
      {activeTab === "Risks" && (
        <FactTable title="Risks" rows={buildRisksRows(tabs.risks)} emptyMessage="No risks recorded for this contract." />
      )}
      {activeTab === "Documents" && (
        <FactTable title="Documents" rows={buildDocumentsRows(tabs.documents)} emptyMessage="No documents linked to this contract." />
      )}
      {activeTab === "Benchmark" && (
        <FactTable
          title="Benchmark"
          rows={[]}
          emptyMessage="Benchmark data is not available yet — the Benchmark Service ships in R3."
        />
      )}
      {activeTab === "Renewal" && <RenewalTab renewal={tabs.renewal} priority={priority} />}
      {activeTab === "Activity" && (
        <FactTable title="Activity" rows={[]} emptyMessage="Activity history is not available yet — ships in a later release." />
      )}
    </div>
  );
}
