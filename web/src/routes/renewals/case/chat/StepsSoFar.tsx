import { useId, useState } from "react";
import type { Translate } from "../../../../i18n/translate";
import type { HistoryGroup } from "./chatModel";

export interface StepsSoFarProps {
  groups: readonly HistoryGroup[];
  t: Translate;
  /** Opens one past card in the read-only drawer. */
  onOpen: (cardId: string) => void;
}

/** One collapsible row per wizard step that has history, above the current step. */
export default function StepsSoFar({ groups, t, onOpen }: StepsSoFarProps) {
  const [open, setOpen] = useState<ReadonlySet<number>>(new Set());
  const uid = useId();
  if (groups.length === 0) return null;

  const toggle = (n: number) =>
    setOpen((current) => {
      const next = new Set(current);
      if (next.has(n)) next.delete(n);
      else next.add(n);
      return next;
    });

  return (
    <section className="rc-history" aria-label={t("Steps so far")}>
      <h3 className="rc-label rc-history-title">{t("Steps so far")}</h3>
      {groups.map((group) => {
        const isOpen = open.has(group.n);
        const panelId = `${uid}-${group.n}`;
        return (
          <div className="rc-hgroup" key={group.n}>
            <button type="button" className="rc-hrow" aria-expanded={isOpen} aria-controls={panelId} onClick={() => toggle(group.n)}>
              <span className="rc-hmark" aria-hidden="true" />
              <span className="rc-hn">{group.n}</span>
              <strong>{group.label}</strong>
              <span className="rc-hsum">{group.rows.length === 1 ? t("1 item") : t("{n} items", { n: group.rows.length })}</span>
              <span className="rc-harrow" aria-hidden="true">
                {isOpen ? "▾" : "▸"}
              </span>
            </button>
            {isOpen && (
              <ul className="rc-hlist" id={panelId} aria-label={group.label}>
                {group.rows.map((row) => (
                  <li key={row.id}>
                    <button type="button" className="rc-hitem" onClick={() => onOpen(row.id)}>
                      <span className="rc-hat">{row.at}</span>
                      <span className="rc-hlabel">{row.label}</span>
                      {row.note && <span className="rc-hnote">{row.note}</span>}
                      <span className="rc-hview">{t("View →")}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        );
      })}
    </section>
  );
}
