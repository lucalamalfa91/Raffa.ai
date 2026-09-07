import { formatMoney, formatMoneyInputValue, type QuoteAggregate } from "./quoteCheckViewModel";

export interface TargetStepProps {
  aggregate: QuoteAggregate;
  targetPrice: string;
  onChangeTargetPrice: (value: string) => void;
  walkAway: string;
  onChangeWalkAway: (value: string) => void;
  onContinue: () => void;
}

/**
 * Target step (screens.md #10 AC-3: "price ladder (range, target, quote), opening/acceptable/
 * walk-away table, editable target"). Two honest departures from the cited prototype, both named
 * here rather than silently copied: no backend endpoint computes a distinct "opening target" or
 * "walk-away/escalation" figure (`Contigo.Quotes.Application.Assessment.LineTargetSaving` gives
 * exactly one recommended range, `RecommendedTargetLow/High`) -- "Your target"/"Walk-away" are
 * therefore real, user-editable inputs (pre-filled from the real recommended-range/quote-total
 * aggregate as a starting point), not a third computed tier; and the ladder is drawn proportionally
 * from whatever real figures `aggregateQuote` produced, not the prototype's own fixed pixel
 * positions (those were specific to its one hard-coded demo quote).
 */
export default function TargetStep({ aggregate, targetPrice, onChangeTargetPrice, walkAway, onChangeWalkAway, onContinue }: TargetStepProps) {
  const ladder = buildLadder(aggregate);

  return (
    <div className="quote-target-step">
      <h6>Price ladder — total contract</h6>
      {ladder === null ? (
        <p className="micro-meta">Not enough benchmark data yet to draw a price ladder for this quote.</p>
      ) : (
        <div className="quote-ladder">
          <div className="quote-ladder-track">
            <div className="quote-ladder-market" style={{ left: `${ladder.marketLowPct}%`, width: `${ladder.marketWidthPct}%` }} />
            <div className="quote-ladder-target" style={{ left: `${ladder.targetLowPct}%`, width: `${ladder.targetWidthPct}%` }} />
            {ladder.quotePct !== null && <div className="quote-ladder-quote-tick" style={{ left: `${ladder.quotePct}%` }} />}
          </div>
          <div className="quote-ladder-labels micro-meta">
            <span>{formatMoney(aggregate.expectedMarketLow, aggregate.currency)}</span>
            <span>{formatMoney(aggregate.expectedMarketHigh, aggregate.currency)}</span>
          </div>
        </div>
      )}

      <table className="table quote-target-table">
        <tbody>
          <tr>
            <td className="micro-meta">Recommended target</td>
            <td className="quote-target-table-value">
              {formatMoney(aggregate.recommendedTargetLow, aggregate.currency)}–{formatMoney(aggregate.recommendedTargetHigh, aggregate.currency)}
            </td>
          </tr>
          <tr>
            <td className="micro-meta">Potential saving</td>
            <td className="quote-target-table-value">
              {formatMoney(aggregate.totalSavingsLow, aggregate.currency)}–{formatMoney(aggregate.totalSavingsHigh, aggregate.currency)}
            </td>
          </tr>
        </tbody>
      </table>

      <h6>Adjust target</h6>
      <div className="field">
        <label htmlFor="quote-target-price">Your target ({aggregate.currency ?? "amount"})</label>
        <input id="quote-target-price" className="input" type="number" step="1" value={targetPrice} onChange={(event) => onChangeTargetPrice(event.target.value)} />
      </div>
      <div className="field">
        <label htmlFor="quote-walk-away">Walk-away ({aggregate.currency ?? "amount"})</label>
        <input id="quote-walk-away" className="input" type="number" step="1" value={walkAway} onChange={(event) => onChangeWalkAway(event.target.value)} />
      </div>
      <p className="micro-meta">
        Pre-filled from the recommended target range and the supplier quote total; adjust either figure freely — overrides
        are yours to make.
      </p>

      <button type="button" className="btn btn-primary" onClick={onContinue}>
        Build negotiation strategy →
      </button>
    </div>
  );
}

/** Falls back to `formatMoneyInputValue`'s own "" for an unknown figure -- exported only for this
 * step's own initial-value computation in `index.tsx`. */
export function defaultTargetPrice(aggregate: QuoteAggregate): string {
  return formatMoneyInputValue(aggregate.recommendedTargetHigh ?? aggregate.originalTotal);
}

export function defaultWalkAway(aggregate: QuoteAggregate): string {
  return formatMoneyInputValue(aggregate.originalTotal);
}

interface LadderGeometry {
  marketLowPct: number;
  marketWidthPct: number;
  targetLowPct: number;
  targetWidthPct: number;
  quotePct: number | null;
}

function buildLadder(aggregate: QuoteAggregate): LadderGeometry | null {
  const { expectedMarketLow, expectedMarketHigh, recommendedTargetLow, recommendedTargetHigh, originalTotal } = aggregate;
  if (expectedMarketLow === null || expectedMarketHigh === null || expectedMarketHigh <= expectedMarketLow) return null;

  const scaleLow = Math.min(expectedMarketLow, recommendedTargetLow ?? expectedMarketLow, originalTotal ?? expectedMarketLow) * 0.95;
  const scaleHigh = Math.max(expectedMarketHigh, recommendedTargetHigh ?? expectedMarketHigh, originalTotal ?? expectedMarketHigh) * 1.05;
  const span = scaleHigh - scaleLow;
  if (span <= 0) return null;

  const pct = (value: number) => ((value - scaleLow) / span) * 100;

  const targetLow = recommendedTargetLow ?? expectedMarketLow;
  const targetHigh = recommendedTargetHigh ?? expectedMarketHigh;

  return {
    marketLowPct: pct(expectedMarketLow),
    marketWidthPct: pct(expectedMarketHigh) - pct(expectedMarketLow),
    targetLowPct: pct(targetLow),
    targetWidthPct: Math.max(pct(targetHigh) - pct(targetLow), 0.5),
    quotePct: originalTotal !== null ? pct(originalTotal) : null,
  };
}
