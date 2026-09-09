import { Link } from "react-router-dom";
import type { ReplyAction } from "./replyTypes";

export interface ActionRowProps {
  actions: readonly ReplyAction[];
}

/**
 * `actions[]` -> `.btn-primary` / `.btn-secondary` buttons (task text; requirements.md §6:
 * `{ label, href, kind }` -- no `onClick` on the wire, every action is a plain in-app navigation).
 * A real `<Link>` (react-router-dom), the same primitive `../../renewals/InsightCard.tsx` already
 * uses for an identical "backend-authored href, styled as a button" shape -- never a bare
 * `<button>`, so middle-click / ctrl-click / "open in new tab" keep working (ADR-019 accessibility
 * baseline: native controls).
 */
export default function ActionRow({ actions }: ActionRowProps) {
  if (actions.length === 0) return null;

  return (
    <div className="reply-actions">
      {actions.map((action) => (
        <Link key={`${action.kind}-${action.label}`} to={action.href} className={`btn btn-${action.kind}`}>
          {action.label}
        </Link>
      ))}
    </div>
  );
}
