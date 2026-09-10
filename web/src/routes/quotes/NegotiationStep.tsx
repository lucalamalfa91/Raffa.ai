import { useState } from "react";
import { Link } from "react-router-dom";
import type { NegotiationLeverTypeName, NegotiationOutcomeBody } from "../../api/client";
import {
  NEGOTIATION_LEVER_LABELS,
  formatMoney,
  formatPercent,
  previewOutcome,
  type QuoteAggregate,
} from "./quoteCheckViewModel";

export interface NegotiationOutcomeInput {
  originalQuoteTotal: number;
  targetPrice: number | null;
  finalPrice: number;
  negotiationDurationDays: number;
  leversUsed: NegotiationLeverTypeName[];
}

export interface NegotiationStepProps {
  aggregate: QuoteAggregate;
  targetPrice: string;
  outcome: NegotiationOutcomeBody | null;
  onSubmit: (input: NegotiationOutcomeInput) => void;
  submitting: boolean;
  submitError: string | null;
}

/**
 * Negotiation step (screens.md #10 AC-4: "levers with evidence and impact; outcome form -> recorded
 * outcome ... -> Home Savings Realized updates"). The "levers with evidence" half is a named,
 * honest gap, not a divergent invention: `Contigo.Quotes.Application.Strategy
 * .NegotiationStrategyService` (task E05/F03/US01/T01, negotiation-strategy) computes exactly that
 * -- opening target, acceptable range, walk-away threshold, levers, rationale -- fully unit-tested,
 * but `backend/src/Contigo.Api/Program.cs` never maps an HTTP endpoint for it (checked: no
 * `MapGet`/`MapPost` anywhere in `backend/src/Contigo.Api` references `NegotiationStrategyService`/
 * `NegotiationStrategyCalculator`/`QuoteNegotiationStrategy`). A backend task would need to add one
 * (e.g. `GET /api/quotes/{id}/strategy`) before this screen can show AI-recommended levers/evidence
 * for real; see `../../api/client.ts`'s own `captureNegotiationOutcome` doc comment for the same
 * note. The outcome-capture control below is real: a required multi-select of the same 7-member
 * `NegotiationLeverType` vocabulary `POST /api/negotiations/outcomes` itself validates
 * `leversUsed` against, so a person records which real levers they actually used, even without an
 * AI recommendation to start from.
 */
