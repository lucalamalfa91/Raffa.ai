import { useCallback, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import type {
  ApiClient,
  NegotiationLeverTypeName,
  NegotiationOutcomeBody,
  QuoteRecalculationBody,
  SkuMappingCorrectionInput,
  UploadedQuote,
  UploadQuoteFields,
} from "../../api/client";
import { loadCurrentWorkspace } from "../signin/workspaceStore";
import UploadQuoteForm from "./UploadQuoteForm";
import QuoteLinesTable from "./QuoteLinesTable";
import MappingBlock, { type MapDraft } from "./MappingBlock";
import TargetStep, { defaultTargetPrice, defaultWalkAway } from "./TargetStep";
import NegotiationStep, { type NegotiationOutcomeInput } from "./NegotiationStep";
import {
  QUOTE_INTRO,
  QUOTE_LEVERS_FOOTER,
  aggregateQuote,
  buildAssessmentBand,
  buildExtractRows,
  buildQuoteLineRows,
  formatQuoteMeta,
  isAssessmentBlocked,
  mergeKnownLineDetails,
  type LineDetail,
} from "./quoteCheckViewModel";
import { rememberNegotiationOutcome } from "./quoteOutcomeStore";
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

/**
 * Route `/quotes` and `/quotes/:quoteId` -- Quote check, V2 (ADR-024 V2 IA; screens-v2.md #9;
 * `raffa-v2/markup.html` "QUOTE CHECK (optional)" block). The header is constant ("Optional · new
 * purchase" · "Quote check" · the intro sentence); below it either the landing (`UploadQuoteForm`)
 * or, once a quote is loaded, the three-cell band (Supplier quote · Market range · Assessment), the
 * lines table (Line · Quoted · P50 · Position · Benchmark) and the footer "Target and negotiation
 * levers are one step further — shown only if you want them." that reveals the Target step, then
 * Negotiation. The Day-1 four-step stepper is gone; the same real calls remain.
 *
 * **Real backend, not the prototype's fixture.** `POST /api/quotes`, `POST /api/quotes/{id}/
 * assessment/recalculate` (a strict superset of `GET …/assessment`: same `assessment`, plus
 * `unmatchedLines`; an empty `mappings` array is its documented "pure refresh") and
 * `POST /api/negotiations/outcomes` -- see `../../api/client.ts`. Two named gaps: no
 * `GET /api/quotes/{id}` to re-read upload metadata after this session (the header meta line falls
 * back to the quote id), and no HTTP endpoint for `NegotiationStrategyService`'s lever
 * recommendations (`NegotiationStep.tsx`).
 *
 * **Blocked assessment.** While any line is still `SkuMatchStatus.Unmatched`, the band shows what
 * it honestly can, the unmapped lines say "Needs mapping", and the mapping block takes the footer's
 * place -- target and levers only open once every line is resolved.
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
  const [outcome, setOutcome] = useState<NegotiationOutcomeBody | null>(null);
  const [outcomeSubmitting, setOutcomeSubmitting] = useState(false);
  const [outcomeError, setOutcomeError] = useState<string | null>(null);

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
                ? "Raffa's quote service is temporarily unavailable. Try again in a moment."
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

  useEffect(() => {
    setLeversStage("hidden");
    setMapDrafts({});
    setApplyError(null);
    setOutcome(null);
    setOutcomeError(null);
    if (routeQuoteId) {
      load(routeQuoteId);
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
  const band = buildAssessmentBand(aggregate, recalculation.assessment.lines);

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
        rememberNegotiationOutcome(result.outcome);
      });
  };

  return (
    <div className="quote-screen">
      {header(formatQuoteMeta(quoteMeta, routeQuoteId))}

      <section className="quote-results" aria-label="Quote assessment">
        <div className="quote-band">
          {band.map((cell) => (
            <div key={cell.key} className="quote-band-cell">
              <span className="quote-band-label">{cell.label}</span>
              <span className={`quote-band-value${cell.emphasize ? " quote-emphasize" : ""}`}>{cell.value}</span>
            </div>
          ))}
        </div>

        <QuoteLinesTable rows={lineRows} />

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
          {leversStage === "negotiation" && (
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
