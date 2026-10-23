import type { RenewalSummary } from "./case/portfolio";
import type { Translate } from "../../i18n/translate";

export interface NotStartedPanelProps {
  item: RenewalSummary;
  t: Translate;
  /** The approver (CPO) starts and assigns; everyone else starts it for themselves. */
  isApprover: boolean;
  assignee: string;
  onStartAssigned: () => void;
  onStartOwn: () => void;
  onStartNotice: () => void;
}

/** A contract with no renewal case yet: what is at stake, and how to start. */
export default function NotStartedPanel({ item, t, isApprover, assignee, onStartAssigned, onStartOwn, onStartNotice }: NotStartedPanelProps) {
  const first = assignee.split(" ")[0] ?? assignee;
  return (
    <div className="rn-notstarted">
      <div>
        <div className="rn-case-status">{t("Not started")}</div>
        <h3 className="rn-case-title">
          {item.supplier} — {item.fullName}
        </h3>
      </div>
      <div className="rn-ruled">
        <div className="rn-ruled-days">
          <div className="rn-label">{t("Days to notice")}</div>
          <div className={`rn-big${item.noticeDays <= 30 ? " is-urgent" : ""}`}>{item.noticeDays}</div>
          <div className="rn-small">{t("by {date} · ends {end}", { date: item.cancel, end: item.end })}</div>
        </div>
        <div className="rn-ruled-why">
          <p className="rn-strong">{item.autoRenews ? t("Auto-renews for 12 months at +{uplift} if nothing happens.", { uplift: item.uplift }) : t("Does not auto-renew.")}</p>
          <p>{item.rationale}</p>
        </div>
      </div>
      <div className="rn-buttons">
        {isApprover ? (
          <>
            <button type="button" className="btn btn-primary" onClick={onStartAssigned}>
              {t("Start & assign to {name}", { name: first })}
            </button>
            <button type="button" className="btn btn-secondary" onClick={onStartOwn}>
              {t("I’ll handle it")}
            </button>
            <button type="button" className="btn btn-ghost" onClick={onStartNotice}>
              {t("Prepare non-renewal notice")}
            </button>
          </>
        ) : (
          <button type="button" className="btn btn-primary" onClick={onStartOwn}>
            {t("Start renewal")}
          </button>
        )}
      </div>
      {isApprover && <p className="rn-small">{t("The owner works the case in the chat; you approve emails and closings.")}</p>}
    </div>
  );
}
