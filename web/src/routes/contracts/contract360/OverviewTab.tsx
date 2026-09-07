import { Link } from "react-router-dom";
import type { Contract360RiskBody } from "../../../api/client";
import FactTable from "./FactTable";
import type { AttentionTerm, FactRow, Recommendation } from "./contract360ViewModel";

export interface OverviewTabProps {
  contractId: string;
  recommendation: Recommendation;
  attention: readonly AttentionTerm[];
  topRisks: readonly Contract360RiskBody[];
  detailRows: readonly FactRow[];
  /** "Why this score" (day1-demo.html: `tabRenewal:()=>this.setState({tab:'Renewal'})`) -- switches this same screen's own tab, not a navigation. */
  onOpenRenewalTab: () => void;
  /** "All risks →" (day1-demo.html: `tabRisks`) -- switches this same screen's own tab. */
  onOpenRisksTab: () => void;
}

/**
 * Contract 360's Overview tab (ADR-020 screen 5, screens.md #5 AC-3): recommended-action block + 3
 * driver numbers + "Needs your attention" + "Top risks". The ONLY block on this whole screen that
 * renders AI-derived text (`.ai-recommendation`, ADR-019 component catalogue) -- every other
 * element here (drivers' Cancellation-deadline number, the attention list's confidence tags, the
 * risk list, and the "Contract details" `FactTable` below) renders a deterministic fact, visually
 * separated by never sharing that card (ADR-019 "Facts vs AI separation", council decision "AI
 * recommendation lives in its own labelled block, never mixed with deterministic facts").
 */
export default function OverviewTab({
  contractId,
  recommendation,
  attention,
  topRisks,
  detailRows,
  onOpenRenewalTab,
  onOpenRisksTab,
}: OverviewTabProps) {
  return (
    <div className="contract360-overview">
      <div className="card ai-recommendation">
        <p className="ai-recommendation-label">Recommended action</p>
        <p className="contract360-recommendation-statement">{recommendation.statement}</p>
        <p className="micro-meta">{recommendation.rationale}</p>

        <div className="contract360-drivers">
          {recommendation.drivers.map((driver) => (
            <div key={driver.key} className="contract360-driver">
              <span className="micro-meta">{driver.label}</span>
              <span className="key-fact-number">{driver.value}</span>
            </div>
          ))}
        </div>

        <div className="contract360-recommendation-actions">
          <Link to="/renewals" className="btn btn-primary">
            Open in renewals
          </Link>
          <button type="button" className="btn btn-secondary" onClick={onOpenRenewalTab}>
            Why this score
          </button>
        </div>
      </div>

      <div className="contract360-overview-columns">
        <section className="contract360-overview-column">
          <div className="contract360-overview-column-header">
            <h6>Needs your attention</h6>
            <Link to={`/contracts/${contractId}/review`} className="btn btn-ghost contract360-column-link">
              Review all →
            </Link>
          </div>
          {attention.length === 0 ? (
            <p className="micro-meta">Nothing needs attention — every extracted field is at or above the 95% confidence threshold.</p>
          ) : (
            <ul className="contract360-term-list">
              {attention.map((term) => (
                <li key={term.key}>
                  <span>{term.term}</span>
                  <span className={`tag tag-${term.tag.variant}`}>{term.tag.label}</span>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="contract360-overview-column">
          <div className="contract360-overview-column-header">
            <h6>Top risks</h6>
            <button type="button" className="btn btn-ghost contract360-column-link" onClick={onOpenRisksTab}>
              All risks →
            </button>
          </div>
          {topRisks.length === 0 ? (
            <p className="micro-meta">No risks recorded for this contract.</p>
          ) : (
            <ul className="contract360-term-list">
              {topRisks.map((risk) => (
                <li key={risk.riskId}>
                  <span>{risk.description}</span>
                  <span className="tag tag-accent">{risk.severity} risk</span>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      <FactTable title="Contract details" rows={detailRows} emptyMessage="No additional contract details recorded." />
    </div>
  );
}