export default function NegotiationStep({ aggregate, targetPrice, outcome, onSubmit, submitting, submitError }: NegotiationStepProps) {
  const [originalQuoteTotal, setOriginalQuoteTotal] = useState(() =>
    aggregate.originalTotal === null ? "" : String(Math.round(aggregate.originalTotal)),
  );
  const [finalPrice, setFinalPrice] = useState("");
  const [durationDays, setDurationDays] = useState("0");
  const [leversUsed, setLeversUsed] = useState<ReadonlySet<NegotiationLeverTypeName>>(new Set());

  const parsedOriginal = originalQuoteTotal.trim() === "" ? null : Number(originalQuoteTotal);
  const parsedFinal = finalPrice.trim() === "" ? null : Number(finalPrice);
  const preview = previewOutcome(parsedOriginal, parsedFinal);

  const toggleLever = (lever: NegotiationLeverTypeName) => {
    setLeversUsed((previous) => {
      const next = new Set(previous);
      if (next.has(lever)) next.delete(lever);
      else next.add(lever);
      return next;
    });
  };

  const canSubmit =
    parsedOriginal !== null && parsedOriginal > 0 && parsedFinal !== null && parsedFinal > 0 && leversUsed.size > 0 && !submitting;

  const handleSubmit = () => {
    if (!canSubmit || parsedOriginal === null || parsedFinal === null) return;
    const parsedTarget = targetPrice.trim() === "" ? null : Number(targetPrice);
    onSubmit({
      originalQuoteTotal: parsedOriginal,
      targetPrice: parsedTarget !== null && Number.isFinite(parsedTarget) ? parsedTarget : null,
      finalPrice: parsedFinal,
      negotiationDurationDays: Number(durationDays) || 0,
      leversUsed: Array.from(leversUsed),
    });
  };

  return (
    <div className="quote-negotiation-step">
      <div className="quote-negotiation-levers">
        <h6>Levers</h6>
        <p className="micro-meta">
          Automated lever recommendations and evidence are not available yet — the negotiation-strategy service has no HTTP
          endpoint (a pre-existing backend gap; see this file's own header comment). Record which of the real, spec-named
          levers you actually used below.
        </p>
      </div>

      <div className="quote-outcome-panel">
        {outcome === null ? (
          <>
            <h6>Record the outcome</h6>
            <div className="field">
              <label htmlFor="quote-outcome-original">Original quote total</label>
              <input
                id="quote-outcome-original"
                className="input"
                type="number"
                step="1"
                value={originalQuoteTotal}
                onChange={(event) => setOriginalQuoteTotal(event.target.value)}
              />
            </div>
            <div className="field">
              <label htmlFor="quote-outcome-final">Final price</label>
              <input
                id="quote-outcome-final"
                className="input"
                type="number"
                step="1"
                value={finalPrice}
                onChange={(event) => setFinalPrice(event.target.value)}
              />
            </div>
            <div className="field">
              <label htmlFor="quote-outcome-duration">Duration (days)</label>
              <input
                id="quote-outcome-duration"
                className="input"
                type="number"
                step="1"
                min="0"
                value={durationDays}
                onChange={(event) => setDurationDays(event.target.value)}
              />
            </div>
            <fieldset className="quote-lever-checklist">
              <legend>Levers used (at least one)</legend>
              {NEGOTIATION_LEVER_LABELS.map(({ value, label }) => (
                <label key={value} className="quote-lever-option">
                  <input type="checkbox" checked={leversUsed.has(value)} onChange={() => toggleLever(value)} />
                  {label}
                </label>
              ))}
            </fieldset>

            <p className="micro-meta">
              Realized saving (preview): {formatMoney(preview.realizedSaving, aggregate.currency)} ({formatPercent(preview.discountPercent)})
            </p>

            {submitError !== null && (
              <p className="hint" role="alert">
                {submitError}
              </p>
            )}

            <button type="button" className="btn btn-primary btn-block" disabled={!canSubmit} onClick={handleSubmit}>
              {submitting ? "Recording…" : "Record outcome"}
            </button>
          </>
        ) : (
          <>
            <span className="card-kicker">Negotiation outcome</span>
            <table className="table" style={{ marginTop: "8px" }}>
              <tbody>
                <tr>
                  <td className="micro-meta">Original quote</td>
                  <td className="quote-target-table-value">{formatMoney(outcome.originalQuoteTotal, aggregate.currency)}</td>
                </tr>
                <tr>
                  <td className="micro-meta">Target</td>
                  <td className="quote-target-table-value">{formatMoney(outcome.targetPrice, aggregate.currency)}</td>
                </tr>
                <tr>
                  <td className="micro-meta">Final price</td>
                  <td className="quote-target-table-value">{formatMoney(outcome.finalPrice, aggregate.currency)}</td>
                </tr>
                <tr>
                  <td className="micro-meta">Realized saving</td>
                  <td className="quote-target-table-value quote-emphasize">
                    {formatMoney(outcome.realizedSaving, aggregate.currency)} · {formatPercent(outcome.discountPercent)}
                  </td>
                </tr>
                <tr>
                  <td className="micro-meta">Duration</td>
                  <td>{outcome.negotiationDurationDays} days</td>
                </tr>
                <tr>
                  <td className="micro-meta">Levers used</td>
                  <td>{outcome.leversUsed.join(", ")}</td>
                </tr>
              </tbody>
            </table>
            <p className="micro-meta" style={{ marginTop: "10px" }}>
              Saved to this browser's Savings Realized record for this session (see this screen's own outcome-store gap
              note — no backend list endpoint exists yet).
            </p>
            <Link to="/savings" className="btn btn-secondary">
              See it in Savings →
            </Link>
          </>
        )}
      </div>
    </div>
  );
}
