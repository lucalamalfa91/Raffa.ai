import type { SavingsOpportunityBody } from "../../api/client";
import { MIDPOINT_NOTE, type PipelineView } from "./savingsDashboard";

export interface SavingsPipelineProps {
  pipeline: PipelineView | null;
  /** The status the table is filtered to right now, so the matching legend entry reads as pressed. */
  activeStatus: SavingsOpportunityBody["status"] | null;
  /** A legend entry filters the opportunities table to that stage (a second press clears it). */
  onSelectStatus: (status: SavingsOpportunityBody["status"] | null) => void;
}

/**
 * "From identified to verified": one horizontal part-to-whole bar -- identified, in progress,
 * verified -- sized by money in one currency, with a 2px surface gap between segments and the
 * three stages named in a legend under it (identity is never colour alone). The legend entries are
 * also the way into the table: pressing one filters the opportunities below to that stage.
 */
export default function SavingsPipeline({ pipeline, activeStatus, onSelectStatus }: SavingsPipelineProps) {
  return (
    <section className="savings-panel savings-pipeline" aria-labelledby="savings-pipeline-title">
      <div className="savings-panel-head">
        <h3 id="savings-pipeline-title" className="savings-panel-title">
          From identified to verified
        </h3>
        <p className="savings-panel-meta">
          {pipeline === null ? "No opportunity in this currency yet." : (pipeline.verifiedShareLine ?? "Nothing verified yet — every figure below is still an estimate.")}
        </p>
      </div>

      {pipeline !== null && (
        <>
          <div
            className="savings-pipeline-bar"
            role="img"
            aria-label={pipeline.segments.map((segment) => `${segment.label} ${segment.amountLabel}, ${segment.countLabel}`).join("; ")}
          >
            {pipeline.segments
              .filter((segment) => segment.value > 0)
              .map((segment) => (
                <span
                  key={segment.key}
                  className={`savings-pipeline-segment savings-stage-${segment.key}`}
                  style={{ flexGrow: segment.value }}
                  title={`${segment.label}: ${segment.amountLabel} · ${segment.countLabel}`}
                />
              ))}
          </div>

          <ul className="savings-pipeline-legend">
            {pipeline.segments.map((segment) => {
              const pressed = activeStatus === segment.status;
              return (
                <li key={segment.key}>
                  <button
                    type="button"
                    className="savings-pipeline-stage"
                    aria-pressed={pressed}
                    aria-label={`${pressed ? "Show all opportunities" : `Show ${segment.label.toLowerCase()} opportunities`} (${segment.amountLabel}, ${segment.countLabel})`}
                    onClick={() => onSelectStatus(pressed ? null : segment.status)}
                  >
                    <span className={`savings-swatch savings-stage-${segment.key}`} aria-hidden="true" />
                    <span className="savings-pipeline-stage-label">{segment.label}</span>
                    <span className="savings-pipeline-stage-value">{segment.amountLabel}</span>
                    <span className="savings-pipeline-stage-count">
                      {segment.countLabel}
                      {segment.value > 0 && ` · ${Math.round(segment.share * 100)}%`}
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>
          <p className="savings-footnote">{MIDPOINT_NOTE}</p>
        </>
      )}
    </section>
  );
}
