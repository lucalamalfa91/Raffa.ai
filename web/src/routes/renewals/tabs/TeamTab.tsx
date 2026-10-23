import { SectionHead } from "./TabParts";
import type { TabProps } from "./NowTab";

/** Who is on the case and what each person answers for; the approver can hand over ownership and delegate approvals. */
export default function TeamTab({ v, c, item, w }: TabProps) {
  const { t } = w;
  return (
    <div className="ct-team">
      <SectionHead>{t("Team & delegation")}</SectionHead>
      <ul className="ct-list">
        {v.hdr.people.map((p) => {
          const canAct = v.permissions.manageTeam && p.responsibility !== "approver";
          return (
            <li key={p.name} className="ct-person">
              <strong>{p.name}</strong>
              <span className="ct-soft">{p.role}</span>
              {canAct && (
                <span className="ct-person-actions">
                  {p.name === c.owner ? (
                    <span className="ct-muted">{t("Owner")}</span>
                  ) : (
                    <button type="button" className="btn btn-ghost" onClick={() => w.act(item.cid, "transfer", { who: p.name })}>
                      {t("Make owner")}
                    </button>
                  )}
                  <button type="button" className="btn btn-ghost" onClick={() => w.act(item.cid, "delegate", { who: p.name, on: !p.isDelegate })}>
                    {p.isDelegate ? t("Remove approval delegation") : t("Delegate approvals")}
                  </button>
                </span>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
