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
import QuoteStepper from "./QuoteStepper";
import UploadQuoteForm from "./UploadQuoteForm";
import ExtractStep, { type MapDraft } from "./ExtractStep";
import AssessmentStep from "./AssessmentStep";
import TargetStep, { defaultTargetPrice, defaultWalkAway } from "./TargetStep";
import NegotiationStep, { type NegotiationOutcomeInput } from "./NegotiationStep";
import {
  aggregateQuote,
  buildAssessmentNumbers,
  buildExtractRows,
  buildLineMarketPositionRows,
  isAssessmentBlocked,
  mergeKnownLineDetails,
  type LineDetail,
  type QuoteStepIndex,
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

/**
 * Route `/quotes/:quoteId` (ADR-018; screens.md #10 "Quote check"; ADR-020 screen 10; task
 * E08/F03/US01/T01, us-01-quote-check AC-1/AC-2/AC-3/AC-4). Wired into
 * `../../components/shell/WorkspaceShellApp.tsx`'s `quotes`/`quotes/:quoteId` routes in place of
 * that shell task's own `ScaffoldScreen` placeholder, the same seam `../contracts/contract360/index.tsx`
 * already used.
 *
 * **Real backend, not the cited prototype's own fixture.** `inputs/design/prototypes/day1-demo.html`'s
 * Quote check screen is one hard-coded demo scenario. By the time this task started, backend epic
 * E05 (`Contigo.Quotes` module) had already implemented and wired `POST /api/quotes`,
 * `GET /api/quotes/{id}/assessment`, `POST /api/quotes/{id}/assessment/recalculate`, and
 * `POST /api/negotiations/outcomes` into `Program.cs` (the parent story's own "E05 quote API
 * (assumed)" dependency turned out to already be real) -- this route calls them for real. See
 * `../../api/client.ts`'s own doc comments on each method, and `./quoteCheckViewModel.ts`'s own
 * header comment, for the exact provenance of every number this screen shows and the two real,
 * named backend gaps it works around (no `GET /api/quotes/{id}` to re-read upload metadata after
 * this session ends; no HTTP endpoint for `NegotiationStrategyService`'s lever
 * recommendations/evidence).
 *
 * **No dedicated "new quote" screen exists in the design** (screens.md #10 starts directly at
 * "Extracted line items"; ADR-018's route map names only the detail route `/quotes/:id`) -- this
 * component renders `UploadQuoteForm` itself whenever the route has no `quoteId` yet (both the rail
 * nav's own `/quotes` landing path and a direct `/quotes/:quoteId` visit before any upload has
 * happened), then navigates to the real id `POST /api/quotes` returns.
 *
 * **`recalculateQuoteAssessment`, not `getQuoteAssessment`, is this screen's own read call** -- see
 * `../../api/client.ts`'s header comment on `recalculateQuoteAssessment` for why (it is a strict
 * superset: the same `assessment` shape `getQuoteAssessment` returns, plus `unmatchedLines`, and
 * calling it with an empty `mappings` array is the endpoint's own documented "pure refresh").
 */
export default function QuoteCheckRoute({ apiClient }: QuoteCheckRouteProps) {
  const { quoteId: routeQuoteId } = useParams<{ quoteId?: string }>();
  const navigate = useNavigate();
  const workspace = loadCurrentWorkspace();

  const [fetchState, setFetchState] = useState<FetchState | null>(null);
  const [step, setStep] = useState<QuoteStepIndex>(0);
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
                ? "Contigo's quote service is temporarily unavailable. Try again in a moment."
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
    setStep(0);
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

  if (!routeQuoteId) {
    return <UploadQuoteForm onUpload={handleUpload} submitting={uploading} />;
  }

  if (fetchState === null || fetchState.phase === "loading") {
    return (
      <div className="quote-skeleton" role="status" aria-live="polite">
        <p className="micro-meta">Loading quote…</p>
        {Array.from({ length: 4 }, (_, index) => (
          <div key={index} className="skeleton" style={{ height: 32, marginBottom: 8 }} />
        ))}
      </div>
    );
  }

  if (fetchState.phase === "not-found") {
    return (
      <div className="empty-state" role="status">
        <h3>Quote not found</h3>
        <p className="micro-meta">This quote does not exist, or is not in your workspace.</p>
      </div>
    );
  }

  if (fetchState.phase === "error") {
    return (
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
    );
  }

  const { recalculation } = fetchState;
  const quoteMeta = quoteMetaById[routeQuoteId] ?? null;
  const blocked = isAssessmentBlocked(recalculation.unmatchedLines);
  const rows = buildExtractRows(recalculation.assessment.lines, recalculation.unmatchedLines, knownLineDetails);
  const aggregate = aggregateQuote(recalculation.assessment.lines);
  const currency = quoteMeta?.currency ?? aggregate.currency;

  if (targetInitializedFor !== routeQuoteId && !blocked) {
    // Seed the two editable Target-step inputs from the real aggregate exactly once per quote --
    // never again on a later recalculation refresh (e.g. after applying a mapping), so mid-edit
    // keystrokes are never clobbered by a background reload. Deferred until `!blocked`: before every
    // line resolves, `aggregate.recommendedTargetHigh`/`originalTotal` are the most incomplete they
    // will ever be (see `../contracts/review/EvidencePane.tsx`'s own precedent for re-seeding a form
    // from freshly-loaded data rather than doing it inline during render).
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
      <header>
        <p className="screen-kicker">R4 · New Purchase / Quote Check</p>
        <h2 className="screen-title">{quoteMeta?.fileName ?? `Quote ${routeQuoteId}`}</h2>
        {quoteMeta && (
          <p className="micro-meta">
            {[quoteMeta.supplier, quoteMeta.currency, quoteMeta.geography].filter(Boolean).join(" · ") || "No supplier/currency/geography recorded"}
          </p>
        )}
      </header>

      <QuoteStepper activeStep={step} onSelectStep={setStep} />

      {step === 0 && (
        <ExtractStep
          rows={rows}
          unmatchedLines={recalculation.unmatchedLines}
          currency={currency}
          mapDrafts={mapDrafts}
          onChangeMapDraft={handleChangeMapDraft}
          onApplyMappings={handleApplyMappings}
          applying={applying}
          applyError={applyError}
          onContinue={() => setStep(1)}
        />
      )}
      {step === 1 && (
        <AssessmentStep
          blocked={blocked}
          onBackToExtract={() => setStep(0)}
          numbers={buildAssessmentNumbers(aggregate, recalculation.assessment.lines)}
          positionRows={buildLineMarketPositionRows(rows, recalculation.assessment.lines)}
          aggregate={aggregate}
          onContinue={() => setStep(2)}
        />
      )}
      {step === 2 && (
        <TargetStep
          aggregate={aggregate}
          targetPrice={targetPrice}
          onChangeTargetPrice={setTargetPrice}
          walkAway={walkAway}
          onChangeWalkAway={setWalkAway}
          onContinue={() => setStep(3)}
        />
      )}
      {step === 3 && (
        <NegotiationStep
          aggregate={aggregate}
          targetPrice={targetPrice}
          outcome={outcome}
          onSubmit={handleSubmitOutcome}
          submitting={outcomeSubmitting}
          submitError={outcomeError}
        />
      )}
    </div>
  );
}
