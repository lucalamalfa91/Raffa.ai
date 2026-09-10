import type { UnmatchedQuoteLineBody } from "../../api/client";

export interface MapDraft {
  canonicalSku: string;
  canonicalProductName: string;
}

export interface MappingBlockProps {
  unmatchedLines: readonly UnmatchedQuoteLineBody[];
  mapDrafts: Readonly<Record<string, MapDraft>>;
  onChangeMapDraft: (quoteLineId: string, field: keyof MapDraft, value: string) => void;
  onApplyMappings: () => void;
  applying: boolean;
  applyError: string | null;
}

/**
 * The unmatched-SKU block ("assessment blocked until resolved"): one free-text canonical SKU /
 * product name pair per line still `SkuMatchStatus.Unmatched`, submitted together in one
 * `recalculateQuoteAssessment` call. Free text, not a fixed select: `SkuMappingCorrection`'s real
 * wire shape takes an arbitrary caller-chosen canonical SKU and no endpoint exposes a catalogue of
 * known products to pick from -- a select would be decorative or silently wrong. Shown in place of
 * the levers footer while any line is unmapped (the V2 prototype has no such state because its data
 * is already resolved; the real pipeline does).
 */
export default function MappingBlock({ unmatchedLines, mapDrafts, onChangeMapDraft, onApplyMappings, applying, applyError }: MappingBlockProps) {
  const anyDraftFilled = unmatchedLines.some((line) => (mapDrafts[line.quoteLineId]?.canonicalSku ?? "").trim() !== "");

  return (
    <div className="quote-unmatched-block">
      <div className="quote-unmatched-heading">
        {unmatchedLines.length} line{unmatchedLines.length === 1 ? "" : "s"} could not be matched to the benchmark model
      </div>
      <p className="micro-meta">
        Map each line to a canonical product so unit economics can be normalised — Raffa.ai will not compute a target until
        every line is resolved.
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
  );
}
