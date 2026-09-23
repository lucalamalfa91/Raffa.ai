import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { buildAskLaunch } from "./askLaunch";

export interface AskRaffaLinkProps {
  /** Asked as soon as Ask opens -- a new chat, never appended to one already on screen. */
  question: string;
  /** Binds the new chat to this contract (`/ask?scope=`), the same binding Contract 360's "Ask about it" makes. */
  scopeContractId?: string | null;
  className?: string;
  /** Accessible name when the visible text alone is ambiguous (e.g. a row's short "Ask" link). */
  ariaLabel?: string;
  children: ReactNode;
}

/**
 * A plain `<Link>` into Ask Raffa that asks `question` on arrival (`askLaunch.ts`). A link, not a
 * button: it is navigation, so it opens in a new tab on modifier-click and reads as a link to
 * assistive tech. The full question rides the `title`, so the reader sees what will be asked.
 */
export default function AskRaffaLink({ question, scopeContractId = null, className, ariaLabel, children }: AskRaffaLinkProps) {
  const launch = buildAskLaunch(question, scopeContractId);
  return (
    <Link to={launch.to} state={launch.state} className={className} title={launch.state.query} aria-label={ariaLabel}>
      {children}
    </Link>
  );
}
