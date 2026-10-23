import { useId, type KeyboardEvent } from "react";
import { view } from "../case/engine";
import { demoContext } from "../case/fixtures";
import type { RenewalSummary } from "../case/portfolio";
import type { RenewalsWorkspace } from "../case/useRenewalsWorkspace";
import { TAB_KEYS, type TabKey } from "../caseTabs";
import ClausesTab from "./ClausesTab";
import MailTab from "./MailTab";
import NowTab from "./NowTab";
import TeamTab from "./TeamTab";
import TermsTab from "./TermsTab";
import TimelineTab from "./TimelineTab";
import { tabBadges } from "./tabModel";
import "./caseTabs.css";

export interface CaseTabsProps {
  item: RenewalSummary;
  workspace: RenewalsWorkspace;
  tab: TabKey;
  onTab: (tab: TabKey) => void;
}

/**
 * The six fixed tabs of a renewal case, always in this order: Do now (the default: the step guide
 * and the key data), Terms & goals, Timeline, Emails & documents, What to expect, Team. Each tab has
 * a count; the bar scrolls sideways on a narrow screen and stays in view while the page scrolls.
 */
export default function CaseTabs({ item, workspace: w, tab, onTab }: CaseTabsProps) {
  const { t } = w;
  const uid = useId();
  const c = w.cases[item.cid];
  if (!c) return null;
  const v = view(c, demoContext(item.cid, w.viewer, w.lang));
  const badges = tabBadges(v);

  const labels: Record<TabKey, string> = {
    now: t("Do now"),
    terms: t("Terms & goals"),
    timeline: t("Timeline"),
    mail: t("Emails & documents"),
    clauses: t("What to expect"),
    team: t("Team"),
  };

  const onKey = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const last = TAB_KEYS.length - 1;
    const next = event.key === "ArrowRight" ? (index === last ? 0 : index + 1) : event.key === "ArrowLeft" ? (index === 0 ? last : index - 1) : event.key === "Home" ? 0 : event.key === "End" ? last : null;
    if (next === null) return;
    event.preventDefault();
    onTab(TAB_KEYS[next] as TabKey);
    document.getElementById(`${uid}-tab-${TAB_KEYS[next]}`)?.focus();
  };

  const props = { v, c, item, w };
  return (
    <div className="ct">
      <div className="ct-bar" role="tablist" aria-label={t("Renewal sections")}>
        {TAB_KEYS.map((key, i) => (
          <button
            key={key}
            type="button"
            role="tab"
            id={`${uid}-tab-${key}`}
            aria-selected={tab === key}
            aria-controls={`${uid}-panel-${key}`}
            tabIndex={tab === key ? 0 : -1}
            className={`ct-tab${tab === key ? " is-selected" : ""}`}
            onClick={() => onTab(key)}
            onKeyDown={(event) => onKey(event, i)}
          >
            {labels[key]}
            {badges[key] && <span className="ct-badge">{badges[key]}</span>}
          </button>
        ))}
      </div>
      <div role="tabpanel" id={`${uid}-panel-${tab}`} aria-labelledby={`${uid}-tab-${tab}`} className="ct-panel">
        {tab === "now" && <NowTab {...props} />}
        {tab === "terms" && <TermsTab {...props} />}
        {tab === "timeline" && <TimelineTab {...props} />}
        {tab === "mail" && <MailTab {...props} />}
        {tab === "clauses" && <ClausesTab {...props} />}
        {tab === "team" && <TeamTab {...props} />}
      </div>
    </div>
  );
}
