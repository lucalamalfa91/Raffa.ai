import type { UnmatchedQuoteLineBody } from "../../api/client";
import { formatMoney, type ExtractLineRow } from "./quoteCheckViewModel";

export interface MapDraft {
  canonicalSku: string;
  canonicalProductName: string;
}

export interface ExtractStepProps {
  rows: readonly ExtractLineRow[];
  unmatchedLines: readonly UnmatchedQuoteLineBody[];
  currency: string | null;
  mapDrafts: Readonly<Record<string, MapDraft>>;
  onChangeMapDraft: (quoteLineId: string, field: keyof MapDraft, value: string) => void;
  onApplyMappings: () => void;
  applying: boolean;
  applyError: string | null;
  onContinue: () => void;
}

/**
 * Extract step (screens.md #10 AC-2: "line table with benchmark match; unmatched SKU block with
 * manual mapping select and recalculate"). The manual-mapping control is a free-text "canonical
 * SKU"/"product name" pair, not the cited prototype's fixed 3-option `<select>` (`std`/`prem`/
 * `skip`): `Contigo.Quotes.Application.Normalization.SkuMappingCorrection`'s real wire shape takes
 * an arbitrary caller-chosen canonical SKU (`SkuMappingService`'s own doc comment: "the confirmed
 * canonical SKU code"), and no endpoint anywhere exposes a catalog of known products to pick from --
 * a 3-option select would either be decorative or silently wrong. `mapDrafts` covers *every*
 * currently-unmatched line at once (one row per line), submitted together in one
 * `recalculateQuoteAssessment` call -- the real endpoint's own `mappings` array already accepts more
 * than one correction per request.
 */
export default function ExtractStep({
  rows,
  unmatchedLines,
  currency,
  mapDrafts,
  onChangeMapDraft,
  onApplyMappings,
  applying,
  applyError,
  onContinue,
}: ExtractStepProps) {
  const anyDraftFilled = unmatchedLines.some((line) => (mapDrafts[line.quoteLineId]?.canonicalSku ?? "").trim() !== "");

  return (
    <div className="quote-extract-step">
      <div className="quote-step-heading">
        <h4>Extracted line items</h4>
        <span className="micro-meta">
          {rows.length} line{rows.length === 1 ? "" : "s"}
        </span>
      </div>

      <table className="table">
        <thead>
          <tr>
            <th>Line</th>
            <th>SKU</th>
            <th style={{ textAlign: "right" }}>Qty</th>
            <th style={{ textAlign: "right" }}>Unit price</th>
            <th style={{ textAlign: "right" }}>Annual</th>
            <th>Benchmark match</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.quoteLineId} className={row.isUnmatched ? "row-critical" : undefined}>
              <td>
                {row.label}
                {row.edition !== null && <div className="micro-meta">{row.edition}</div>}
              </td>
              <td className="quote-sku-cell">{row.sku ?? "Not yet available"}</td>
              <td style={{ textAlign: "right" }}>{row.quantity === null ? "—" : new Intl.NumberFormat("en-GB").format(row.quantity)}</td>
              <td style={{ textAlign: "right" }}>{formatMoney(row.unitPrice, currency)}</td>
              <td style={{ textAlign: "right" }}>{formatMoney(row.annual, currency)}</td>
              <td>
                <span className={`tag tag-${row.matchTag.variant}`}>{row.matchTag.label}</span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {unmatchedLines.length > 0 ? (
        <div className="quote-unmatched-block">
          <div className="quote-unmatched-heading">
            {unmatchedLines.length} line{unmatchedLines.length === 1 ? "" : "s"} could not be matched to the benchmark model
          </div>
          <p className="micro-meta">
            Map each line to a canonical product manually so unit economics can be normalised — Contigo will
            not compute a savings target until every line is resolved.
          </p>

          {unmatchedLines.map((line) => {
            const draft = mapDrafts[line.quoteLineId] ?? { canonicalSku: "", canonicalProductName: "" };
            return (
              <div key={line.quoteLineId} className="quote-map-row">
                <div className="quote-map-row-source">
                  <strong>{line.description}</strong>
                  <span className="micro-meta">SKU: {line.sku}</span>
                </div>
                <div className="field">
                  <label htmlFor={`quote-map-sku-${line.quoteLineId}`}>Canonical SKU</label>
                  <input
                    id={`quote-map-sku-${line.quoteLineId}`}
                    className="input"
                    value={draft.canonicalSku}
                    onChange={(event) => onChangeMapDraft(line.quoteLineId, "canonicalSku", event.target.value)}
                  />
                </div>
                <div className="field">
                  <label htmlFor={`quote-map-name-${line.quoteLineId}`}>Product name (optional)</label>
                  <input
                    id={`quote-map-name-${line.quoteLineId}`}
                    className="input"
                    value={draft.canonicalProductName}
                    onChange={(event) => onChangeMapDraft(line.quoteLineId, "canonicalProductName", event.target.value)}
                  />
                </div>
              </div>
            );
          })}

          {applyError !== null && (
            <p className="hint" role="alert">
              {applyError}
            </p>
          )}

          <button type="button" className="btn btn-primary" disabled={!anyDraftFilled || applying} onClick={onApplyMappings}>
            {applying ? "Applying…" : "Apply mapping & recalculate"}
          </button>
        </div>
      ) : (
        <div className="quote-mapped-block">
          <span className="tag tag-neutral">All lines normalised</span>
          <button type="button" className="btn btn-primary" onClick={onContinue}>
            Continue to assessment →
          </button>
        </div>
      )}
    </div>
  );
}
