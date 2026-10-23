import { useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import AskRaffaLink from "../../components/ask-bar/AskRaffaLink";
import { ASK_PROMPTS } from "../../components/ask-bar/askLaunch";
import {
  applyKpi,
  buildKpis,
  groupByKind,
  headline,
  inScope,
  type KpiKey,
  type RenewalsView,
  type Scope,
} from "./case/portfolio";
import { useRenewalsWorkspace } from "./case/useRenewalsWorkspace";
import { parseTab, type TabKey } from "./caseTabs";
import CasePanel from "./CasePanel";
import PipelineView from "./PipelineView";
import QueueGroups from "./queue/QueueGroups";
import RenewalCard from "./RenewalCard";
import RenewalKpiStrip from "./RenewalKpiStrip";
import SegControl from "./SegControl";
import "./renewals.css";

export interface RenewalsRouteProps {
  /** The signed-in account's label (the demo person it maps to is resolved in the workspace hook). */
  userLabel: string;
}

/**
 * Route `/renewals`. The chat is for action, Renewals is for context: a compact card per contract
 * (supplier, contract, the seven steps, days to the notice deadline, status), the KPI strip as the
 * filter above them, and the selected renewal's case on the right. Three views of the same cases:
 * Cases, Pipeline (a column per wizard step) and Action queue (everything to do, by kind).
 *
 * `?select=<contract>&tab=<tab>` opens a given case on a given tab (the chat's "History" link).
 */
export default function RenewalsRoute({ userLabel }: RenewalsRouteProps) {
  const w = useRenewalsWorkspace(userLabel);
  const { t } = w;
  const [search] = useSearchParams();
  const [view, setView] = useState<RenewalsView>("cases");
  const [scope, setScope] = useState<Scope>("team");
  const [kpi, setKpi] = useState<KpiKey | null>(null);
  const [selected, setSelected] = useState<string | null>(() => search.get("select"));
  const [tab, setTab] = useState<TabKey>(() => parseTab(search.get("tab")));

  const base = useMemo(() => inScope(w.summaries, scope), [w.summaries, scope]);
  const kpis = useMemo(() => buildKpis(base, t), [base, t]);
  const list = useMemo(() => applyKpi(base, kpi), [base, kpi]);
  const current = list.find((x) => x.cid === selected) ?? list[0] ?? null;

  // The action queue follows the same "Show" switch: mine, everyone's, or deadlines nobody owns yet.
  const queueGroups = useMemo(() => {
    const rows = scope === "mine" ? w.queue.filter((x) => x.mine) : scope === "unassigned" ? w.queue.filter((x) => x.kind === "unassigned") : w.queue;
    return groupByKind(rows, t);
  }, [scope, w.queue, t]);

  const pick = (cid: string) => {
    setSelected(cid);
    setTab("now");
    setView("cases");
  };

  return (
    <div className="renewal-screen rn-screen" data-screen-label="Renewals">
      <header className="rn-header">
        <div className="rn-heading">
          <h2 className="rn-title">{t("Renewals")}</h2>
          <span className="rn-headline">{headline(w.summaries, t)}</span>
        </div>
        <div className="rn-controls">
          <AskRaffaLink question={ASK_PROMPTS.renewalsStartFirst} className="btn btn-ghost">
            {t("Ask Raffa which to start first")} →
          </AskRaffaLink>
          <SegControl
            label={t("View")}
            value={view}
            onChange={setView}
            options={[
              { value: "cases", label: t("Cases") },
              { value: "pipeline", label: t("Pipeline") },
              { value: "queue", label: t("Action queue") },
            ]}
          />
          <SegControl
            label={t("Show")}
            value={scope}
            onChange={setScope}
            options={[
              { value: "mine", label: t("Mine") },
              { value: "team", label: t("Team") },
              { value: "unassigned", label: t("Unassigned") },
            ]}
          />
        </div>
      </header>

      {view === "cases" && (
        <div className="rn-body">
          <RenewalKpiStrip cells={kpis} active={kpi} onToggle={(key) => setKpi((c) => (c === key ? null : key))} label={t("Renewal KPIs")} />
          <div className="rn-panes">
            <section className="rn-list" aria-label={t("Renewals · by notice date")}>
              <div className="rn-list-head">
                <span>{t("Renewals · by notice date")}</span>
                <span>{t("Days to notice")}</span>
              </div>
              {list.length > 0 ? (
                <ul className="rn-cards">
                  {list.map((item) => (
                    <RenewalCard key={item.cid} item={item} selected={item.cid === current?.cid} t={t} onSelect={pick} />
                  ))}
                </ul>
              ) : (
                <p className="rn-empty">{t("No renewal matches this filter.")}</p>
              )}
            </section>
            <section className="rn-panel" aria-label={t("Selected renewal")}>
              {current && <CasePanel key={current.cid} item={current} workspace={w} tab={tab} onTab={setTab} />}
            </section>
          </div>
        </div>
      )}

      {view === "pipeline" && <PipelineView items={list} lang={w.lang} t={t} onPick={pick} />}

      {view === "queue" && (
        <div className="rn-queue">
          <QueueGroups
            groups={queueGroups}
            t={t}
            actionsFor={w.actionsFor}
            onRun={w.run}
            onDelegate={w.delegate}
            onSnooze={w.snooze}
            onDone={w.dismiss}
            onOpenInRenewals={(item) => pick(item.cid)}
          />
          {queueGroups.length === 0 && <p className="rn-empty">{t("No action matches this filter.")}</p>}
        </div>
      )}
    </div>
  );
}
