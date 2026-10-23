import { useCallback, useMemo } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { useI18n } from "../../../i18n/I18nProvider";
import { addDays } from "./dates";
import { rememberedViewer } from "./demoSession";
import { DEMO_APPROVER, DEMO_OWNER, DEMO_PEOPLE, demoContext } from "./fixtures";
import {
  buildQueue,
  buildSummaries,
  rowActions,
  snoozedItems,
  visibleQueue,
  type QueueItem,
  type RowActions,
  type RowCta,
} from "./portfolio";
import type { ActPayload, ActionType } from "./types";
import { getCaseStore, useCaseStore } from "./useCaseStore";
import { resolveViewer } from "./viewer";

/**
 * Everything Renewals and Today share: who is looking, the language, the portfolio and the action
 * queue derived from the cases, and the things a person can do from a row of either screen.
 * (Backed by the demo tenant's cases until the backend owns them -- see `caseStore.ts`.)
 */
export function useRenewalsWorkspace(userLabel: string) {
  const { lang, t } = useI18n();
  const location = useLocation();
  const navigate = useNavigate();
  const store = getCaseStore();
  const state = useCaseStore(store);

  const viewer = resolveViewer({ userLabel, as: rememberedViewer(location.search), members: DEMO_PEOPLE, approver: DEMO_APPROVER });
  const today = demoContext("1", viewer, lang).today;

  const summaries = useMemo(() => buildSummaries(state.cases, viewer, lang), [state.cases, viewer, lang]);
  const queueAll = useMemo(() => buildQueue(state.cases, viewer, lang), [state.cases, viewer, lang]);
  const queue = useMemo(() => visibleQueue(queueAll, state.queue, today), [queueAll, state.queue, today]);
  const snoozed = useMemo(() => snoozedItems(queueAll, state.queue, today), [queueAll, state.queue, today]);

  const act = useCallback(
    (cid: string, type: ActionType, payload?: ActPayload) => store.act(cid, type, payload, demoContext(cid, viewer, lang)),
    [store, viewer, lang],
  );

  const openChat = useCallback((cid: string) => navigate(`/renewals/${cid}/chat`), [navigate]);

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
    (item: QueueItem): RowActions => rowActions(item, viewer, state.cases[item.cid], t),
    [viewer, state.cases, t],
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
    cases: state.cases,
    viewer,
    lang,
    t,
    today,
    isApprover: viewer === DEMO_APPROVER,
    assignee: DEMO_OWNER,
    summaries,
    queue,
    snoozed,
    act,
    openChat,
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
