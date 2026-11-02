import { useNavigate } from "react-router-dom";
import "./askShortcuts.css";
import { useI18n } from "../../i18n/I18nProvider";

/** One question to hand to Raffa's agentic Ask engine (strategy, negotiation council, drafting, savings). */
export interface AskShortcut {
  label: string;
  question: string;
}

/**
 * Quiet one-click questions that open Chiedi on the full agentic engine, scoped to a contract when one is
 * given — the launch points the 4.0 restyle had dropped (renewal approach, negotiation email, where to
 * save, which renewals first). Hidden in the parity run: the prototype has no such row.
 */
export default function AskShortcuts({ items, scopeContractId }: { items: readonly AskShortcut[]; scopeContractId?: string | null }) {
  const { t } = useI18n();
  const navigate = useNavigate();
  if (items.length === 0) return null;
  const go = (q: string) =>
    navigate(`/ask?${scopeContractId ? `scope=${encodeURIComponent(scopeContractId)}&` : ""}q=${encodeURIComponent(q)}`);
  return (
    <div className="ask-shortcuts" role="group" aria-label={t("Ask Raffa")} data-parity-hide>
      <span className="ask-shortcuts-label">{t("Ask Raffa")}</span>
      {items.map((s) => (
        <button key={s.label} type="button" className="ask-shortcut" onClick={() => go(s.question)}>
          {s.label} <span aria-hidden="true">↗</span>
        </button>
      ))}
    </div>
  );
}
