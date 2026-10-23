import { demoContext } from "./case/fixtures";
import { view } from "./case/engine";
import type { RenewalSummary } from "./case/portfolio";
import type { RenewalsWorkspace } from "./case/useRenewalsWorkspace";
import type { TabKey } from "./caseTabs";
import NotStartedPanel from "./NotStartedPanel";
import CaseTabs from "./tabs/CaseTabs";

export interface CasePanelProps {
  item: RenewalSummary;
  workspace: RenewalsWorkspace;
  tab: TabKey;
  onTab: (tab: TabKey) => void;
}

/** The selected renewal: not-started block, or the case header with its tabs. */
export default function CasePanel({ item, workspace: w, tab, onTab }: CasePanelProps) {
  const { t } = w;
  const c = w.cases[item.cid];

  if (!c) {
    return (
      <NotStartedPanel
        item={item}
        t={t}
        isApprover={w.isApprover}
        assignee={w.assignee}
        onStartAssigned={() => w.startAssigned(item.cid, w.assignee)}
        onStartOwn={() => w.startOwn(item.cid)}
        onStartNotice={() => w.startOwn(item.cid, "notice")}
      />
    );
  }

  const v = view(c, demoContext(item.cid, w.viewer, w.lang));
  return (
    <div className="rn-case">
      <div>
        <div className="rn-case-status">{v.hdr.status}</div>
        <h3 className="rn-case-title">
          {item.supplier} — {item.fullName}
        </h3>
        <p className="rn-case-meta">
          {t("Owner")} <strong>{c.owner}</strong> · {t("Approver")} <strong>{c.approver}</strong> · {t("Notice by")} <strong>{item.cancel}</strong> · {t("Ends")} {item.end}
        </p>
      </div>
      <CaseTabs item={item} workspace={w} tab={tab} onTab={onTab} />
    </div>
  );
}
