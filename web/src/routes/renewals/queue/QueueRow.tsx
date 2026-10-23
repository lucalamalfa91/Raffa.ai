import type { QueueItem, RowActions, RowCta } from "../case/portfolio";
import type { Translate } from "../../../i18n/translate";

export interface QueueRowProps {
  item: QueueItem;
  actions: RowActions;
  t: Translate;
  onRun: (cta: RowCta) => void;
  onDelegate: (delegate: NonNullable<RowActions["delegate"]>) => void;
  onSnooze: () => void;
  onDone: () => void;
  /** Shows "Open in Renewals" (Today only; on Renewals the user is already there). */
  onOpenInRenewals?: () => void;
}

/**
 * One thing somebody must do: how many days to the notice deadline, which contract and step, what
 * and why, who it waits on, and the buttons -- one primary action, a few quiet ones.
 */
export default function QueueRow({ item, actions, t, onRun, onDelegate, onSnooze, onDone, onOpenInRenewals }: QueueRowProps) {
  const mine = item.mine;
  return (
    <li className="rn-qrow">
      <div className="rn-qdays">
        <div className={`rn-qdays-n${item.dueDays <= 30 ? " is-urgent" : ""}`}>{item.dueDays}</div>
        <div className="rn-small">{t("days to notice · {date}", { date: item.due })}</div>
      </div>
      <div className="rn-qbody">
        <div className="rn-kicker">
          {item.supplier} · {item.contract} · {item.phase}
        </div>
        <h3 className="rn-qtitle">{item.title}</h3>
        <p className="rn-qwhy">{item.why}</p>
        <div className="rn-qmeta">
          <span className={`tag ${mine ? "tag-accent" : "tag-outline"}`}>{mine ? t("Waiting for you") : t("Waiting for {name}", { name: item.who })}</span>
          {item.stalled > 0 && <span className="rn-stalled">{t("No activity for {n} days", { n: item.stalled })}</span>}
        </div>
      </div>
      <div className="rn-qactions">
        <button type="button" className="btn btn-primary" onClick={() => onRun(actions.primary)}>
          {actions.primary.label}
        </button>
        {actions.secondary && (
          <button type="button" className="btn btn-secondary" onClick={() => actions.secondary && onRun(actions.secondary)}>
            {actions.secondary.label}
          </button>
        )}
        <div className="rn-qquiet">
          {actions.delegate && (
            <button type="button" className="btn btn-ghost" onClick={() => actions.delegate && onDelegate(actions.delegate)}>
              {actions.delegate.label}
            </button>
          )}
          {onOpenInRenewals && item.hasCase && (
            <button type="button" className="btn btn-ghost" onClick={onOpenInRenewals}>
              {t("Open in Renewals")}
            </button>
          )}
          <button type="button" className="btn btn-ghost" onClick={onSnooze}>
            {t("Snooze 3 days")}
          </button>
          <button type="button" className="btn btn-ghost rn-quiet" onClick={onDone}>
            {t("Done")}
          </button>
        </div>
      </div>
    </li>
  );
}
