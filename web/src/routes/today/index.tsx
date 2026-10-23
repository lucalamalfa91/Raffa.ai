import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { DEMO_TITLES } from "../renewals/case/fixtures";
import { groupByBand, inQueueScope, type Scope } from "../renewals/case/portfolio";
import { useRenewalsWorkspace } from "../renewals/case/useRenewalsWorkspace";
import QueueGroups from "../renewals/queue/QueueGroups";
import SegControl from "../renewals/SegControl";
import "../renewals/renewals.css";
import "./today.css";

export interface TodayRouteProps {
  /** The signed-in account's label (the demo person it maps to is resolved in the workspace hook). */
  userLabel: string;
}

/**
 * Route `/today` -- the task queue, by urgency. Everything anybody must do, nearest notice deadline
 * first, in three bands (within 30 days, 31–120, later), for you, for the team, or with no owner yet.
 * Each row opens the case at the step that needs doing; a person can delegate (approver), snooze it
 * for three days, or mark it done.
 */
export default function TodayRoute({ userLabel }: TodayRouteProps) {
  const w = useRenewalsWorkspace(userLabel);
  const { t } = w;
  const navigate = useNavigate();
  const [scope, setScope] = useState<Scope>("mine");

  const counts = useMemo(
    () => ({
      mine: inQueueScope(w.queue, "mine").length,
      team: inQueueScope(w.queue, "team").length,
      unassigned: inQueueScope(w.queue, "unassigned").length,
    }),
    [w.queue],
  );
  const rows = useMemo(() => inQueueScope(w.queue, scope), [w.queue, scope]);
  const bands = useMemo(() => groupByBand(rows, t), [rows, t]);

  const title = DEMO_TITLES[w.viewer];
  const nextDeadline = w.summaries.filter((x) => !x.done)[0];
  const empty: Record<Scope, string> = {
    mine: t("Nothing needs you right now. When a colleague finishes a step that needs your approval, it appears here."),
    team: t("No renewal in progress with the team."),
    unassigned: t("Every notice deadline has an owner."),
  };

  return (
    <div className="rn-today" data-screen-label="Today">
      <header className="today-header">
        <div>
          <p className="today-kicker">{title ? `${w.viewer} · ${t(title)}` : w.viewer}</p>
          <h2 className="today-title">
            {t("Today")} · {w.today}
          </h2>
          <p className="today-summary">{t(counts.mine === 1 ? "{n} action waiting for you · ordered by notice deadline" : "{n} actions waiting for you · ordered by notice deadline", { n: counts.mine })}</p>
        </div>
        <SegControl
          label={t("Show")}
          value={scope}
          onChange={setScope}
          options={[
            { value: "mine", label: t("Mine"), badge: counts.mine },
            { value: "team", label: t("Team"), badge: counts.team },
            { value: "unassigned", label: t("Unassigned"), badge: counts.unassigned },
          ]}
        />
      </header>

      <div className="today-list">
        <QueueGroups
          groups={bands}
          t={t}
          actionsFor={w.actionsFor}
          onRun={w.run}
          onDelegate={w.delegate}
          onSnooze={w.snooze}
          onDone={w.dismiss}
          onOpenInRenewals={(item) => navigate(`/renewals?select=${item.cid}&tab=now`)}
        />
        {bands.length === 0 && <p className="rn-empty">{empty[scope]}</p>}
      </div>

      {w.snoozed.length > 0 && (
        <section className="today-snoozed" aria-label={t("Snoozed")}>
          <h3 className="rn-label today-snoozed-head">{t("Snoozed")}</h3>
          <ul>
            {w.snoozed.map((item) => (
              <li key={item.key}>
                <span className="today-snoozed-title">{item.title}</span>
                <span className="rn-small">
                  {item.supplier} · {item.contract} · {t("until {date}", { date: item.until })}
                </span>
                <button type="button" className="btn btn-ghost" onClick={() => w.unsnooze(item.key)}>
                  {t("Bring back")}
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}

      <p className="today-foot">
        {nextDeadline && `${t("Next notice deadline: {supplier} in {n} days ({date})", { supplier: nextDeadline.supplier, n: nextDeadline.noticeDays, date: nextDeadline.cancel })} · `}
        {t("{n} in progress with the team", { n: counts.team })}
      </p>
    </div>
  );
}
