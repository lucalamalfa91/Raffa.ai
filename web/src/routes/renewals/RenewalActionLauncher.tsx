import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import AskRaffaLink from "../../components/ask-bar/AskRaffaLink";
import { CopyTip } from "../../components/InfoTip";
import { TIPS } from "../../components/infoTipCopy";
import type { RenewalActionGroupView, RenewalActionTarget } from "./renewalActions";

export interface RenewalActionLinkProps {
  target: Extract<RenewalActionTarget, { kind: "link" | "ask" }>;
  className: string;
  ariaLabel?: string;
  children: ReactNode;
}

/** A `link` target is a router link; an `ask` target opens a new Ask Raffa chat asking the prepared question (`askLaunch.ts`). */
export function RenewalActionLink({ target, className, ariaLabel, children }: RenewalActionLinkProps) {
  if (target.kind === "ask") {
    return (
      <AskRaffaLink question={target.question} scopeContractId={target.scopeContractId} className={className} ariaLabel={ariaLabel}>
        {children}
      </AskRaffaLink>
    );
  }
  return (
    <Link to={target.to} state={target.state} className={className} aria-label={ariaLabel}>
      {children}
    </Link>
  );
}

export interface RenewalActionLauncherProps {
  groups: readonly RenewalActionGroupView[];
}

/**
 * The pane's "What you can do from here": the action registry (`renewalActions.ts`) resolved for
 * the selected renewal, one group per heading -- Ask Raffa, Open with this contract, Coming to
 * Raffa.ai. Every entry is a link (a new screen, or a new Ask chat), so nothing here writes; the
 * workflow writes stay the pane's own buttons above. "Soon" entries carry a tag, never colour alone.
 */
export default function RenewalActionLauncher({ groups }: RenewalActionLauncherProps) {
  return (
    <section className="renewal-launcher" aria-label="What you can do from here">
      <p className="card-kicker">
        What you can do from here
        <CopyTip tip={TIPS.renewalsLauncher} align="end" />
      </p>
      {groups.map((group) => (
        <div key={group.key} className={`renewal-launcher-group is-${group.key}`}>
          <p className="renewal-launcher-title">{group.title}</p>
          {group.note !== null && <p className="renewal-launcher-note">{group.note}</p>}
          <ul className={group.key === "open" ? "renewal-launcher-chips" : "renewal-launcher-list"}>
            {group.actions.map((action) => {
              if (action.target.kind === "write") return null;
              return (
                <li key={action.id}>
                  <RenewalActionLink
                    target={action.target}
                    className={group.key === "open" ? "renewal-launcher-chip" : "renewal-launcher-item"}
                    ariaLabel={group.key === "open" ? `Open ${action.label} — ${action.hint}` : undefined}
                  >
                    {group.key === "open" ? (
                      <>{action.label} →</>
                    ) : (
                      <>
                        <span className="renewal-launcher-label">
                          {action.label}
                          {action.status === "soon" && <span className="tag tag-outline renewal-launcher-soon">Soon</span>}
                        </span>
                        <span className="renewal-launcher-hint">{action.hint}</span>
                        <span className="renewal-launcher-arrow" aria-hidden="true">
                          →
                        </span>
                      </>
                    )}
                  </RenewalActionLink>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </section>
  );
}
