import { useState } from "react";
import {
  buildMonthlyScale,
  findPeakVerifiedMonth,
  formatCompactMoney,
  formatMoney,
  type MonthBucketView,
  type VerifiedStatView,
} from "./savingsDashboard";

export interface SavingsTimelineProps {
  currency: string;
  buckets: readonly MonthBucketView[];
  stats: readonly VerifiedStatView[];
}

function percentOf(value: number, max: number): string {
  return max <= 0 ? "0%" : `${Math.min(100, (value / max) * 100)}%`;
}

function describeMonth(currency: string, bucket: MonthBucketView): string {
  const verified =
    bucket.verifiedCount === 0 ? "nothing verified" : `${formatMoney(currency, bucket.verified)} verified from ${bucket.verifiedCount} outcome${bucket.verifiedCount === 1 ? "" : "s"}`;
  const identified =
    bucket.identifiedCount === 0 ? "nothing new identified" : `${formatMoney(currency, bucket.identified)} identified in ${bucket.identifiedCount} opportunit${bucket.identifiedCount === 1 ? "y" : "ies"}`;
  return `${bucket.longLabel}: ${verified}; ${identified}`;
}

/**
 * "When you saved": the three "when" figures (verified this year, in the last 90 days, the last
 * verified saving), then twelve months of columns -- verified money (accent) beside the savings
 * Raffa.ai identified that month (grey) on one shared y-scale. Only the peak verified month is
 * direct-labelled; every month shows both values (and the running verified total) on hover or
 * keyboard focus, and the same figures are one click away as a table.
 */
export default function SavingsTimeline({ currency, buckets, stats }: SavingsTimelineProps) {
  const scale = buildMonthlyScale(buckets);
  const peakKey = findPeakVerifiedMonth(buckets);
  const empty = scale.max === 0;
  // The table view is built only once opened, so a closed "Show as table" adds no second table to the page.
  const [tableOpen, setTableOpen] = useState(false);

  return (
    <section className="savings-panel savings-timeline" aria-labelledby="savings-timeline-title">
      <div className="savings-panel-head">
        <h3 id="savings-timeline-title" className="savings-panel-title">
          When you saved
        </h3>
        <p className="savings-panel-meta">Verified money by the month its outcome was recorded · last 12 months · {currency}</p>
      </div>

      <dl className="savings-stat-row">
        {stats.map((stat) => (
          <div key={stat.key} className="savings-stat" data-stat={stat.key}>
            <dt className="savings-stat-label">{stat.label}</dt>
            <dd className="savings-stat-value">{stat.value}</dd>
            <dd className="savings-stat-meta">{stat.meta}</dd>
          </div>
        ))}
      </dl>

      <figure className="savings-chart-figure">
        <ul className="savings-legend" aria-label="Series">
          <li>
            <span className="savings-swatch savings-stage-identified" aria-hidden="true" />
            Identified that month
          </li>
          <li>
            <span className="savings-swatch savings-stage-verified" aria-hidden="true" />
            Verified that month
          </li>
        </ul>

        {empty ? (
          <p className="savings-chart-empty">Nothing identified or verified in {currency} in the last 12 months.</p>
        ) : (
          <div className="savings-chart" role="group" aria-label={`Savings by month, ${currency}`}>
            <div className="savings-chart-grid" aria-hidden="true">
              {scale.ticks.map((tick) => (
                <div key={tick} className="savings-chart-gridline" style={{ bottom: percentOf(tick, scale.max) }}>
                  <span className="savings-chart-tick">{formatCompactMoney(currency, tick)}</span>
                </div>
              ))}
            </div>
            <div className="savings-chart-columns">
              {buckets.map((bucket, index) => (
                <div
                  key={bucket.key}
                  className={`savings-chart-month${index >= buckets.length - 2 ? " tip-left" : ""}`}
                  tabIndex={0}
                  aria-label={describeMonth(currency, bucket)}
                >
                  <div className="savings-chart-bars" aria-hidden="true">
                    <span className="savings-chart-bar savings-stage-identified" style={{ height: percentOf(bucket.identified, scale.max) }} />
                    <span className="savings-chart-bar savings-stage-verified" style={{ height: percentOf(bucket.verified, scale.max) }}>
                      {bucket.key === peakKey && <span className="savings-chart-peak">{formatCompactMoney(currency, bucket.verified)}</span>}
                    </span>
                  </div>
                  <span className="savings-chart-x" aria-hidden="true">
                    {bucket.label}
                  </span>
                  <div className="savings-tip" aria-hidden="true">
                    <span className="savings-tip-title">{bucket.longLabel}</span>
                    <span className="savings-tip-row">
                      <span className="savings-tip-key savings-stage-verified" />
                      <strong>{formatCompactMoney(currency, bucket.verified)}</strong> verified
                      {bucket.verifiedCount > 0 && ` · ${bucket.verifiedCount} outcome${bucket.verifiedCount === 1 ? "" : "s"}`}
                    </span>
                    <span className="savings-tip-row">
                      <span className="savings-tip-key savings-stage-identified" />
                      <strong>{formatCompactMoney(currency, bucket.identified)}</strong> identified
                      {bucket.identifiedCount > 0 && ` · ${bucket.identifiedCount} found`}
                    </span>
                    <span className="savings-tip-total">{formatCompactMoney(currency, bucket.cumulativeVerified)} verified in the window so far</span>
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}

        <details className="savings-chart-table" onToggle={(event) => setTableOpen(event.currentTarget.open)}>
          <summary>Show as table</summary>
          {tableOpen && (
            <table className="table" aria-label={`Savings by month, ${currency}`}>
              <thead>
                <tr>
                  <th scope="col">Month</th>
                  <th scope="col" className="savings-table-numeric">
                    Verified
                  </th>
                  <th scope="col" className="savings-table-numeric">
                    Identified (midpoint)
                  </th>
                  <th scope="col" className="savings-table-numeric">
                    Verified so far
                  </th>
                </tr>
              </thead>
              <tbody>
                {buckets.map((bucket) => (
                  <tr key={bucket.key}>
                    <td>{bucket.longLabel}</td>
                    <td className="savings-table-numeric">{bucket.verifiedCount === 0 ? "—" : formatMoney(currency, bucket.verified)}</td>
                    <td className="savings-table-numeric">{bucket.identifiedCount === 0 ? "—" : formatMoney(currency, bucket.identified)}</td>
                    <td className="savings-table-numeric">{formatMoney(currency, bucket.cumulativeVerified)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </details>
      </figure>
    </section>
  );
}
