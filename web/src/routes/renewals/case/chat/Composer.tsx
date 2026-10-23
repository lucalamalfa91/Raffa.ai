import { useState } from "react";
import type { ActPayload, ActionType, CaseView } from "../types";
import type { Translate } from "./chatCopy";

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
        <section className="rc-reply" aria-label={t("pasteReply")}>
          <div className="rc-label">{t("pasteReply")}</div>
          <p className="rc-reply-hint">{t("pasteReplyHint", { from: view.reply.from })}</p>
          <textarea
            className="input rc-input"
            aria-label={t("pasteReply")}
            placeholder={t("pastePlaceholder")}
            value={view.reply.text}
            onChange={(e) => onAct("ui", { k: "replyText", v: e.target.value })}
          />
          <div className="rc-buttons">
            <button type="button" className="btn btn-primary" onClick={() => onAct("reply", {})}>
              {t("addReply")}
            </button>
            <button type="button" className="btn btn-ghost" onClick={() => onAct("sampleReply")}>
              {t("sampleReply")}
            </button>
            <button type="button" className="btn btn-ghost" onClick={() => onAct("closeReply")}>
              {t("cancel")}
            </button>
          </div>
        </section>
      )}

      {view.threads.length > 0 && (
        <div className="rc-threads" role="group" aria-label={t("vendorThread")}>
          <span className="rc-label">{t("vendorThread")}</span>
          {view.threads.map((thread) => (
            <span className="rc-thread" key={thread.t}>
              <button type="button" className={`rc-thread-pick${thread.on ? " is-on" : ""}`} aria-pressed={thread.on} onClick={() => onAct("thread", { t: thread.t })}>
                {thread.t}
              </button>
              {thread.offer && (
                <button type="button" className="btn btn-ghost" onClick={() => onAct("altOffer", { vendor: thread.t })}>
                  {t("addOfferFrom", { vendor: thread.t })}
                </button>
              )}
            </span>
          ))}
        </div>
      )}

      <form className="rc-inputbar" onSubmit={submit}>
        <input className="rc-say-input" aria-label={t("composerLabel")} placeholder={t("composerPlaceholder")} value={draft} onChange={(e) => setDraft(e.target.value)} autoComplete="off" />
        <button type="submit" className="btn btn-primary">
          {t("send")}
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
              <span className="rc-label">{t("demo")}</span>
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
