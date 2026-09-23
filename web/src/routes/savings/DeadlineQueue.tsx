import { Link } from "react-router-dom";
import AskRaffaLink from "../../components/ask-bar/AskRaffaLink";
import { ASK_PROMPTS } from "../../components/ask-bar/askLaunch";
import type { DeadlineQueueRowView } from "./savingsDashboard";

export interface DeadlineQueueProps {
  rows: readonly DeadlineQueueRowView[];
  /** `false` while the portfolio (the notice dates' source) has not loaded -- the list says so rather than claiming there is nothing. */
  portfolioLoaded: boolean;
  /** contractId -> the supplier's real name, for the Ask question (which never carries an id fragment). */
  supplierNames: ReadonlyMap<string, string>;
}

const QUEUE_LIMIT = 5;

/**
 * "Act before the notice deadline": open savings on contracts whose notice date is still ahead,
 * soonest first -- the money that is lost to an auto-renewal if nobody moves. Each row leads to
 * Renewals with that contract selected (where the notice is worked) and to Ask Raffa for the
 * approach, bound to the same contract.
 */
export default function DeadlineQueue({ rows, portfolioLoaded, supplierNames }: DeadlineQueueProps) {
  const shown = rows.slice(0, QUEUE_LIMIT);
  return (
    <section className="savings-panel savings-deadlines" aria-labelledby="savings-deadlines-title">
      <div className="savings-panel-head savings-panel-head-row">
        <h3 id="savings-deadlines-title" className="savings-panel-title">
          Act before the notice deadline
        </h3>
        <Link to="/renewals" className="btn btn-ghost savings-panel-link">
          Renewals →
        </Link>
      </div>

      {!portfolioLoaded ? (
        <p className="savings-panel-empty">Notice dates come from the portfolio, which is not available right now.</p>
      ) : shown.length === 0 ? (
        <p className="savings-panel-empty">No open saving has a notice deadline in the next 180 days.</p>
      ) : (
        <ol className="savings-deadline-list">
          {shown.map((row) => (
            <li key={row.key} className="savings-deadline-item">
              <span className={`savings-deadline-days${row.isUrgent ? " deadline-critical" : ""}`}>
                <span className="savings-deadline-days-number">{row.daysToNotice}</span>
                <span className="savings-deadline-days-unit">{row.daysToNotice === 1 ? "day" : "days"}</span>
              </span>
              <span className="savings-deadline-body">
                <span className="savings-deadline-who">
                  <Link to={`/contracts/${row.contractId}`} state={{ from: "savings" }}>
                    {row.supplierLabel}
                  </Link>{" "}
                  <span className="savings-deadline-lever">· {row.lever}</span>
                </span>
                <span className="savings-deadline-meta">
                  {row.estimate} · {row.statusLabel} · notice by {row.deadline}
                </span>
              </span>
              <span className="savings-deadline-actions">
                <Link to={`/renewals?select=${encodeURIComponent(row.contractId)}`} className="btn btn-secondary savings-action">
                  Work it in Renewals
                </Link>
                <AskRaffaLink
                  question={ASK_PROMPTS.renewalApproach(supplierNames.get(row.contractId) ?? null)}
                  scopeContractId={row.contractId}
                  className="btn btn-ghost savings-action"
                  ariaLabel={`Ask Raffa how to approach the ${row.supplierLabel} renewal`}
                >
                  Ask Raffa
                </AskRaffaLink>
              </span>
            </li>
          ))}
        </ol>
      )}
      {rows.length > shown.length && (
        <p className="savings-footnote">
          {rows.length - shown.length} more with a notice date in the next 180 days — see them all in{" "}
          <Link to="/renewals">Renewals</Link>.
        </p>
      )}
    </section>
  );
}
