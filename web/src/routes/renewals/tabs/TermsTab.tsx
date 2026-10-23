import { Empty, SectionHead } from "./TabParts";
import type { TabProps } from "./NowTab";

/** Terms agreed against terms still open, next to the goals the plan was built on. */
export default function TermsTab({ v, w }: TabProps) {
  const { t } = w;
  return (
    <div className="ct-two">
      <section className="ct-col">
        <SectionHead>{t("Terms — agreed vs open")}</SectionHead>
        {v.summary.terms.length === 0 ? (
          <Empty>{t("Terms appear once the plan is confirmed.")}</Empty>
        ) : (
          <ul className="ct-list">
            {v.summary.terms.map((term, i) => (
              <li key={i} className="ct-term">
                <span>{term.label}</span>
                <span>{term.val}</span>
                <span className={`ct-term-state is-${term.status}`}>{term.state}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
      <section className="ct-col">
        <SectionHead>{t("Plan goals")}</SectionHead>
        <ul className="ct-list">
          {v.summary.goals.map((goal) => (
            <li key={goal.k} className="ct-goal">
              <span className="ct-muted">{goal.k}</span>
              <span>{goal.v}</span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
