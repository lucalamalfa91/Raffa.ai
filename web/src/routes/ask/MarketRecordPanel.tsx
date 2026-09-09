import { useEffect, useState } from "react";
import type { ApiClient, MarketRecordBody } from "../../api/client";

export interface MarketRecordPanelProps {
  apiClient: ApiClient;
  recordId: string;
  onClose: () => void;
}

type FetchState =
  | { phase: "loading" }
  | { phase: "error"; message: string }
  | { phase: "ready"; record: MarketRecordBody };

/**
 * The market citation side panel (task text point (3): "a market citation opens a side panel
 * loading `GET /api/market/records/{id}` (title, category, geography, band, provenance label,
 * updatedAt)"; R-EVD-02: "a market citation opens a side panel with the record"). Reuses the
 * shared `.detail-pane` composite (ADR-019 / `styles/components.css`: "340-400px, surface, 2px left
 * rule") -- the same primitive `../contracts/review/EvidencePane.tsx` already uses for an identical
 * "supporting detail beside the main content" shape, not a screen-specific panel invented here.
 *
 * `recordId` is a prop, not read from `AskTurnView.wireCitations` itself -- `index.tsx#openCitation`
 * already resolved it once (`askViewModel.ts#resolveCitationOpenAction`) before ever mounting this
 * component, so this component's own job is only "fetch and render one record", not "figure out
 * which one".
 */
export default function MarketRecordPanel({ apiClient, recordId, onClose }: MarketRecordPanelProps) {
  const [state, setState] = useState<FetchState>({ phase: "loading" });

  useEffect(() => {
    let cancelled = false;
    setState({ phase: "loading" });

    void apiClient.getMarketRecord(recordId).then((result) => {
      if (cancelled) return;
      if (!result.ok || !result.record) {
        setState({
          phase: "error",
          message:
            result.statusCode === 404
              ? "This market record is no longer available."
              : (result.error ?? "This market record could not be loaded right now. Try again in a moment."),
        });
        return;
      }
      setState({ phase: "ready", record: result.record });
    });

    return () => {
      cancelled = true;
    };
  }, [apiClient, recordId]);

  return (
    <aside className="detail-pane market-record-panel" aria-label="Market record">
      <div className="market-record-panel-header">
        <span className="tag tag-outline">Market · representative</span>
        <button type="button" className="btn-ghost btn market-record-panel-close" onClick={onClose} aria-label="Close">
          Close
        </button>
      </div>

      {state.phase === "loading" && (
        <div role="status" aria-live="polite">
          <div className="skeleton market-record-panel-skeleton" />
          <div className="skeleton market-record-panel-skeleton" />
        </div>
      )}

      {state.phase === "error" && (
        <div className="error-state" role="alert">
          <p className="micro-meta">{state.message}</p>
        </div>
      )}

      {state.phase === "ready" && (
        <div>
          <h4 className="market-record-panel-title">{state.record.title}</h4>
          <p className="micro-meta market-record-panel-meta">
            {state.record.category} · {state.record.geography}
          </p>
          <table className="table market-record-panel-band">
            <thead>
              <tr>
                <th>P25</th>
                <th>P50</th>
                <th>P75</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>
                  {state.record.band.currency} {state.record.band.p25}
                </td>
                <td>
                  {state.record.band.currency} {state.record.band.p50}
                </td>
                <td>
                  {state.record.band.currency} {state.record.band.p75}
                </td>
              </tr>
            </tbody>
          </table>
          <p className="hint">
            {state.record.provenance} · updated {state.record.updatedAt.slice(0, 10)}
          </p>
        </div>
      )}
    </aside>
  );
}
