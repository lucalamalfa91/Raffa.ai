import { Empty, SectionHead } from "./TabParts";
import type { TabProps } from "./NowTab";

/** Every email Raffa drafted with its state, and the documents with their versions. */
export default function MailTab({ v, w }: TabProps) {
  const { t } = w;
  return (
    <div className="ct-two">
      <section className="ct-col">
        <SectionHead>{t("Emails")}</SectionHead>
        {v.emailsList.length === 0 ? (
          <Empty>{t("No email yet.")}</Empty>
        ) : (
          <ul className="ct-list">
            {v.emailsList.map((e, i) => (
              <li key={i} className="ct-pair">
                <span>{e.label}</span>
                <span className={`ct-pair-state${e.pending ? " is-alert" : ""}`}>{e.state}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
      <section className="ct-col">
        <SectionHead>{t("Documents & versions")}</SectionHead>
        {v.docsList.length === 0 ? (
          <Empty>{t("The current contract is the only document. Drafts appear here when they arrive.")}</Empty>
        ) : (
          <ul className="ct-list">
            {v.docsList.map((d, i) => (
              <li key={i} className="ct-pair">
                <span className="ct-file">{d.label}</span>
                <span className={`ct-pair-state${d.open ? " is-alert" : d.final ? "" : " is-muted"}`}>{d.state}</span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
