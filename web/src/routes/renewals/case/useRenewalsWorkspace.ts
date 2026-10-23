import { useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { addDays } from "./dates";
import { DEMO_APPROVER, DEMO_OWNER, demoContext } from "./fixtures";
import { rowActions, type QueueItem, type RowActions, type RowCta } from "./portfolio";
import type { ActPayload, ActionType } from "./types";
import { useQueue } from "./useQueue";

/**
 * Everything Renewals and Today share: who is looking, the language, the portfolio and the action
 * queue derived from the cases, and the things a person can do from a row of either screen.
 * (Backed by the demo tenant's cases until the backend owns them -- see `caseStore.ts`.)
 */
export function useRenewalsWorkspace(userLabel: string) {
  const { store, cases, viewer, lang, t, today, summaries, queueAll, queue, snoozed } = useQueue(userLabel);
  const navigate = useNavigate();

  const act = useCallback(
    (cid: string, type: ActionType, payload?: ActPayload) => store.act(cid, type, payload, demoContext(cid, viewer, lang)),
    [store, viewer, lang],
  );

  const openChat = useCallback((cid: string) => navigate(`/renewals/${cid}/chat`), [navigate]);
  const openInRenewals = useCallback((cid: string) => navigate(`/renewals?select=${cid}&tab=now`), [navigate]);

  /** Starts a case owned by someone else: the approver opens it and assigns it. The screen stays put. */
  const startAssigned = useCallback(
    (cid: string, owner: string) => store.open(cid, demoContext(cid, DEMO_APPROVER, lang), { approver: DEMO_APPROVER, owner }),
    [store, lang],
  );

  /** Starts a case owned by the viewer and goes to its chat; `branch: "notice"` opens on the notice. */
  const startOwn = useCallback(
    (cid: string, branch: "notice" | null = null, thenAlternatives = false) => {
      const ctx = demoContext(cid, viewer, lang);
      store.open(cid, ctx, { approver: DEMO_APPROVER, owner: viewer, branch });
      if (thenAlternatives) store.act(cid, "startAlt", {}, ctx);
      navigate(`/renewals/${cid}/chat`);
    },
    [store, viewer, lang, navigate],
  );

  const actionsFor = useCallback(
    (item: QueueItem): RowActions => rowActions(item, viewer, cases[item.cid], t),
    [viewer, cases, t],
  );

  const run = useCallback(
    (item: QueueItem, cta: RowCta) => {
      if (cta.do === "open") openChat(item.cid);
      else if (cta.do === "startAssign") startAssigned(item.cid, cta.assignTo);
      else startOwn(item.cid);
    },
    [openChat, startAssigned, startOwn],
  );

  const delegate = useCallback(
    (item: QueueItem, d: NonNullable<RowActions["delegate"]>) => {
      if (d.do === "delegate") act(item.cid, "delegate", { who: d.who, on: true });
      else act(item.cid, "transfer", { who: d.who });
    },
    [act],
  );

  const snooze = useCallback((item: QueueItem) => store.snooze(item.key, addDays(today, 3)), [store, today]);

  return {
    store,
    cases,
    viewer,
    lang,
    t,
    today,
    isApprover: viewer === DEMO_APPROVER,
    assignee: DEMO_OWNER,
    summaries,
    queue,
    queueAll,
    snoozed,
    act,
    openChat,
    openInRenewals,
    startAssigned,
    startOwn,
    actionsFor,
    run,
    delegate,
    snooze,
    unsnooze: store.unsnooze,
    dismiss: (item: QueueItem) => store.dismiss(item.key),
  };
}

export type RenewalsWorkspace = ReturnType<typeof useRenewalsWorkspace>;
