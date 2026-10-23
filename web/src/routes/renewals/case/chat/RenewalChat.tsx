import { useCallback, useLayoutEffect, useMemo, useRef, useState } from "react";
import type { ActPayload, ActionType, CardButton, CardChip, CaseView } from "../types";
import type { Lang } from "../../../../i18n/translate";
import CaseHeader from "./CaseHeader";
import Composer from "./Composer";
import HistoryDrawer from "./HistoryDrawer";
import StepCard from "./StepCard";
import StepsSoFar from "./StepsSoFar";
import { translator } from "../../../../i18n/translate";
import { splitFeed } from "./chatModel";
import "./renewalChat.css";

export type ChatTarget = "history" | "contract" | "renewals" | "newChat";

export interface RenewalChatProps {
  /** `view(case, ctx)` from the engine -- the chat renders only this. */
  view: CaseView;
  lang: Lang;
  onAct: (type: ActionType, payload?: ActPayload) => void;
  onNavigate: (target: ChatTarget) => void;
}

/**
 * The renewal wizard as a chat. The chat is for action: it shows only the *current step*, answered
 * with structured choices (chips, buttons). Everything earlier is one collapsible row per step above
 * it ("Steps so far"); a row opens a read-only drawer. Context lives in Renewals, one link away.
 */
export default function RenewalChat({ view, lang, onAct, onNavigate }: RenewalChatProps) {
  const t = useMemo(() => translator(lang), [lang]);
  const split = useMemo(() => splitFeed(view.cards, view.steps7), [view.cards, view.steps7]);
  const [peekId, setPeekId] = useState<string | null>(null);
  const [scrollTick, setScrollTick] = useState(0);
  const feedRef = useRef<HTMLDivElement>(null);
  const peek = peekId ? split.byId.get(peekId) : undefined;

  const dispatch = useCallback(
    (type: ActionType, payload?: ActPayload) => {
      // Typing into a field must not move the page; any real step does.
      if (type !== "ui") setScrollTick((n) => n + 1);
      onAct(type, payload);
    },
    [onAct],
  );

  // Open (and after each step) scroll to the card the user acts on.
  useLayoutEffect(() => {
    const box = feedRef.current;
    if (!box) return;
    const marked = box.querySelectorAll<HTMLElement>('[data-current="true"]');
    const target = marked[marked.length - 1];
    if (target) box.scrollTop = Math.max(0, target.offsetTop - 24);
  }, [scrollTick]);

  const press = (button: CardButton) => {
    if (button.off) return;
    if (button.act === "nav") {
      onNavigate(button.p.to === "c360" ? "contract" : "renewals");
      return;
    }
    dispatch(button.act, button.p);
  };
  const choose = (chip: CardChip) => {
    if (!chip.off) dispatch(chip.act, chip.p);
  };
  const setField = (key: string, value: string | boolean) => dispatch("ui", { k: key, v: value });

  return (
    <div className="renewal-chat" data-screen-label="Renewal case">
      <CaseHeader view={view} t={t} onAct={dispatch} onHistory={() => onNavigate("history")} onNewChat={() => onNavigate("newChat")} />

      <div className="rc-feed" ref={feedRef}>
        <div className="rc-column">
          {view.since && (
            <section className="rc-since" aria-label={t("Since your last visit")}>
              <div className="rc-label">{t("Since your last visit")}</div>
              <ul>
                {view.since.lines.map((line) => (
                  <li key={line}>{line}</li>
                ))}
              </ul>
              <button type="button" className="btn btn-ghost" onClick={() => dispatch("sinceOk")}>
                {t("Got it")}
              </button>
            </section>
          )}

          <StepsSoFar groups={split.history} t={t} onOpen={setPeekId} />

          {split.current.map((card) => (
            <StepCard key={card.id} card={card} t={t} onPress={press} onChip={choose} onField={setField} onAct={dispatch} />
          ))}
        </div>
      </div>

      <Composer view={view} t={t} onAct={dispatch} />

      {peek && <HistoryDrawer card={peek} t={t} onClose={() => setPeekId(null)} />}
    </div>
  );
}
