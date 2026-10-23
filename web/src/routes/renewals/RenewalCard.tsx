import { isNoticeUrgent, miniBars, type RenewalSummary } from "./case/portfolio";
import type { Translate } from "../../i18n/translate";

export interface RenewalCardProps {
  item: RenewalSummary;
  selected: boolean;
  t: Translate;
  onSelect: (cid: string) => void;
}

/**
 * One renewal as a compact card: supplier, contract, the seven step bars, status and owner on the
 * left; the days to the notice deadline on the right. Text wraps -- nothing is truncated.
 */
export default function RenewalCard({ item, selected, t, onSelect }: RenewalCardProps) {
  const urgent = isNoticeUrgent(item);
  const tag = item.done ? "tag-neutral" : item.hasCase ? "tag-accent" : "tag-outline";
  const progress = item.stage < 0 ? t("not started") : t("step {n} of 7", { n: item.stage + 1 });

  return (
    <li>
      <button type="button" className={`rn-card${selected ? " is-selected" : ""}`} aria-pressed={selected} onClick={() => onSelect(item.cid)}>
        <span className="rn-card-main">
          <span className="rn-supplier">{item.supplier}</span>
          <span className="rn-short">{item.short}</span>
          <span className="rn-bars" role="img" aria-label={progress}>
            {miniBars(item).map((bar, i) => (
              <span key={i} className={`rn-bar is-${bar}`} />
            ))}
          </span>
          <span className="rn-card-meta">
            <span className={`tag ${tag}`}>{item.status}</span>
            <span className="rn-owner">{item.owner}</span>
          </span>
        </span>
        <span className="rn-card-days">
          <span className={`rn-days${urgent ? " is-urgent" : ""}`}>{item.done ? "—" : item.noticeDays}</span>
          <span className="rn-days-sub">{item.done ? t("closed") : t("ends in {n} d", { n: item.endsInDays })}</span>
        </span>
      </button>
    </li>
  );
}
