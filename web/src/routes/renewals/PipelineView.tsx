import { STEPS } from "./case/model";
import type { RenewalSummary } from "./case/portfolio";
import { resolveText, type Lang, type Translate } from "../../i18n/translate";

export interface PipelineViewProps {
  items: readonly RenewalSummary[];
  lang: Lang;
  t: Translate;
  onPick: (cid: string) => void;
}

/** Columns: not started, then the seven wizard steps. A card is a renewal; closed ones leave. */
export default function PipelineView({ items, lang, t, onPick }: PipelineViewProps) {
  const columns = [{ stage: -1, n: "—", label: t("Not started") }, ...STEPS.map((step, i) => ({ stage: i, n: String(i + 1), label: resolveText(step, lang) }))];
  return (
    <div className="rn-pipeline">
      {columns.map((column) => {
        const cards = items.filter((x) => x.stage === column.stage && !x.done);
        return (
          <section className="rn-column" key={column.stage} aria-label={`${column.n} · ${column.label}`}>
            <div className="rn-column-head">
              <span className="rn-column-title">
                {column.n} · {column.label}
              </span>
              <span className="rn-small">{cards.length}</span>
            </div>
            {cards.map((x) => (
              <button type="button" className="rn-pipe-card" key={x.cid} onClick={() => onPick(x.cid)}>
                <span className="rn-supplier-sm">{x.supplier}</span>
                <span className="rn-small">{x.short}</span>
                <span className={`rn-pipe-days${x.noticeDays <= 30 ? " is-urgent" : ""}`}>{t("{n} days", { n: x.noticeDays })}</span>
                <span className="rn-small">{x.owner}</span>
              </button>
            ))}
          </section>
        );
      })}
    </div>
  );
}
