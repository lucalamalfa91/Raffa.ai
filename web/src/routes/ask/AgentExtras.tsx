import { useI18n } from "../../i18n/I18nProvider";
import type { ConversationMessage4 } from "../../api/client";

/** One action the agentic engine proposes (open a screen, upload a document, an outside link). */
export interface AgentAction {
  label: string;
  href: string;
}

/** The engine's actions that are links (the 4.0 scripted CTAs — `compareQuote` / `openContract` — are drawn apart). */
export function agentActions(m: ConversationMessage4): AgentAction[] {
  return m.actions.filter((a): a is { label: string; href: string } => !!a.label && !!a.href).slice(0, 3);
}

/**
 * What the agentic Ask engine adds under a reply: the actions it proposes, its interview (Raffa asks
 * before acting — clickable options) and, on the latest turn only, the next questions worth asking.
 */
export default function AgentExtras({
  message,
  latest,
  busy,
  onAsk,
  onInterview,
  onNavigate,
}: {
  message: ConversationMessage4;
  latest: boolean;
  busy: boolean;
  onAsk: (question: string) => void;
  onInterview: (messageId: string, questionKey: string, option: { key: string; label: string }) => void;
  onNavigate: (href: string) => void;
}) {
  const { t } = useI18n();
  const actions = agentActions(message);
  const interview = message.interview;
  const followUps = latest ? (message.payload?.followUps ?? []).filter((q) => q.trim() !== "").slice(0, 4) : [];
  if (actions.length === 0 && !interview && followUps.length === 0) return null;
  return (
    <div className="agent-extras" data-parity-hide>
      {interview && !interview.answered && latest
        ? interview.questions.map((q) => (
            <div key={q.key} className="agent-interview" role="group" aria-label={q.prompt}>
              {q.prompt && q.prompt !== interview.prompt ? <div className="agent-interview-q">{q.prompt}</div> : null}
              <div className="agent-interview-opts">
                {q.options.map((o) => (
                  <button
                    key={o.key}
                    type="button"
                    className="agent-option"
                    disabled={busy}
                    title={o.hint ?? undefined}
                    onClick={() => onInterview(message.id, q.key, o)}
                  >
                    {o.label}
                  </button>
                ))}
              </div>
              {q.allowFreeText ? <div className="agent-interview-hint">{t("Or write your answer below.")}</div> : null}
            </div>
          ))
        : null}
      {actions.length > 0 ? (
        <div className="agent-actions">
          {actions.map((a) => (
            <button key={a.href + a.label} type="button" className="btn btn-secondary agent-action" onClick={() => onNavigate(a.href)}>
              {a.label}
            </button>
          ))}
        </div>
      ) : null}
      {followUps.length > 0 ? (
        <div className="reply-followups" role="group" aria-label={t("Suggested questions")}>
          {followUps.map((q) => (
            <button key={q} type="button" className="reply-followup" disabled={busy} onClick={() => onAsk(q)}>
              <span className="reply-followup-text">{q}</span>
              <span className="reply-followup-arrow" aria-hidden="true">
                ↗
              </span>
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}
