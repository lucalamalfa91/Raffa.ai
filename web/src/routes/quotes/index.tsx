import { useCallback, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import type {
  ApiClient,
  NegotiationLeverTypeName,
  NegotiationOutcomeBody,
  QuoteBenchmarkHistoryEntryBody,
  QuoteNegotiationOutcomeBody,
  QuoteRecalculationBody,
  SkuMappingCorrectionInput,
  UploadedQuote,
  UploadQuoteFields,
} from "../../api/client";
import UploadQuoteForm from "./UploadQuoteForm";
import MappingBlock, { type MapDraft } from "./MappingBlock";
import TargetStep, { defaultTargetPrice, defaultWalkAway } from "./TargetStep";
import NegotiationStep, { type NegotiationOutcomeInput } from "./NegotiationStep";
import AssessmentResult from "./assessment/AssessmentResult";
import QuoteHistoryList from "./history/QuoteHistoryList";
import {
  QUOTE_INTRO,
  QUOTE_LEVERS_FOOTER,
  aggregateQuote,
  buildExtractRows,
  buildQuoteLineRows,
  formatQuoteMeta,
  isAssessmentBlocked,
  mergeKnownLineDetails,
  type LineDetail,
} from "./quoteCheckViewModel";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import "./quotes.css";

export interface QuoteCheckRouteProps {
  apiClient: ApiClient;
}

type FetchState =
  | { phase: "loading"; quoteId: string }
  | { phase: "not-found"; quoteId: string }
  | { phase: "error"; quoteId: string; statusCode: number | null; message: string }
  | { phase: "ready"; quoteId: string; recalculation: QuoteRecalculationBody };

/** How far past the lines table the reader has chosen to go (`QUOTE_LEVERS_FOOTER`: "shown only if you want them"). */
type LeversStage = "hidden" | "target" | "negotiation";

/** AC-2: `GET /api/quotes/benchmark-history` is workspace-wide, not quote-specific -- tracked
 * independently of `FetchState` above and loaded once per workspace (see the effect below), so
 * browsing from the landing into a quote and back never re-fetches or blanks out an already-loaded
 * history list. */
type HistoryState =
  | { phase: "loading" }
  | { phase: "error"; message: string }
  | { phase: "ready"; entries: readonly QuoteBenchmarkHistoryEntryBody[] };

/**
 * Route `/quotes` and `/quotes/:quoteId` -- Quote check, V2 (ADR-024 V2 IA; screens-v2.md #9;
 * `raffa-v2/markup.html` "QUOTE CHECK (optional)" block). The header is constant ("Optional · new
 * purchase" · "Quote check" · the intro sentence); below it either the landing (`UploadQuoteForm` +
 * `./history/QuoteHistoryList`) or, once a quote is loaded, the market-benchmark result
 * (`./assessment/AssessmentResult` -- the three-cell band, honest cold-start copy, and the lines
 * table) and the footer "Target and negotiation levers are one step further — shown only if you want
 * them." that reveals the Target step, then Negotiation. The Day-1 four-step stepper is gone; the
 * same real calls remain.
 *
 * **Real backend, not the prototype's fixture.** `POST /api/quotes`, `POST /api/quotes/{id}/
 * assessment/recalculate` (a strict superset of `GET …/assessment`: same `assessment`, plus
 * `unmatchedLines`; an empty `mappings` array is its documented "pure refresh"),
 * `GET /api/quotes/benchmark-history` (task E25/F04/US02/T01, closes NW-57) and
 * `POST /api/negotiations/outcomes` -- see `../../api/client.ts`. `GET /api/quotes/{id}` (`loadQuote`
 * below) reads back this quote's own recorded negotiation outcome, not its upload metadata -- the
 * header meta line is still keyed off `quoteMetaById`, populated only by this session's own
 * `uploadQuote` response, so a quote reopened from `QuoteHistoryList` (never uploaded this session)
 * falls back to the truncated id there (`formatQuoteMeta`'s own null-quote branch); a named,
 * pre-existing gap, not something this task's file scope closes. Second named gap: no HTTP endpoint
 * for `NegotiationStrategyService`'s lever recommendations (`NegotiationStep.tsx`).
 *
 * **Blocked assessment.** While any line is still `SkuMatchStatus.Unmatched`, the band shows what
 * it honestly can, the unmapped lines say "Needs mapping", and the mapping block takes the footer's
 * place -- target and levers only open once every line is resolved. This never hides
 * `AssessmentResult`: the mapping block/footer/levers/negotiation sections are additional siblings
 * below it, never a replacement -- so the "See it in Savings →" CTA deep inside `NegotiationStep`
 * (AC-3) can never stand in for the benchmark result above it.
 */
export default function QuoteCheckRoute({ apiClient }: QuoteCheckRouteProps) {
  const { quoteId: routeQuoteId } = useParams<{ quoteId?: string }>();
  const navigate = useNavigate();
  const workspace = loadCurrentWorkspace();

  const [fetchState, setFetchState] = useState<FetchState | null>(null);
  const [leversStage, setLeversStage] = useState<LeversStage>("hidden");
  const [quoteMetaById, setQuoteMetaById] = useState<Readonly<Record<string, UploadedQuote>>>({});
  const [knownLineDetails, setKnownLineDetails] = useState<ReadonlyMap<string, LineDetail>>(new Map());
  const [mapDrafts, setMapDrafts] = useState<Readonly<Record<string, MapDraft>>>({});
  const [applying, setApplying] = useState(false);
  const [applyError, setApplyError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [targetPrice, setTargetPrice] = useState("");
  const [walkAway, setWalkAway] = useState("");
  const [targetInitializedFor, setTargetInitializedFor] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<QuoteNegotiationOutcomeBody | NegotiationOutcomeBody | null>(null);
  const [outcomeSubmitting, setOutcomeSubmitting] = useState(false);
  const [outcomeError, setOutcomeError] = useState<string | null>(null);
  const [quoteOutcomeState, setQuoteOutcomeState] = useState<"idle" | "loading" | "error" | "ready">("idle");
  const [quoteOutcomeError, setQuoteOutcomeError] = useState<string | null>(null);
  const [historyState, setHistoryState] = useState<HistoryState>({ phase: "loading" });

  const load = useCallback(
    (quoteId: string, mappings: readonly SkuMappingCorrectionInput[] = []) => {
      if (!workspace) return;
      setFetchState({ phase: "loading", quoteId });
      void apiClient.recalculateQuoteAssessment(workspace.id, quoteId, mappings).then((result) => {
        if (!result.ok || !result.recalculation) {
          if (result.statusCode === 404) {
            setFetchState({ phase: "not-found", quoteId });
            return;
          }
          setFetchState({
            phase: "error",
            quoteId,
            statusCode: result.statusCode,
            message:
              result.statusCode === 503 || result.statusCode === null
                ? "Raffa.ai's quote service is temporarily unavailable. Try again in a moment."
                : (result.error ?? "The quote could not be loaded."),
          });
          return;
        }

        setKnownLineDetails((previous) => mergeKnownLineDetails(previous, result.recalculation!.unmatchedLines));
        setFetchState({ phase: "ready", quoteId, recalculation: result.recalculation });
      });
    },
    // Depends on workspace?.id (a primitive), not workspace itself: loadCurrentWorkspace() returns a
    // fresh object every call, the same convention every other route in this codebase uses.
    [apiClient, workspace?.id],
  );

  const loadQuote = useCallback(
    (quoteId: string) => {
      if (!workspace) return;
      setQuoteOutcomeState("loading");
      setQuoteOutcomeError(null);
      void apiClient.getQuote(workspace.id, quoteId).then((result) => {
        if (!result.ok || !result.quote) {
          setQuoteOutcomeState("error");
          setQuoteOutcomeError(result.error ?? "The quote could not be loaded.");
          return;
        }
        const newest = result.quote.outcomes[0] ?? null;
        setOutcome(newest);
        setQuoteOutcomeState("ready");
      });
    },
    [apiClient, workspace?.id],
  );

  /** AC-2: every request this workspace has made, newest first, read back from durable server state
   * (ADR-028) -- never a client store. Rendered only on the landing (see the `!routeQuoteId` branch
   * below); see `./history/QuoteHistoryList.tsx`'s own header comment for why it never also renders
   * beside an already-open quote's own live result. */
  const loadHistory = useCallback(() => {
    if (!workspace) return;
    setHistoryState({ phase: "loading" });
    void apiClient.getQuoteBenchmarkHistory(workspace.id).then((result) => {
      if (!result.ok || !result.history) {
        setHistoryState({ phase: "error", message: result.error ?? "Quote check history could not be loaded." });
        return;
      }
      setHistoryState({ phase: "ready", entries: result.history.items });
    });
  }, [apiClient, workspace?.id]);

  useEffect(() => {
    loadHistory();
    // Workspace-wide, not quote-specific: this effect depends only on `loadHistory`'s own identity
    // (which itself only changes with `workspace?.id`), never on `routeQuoteId` -- unlike the sibling
    // effect below, navigating between the landing and a loaded quote must not re-fetch or reset it.
  }, [loadHistory]);

  useEffect(() => {
    setLeversStage("hidden");
    setMapDrafts({});
    setApplyError(null);
    setOutcome(null);
    setOutcomeError(null);
    setQuoteOutcomeState("idle");
    setQuoteOutcomeError(null);
    if (routeQuoteId) {
      load(routeQuoteId);
      loadQuote(routeQuoteId);
    } else {
      setFetchState(null);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `load` is stable per workspace id; a
    // full-deps list would also re-run this reset on every workspace re-render, which is not what
    // "the route's quoteId changed" means here.
  }, [routeQuoteId, workspace?.id]);

  if (!workspace) {
    return (
      <div className="empty-state" role="status">
        <h3>No workspace selected</h3>
        <p className="micro-meta">Choose a workspace before checking a quote.</p>
      </div>
    );
  }

  const handleUpload = async (file: File, fields: UploadQuoteFields) => {
    setUploading(true);
    const result = await apiClient.uploadQuote(workspace.id, file, fields);
    setUploading(false);
    if (!result.ok || !result.quote) {
      return { ok: false, error: result.error ?? "The quote could not be uploaded." };
    }
    setQuoteMetaById((previous) => ({ ...previous, [result.quote!.id]: result.quote! }));
    // AC-2: "keep every request" -- this new quote is now part of the workspace's durable history,
    // so refresh it immediately rather than waiting for a future mount to notice it.
    loadHistory();
    navigate(`/quotes/${result.quote.id}`, { replace: true });
    return { ok: true };
  };

  const header = (metaLine: string | null) => (
    <header className="quote-header">
      <p className="screen-kicker">Optional · new purchase</p>
      <h2 className="screen-title">Quote check</h2>
      <p className="quote-header-intro">{QUOTE_INTRO}</p>
      {metaLine !== null && <p className="micro-meta quote-header-meta">{metaLine}</p>}
    </header>
  );

  if (!routeQuoteId) {
    return (
      <div className="quote-screen">
        {header(null)}
        <UploadQuoteForm onUpload={handleUpload} submitting={uploading} />
        <section className="quote-history-section" aria-label="Quote check history">
          <h6>Quote check history</h6>
          <p className="micro-meta quote-history-intro">
            Every quote this workspace has checked, newest first — durable on the server, not this browser tab.
            Reopen one to see its market position again.
          </p>
          {historyState.phase === "loading" && (
            <div className="quote-history-skeleton" role="status" aria-live="polite">
              <p className="micro-meta">Loading quote check history…</p>
              <div className="skeleton quote-skeleton-row" />
            </div>
          )}
          {historyState.phase === "error" && (
            <div className="error-state" role="alert">
              <h4>Quote check history unavailable</h4>
              <p className="micro-meta">{historyState.message}</p>
              <button type="button" className="btn btn-secondary" onClick={loadHistory}>
                Retry
              </button>
            </div>
          )}
          {historyState.phase === "ready" && <QuoteHistoryList entries={historyState.entries} />}
        </section>
      </div>
    );
  }

  if (fetchState === null || fetchState.phase === "loading") {
    return (
      <div className="quote-screen">
        {header(null)}
        <div className="quote-skeleton" role="status" aria-live="polite">
          <p className="micro-meta">Loading quote…</p>
          {Array.from({ length: 4 }, (_, index) => (
            <div key={index} className="skeleton quote-skeleton-row" />
          ))}
        </div>
      </div>
    );
  }

  if (fetchState.phase === "not-found") {
    return (
      <div className="quote-screen">
        {header(null)}
        <div className="screen-reroute" role="status">
          <h3>Quote not found</h3>
          <p>This quote does not exist, or is not in your workspace.</p>
          <button type="button" className="btn btn-primary" onClick={() => navigate("/quotes")}>
            Upload a quote
          </button>
        </div>
      </div>
    );
  }

  if (fetchState.phase === "error") {
    return (
      <div className="quote-screen">
        {header(null)}
        <div className="error-state" role="alert">
          <h4>Quote check unavailable</h4>
          <p className="micro-meta">
            {fetchState.message}
            {fetchState.statusCode !== null && ` (HTTP ${fetchState.statusCode})`}
          </p>
          <button type="button" className="btn btn-secondary" onClick={() => load(routeQuoteId)}>
            Retry
          </button>
        </div>
      </div>
    );
  }

  const { recalculation } = fetchState;
  const quoteMeta = quoteMetaById[routeQuoteId] ?? null;
  const blocked = isAssessmentBlocked(recalculation.unmatchedLines);
  const extractRows = buildExtractRows(recalculation.assessment.lines, recalculation.unmatchedLines, knownLineDetails);
  const lineRows = buildQuoteLineRows(extractRows, recalculation.assessment.lines);
  const aggregate = aggregateQuote(recalculation.assessment.lines);

  if (targetInitializedFor !== routeQuoteId && !blocked) {
    // Seed the two editable Target inputs from the real aggregate exactly once per quote -- never
    // again on a later recalculation refresh, so mid-edit keystrokes are never clobbered. Deferred
    // until `!blocked`: before every line resolves the aggregate is the most incomplete it will be.
    setTargetInitializedFor(routeQuoteId);
    setTargetPrice(defaultTargetPrice(aggregate));
    setWalkAway(defaultWalkAway(aggregate));
  }

  const handleChangeMapDraft = (quoteLineId: string, field: keyof MapDraft, value: string) => {
    setMapDrafts((previous) => ({
      ...previous,
      [quoteLineId]: { ...(previous[quoteLineId] ?? { canonicalSku: "", canonicalProductName: "" }), [field]: value },
    }));
  };

  const handleApplyMappings = () => {
    const mappings: SkuMappingCorrectionInput[] = recalculation.unmatchedLines
      .map((line) => ({ line, draft: mapDrafts[line.quoteLineId] }))
      .filter(({ draft }) => (draft?.canonicalSku ?? "").trim() !== "")
      .map(({ line, draft }) => ({
        sku: line.sku,
        edition: line.edition,
        canonicalSku: draft!.canonicalSku.trim(),
        canonicalProductName: draft!.canonicalProductName.trim() === "" ? null : draft!.canonicalProductName.trim(),
      }));

    if (mappings.length === 0) return;

    setApplying(true);
    setApplyError(null);
    void apiClient.recalculateQuoteAssessment(workspace.id, routeQuoteId, mappings).then((result) => {
      setApplying(false);
      if (!result.ok || !result.recalculation) {
        setApplyError(result.error ?? "The mapping could not be applied.");
        return;
      }
      setMapDrafts({});
      setKnownLineDetails((previous) => mergeKnownLineDetails(previous, result.recalculation!.unmatchedLines));
      setFetchState({ phase: "ready", quoteId: routeQuoteId, recalculation: result.recalculation });
    });
  };

  const handleSubmitOutcome = (input: NegotiationOutcomeInput) => {
    setOutcomeSubmitting(true);
    setOutcomeError(null);
    void apiClient
      .captureNegotiationOutcome(workspace.id, {
        quoteId: routeQuoteId,
        originalQuoteTotal: input.originalQuoteTotal,
        targetPrice: input.targetPrice,
        finalPrice: input.finalPrice,
        negotiationDurationDays: input.negotiationDurationDays,
        leversUsed: input.leversUsed as NegotiationLeverTypeName[],
      })
      .then((result) => {
        setOutcomeSubmitting(false);
        if (!result.ok || !result.outcome) {
          setOutcomeError(result.error ?? "The outcome could not be recorded.");
          return;
        }
        setOutcome(result.outcome);
      });
  };

  return (
    <div className="quote-screen">
      {header(formatQuoteMeta(quoteMeta, routeQuoteId))}

      <section className="quote-results" aria-label="Quote assessment">
        <AssessmentResult aggregate={aggregate} lines={recalculation.assessment.lines} lineRows={lineRows} />

        {blocked ? (
          <MappingBlock
            unmatchedLines={recalculation.unmatchedLines}
            mapDrafts={mapDrafts}
            onChangeMapDraft={handleChangeMapDraft}
            onApplyMappings={handleApplyMappings}
            applying={applying}
            applyError={applyError}
          />
        ) : (
          <div className="quote-levers-footer">
            <p className="quote-levers-footer-copy">{QUOTE_LEVERS_FOOTER}</p>
            <button
              type="button"
              className="btn btn-ghost quote-levers-toggle"
              aria-expanded={leversStage !== "hidden"}
              onClick={() => setLeversStage((stage) => (stage === "hidden" ? "target" : "hidden"))}
            >
              {leversStage === "hidden" ? "Show target and levers →" : "Hide target and levers"}
            </button>
          </div>
        )}
      </section>

      {!blocked && leversStage !== "hidden" && (
        <section className="quote-levers" aria-label="Target and negotiation levers">
          <TargetStep
            aggregate={aggregate}
            targetPrice={targetPrice}
            onChangeTargetPrice={setTargetPrice}
            walkAway={walkAway}
            onChangeWalkAway={setWalkAway}
            onContinue={() => setLeversStage("negotiation")}
          />
          {leversStage === "negotiation" && quoteOutcomeState === "loading" && (
            <div className="quote-outcome-panel" role="status" aria-live="polite">
              <div className="skeleton" />
              <div className="skeleton" />
              <div className="skeleton" />
            </div>
          )}
          {leversStage === "negotiation" && quoteOutcomeState === "error" && (
            <div className="error-state" role="alert">
              <h4>The quote</h4>
              <p className="micro-meta">{quoteOutcomeError}</p>
              <button type="button" className="btn btn-secondary" onClick={() => routeQuoteId && loadQuote(routeQuoteId)}>
                Retry
              </button>
            </div>
          )}
          {leversStage === "negotiation" && quoteOutcomeState !== "loading" && quoteOutcomeState !== "error" && (
            <NegotiationStep
              aggregate={aggregate}
              targetPrice={targetPrice}
              outcome={outcome}
              onSubmit={handleSubmitOutcome}
              submitting={outcomeSubmitting}
              submitError={outcomeError}
            />
          )}
        </section>
      )}
    </div>
  );
}
