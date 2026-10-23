import { Empty, SectionHead } from "./TabParts";
import type { TabProps } from "./NowTab";

const LATEST = 14;

/** What is coming (hollow squares), what happened (filled), and the log of decisions. */
export default function TimelineTab({ v, w }: TabProps) {
  const { t } = w;
  return (
    <div className="ct-stack">
      <section>
        <SectionHead>{t("Timeline — what is coming, what happened")}</SectionHead>
        <ol className="ct-timeline">
          {v.upcoming.map((u, i) => (
            <li key={`u${i}`} className="ct-tl-row">
              <span className="ct-muted">{u.date}</span>
              <span className="ct-sq is-hollow" aria-hidden="true" />
              <span>
                <strong>{u.label}</strong> <span className="ct-muted">· {u.when}</span>
              </span>
            </li>
          ))}
          {v.timeline.slice(0, LATEST).map((e, i) => (
            <li key={`t${i}`} className="ct-tl-row is-past">
              <span className="ct-muted">
                {e.at} · {e.time}
              </span>
              <span className="ct-sq" aria-hidden="true" />
              <span>
                <strong>{e.by}</strong> · {e.label}
                {e.text && <span className="ct-soft"> — {e.text}</span>}
              </span>
            </li>
          ))}
        </ol>
      </section>
      <section>
        <SectionHead>{t("Decision log")}</SectionHead>
        {v.summary.decisions.length === 0 ? (
          <Empty>{t("No decisions recorded yet.")}</Empty>
        ) : (
          <ul className="ct-list">
            {v.summary.decisions.map((d, i) => (
              <li key={i} className="ct-decision">
                <span className="ct-muted">
                  {d.at} · {d.by}
                </span>{" "}
                — {d.text}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
