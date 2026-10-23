import { useState } from "react";
import type { ActPayload, ActionType, CaseView } from "../types";
import type { Translate } from "../../../../i18n/translate";

export interface ComposerProps {
  view: CaseView;
  t: Translate;
  onAct: (type: ActionType, payload?: ActPayload) => void;
}

/**
 * Free text for what the structured answers cannot say: an answer in your own words, a comment, or a
 * command (approve / copy / I sent it). Also hosts the "paste the supplier reply" box, the vendor
 * threads and the case's extra actions. Flat, square, no shadow.
 */
export default function Composer({ view, t, onAct }: ComposerProps) {
  const [draft, setDraft] = useState("");

  const submit = (event: { preventDefault: () => void }) => {
    event.preventDefault();
    const text = draft.trim();
    if (!text) return;
    setDraft("");
    onAct("say", { text });
  };

  return (
    <footer className="rc-composer">
      {view.reply.open && (
        <section className="rc-reply" aria-label={t("Paste the supplier reply")}>
          <div className="rc-label">{t("Paste the supplier reply")}</div>
          <p className="rc-reply-hint">{t("From: {from} · date: today — suggested from the thread.", { from: view.reply.from })}</p>
          <textarea
            className="input rc-input"
            aria-label={t("Paste the supplier reply")}
            placeholder={t("Paste text or the whole thread — duplicates are detected")}
            value={view.reply.text}
            onChange={(e) => onAct("ui", { k: "replyText", v: e.target.value })}
          />
          <div className="rc-buttons">
            <button type="button" className="btn btn-primary" onClick={() => onAct("reply", {})}>
              {t("Add reply to the case")}
            </button>
            <button type="button" className="btn btn-ghost" onClick={() => onAct("sampleReply")}>
              {t("Use sample reply")}
            </button>
            <button type="button" className="btn btn-ghost" onClick={() => onAct("closeReply")}>
              {t("Cancel")}
            </button>
          </div>
        </section>
      )}

      {view.threads.length > 0 && (
        <div className="rc-threads" role="group" aria-label={t("Vendor thread")}>
          <span className="rc-label">{t("Vendor thread")}</span>
          {view.threads.map((thread) => (
            <span className="rc-thread" key={thread.t}>
              <button type="button" className={`rc-thread-pick${thread.on ? " is-on" : ""}`} aria-pressed={thread.on} onClick={() => onAct("thread", { t: thread.t })}>
                {thread.t}
              </button>
              {thread.offer && (
                <button type="button" className="btn btn-ghost" onClick={() => onAct("altOffer", { vendor: thread.t })}>
                  {t("Add offer from {vendor} (sample)", { vendor: thread.t })}
                </button>
              )}
            </span>
          ))}
        </div>
      )}

      <form className="rc-inputbar" onSubmit={submit}>
        <input className="rc-say-input" aria-label={t("Message")} placeholder={t("Answer, add a comment, or type approve / copy / I sent it…")} value={draft} onChange={(e) => setDraft(e.target.value)} autoComplete="off" />
        <button type="submit" className="btn btn-primary">
          {t("Send")}
        </button>
      </form>

      {(view.actions.length > 0 || view.demo.length > 0) && (
        <div className="rc-extras">
          {view.actions.map((action) => (
            <button type="button" className="rc-extra" key={action.act} onClick={() => onAct(action.act)}>
              + {action.label}
            </button>
          ))}
          {view.demo.length > 0 && (
            <span className="rc-demo">
              <span className="rc-label">{t("Demo")}</span>
              {view.demo.map((item) => (
                <button type="button" className="rc-demo-btn" key={item.act} onClick={() => onAct(item.act)}>
                  {item.label}
                </button>
              ))}
            </span>
          )}
        </div>
      )}
    </footer>
  );
}
