import { Link } from "react-router-dom";
import type { AskOffCopy } from "./askViewModel";

export interface AskOffStateProps {
  copy: AskOffCopy;
}

/**
 * screens-v2.md #2 "Off (no validated contract)" (R-ASK-10; task E13/F09/US01/T04 task text point
 * (1)). Markup quoted from the compiled prototype's own off-state block
 * (`raffa-v2/markup.html`'s `sc-if value="{{ kbOff }}"` block): a kicker, the fixed headline
 * ("Ask needs at least one validated contract." -- never varies, unlike `copy.reason`/
 * `copy.ctaLabel`), the reason, and one CTA that always goes to `/documents`
 * (`app.jsx`'s own `goDocuments` handler is the same one-target navigation regardless of which
 * label is showing).
 */
export default function AskOffState({ copy }: AskOffStateProps) {
  return (
    <div className="ask-off">
      <p className="screen-kicker">Ask Raffa</p>
      <h2 className="screen-title ask-off-title">Ask needs at least one validated contract.</h2>
      <p className="micro-meta ask-off-reason">{copy.reason}</p>
      <div className="ask-off-actions">
        <Link to="/documents" className="btn btn-primary">
          {copy.ctaLabel}
        </Link>
      </div>
    </div>
  );
}
