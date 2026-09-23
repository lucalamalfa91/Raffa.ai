import { Link } from "react-router-dom";
import type { ContextCellView } from "./savingsViewModel";

export interface ContextStripProps {
  cells: readonly ContextCellView[];
}

/**
 * The portfolio context under the headline band -- contracts and spend analyzed, upcoming renewals,
 * notice deadlines inside 45 days -- each cell a link to the screen that owns the figure (Portfolio,
 * Renewals), so the dashboard is a way in, not a dead end. The same `.attention-strip` shape the
 * design system names for "big number + label" rows; the urgent number is accent, always beside
 * its own label.
 */
export default function ContextStrip({ cells }: ContextStripProps) {
  return (
    <nav className="savings-context" aria-label="Portfolio context">
      <ul className="attention-strip savings-context-strip">
        {cells.map((cell) => (
          <li key={cell.key} className={`savings-context-cell${cell.urgent ? " is-urgent" : ""}`} data-context={cell.key}>
            <Link to={cell.href} className="savings-context-link">
              <span className="savings-context-label">{cell.label}</span>
              <span className="savings-context-value">{cell.value}</span>
              <span className="savings-context-meta">{cell.meta}</span>
              <span className="savings-context-go">{cell.linkLabel} →</span>
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
