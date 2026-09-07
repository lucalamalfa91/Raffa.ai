import type { Contract360RenewalBody, RenewalPriorityBody } from "../../../api/client";
import FactTable from "./FactTable";
import { buildPriorityComponentRows, buildRenewalFactRows } from "./contract360ViewModel";

export interface RenewalTabProps {
  renewal: Contract360RenewalBody;
  /** `null` while the priority fetch has not resolved yet, or failed -- rendered as its own honest gap below, never a fabricated all-zero table. */
  priority: RenewalPriorityBody | null;
}

/**
 * Contract 360's Renewal tab (ADR-020 screen 5: "Renewal adds priority-score component table" on
 * top of the generic Term/Value/Source/Confidence template every other tab uses). Two blocks, both
 * deterministic facts (never the Overview tab's AI recommendation, ADR-019 facts/AI separation):
 * the plain renewal-facts `FactTable`, and the explainable priority-score breakdown
 * (`PriorityScoreCalculator`'s own five named components, each with its own real, computed
 * `explanation` string -- never a bare number).
 */
export default function RenewalTab({ renewal, priority }: RenewalTabProps) {
  const factRows = buildRenewalFactRows(renewal);
  const componentRows = buildPriorityComponentRows(priority);

  return (
    <div className="contract360-renewal-tab">
      <FactTable title="Renewal" rows={factRows} emptyMessage="No renewal facts recorded." />

      <section className="contract360-tab-panel">
        <div className="contract360-tab-panel-header">
          <h6>Priority score</h6>
          {priority !== null && <span className="micro-meta">{Math.round(priority.totalScore)}/100</span>}
        </div>
        {componentRows.length === 0 ? (
          <p className="micro-meta">Priority score is not available yet.</p>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Component</th>
                <th>Score</th>
                <th>Explanation</th>
              </tr>
            </thead>
            <tbody>
              {componentRows.map((row) => (
                <tr key={row.key}>
                  <td>{row.label}</td>
                  <td>{Math.round(row.score)}</td>
                  <td className="micro-meta">{row.explanation}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}
